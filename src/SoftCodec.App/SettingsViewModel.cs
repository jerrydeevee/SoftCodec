using CommunityToolkit.Mvvm.ComponentModel;
using SoftCodec.Core;
using SoftCodec.Windows;
namespace SoftCodec.App;
public partial class SettingsViewModel : ObservableObject
{
 [ObservableProperty] private string displayName="";
 [ObservableProperty] private string advertisedIp="";
 [ObservableProperty] private string bindAddress="0.0.0.0";
 [ObservableProperty] private int sipPort=5060;
 [ObservableProperty] private int rtpPort=5004;
 [ObservableProperty] private int rtcpPort=5005;
 [ObservableProperty] private bool autoAnswer=true;
 [ObservableProperty] private int mediaTimeout=20;
 [ObservableProperty] private int maxCallSeconds=0;
 [ObservableProperty] private int dscp=46;
 [ObservableProperty] private bool symmetricRtp=true;
 [ObservableProperty] private string allowlist="";
 [ObservableProperty] private bool digestEnabled=true;
 [ObservableProperty] private string digestUser="studio";
 [ObservableProperty] private string digestRealm="softcodec.raah";
 [ObservableProperty] private int rateLimit=10;
 [ObservableProperty] private bool dropScanners=true;
 [ObservableProperty] private string priority="opus,g722,pcma,pcmu,l16-48,l24-48";
 [ObservableProperty] private int bitrate=128000;
 [ObservableProperty] private int ptime=20;
 [ObservableProperty] private int complexity=7;
 [ObservableProperty] private bool stereo=true;
 [ObservableProperty] private bool fec=true;
 [ObservableProperty] private bool dtx=false;
 [ObservableProperty] private bool cbr=false;
 [ObservableProperty] private int jitterMin=60;
 [ObservableProperty] private int jitterTarget=120;
 [ObservableProperty] private int jitterMax=400;
 [ObservableProperty] private bool adaptive=true;
 [ObservableProperty] private AudioDevice? renderDevice=null;
 [ObservableProperty] private AudioDevice? captureDevice=null;
 [ObservableProperty] private bool exclusive=true;
 [ObservableProperty] private bool raw=true;
 [ObservableProperty] private int latency=10;
 [ObservableProperty] private string renderChannels="0,1";
 [ObservableProperty] private string captureChannels="0,1";
 [ObservableProperty] private bool recordingEnabled=false;
 [ObservableProperty] private bool recordReturn=false;
 [ObservableProperty] private string recordingDirectory="";
 [ObservableProperty] private double silenceDb=-48;
 [ObservableProperty] private int silenceSeconds=8;
 [ObservableProperty] private bool registerEnabled=false;
 [ObservableProperty] private string registrar="";
 [ObservableProperty] private string registerUser="";
 [ObservableProperty] private string outboundProxy="";
 [ObservableProperty] private int registerExpiry=3600;

