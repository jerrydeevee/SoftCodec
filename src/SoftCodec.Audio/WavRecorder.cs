using System.Buffers.Binary;
using System.Threading.Channels;
namespace SoftCodec.Audio;
/// <summary>Disk I/O runs on one background task. Bounded queue fails visibly on storage stalls.</summary>
public sealed class WavRecorder : IAsyncDisposable
{
 readonly FileStream stream;readonly Channel<float[]> queue;readonly Task writer;readonly int channels,rate;long bytes;
 public WavRecorder(string path,int channels,int rate)
 {
  this.channels=channels;this.rate=rate;Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);stream=new(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);stream.Write(new byte[44]);queue=Channel.CreateBounded<float[]>(128);writer=Task.Run(WriteLoop);
 }
 public void Write(ReadOnlySpan<float> samples){if(!queue.Writer.TryWrite(samples.ToArray()))throw new IOException("Opnamebuffer vol; controleer de opnameschijf.");}
 async Task WriteLoop(){await foreach(var samples in queue.Reader.ReadAllAsync()){var buffer=new byte[samples.Length*3];for(int i=0;i<samples.Length;i++){int v=Math.Clamp((int)MathF.Round(samples[i]*8388608),-8388608,8388607);buffer[i*3]=(byte)v;buffer[i*3+1]=(byte)(v>>8);buffer[i*3+2]=(byte)(v>>16);}if(bytes+buffer.Length>uint.MaxValue-36)throw new IOException("WAV bereikt de RIFF 4 GB-limiet.");await stream.WriteAsync(buffer);bytes+=buffer.Length;}}
 public async ValueTask DisposeAsync(){queue.Writer.TryComplete();try{await writer;}finally{var b=new byte[44];"RIFF"u8.CopyTo(b);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4),(uint)(bytes+36));"WAVEfmt "u8.CopyTo(b.AsSpan(8));BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16),16);BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(20),1);BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(22),(ushort)channels);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24),(uint)rate);BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28),(uint)(rate*channels*3));BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(32),(ushort)(channels*3));BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(34),24);"data"u8.CopyTo(b.AsSpan(36));BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(40),(uint)bytes);stream.Position=0;stream.Write(b);stream.Flush(true);await stream.DisposeAsync();}}
}
