using System.Net;
using SoftCodec.Audio;
using SoftCodec.Core;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
namespace SoftCodec.Sip;
public sealed class CodecMediaSession : IMediaSession,IAsyncDisposable
{
 readonly SdpNegotiator negotiator=new();readonly IAudioDevices devices;readonly EventLog log;readonly SemaphoreSlim renegotiation=new(1,1);SdpAudio? pendingRemote;CodecFormat? pendingCodec;SDP? pendingDescription;readonly SoftCodecOptions options;readonly IPAddress advertised;readonly IPAddress? signalingAddress;readonly ulong id=(ulong)DateTime.UtcNow.Ticks;
 ulong version;SdpAudio? remote;CodecFormat? chosen;IReadOnlyList<CodecFormat>? offered;public MediaEngine Engine{get;private set;}
 public event Action<string>? Faulted;public event Action<Alarm>? AlarmRaised;
 public CodecFormat? Negotiated=>chosen;
 public CodecMediaSession(SoftCodecOptions options,IAudioDevices devices,EventLog log,IPAddress advertised,IPAddress? peer)
 {this.options=options;this.devices=devices;this.log=log;this.advertised=advertised;signalingAddress=peer;Engine=CreateEngine();}
 public bool HasText=>false;public bool HasAudio=>true;public bool HasVideo=>false;public bool IsClosed{get;private set;}
 public SDP RemoteDescription{get;private set;}=null!;public IPAddress RtpBindAddress=>IPAddress.Parse(options.Identity.BindAddress);
 public event Action<string>? OnRtpClosed;
 public event Action<IPEndPoint,RTPEvent,RTPHeader>? OnRtpEvent {add{}remove{}}
 public event Action<SDPMediaTypesEnum>? OnTimeout;
 public SDP CreateOffer(IPAddress? connectionAddress=null){offered=negotiator.CreateFormats(options.Codecs);return SDP.ParseSDPDescription(negotiator.Build(advertised,options.Rtp.Port,options.Rtp.RtcpPort,offered,id,++version));}
 public SetDescriptionResultEnum SetRemoteDescription(SdpType type,SDP description)
 {
  try{var parsed=negotiator.Parse(description.ToString());var candidate=negotiator.Choose(parsed,options.Codecs);if(candidate is null)return SetDescriptionResultEnum.AudioIncompatible;
   if(type==SdpType.answer && (offered is null||!offered.Any(f=>f.PayloadType==candidate.PayloadType&&f.Name==candidate.Name&&f.ClockRate==candidate.ClockRate&&f.Channels==candidate.Channels)))return SetDescriptionResultEnum.AudioIncompatible;
   // Re-INVITE currently accepts refreshes without changing the live media contract.
   if(Engine.IsStarted){if(chosen==candidate && remote?.Address.Equals(parsed.Address)==true && remote.Port==parsed.Port)return SetDescriptionResultEnum.OK;throw new NotSupportedException("Mediawijziging vereist bevestiging met ACK.");}
   remote=parsed;chosen=candidate;RemoteDescription=description;Engine.Configure(chosen,new(remote.Address,remote.Port),new(remote.Address,remote.RtcpPort),signalingAddress);return SetDescriptionResultEnum.OK;
  }catch(Exception e)when(e is FormatException or ArgumentException or OverflowException){return SetDescriptionResultEnum.AudioIncompatible;}
 }
 public SDP CreateAnswer(IPAddress? connectionAddress=null)=>SDP.ParseSDPDescription(negotiator.Build(advertised,options.Rtp.Port,options.Rtp.RtcpPort,[chosen??throw new InvalidOperationException("Geen codec onderhandeld.")],id,++version));

 MediaEngine CreateEngine(){var engine=new MediaEngine(options,devices,log);engine.AlarmRaised+=alarm=>AlarmRaised?.Invoke(alarm);engine.Faulted+=message=>{OnTimeout?.Invoke(SDPMediaTypesEnum.audio);Faulted?.Invoke(message);};return engine;}
 public SDP PrepareReinvite(SDP description)
 {
  if(pendingRemote is not null)throw new InvalidOperationException("Heronderhandeling is al actief.");
  var parsed=negotiator.Parse(description.ToString());var candidate=negotiator.Choose(parsed,options.Codecs)??throw new FormatException("Geen gemeenschappelijke codec.");
  pendingRemote=parsed;pendingCodec=candidate;pendingDescription=description;
  return SDP.ParseSDPDescription(negotiator.Build(advertised,options.Rtp.Port,options.Rtp.RtcpPort,[candidate],id,++version));
 }
 public void CancelReinvite(){pendingRemote=null;pendingCodec=null;pendingDescription=null;}
 public async Task CommitReinviteAsync()
 {
  await renegotiation.WaitAsync();try{if(pendingRemote is null||pendingCodec is null||IsClosed)return;
   var parsed=pendingRemote;var candidate=pendingCodec;var description=pendingDescription!;CancelReinvite();bool mute=Engine.Muted;
   await Engine.DisposeAsync();Engine=CreateEngine();Engine.Muted=mute;remote=parsed;chosen=candidate;RemoteDescription=description;
   Engine.Configure(candidate,new(parsed.Address,parsed.Port),new(parsed.Address,parsed.RtcpPort),signalingAddress);await Engine.StartAsync();
   log.Write("INF","Heronderhandeling toegepast na ACK: "+candidate.Description);
  }finally{renegotiation.Release();}
 }
 public Task Start()=>Engine.StartAsync();
 public void SetMediaStreamStatus(SDPMediaTypesEnum kind,MediaStreamStatusEnum status){if(status!=MediaStreamStatusEnum.SendRecv)throw new NotSupportedException("Alleen sendrecv is ondersteund.");}
 public Task SendDtmf(byte key,CancellationToken ct)=>Task.FromException(new NotSupportedException("DTMF staat buiten het audioprofiel."));
 public void Close(string reason){if(IsClosed)return;IsClosed=true;OnRtpClosed?.Invoke(reason);}
 public async ValueTask DisposeAsync(){Close("disposed");await renegotiation.WaitAsync();try{await Engine.DisposeAsync();}finally{renegotiation.Release();}}
}