 [ObservableProperty] private string stunServer="stun.l.google.com";
 SoftCodecOptions original=new();
 public void Load(SoftCodecOptions o,IReadOnlyList<AudioDevice> render,IReadOnlyList<AudioDevice> capture)
 {
  original=o;if(o.Identity.StunServer.Length>0)StunServer=o.Identity.StunServer;DisplayName=o.Identity.DisplayName;AdvertisedIp=o.Identity.AdvertisedIp;BindAddress=o.Identity.BindAddress;SipPort=o.Sip.Port;RtpPort=o.Rtp.Port;RtcpPort=o.Rtp.RtcpPort;AutoAnswer=o.Sip.AutoAnswer;MediaTimeout=o.Sip.MediaTimeoutSeconds;MaxCallSeconds=o.Sip.MaxCallSeconds;Dscp=o.Rtp.Dscp;SymmetricRtp=o.Rtp.SymmetricRtp;
  Allowlist=string.Join(Environment.NewLine,o.Security.AllowlistCidrs);DigestEnabled=o.Security.Digest.Enabled;DigestUser=o.Security.Digest.Username;DigestRealm=o.Security.Digest.Realm;RateLimit=o.Security.InviteRateLimitPerMinute;DropScanners=o.Security.DropScannerUserAgents;
  Priority=string.Join(",",o.Codecs.Priority);Bitrate=o.Codecs.Opus.MaxAverageBitrate;Ptime=o.Codecs.Opus.PtimeMs;Complexity=o.Codecs.Opus.Complexity;Stereo=o.Codecs.Opus.Stereo;Fec=o.Codecs.Opus.UseInBandFec;Dtx=o.Codecs.Opus.UseDtx;Cbr=o.Codecs.Opus.Cbr;
  JitterMin=o.Jitter.MinMs;JitterTarget=o.Jitter.TargetMs;JitterMax=o.Jitter.MaxMs;Adaptive=o.Jitter.Mode=="adaptive";
  RenderDevice=render.FirstOrDefault(d=>d.Id==o.Audio.RenderDeviceId)??render.FirstOrDefault();CaptureDevice=capture.FirstOrDefault(d=>d.Id==o.Audio.CaptureDeviceId)??capture.FirstOrDefault();Exclusive=o.Audio.ShareMode=="exclusive";Raw=o.Audio.RawMode;Latency=o.Audio.RequestedLatencyMs;RenderChannels=string.Join(",",o.Audio.RenderChannels);CaptureChannels=string.Join(",",o.Audio.CaptureChannels);
  RecordingEnabled=o.Recording.Enabled;RecordReturn=o.Recording.RecordReturn;RecordingDirectory=o.Recording.Directory;SilenceDb=o.Alarms.SilenceThresholdDbFs;SilenceSeconds=o.Alarms.SilenceSeconds;
  RegisterEnabled=o.Sip.Register.Enabled;Registrar=o.Sip.Register.Registrar;RegisterUser=o.Sip.Register.User;OutboundProxy=o.Sip.Register.OutboundProxy;RegisterExpiry=o.Sip.Register.ExpirySeconds;
 }
 static string[] Split(string text)=>text.Split([',','\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
 public SoftCodecOptions Build()=>original with{
  Identity=original.Identity with{DisplayName=DisplayName,AdvertisedIp=AdvertisedIp.Trim(),BindAddress=BindAddress.Trim(),StunServer=StunServer},
  Sip=original.Sip with{Port=SipPort,AutoAnswer=AutoAnswer,MediaTimeoutSeconds=MediaTimeout,MaxCallSeconds=MaxCallSeconds,Register=original.Sip.Register with{Enabled=RegisterEnabled,Registrar=Registrar,User=RegisterUser,OutboundProxy=OutboundProxy,ExpirySeconds=RegisterExpiry}},
  Rtp=original.Rtp with{Port=RtpPort,RtcpPort=RtcpPort,Dscp=Dscp,SymmetricRtp=SymmetricRtp},
  Security=original.Security with{AllowlistCidrs=Split(Allowlist),InviteRateLimitPerMinute=RateLimit,DropScannerUserAgents=DropScanners,Digest=original.Security.Digest with{Enabled=DigestEnabled,Username=DigestUser,Realm=DigestRealm}},
  Codecs=original.Codecs with{Priority=Split(Priority),Opus=original.Codecs.Opus with{MaxAverageBitrate=Bitrate,PtimeMs=Ptime,Complexity=Complexity,Stereo=Stereo,UseInBandFec=Fec,UseDtx=Dtx,Cbr=Cbr}},
  Jitter=new(){MinMs=JitterMin,TargetMs=JitterTarget,MaxMs=JitterMax,Mode=Adaptive?"adaptive":"static"},
  Audio=original.Audio with{RenderDeviceId=RenderDevice?.Id??"",CaptureDeviceId=CaptureDevice?.Id??"",ShareMode=Exclusive?"exclusive":"shared",RawMode=Raw,RequestedLatencyMs=Latency,RenderChannels=Split(RenderChannels).Select(int.Parse).ToArray(),CaptureChannels=Split(CaptureChannels).Select(int.Parse).ToArray()},
  Recording=original.Recording with{Enabled=RecordingEnabled,RecordReturn=RecordReturn,Directory=RecordingDirectory},Alarms=original.Alarms with{SilenceThresholdDbFs=SilenceDb,SilenceSeconds=SilenceSeconds}
 };
}
