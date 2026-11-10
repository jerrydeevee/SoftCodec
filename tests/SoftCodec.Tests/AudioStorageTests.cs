using SoftCodec.Audio;
using SoftCodec.Core;
using Xunit;
namespace SoftCodec.Tests;
public class AudioStorageTests
{
 [Theory][InlineData("PCMA",8000,1)][InlineData("PCMU",8000,1)][InlineData("G722",16000,1)][InlineData("L16",48000,2)][InlineData("L24",48000,2)][InlineData("OPUS",48000,2)]
 public void CodecRoundtripHasSignalAndFiniteOutput(string name,int rate,int channels)
 {
  using var codec=AudioCodecFactory.Create(new(name,111,name=="G722"?8000:rate,rate,channels,20),new());var pcm=new float[rate/50*channels];for(int i=0;i<pcm.Length/channels;i++)for(int c=0;c<channels;c++)pcm[i*channels+c]=.2f*MathF.Sin(2*MathF.PI*440*i/rate);var packet=new byte[8192];var decoded=new float[12000];int n=codec.Encode(pcm,packet);Assert.True(n>0);int count=codec.Decode(packet.AsSpan(0,n),decoded);Assert.Equal(pcm.Length,count);Assert.True(AudioLevel.Dbfs(decoded.AsSpan(0,count))>-45);Assert.All(decoded.Take(count),x=>Assert.True(float.IsFinite(x)));
 }
 [Theory][InlineData("L16",.00004)][InlineData("L24",.000001)]
 public void LinearPcmPreservesNegativeAndClips(string name,double tolerance){using var codec=new LinearPcmCodec(new(name,96,48000,48000,1,20));float[] samples=[-1,-.5f,0,.5f,1];var bytes=new byte[100];var decoded=new float[5];int n=codec.Encode(samples,bytes);codec.Decode(bytes.AsSpan(0,n),decoded);for(int i=0;i<5;i++)Assert.InRange(Math.Abs(samples[i]-decoded[i]),0,tolerance);}
 [Fact]public void G711KnownSilenceVectors(){Assert.Equal(0,G711Codec.DecodeMu(0xff));Assert.Equal(0xff,G711Codec.EncodeMu(0));Assert.Equal(8,G711Codec.DecodeA(0xd5));Assert.Equal(0xd5,G711Codec.EncodeA(0));}
 [Fact]public void ResamplerPreservesStreamingDuration(){using var r=new StreamingResampler(2,48000,16000);var source=new float[1920];var destination=new float[3000];int total=0;for(int i=0;i<100;i++)total+=r.Process(source,destination);Assert.InRange(total,63000,65000);}
 [Fact]public void ResamplerAcceptsDriftCorrection(){using var r=new StreamingResampler(2,48000,48000);var source=new float[1920];var destination=new float[3000];int total=0;for(int i=0;i<100;i++)total+=r.Process(source,destination,1.0003);Assert.InRange(total,190000,192100);}
 [Fact]public void AtomicConfigurationRecoversPreviousVersion(){string d=Temp();try{var store=new JsonStore<SoftCodecOptions>(Path.Combine(d,"config.json"));store.Save(new());store.Save(new(){Identity=new(){DisplayName="Tweede"}});File.WriteAllText(store.PathName,"{broken");var restored=store.Load(out var recovery);Assert.NotNull(recovery);Assert.Equal("Studio Radio Aa en Hunze",restored.Identity.DisplayName);Assert.Single(Directory.GetFiles(d,"*.broken-*"));}finally{Directory.Delete(d,true);}}
 [Fact]public async Task WavHasCorrect24BitHeaderAndDataLength(){string d=Temp();try{string path=Path.Combine(d,"record.wav");await using(var wav=new WavRecorder(path,2,48000))wav.Write(new float[]{-1,0,1,.5f});var b=File.ReadAllBytes(path);Assert.Equal(56,b.Length);Assert.Equal("RIFF",System.Text.Encoding.ASCII.GetString(b,0,4));Assert.Equal(24,BitConverter.ToInt16(b,34));Assert.Equal(12,BitConverter.ToInt32(b,40));Assert.Equal(0x80,b[46]);}finally{Directory.Delete(d,true);}}
 static string Temp(){string d=Path.Combine(Path.GetTempPath(),"SoftCodec.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(d);return d;}
}
