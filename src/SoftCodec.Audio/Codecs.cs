using System.Buffers.Binary;
using Concentus;
using Concentus.Enums;
using SIPSorcery.Media;
using SoftCodec.Core;
namespace SoftCodec.Audio;
public interface IAudioCodec : IDisposable
{
 CodecFormat Format {get;}
 int Encode(ReadOnlySpan<float> samples,Span<byte> packet);
 int Decode(ReadOnlySpan<byte> packet,Span<float> samples,bool fec=false);
}
public static class AudioCodecFactory
{
 public static IAudioCodec Create(CodecFormat f,OpusSettings options)=>f.Name switch {"OPUS"=>new OpusCodec(f,options),"PCMA" or "PCMU"=>new G711Codec(f),"G722"=>new G722AudioCodec(f),"L16" or "L24"=>new LinearPcmCodec(f),_=>throw new NotSupportedException(f.Name)};
}
public sealed class OpusCodec : IAudioCodec
{
 readonly IOpusEncoder encoder;readonly IOpusDecoder decoder;public CodecFormat Format{get;}
 public OpusCodec(CodecFormat f,OpusSettings options)
 {
  Format=f;encoder=OpusCodecFactory.CreateEncoder(48000,f.Channels,options.Application=="voip"?OpusApplication.OPUS_APPLICATION_VOIP:OpusApplication.OPUS_APPLICATION_AUDIO);
  decoder=OpusCodecFactory.CreateDecoder(48000,f.Channels);encoder.Bitrate=options.MaxAverageBitrate;encoder.UseInbandFEC=options.UseInBandFec;encoder.PacketLossPercent=options.UseInBandFec?Math.Max(1,options.ExpectedLossPercent):0;encoder.UseVBR=!options.Cbr;encoder.UseDTX=options.UseDtx;encoder.Complexity=options.Complexity;
 }
 public int Encode(ReadOnlySpan<float> samples,Span<byte> packet)=>encoder.Encode(samples,Format.FrameSamples,packet,Math.Min(packet.Length,1275));
 public int Decode(ReadOnlySpan<byte> packet,Span<float> samples,bool fec=false)=>decoder.Decode(packet,samples,Format.FrameSamples,fec)*Format.Channels;
 public void Dispose(){(encoder as IDisposable)?.Dispose();(decoder as IDisposable)?.Dispose();}
}
public sealed class LinearPcmCodec(CodecFormat format) : IAudioCodec
{
 public CodecFormat Format=>format;int Width=>format.Name=="L24"?3:2;
 public int Encode(ReadOnlySpan<float> samples,Span<byte> packet)
 {
  int bytes=samples.Length*Width;if(bytes>packet.Length)throw new ArgumentException("PCM-pakket te groot.");
  for(int i=0;i<samples.Length;i++){if(Width==2)BinaryPrimitives.WriteInt16BigEndian(packet[(i*2)..],(short)Math.Clamp((int)MathF.Round(samples[i]*32768),-32768,32767));else{int v=Math.Clamp((int)MathF.Round(samples[i]*8388608),-8388608,8388607);packet[i*3]=(byte)(v>>16);packet[i*3+1]=(byte)(v>>8);packet[i*3+2]=(byte)v;}}return bytes;
 }
 public int Decode(ReadOnlySpan<byte> packet,Span<float> samples,bool fec=false)
 {
  if(packet.Length==0){samples[..(format.FrameSamples*format.Channels)].Clear();return format.FrameSamples*format.Channels;}
  if(packet.Length%(Width*format.Channels)!=0 || packet.Length/Width>samples.Length)throw new FormatException("Ongeldige PCM-payload.");int count=packet.Length/Width;
  for(int i=0;i<count;i++)samples[i]=Width==2?BinaryPrimitives.ReadInt16BigEndian(packet[(i*2)..])/32768f:((packet[i*3]<<24)|(packet[i*3+1]<<16)|(packet[i*3+2]<<8))/2147483648f;return count;
 }
 public void Dispose(){}
}
public sealed class G711Codec(CodecFormat format) : IAudioCodec
{
 public CodecFormat Format=>format;
 public static short DecodeMu(byte v){v=(byte)~v;int t=((v&15)<<3)+132;t<<=(v&112)>>4;return(short)((v&128)!=0?132-t:t-132);}
 public static byte EncodeMu(short sample){int x=sample;int sign=(x>>8)&128;if(sign!=0)x=-x;x=Math.Min(x,32635)+132;int exponent=7;for(int mask=16384;(x&mask)==0&&exponent>0;mask>>=1)exponent--;return(byte)~(sign|(exponent<<4)|((x>>(exponent+3))&15));}
 public static short DecodeA(byte v){v^=85;int t=(v&15)<<4;int segment=(v&112)>>4;t=segment switch{0=>t+8,1=>t+264,_=>(t+264)<<(segment-1)};return(short)((v&128)!=0?t:-t);}
 public static byte EncodeA(short sample){int x=sample;int sign=x>=0?128:0;if(x<0)x=-x-1;x=Math.Min(x,32767);int segment=0;for(int limit=255;x>limit&&segment<7;limit=(limit<<1)|1)segment++;int mantissa=segment==0?(x>>4)&15:(x>>(segment+3))&15;return(byte)((sign|(segment<<4)|mantissa)^85);}
 public int Encode(ReadOnlySpan<float> samples,Span<byte> packet){if(samples.Length>packet.Length)throw new ArgumentException("Buffer te klein.");for(int i=0;i<samples.Length;i++){short s=(short)Math.Clamp((int)(samples[i]*32768),-32768,32767);packet[i]=format.Name=="PCMA"?EncodeA(s):EncodeMu(s);}return samples.Length;}
 public int Decode(ReadOnlySpan<byte> packet,Span<float> samples,bool fec=false){if(packet.Length==0){samples[..format.FrameSamples].Clear();return format.FrameSamples;}if(packet.Length>samples.Length)throw new FormatException("G.711-payload te groot.");for(int i=0;i<packet.Length;i++)samples[i]=(format.Name=="PCMA"?DecodeA(packet[i]):DecodeMu(packet[i]))/32768f;return packet.Length;}
 public void Dispose(){}
}
public sealed class G722AudioCodec(CodecFormat format) : IAudioCodec
{
 readonly G722Codec codec=new();readonly G722CodecState rx=new(64000,G722Flags.None),tx=new(64000,G722Flags.None);readonly short[] pcm=new short[3840];readonly byte[] bytes=new byte[1920];
 public CodecFormat Format=>format;
 public int Encode(ReadOnlySpan<float> samples,Span<byte> packet){for(int i=0;i<samples.Length;i++)pcm[i]=(short)Math.Clamp((int)(samples[i]*32768),-32768,32767);int n=codec.Encode(tx,bytes,pcm,samples.Length);bytes.AsSpan(0,n).CopyTo(packet);return n;}
 public int Decode(ReadOnlySpan<byte> packet,Span<float> samples,bool fec=false){if(packet.Length==0){samples[..format.FrameSamples].Clear();return format.FrameSamples;}if(packet.Length>bytes.Length)throw new FormatException("G.722-payload te groot.");packet.CopyTo(bytes);int n=codec.Decode(rx,pcm,bytes,packet.Length);for(int i=0;i<n;i++)samples[i]=pcm[i]/32768f;return n;}
 public void Dispose(){}
}
