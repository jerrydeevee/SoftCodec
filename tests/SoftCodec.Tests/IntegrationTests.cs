using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using SoftCodec.Core;
using SoftCodec.Sip;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using Xunit;
namespace SoftCodec.Tests;
public sealed class MemorySecrets : ISecretStore
{
 readonly Dictionary<string,string> values=new();public string? Read(string reference)=>values.GetValueOrDefault(reference);public void Write(string reference,string value)=>values[reference]=value;
}
public sealed class TestAudio(float tone=440) : IAudioDevices
{
 double phase;public long ReceivedSamples;public double Peak;public bool Opened;public event Action<string>? Warning{add{}remove{}}
 public void Open(AudioOptions options,int sampleRate,int channels)=>Opened=true;
 public void Close()=>Opened=false;
 public void Dispose()=>Close();
 public void Write(ReadOnlySpan<float> samples,int channels,int sampleRate){Interlocked.Add(ref ReceivedSamples,samples.Length);foreach(float s in samples)Peak=Math.Max(Peak,Math.Abs(s));}
 public int Read(Span<float> samples,int channels,int sampleRate){for(int i=0;i<samples.Length/channels;i++){float v=.15f*(float)Math.Sin(phase);phase+=2*Math.PI*tone/sampleRate;for(int ch=0;ch<channels;ch++)samples[i*channels+ch]=v;}return samples.Length;}
}
public class IntegrationTests
{
 static int FreePort(){using var s=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));return ((IPEndPoint)s.Client.LocalEndPoint!).Port;}
 static SoftCodecOptions Options(bool digest=false)=>new(){Identity=new(){AdvertisedIp="127.0.0.1",BindAddress="127.0.0.1"},Sip=new(){Port=FreePort(),MediaTimeoutSeconds=5},Rtp=new(){Port=FreePort(),RtcpPort=FreePort()},Jitter=new(){MinMs=20,TargetMs=40,MaxMs=200},Security=new(){AllowlistCidrs=["127.0.0.1/32"],Digest=new(){Enabled=digest}}};
 static EventLog Log()=>new(Path.Combine(Path.GetTempPath(),"SoftCodec.Tests",Guid.NewGuid().ToString("N")));
 static async Task Until(Func<bool> condition,int timeout=10000){var deadline=Environment.TickCount64+timeout;while(!condition()){if(Environment.TickCount64>deadline)throw new TimeoutException("Voorwaarde niet bereikt.");await Task.Delay(20);}}
 [Theory][InlineData("opus")][InlineData("g722")][InlineData("pcma")][InlineData("pcmu")][InlineData("l16-48")][InlineData("l24-48")]
 public async Task TwoHostsExchangeRealUdpAudioAndReleasePorts(string codec)
 {
  var a=Options() with{Codecs=new(){Priority=[codec]}};var b=Options() with{Codecs=new(){Priority=[codec]}};var inputA=new TestAudio(440);var inputB=new TestAudio(880);
  await using var receiver=new SoftCodecHost(a,inputA,new MemorySecrets(),Log());await using var caller=new SoftCodecHost(b,inputB,new MemorySecrets(),Log());
  await receiver.StartAsync();await caller.StartAsync();await caller.PlaceCallAsync(new Uri($"sip:studio@127.0.0.1:{a.Sip.Port}"));
  await Until(()=>receiver.State==CallState.Connected&&caller.State==CallState.Connected&&inputA.Peak>.01&&inputB.Peak>.01);
  Assert.True(receiver.Metrics.PacketsReceived>0);Assert.True(caller.Metrics.PacketsReceived>0);Assert.True(receiver.Metrics.PacketsSent>0);await caller.HangupAsync();await Until(()=>receiver.State==CallState.Idle);Assert.False(inputA.Opened);Assert.False(inputB.Opened);
  await receiver.StopAsync();using var rebound=new UdpClient(new IPEndPoint(IPAddress.Loopback,a.Rtp.Port));
 }
 [Fact]public async Task IncomingDigestChallengeAuthenticatesRealSipCall()
 {
  var a=Options(true);var b=Options();var secrets=new MemorySecrets();secrets.Write("sip-digest","correct horse battery staple");await using var receiver=new SoftCodecHost(a,new TestAudio(),secrets,Log());await receiver.StartAsync();
  using var transport=new SIPTransport();transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback,b.Sip.Port)));using var ua=new SIPUserAgent(transport,null,false);await using var session=new CodecMediaSession(b,new TestAudio(),Log(),IPAddress.Loopback,IPAddress.Loopback);
  bool connected=await ua.Call($"sip:studio@127.0.0.1:{a.Sip.Port}","studio","correct horse battery staple",session,8);Assert.True(connected);await Until(()=>receiver.State==CallState.Connected);ua.Hangup();await Until(()=>receiver.State==CallState.Idle);transport.Shutdown();
 }
 [Fact]public async Task SipOptionsRespondsWithAllowedMethods()
 {
  var a=Options();await using var receiver=new SoftCodecHost(a,new TestAudio(),new MemorySecrets(),Log());await receiver.StartAsync();using var udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));int port=((IPEndPoint)udp.Client.LocalEndPoint!).Port;
  string text=$"OPTIONS sip:studio@127.0.0.1:{a.Sip.Port} SIP/2.0\r\nVia: SIP/2.0/UDP 127.0.0.1:{port};branch=z9hG4bK-options\r\nFrom: <sip:test@127.0.0.1>;tag=test\r\nTo: <sip:studio@127.0.0.1>\r\nCall-ID: options-test\r\nCSeq: 1 OPTIONS\r\nMax-Forwards: 70\r\nContent-Length: 0\r\n\r\n";
  await udp.SendAsync(Encoding.UTF8.GetBytes(text),new IPEndPoint(IPAddress.Loopback,a.Sip.Port));using var timeout=new CancellationTokenSource(5000);var response=await udp.ReceiveAsync(timeout.Token);string received=Encoding.UTF8.GetString(response.Buffer);Assert.Contains("SIP/2.0 200",received);Assert.Contains("INVITE, ACK, BYE, CANCEL, OPTIONS",received);
 }

 [Fact]public async Task ReinviteChangesCodecOnlyAfterAck()
 {
  var a=Options();var b=Options();await using var receiver=new SoftCodecHost(a,new TestAudio(),new MemorySecrets(),Log());await receiver.StartAsync();
  using var transport=new SIPTransport();transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback,b.Sip.Port)));using var ua=new SIPUserAgent(transport,null,false);await using var session=new CodecMediaSession(b,new TestAudio(),Log(),IPAddress.Loopback,IPAddress.Loopback);
  Assert.True(await ua.Call($"sip:studio@127.0.0.1:{a.Sip.Port}",null,null,session,8));await Until(()=>receiver.State==CallState.Connected);
  var invite=ua.Dialogue.GetInDialogRequest(SIPMethodsEnum.INVITE);invite.Header.ContentType="application/sdp";invite.Body=new SdpNegotiator().Build(IPAddress.Loopback,b.Rtp.Port,b.Rtp.RtcpPort,[new CodecFormat("PCMA",8,8000,8000,1,20)],123,2);
  var transaction=new UACInviteTransaction(transport,invite,null,true);var completion=new TaskCompletionSource<SIPResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
  transaction.UACInviteTransactionFinalResponseReceived+=(_,_,_,response)=>{completion.TrySetResult(response);return Task.FromResult(SocketError.Success);};transaction.SendInviteRequest();
  var response=await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));Assert.Equal(SIPResponseStatusCodesEnum.Ok,response.Status);Assert.Contains("OPUS",receiver.ActiveCall!.NegotiatedCodec);
  session.PrepareReinvite(SIPSorcery.Net.SDP.ParseSDPDescription(response.Body));transaction.AckAnswer(response,null,null);await session.CommitReinviteAsync();await Until(()=>receiver.ActiveCall?.NegotiatedCodec.StartsWith("PCMA")==true);await Until(()=>receiver.Metrics.PacketsReceived>3);
  ua.Hangup();await Until(()=>receiver.State==CallState.Idle);transport.Shutdown();
 }
 [Fact]public async Task LongerCallExchangesRtcpAndKeepsAudioFlowing()
 {
  var a=Options();var b=Options();await using var receiver=new SoftCodecHost(a,new TestAudio(),new MemorySecrets(),Log());await using var caller=new SoftCodecHost(b,new TestAudio(880),new MemorySecrets(),Log());await receiver.StartAsync();await caller.StartAsync();await caller.PlaceCallAsync(new Uri($"sip:studio@127.0.0.1:{a.Sip.Port}"));await Until(()=>receiver.State==CallState.Connected);
  int seconds=int.TryParse(Environment.GetEnvironmentVariable("SOFTCODEC_SOAK_SECONDS"),out int n)?n:12;
  var end=Environment.TickCount64+seconds*1000L;while(Environment.TickCount64<end){await Task.Delay(1000);Assert.Equal(CallState.Connected,receiver.State);Assert.Equal(CallState.Connected,caller.State);}
  Assert.True(receiver.Metrics.PacketsReceived>seconds*20);Assert.NotNull(receiver.Metrics.RttMs);Assert.NotNull(caller.Metrics.RttMs);Assert.InRange(receiver.Metrics.RttMs!.Value,0,2000);await caller.HangupAsync();
 }
 [Fact]public async Task ManualAnswerAndRejectDoNotOpenAudioEarly()
 {
  var a=Options() with{Sip=Options().Sip with{AutoAnswer=false}};var b=Options();var audio=new TestAudio();await using var receiver=new SoftCodecHost(a,audio,new MemorySecrets(),Log());await using var caller=new SoftCodecHost(b,new TestAudio(),new MemorySecrets(),Log());await receiver.StartAsync();await caller.StartAsync();var pending=caller.PlaceCallAsync(new Uri($"sip:studio@127.0.0.1:{a.Sip.Port}"));await Until(()=>receiver.State==CallState.Incoming);Assert.False(audio.Opened);await receiver.AnswerAsync();await pending;Assert.True(audio.Opened);await caller.HangupAsync();await Until(()=>receiver.State==CallState.Idle);
  pending=caller.PlaceCallAsync(new Uri($"sip:studio@127.0.0.1:{a.Sip.Port}"));await Until(()=>receiver.State==CallState.Incoming);await receiver.RejectAsync();await pending;Assert.False(audio.Opened);
 }
 [Fact]public void DigestNonceIsBoundToAddressAndReplayRejected()
 {
  var secrets=new MemorySecrets();secrets.Write("sip-digest","secret");var filter=new SecurityFilter(new(),secrets);var ip=IPAddress.Loopback;var challenge=filter.Challenge(ip);
  var request=SIPRequest.GetRequest(SIPMethodsEnum.INVITE,SIPURI.ParseSIPURI("sip:studio@127.0.0.1"));var d=new SIPAuthorisationDigest(SIPAuthorisationHeadersEnum.Authorize,DigestAlgorithmsEnum.MD5){Realm=challenge.SIPDigest.Realm,Nonce=challenge.SIPDigest.Nonce,Qop="auth"};d.SetCredentials("studio","secret",request.URI.ToString(),"INVITE");d.Cnonce="testcnonce";d.NonceCount=1;d.Response=d.GetDigest();request.Header.AuthenticationHeaders=[new SIPAuthenticationHeader(d)];
  Assert.Equal(Admission.Allow,filter.Check(request,ip));Assert.Equal(Admission.Challenge,filter.Check(request,ip));Assert.Equal(Admission.Challenge,filter.Check(request,IPAddress.Parse("127.0.0.2")));
 }
}
