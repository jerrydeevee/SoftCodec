using Concentus;
namespace SoftCodec.Audio;
/// <summary>Bounded preallocated PCM ring. Read never returns stale samples; writes reject overflow.</summary>
public sealed class PcmRing(int capacity)
{
 readonly float[] data=new float[capacity];readonly object gate=new();int read,write,count;
 public int Count{get{lock(gate)return count;}}
 public long Overruns{get;private set;}public long Underruns{get;private set;}
 public int Write(ReadOnlySpan<float> source){lock(gate){if(source.Length>data.Length-count){Overruns++;return 0;}for(int i=0;i<source.Length;i++){data[write]=source[i];write=(write+1)%data.Length;}count+=source.Length;return source.Length;}}
 public int Read(Span<float> destination){lock(gate){int n=Math.Min(count,destination.Length);for(int i=0;i<n;i++){destination[i]=data[read];read=(read+1)%data.Length;}count-=n;destination[n..].Clear();if(n<destination.Length)Underruns++;return n;}}
 public void Clear(){lock(gate){read=write=count=0;}}
}
public sealed class StreamingResampler : IDisposable
{
 readonly IResampler resampler;readonly int channels,inputRate,outputRate;readonly float[] scratch;
 public StreamingResampler(int channels,int inputRate,int outputRate){this.channels=channels;this.inputRate=inputRate;this.outputRate=outputRate;resampler=ResamplerFactory.CreateResampler(channels,inputRate,outputRate,8);scratch=new float[24000];resampler.SkipZeroes();}
 public int Process(ReadOnlySpan<float> input,Span<float> output,double drift=1)
 {
  if(input.Length>scratch.Length)throw new ArgumentException("Resamplerblok te groot.");input.CopyTo(scratch);
  int inputFrames=input.Length/channels,outputFrames=output.Length/channels;
  resampler.SetRateFraction((int)Math.Round(inputRate*drift*1000),outputRate*1000,inputRate,outputRate);
  int consumed=inputFrames;resampler.ProcessInterleaved(scratch.AsSpan(0,input.Length),ref consumed,output,ref outputFrames);
  if(consumed!=inputFrames)throw new InvalidOperationException("Resampler-uitvoerbuffer te klein.");return outputFrames*channels;
 }
 public void Dispose()=>resampler.Dispose();
}
public static class AudioLevel
{
 public static double Dbfs(ReadOnlySpan<float> samples){double sum=0;foreach(float f in samples)sum+=f*f;return samples.Length==0?-96:Math.Max(-96,10*Math.Log10(Math.Max(1e-12,sum/samples.Length)));}
}
