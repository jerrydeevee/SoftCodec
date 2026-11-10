using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SoftCodec.Audio;
using SoftCodec.Core;
using SoftCodec.Rtp;
namespace SoftCodec.Windows;
public sealed record AudioDevice(string Id,string Name){public override string ToString()=>Name;}
public sealed class WasapiDeviceGraph : IAudioDevices
{
 readonly MMDeviceEnumerator enumerator=new();WasapiPlayer? player;WasapiRecorder? recorder;MMDevice? renderDevice,captureDevice;
 readonly PcmRing render=new(48000*16),capture=new(48000*16);StreamingResampler? renderResampler,captureResampler;readonly DriftController drift=new();
 float[] renderScratch=new float[48000],captureScratch=new float[48000],captureOutput=new float[48000];AudioOptions options=new();int streamChannels;RenderProvider? provider;
 public double DriftPpm{get;private set;}
 public event Action<string>? Warning;
 public IReadOnlyList<AudioDevice> GetDevices(bool input)=>enumerator.EnumerateAudioEndPoints(input?DataFlow.Capture:DataFlow.Render,DeviceState.Active).Select(d=>{using(d)return new AudioDevice(d.ID,d.FriendlyName);}).ToArray();
 public void Open(AudioOptions options,int sampleRate,int channels)
 {
  Close();this.options=options;streamChannels=channels;
  try{OpenInternal(options.ShareMode=="exclusive");}catch(Exception e)when(options.ShareMode=="exclusive")
  {Close();Warning?.Invoke("Exclusive audio mislukt; terugval naar shared: "+e.Message);OpenInternal(false);}
 }
 void OpenInternal(bool exclusive)
 {
  try{
   renderDevice=string.IsNullOrWhiteSpace(options.RenderDeviceId)?enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia):enumerator.GetDevice(options.RenderDeviceId);
   captureDevice=string.IsNullOrWhiteSpace(options.CaptureDeviceId)?enumerator.GetDefaultAudioEndpoint(DataFlow.Capture,Role.Multimedia):enumerator.GetDevice(options.CaptureDeviceId);
   var pb=new WasapiPlayerBuilder().WithDevice(renderDevice).WithEventSync().WithLatency(options.RequestedLatencyMs).WithMmcssThreadPriority("Pro Audio");if(exclusive)pb.WithExclusiveMode();else pb.WithSharedMode().WithLowLatency(true);if(options.RawMode)pb.WithRawMode();player=pb.Build();
   using var renderClient=renderDevice.CreateAudioClient();
   WaveFormat output=(exclusive?player.GetSupportedExclusiveFormat(new WaveFormat(48000,24,Math.Max(2,options.RenderChannels.Max()+1))):player.DeviceMixFormat)??renderClient.MixFormat;
   if(output.Channels<=options.RenderChannels.Max())throw new InvalidOperationException("Gekozen uitgangskanaal bestaat niet.");
   provider=new(render,output,streamChannels,options.RenderChannels);renderResampler=new(streamChannels,48000,output.SampleRate);player.Init(provider);player.PlaybackStopped+=(_,e)=>{if(e.Exception is not null)Warning?.Invoke("Weergave gestopt: "+e.Exception.Message);};
   var rb=new WasapiRecorderBuilder().WithDevice(captureDevice).WithEventSync().WithBufferLength(options.RequestedLatencyMs).WithMmcssThreadPriority("Pro Audio");if(exclusive)rb.WithExclusiveMode().WithFormat(new WaveFormat(48000,24,Math.Max(2,options.CaptureChannels.Max()+1)));else rb.WithSharedMode().WithLowLatency(true);if(options.RawMode)rb.WithRawMode();recorder=rb.Build();
   if(recorder.WaveFormat.Channels<=options.CaptureChannels.Max())throw new InvalidOperationException("Gekozen ingangskanaal bestaat niet.");
   captureResampler=new(streamChannels,recorder.WaveFormat.SampleRate,48000);recorder.RecordingStopped+=(_,e)=>{if(e.Exception is not null)Warning?.Invoke("Capture gestopt: "+e.Exception.Message);};recorder.DataAvailable+=OnCapture;player.Play();recorder.StartRecording();
  }catch{Close();throw;}
 }
 void OnCapture(ReadOnlySpan<byte> bytes,AudioClientBufferFlags flags,long devicePosition,long qpc)
 {
  try{var fmt=recorder!.WaveFormat;int frames=bytes.Length/fmt.BlockAlign;int count=frames*streamChannels;if(count>captureScratch.Length)throw new InvalidOperationException("Captureblok te groot.");
   for(int frame=0;frame<frames;frame++)for(int channel=0;channel<streamChannels;channel++){int mapped=options.CaptureChannels[Math.Min(channel,options.CaptureChannels.Length-1)];captureScratch[frame*streamChannels+channel]=(flags&AudioClientBufferFlags.Silent)!=0?0:SampleFormat.Read(bytes.Slice(frame*fmt.BlockAlign+mapped*fmt.BitsPerSample/8),fmt);}
   int converted=captureResampler!.Process(captureScratch.AsSpan(0,count),captureOutput);if(capture.Write(captureOutput.AsSpan(0,converted))==0)Warning?.Invoke("Capturebuffer vol.");
  }catch(Exception e){Warning?.Invoke("Capturefout: "+e.Message);}
 }
 public void Write(ReadOnlySpan<float> interleaved,int channels,int sampleRate)
 {
  if(renderResampler is null||provider is null)return;if(channels!=streamChannels||sampleRate!=48000)throw new ArgumentException("Audioformaat wijkt af van de geopende audioketen.");
  double fill=render.Count/(double)(provider.WaveFormat.SampleRate*channels);double ratio=drift.Update(fill,.04,interleaved.Length/(double)(48000*channels));DriftPpm=(ratio-1)*1e6;
  int count=renderResampler.Process(interleaved,renderScratch,ratio);if(render.Write(renderScratch.AsSpan(0,count))==0)Warning?.Invoke("Renderbuffer vol.");
 }
 public int Read(Span<float> interleaved,int channels,int sampleRate){if(channels!=streamChannels||sampleRate!=48000)throw new ArgumentException("Captureformaat wijkt af.");return capture.Read(interleaved);}
 public void Close()
 {
  if(recorder is not null){recorder.DataAvailable-=OnCapture;recorder.StopRecording();recorder.Dispose();recorder=null;}
  if(player is not null){player.Stop();player.Dispose();player=null;}renderResampler?.Dispose();renderResampler=null;captureResampler?.Dispose();captureResampler=null;renderDevice?.Dispose();renderDevice=null;captureDevice?.Dispose();captureDevice=null;render.Clear();capture.Clear();provider=null;
 }
 public void Dispose(){Close();enumerator.Dispose();}
 sealed class RenderProvider(PcmRing ring,WaveFormat format,int channels,int[] mapping) : IWaveProvider
 {
  readonly float[] samples=new float[48000];public WaveFormat WaveFormat=>format;
  public int Read(Span<byte> buffer)
  {
   buffer.Clear();int frames=buffer.Length/format.BlockAlign;int needed=frames*channels;if(needed>samples.Length)throw new InvalidOperationException("Renderblok te groot.");ring.Read(samples.AsSpan(0,needed));
   for(int frame=0;frame<frames;frame++)for(int ch=0;ch<mapping.Length;ch++){int target=frame*format.BlockAlign+mapping[ch]*format.BitsPerSample/8;SampleFormat.Write(buffer[target..],format,samples[frame*channels+Math.Min(ch,channels-1)]);}return buffer.Length;
  }
 }
}
static class SampleFormat
{
 static bool Float(WaveFormat f)=>f.Encoding==WaveFormatEncoding.IeeeFloat||(f is WaveFormatExtensible ex&&ex.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71"));
 public static float Read(ReadOnlySpan<byte> bytes,WaveFormat format)
 {
  if(Float(format)&&format.BitsPerSample==32)return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
  return format.BitsPerSample switch {16=>BinaryPrimitives.ReadInt16LittleEndian(bytes)/32768f,24=>((bytes[2]<<24)|(bytes[1]<<16)|(bytes[0]<<8))/2147483648f,32=>BinaryPrimitives.ReadInt32LittleEndian(bytes)/2147483648f,_=>throw new NotSupportedException("Audioformaat: "+format)};
 }
 public static void Write(Span<byte> bytes,WaveFormat format,float sample)
 {
  if(Float(format)&&format.BitsPerSample==32){BinaryPrimitives.WriteInt32LittleEndian(bytes,BitConverter.SingleToInt32Bits(sample));return;}
  switch(format.BitsPerSample){case 16:BinaryPrimitives.WriteInt16LittleEndian(bytes,(short)Math.Clamp((int)(sample*32768),-32768,32767));break;case 24:int v=Math.Clamp((int)(sample*8388608),-8388608,8388607);bytes[0]=(byte)v;bytes[1]=(byte)(v>>8);bytes[2]=(byte)(v>>16);break;case 32:BinaryPrimitives.WriteInt32LittleEndian(bytes,(int)Math.Clamp(sample*2147483648d,int.MinValue,int.MaxValue));break;default:throw new NotSupportedException("Audioformaat: "+format);}
 }
}
