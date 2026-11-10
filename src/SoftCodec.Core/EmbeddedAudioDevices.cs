namespace SoftCodec.Core;
/// <summary>REA-Radio-DJ supplies the remote channel sink and a separate mixer mix-minus source.</summary>
public sealed class EmbeddedAudioDevices(IPcmSink remoteChannel,IPcmSource mixMinus) : IAudioDevices
{
 bool opened;public event Action<string>? Warning{add{}remove{}}
 public void Open(AudioOptions options,int sampleRate,int channels)=>opened=true;
 public void Close()=>opened=false;
 public void Write(ReadOnlySpan<float> samples,int channels,int sampleRate){if(opened)remoteChannel.Write(samples,channels,sampleRate);}
 public int Read(Span<float> samples,int channels,int sampleRate){if(!opened){samples.Clear();return 0;}int n=mixMinus.Read(samples,channels,sampleRate);if(n<0||n>samples.Length)throw new InvalidOperationException("Ongeldig sampleaantal uit mix-minus.");samples[n..].Clear();return n;}
 public void Dispose()=>Close();
}
