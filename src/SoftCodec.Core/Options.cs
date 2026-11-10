using System.Net;
namespace SoftCodec.Core;
public sealed record SoftCodecOptions
{
 public int SchemaVersion { get; init; } = 1;
 public IdentityOptions Identity { get; init; } = new(); public SipOptions Sip { get; init; } = new();
 public RtpOptions Rtp { get; init; } = new(); public JitterSettings Jitter { get; init; } = new();
 public CodecOptions Codecs { get; init; } = new(); public AudioOptions Audio { get; init; } = new();
 public SecurityOptions Security { get; init; } = new(); public RecordingOptions Recording { get; init; } = new();
 public AlarmOptions Alarms { get; init; } = new();
 public void Validate()
 {
  if (SchemaVersion != 1) throw new ArgumentException("Onbekende configuratieversie.");
  foreach (var p in new[]{Sip.Port,Rtp.Port,Rtp.RtcpPort}) if (p is < 1 or > 65535) throw new ArgumentException("Poort moet 1–65535 zijn.");
  if (new[]{Sip.Port,Rtp.Port,Rtp.RtcpPort}.Distinct().Count()!=3) throw new ArgumentException("SIP, RTP en RTCP vereisen eigen poorten.");
  if (!IPAddress.TryParse(Identity.BindAddress,out var bind) || bind.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork) throw new ArgumentException("Bindadres moet IPv4 zijn.");
  if (!string.IsNullOrWhiteSpace(Identity.AdvertisedIp) && (!IPAddress.TryParse(Identity.AdvertisedIp,out var advertised) || advertised.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)) throw new ArgumentException("Geadverteerd adres moet IPv4 zijn.");
  if (Jitter.MinMs < 0 || Jitter.MinMs>Jitter.TargetMs || Jitter.TargetMs>Jitter.MaxMs || Jitter.MaxMs>2000) throw new ArgumentException("Jitter: 0 ≤ min ≤ doel ≤ max ≤ 2000 ms.");
  if (Codecs.Opus.PtimeMs is not (10 or 20 or 40)) throw new ArgumentException("Opus ptime moet 10, 20 of 40 ms zijn.");
  if (Codecs.Opus.MaxAverageBitrate is < 6000 or > 510000 || Codecs.Opus.Complexity is <0 or >10) throw new ArgumentException("Ongeldige Opusinstellingen.");
  if (Sip.MediaTimeoutSeconds<1 || Sip.MaxCallSeconds<0 || Security.InviteRateLimitPerMinute<1) throw new ArgumentException("Ongeldige timer of rate limit.");
  if (Audio.SampleRate!=48000 || Audio.RenderChannels.Length==0 || Audio.CaptureChannels.Length==0 || Audio.RenderChannels.Any(x=>x<0) || Audio.CaptureChannels.Any(x=>x<0)) throw new ArgumentException("Audio vereist 48 kHz en geldige kanaaltoewijzing.");
  if (Rtp.Dscp is <0 or >63 || Audio.RequestedLatencyMs is <1 or >1000 || Alarms.SilenceSeconds<1 || Alarms.SilenceThresholdDbFs is >0 or < -120) throw new ArgumentException("Ongeldige DSCP, latency of alarmdrempel.");
  if (Jitter.Mode is not ("adaptive" or "static") || Audio.ShareMode is not ("exclusive" or "shared")) throw new ArgumentException("Onbekende jitter- of audiomodus.");
  if (Codecs.Priority.Length==0 || Codecs.Priority.Distinct().Count()!=Codecs.Priority.Length || Codecs.Priority.Any(p=>!new[]{"opus","g722","pcma","pcmu","l16-48","l16-32","l24-48"}.Contains(p))) throw new ArgumentException("Onbekende of dubbele codecprioriteit.");
  if (Sip.Register.Enabled && (string.IsNullOrWhiteSpace(Sip.Register.Registrar)||string.IsNullOrWhiteSpace(Sip.Register.User)||Sip.Register.ExpirySeconds<60)) throw new ArgumentException("Vul registrar, gebruikersnaam en geldige registratieduur in.");
  if (Sip.SessionTimerSeconds!=0) throw new ArgumentException("SIP session timers zijn nog niet vrijgegeven; gebruik 0.");
  if (Security.Digest.Enabled && (string.IsNullOrWhiteSpace(Security.Digest.Username)||string.IsNullOrWhiteSpace(Security.Digest.Realm))) throw new ArgumentException("Digest vereist gebruikersnaam en realm.");
  if (Recording.Enabled && (string.IsNullOrWhiteSpace(Recording.Directory)||Recording.Format!="wav24")) throw new ArgumentException("Opname vereist een map en wav24-formaat.");
  foreach(var c in Security.AllowlistCidrs) _=Ipv4Cidr.Parse(c);
  if (!Security.Digest.Enabled && Security.AllowlistCidrs.Length==0) throw new ArgumentException("Activeer digest of configureer een IP-allowlist.");
 }
}
public sealed record IdentityOptions { public string DisplayName {get;init;}="Studio Radio Aa en Hunze"; public string SipUser {get;init;}="studio"; public string AdvertisedIp {get;init;}=""; public string BindAddress {get;init;}="0.0.0.0"; public string StunServer {get;init;}=""; }
public sealed record RegistrarOptions { public bool Enabled {get;init;} public string Registrar {get;init;}=""; public string User {get;init;}=""; public string PasswordSecretRef {get;init;}="sip-register"; public string OutboundProxy {get;init;}=""; public int ExpirySeconds {get;init;}=3600; }
public sealed record SipOptions { public int Port {get;init;}=5060; public bool AutoAnswer {get;init;}=true; public RegistrarOptions Register {get;init;}=new(); public int SessionTimerSeconds {get;init;} public int MaxCallSeconds {get;init;} public int MediaTimeoutSeconds {get;init;}=20; }
public sealed record RtpOptions { public int Port {get;init;}=5004; public int RtcpPort {get;init;}=5005; public int Dscp {get;init;}=46; public bool SymmetricRtp {get;init;}=true; }
public sealed record JitterSettings { public string Mode {get;init;}="adaptive"; public int MinMs {get;init;}=60; public int MaxMs {get;init;}=400; public int TargetMs {get;init;}=120; }
public sealed record CodecOptions { public string[] Priority {get;init;}=["opus","g722","pcma","pcmu","l16-48","l24-48"]; public OpusSettings Opus {get;init;}=new(); }
public sealed record OpusSettings { public int MaxAverageBitrate {get;init;}=128000; public bool Stereo {get;init;}=true; public bool UseInBandFec {get;init;}=true; public bool UseDtx {get;init;} public bool Cbr {get;init;} public int PtimeMs {get;init;}=20; public int Complexity {get;init;}=7; public string Application {get;init;}="audio"; public int ExpectedLossPercent {get;init;}=5; }
public sealed record AudioOptions { public string RenderDeviceId {get;init;}=""; public string CaptureDeviceId {get;init;}=""; public string ShareMode {get;init;}="exclusive"; public bool RawMode {get;init;}=true; public int RequestedLatencyMs {get;init;}=10; public int SampleRate {get;init;}=48000; public int[] RenderChannels {get;init;}=[0,1]; public int[] CaptureChannels {get;init;}=[0,1]; }
public sealed record DigestOptions { public bool Enabled {get;init;}=true; public string Realm {get;init;}="softcodec.raah"; public string Username {get;init;}="studio"; public string PasswordSecretRef {get;init;}="sip-digest"; }
public sealed record SecurityOptions { public string[] AllowlistCidrs {get;init;}=[]; public DigestOptions Digest {get;init;}=new(); public int InviteRateLimitPerMinute {get;init;}=10; public bool DropScannerUserAgents {get;init;}=true; }
public sealed record RecordingOptions { public bool Enabled {get;init;} public string Directory {get;init;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"SoftCodec","Opnames"); public string Format {get;init;}="wav24"; public bool RecordReturn {get;init;} }
public sealed record AlarmOptions { public double SilenceThresholdDbFs {get;init;}=-48; public int SilenceSeconds {get;init;}=8; public int LinkLossSeconds {get;init;}=20; }
public readonly record struct Ipv4Cidr(uint Network, int Prefix)
{
 public static Ipv4Cidr Parse(string text)
 {
  var parts=text.Split('/'); if(parts.Length>2 || !IPAddress.TryParse(parts[0],out var ip) || ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork) throw new FormatException("Ongeldig IPv4 CIDR: "+text);
  int prefix=parts.Length==1?32:int.Parse(parts[1]); if(prefix is <0 or >32) throw new FormatException("Ongeldig prefix.");
  return new(ToUInt(ip)&Mask(prefix),prefix);
 }
 static uint Mask(int p)=>p==0?0:uint.MaxValue<<(32-p);
 static uint ToUInt(IPAddress ip)=>System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes());
 public bool Contains(IPAddress ip)=>ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork && (ToUInt(ip)&Mask(Prefix))==Network;
}
