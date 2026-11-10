namespace SoftCodec.Core;
public enum CallState { Idle, Incoming, Outgoing, Establishing, Connected, Disconnecting, Error }
public sealed record CallInfo(string CallId, string RemoteDisplay, string RemoteEndpoint, string NegotiatedCodec, DateTimeOffset ConnectedAt, TimeSpan Duration);
public sealed record Alarm(string Code, string Message, DateTimeOffset At);
public sealed record MediaMetrics
{
 public double PacketLossPercent { get; init; }
 public double JitterMs { get; init; }
 public double BufferDepthMs { get; init; }
 public double TargetBufferMs { get; init; }
 public long PacketsReceived { get; init; }
 public long PacketsLost { get; init; }
 public long PacketsLate { get; init; }
 public long PacketsDiscarded { get; init; }
 public long PacketsSent { get; init; }
 public double BitrateDownKbps { get; init; }
 public double BitrateUpKbps { get; init; }
 public double? RttMs { get; init; }
 public double DriftPpm { get; init; }
 public double LevelRemoteLeftDbfs {get;init;}=-96;
 public double LevelRemoteRightDbfs {get;init;}=-96;
 public double LevelReturnLeftDbfs {get;init;}=-96;
 public double LevelReturnRightDbfs {get;init;}=-96;
 public double LevelRemoteDbfs { get; init; } = -96;
 public double LevelReturnDbfs { get; init; } = -96;
 public double EstimatedOneWayMs { get; init; }
}
public interface ISoftCodecHost : IAsyncDisposable
{
 CallState State { get; }
 CallInfo? ActiveCall { get; }
 MediaMetrics Metrics { get; }
 event Action<CallState, CallInfo?>? StateChanged;
 event Action<Alarm>? AlarmRaised;
 Task StartAsync(CancellationToken ct = default);
 Task StopAsync(); Task AnswerAsync(); Task RejectAsync(); Task HangupAsync();
 Task PlaceCallAsync(Uri sipUri, ReporterProfile? profile = null);
 void ApplyConfig(SoftCodecOptions options);
}
public interface IPcmSink { void Write(ReadOnlySpan<float> interleaved, int channels, int sampleRate); }
public interface IPcmSource { int Read(Span<float> interleaved, int channels, int sampleRate); }
public interface ISecretStore { string? Read(string reference); void Write(string reference, string secret); }
public sealed record ReporterProfile
{
 public Guid Id { get; init; } = Guid.NewGuid(); public string Name { get; init; } = "";
 public string? SipUri { get; init; } public string? ExpectedFromContains { get; init; }
 public string[] AllowedCidrs { get; init; } = []; public string[] CodecPriority { get; init; } = [];
 public bool AutoAnswer { get; init; } = true; public JitterSettings? JitterOverride { get; init; }
 public OpusSettings? OpusOverride { get; init; } public bool Stereo { get; init; } = true;
 public string Notes { get; init; } = "";
}
public interface IAudioDevices : IPcmSink, IPcmSource, IDisposable
{
 void Open(AudioOptions options, int sampleRate, int channels); void Close();
 event Action<string>? Warning;
}
public sealed record CodecFormat(string Name, int PayloadType, int ClockRate, int SampleRate, int Channels, int PtimeMs, string Fmtp = "")
{
 public int FrameSamples => checked(SampleRate * PtimeMs / 1000);
 public int TimestampStep => checked(ClockRate * PtimeMs / 1000);
 public string Description => $"{Name} {SampleRate / 1000} kHz · {Channels} ch · {PtimeMs} ms";
}
