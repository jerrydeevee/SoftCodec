using System.Net;
using System.Net.Sockets;
using SoftCodec.Core;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
namespace SoftCodec.Sip;
public sealed class SoftCodecHost : ISoftCodecHost
{
 SoftCodecOptions options;readonly IAudioDevices devices;readonly ISecretStore secrets;readonly EventLog log;readonly SemaphoreSlim commands=new(1,1);
 SIPTransport? transport;SIPUserAgent? agent;SIPServerUserAgent? incoming;CodecMediaSession? media;SIPRegistrationUserAgent? registrar;SecurityFilter? security;
 IReadOnlyList<ReporterProfile> profiles;CancellationTokenSource? lifetime;Task? keepalive;int incomingPending;
 public CallState State{get;private set;}=CallState.Idle;public CallInfo? ActiveCall{get;private set;}
 public MediaMetrics Metrics=>media?.Engine.Metrics??new();
 public bool Muted{get=>media?.Engine.Muted??false;set{if(media is not null)media.Engine.Muted=value;}}
 public event Action<CallState,CallInfo?>? StateChanged;public event Action<Alarm>? AlarmRaised;
 public SoftCodecHost(SoftCodecOptions options,IAudioDevices devices,ISecretStore secrets,EventLog log,IReadOnlyList<ReporterProfile>? profiles=null)
 {this.options=options;this.devices=devices;this.secrets=secrets;this.log=log;this.profiles=profiles??[];devices.Warning+=DeviceWarning;}
 void DeviceWarning(string message){log.Write("WRN",message);AlarmRaised?.Invoke(new("AUDIO",message,DateTimeOffset.Now));}
 public async Task StartAsync(CancellationToken ct=default)
 {
  await commands.WaitAsync(ct);try{if(transport is not null)return;options.Validate();
   if(options.Security.Digest.Enabled&&string.IsNullOrEmpty(secrets.Read(options.Security.Digest.PasswordSecretRef)))throw new InvalidOperationException("Stel eerst een digest-wachtwoord in bij Beveiliging.");
   security=new(options.Security,secrets);transport=new SIPTransport();transport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Parse(options.Identity.BindAddress),options.Sip.Port)));
   if(!string.IsNullOrWhiteSpace(options.Identity.AdvertisedIp))transport.ContactHost=options.Identity.AdvertisedIp+":"+options.Sip.Port;
   transport.SIPTransportRequestReceived+=OnRequest;
   agent=new SIPUserAgent(transport,null,false);agent.OnIncomingCall+=OnIncoming;agent.OnReinviteRequest+=OnReinvite;
   agent.OnCallHungup+=_=>QueueCleanup("Remote heeft opgehangen.");agent.ServerCallCancelled+=(_,_)=>QueueCleanup("Gesprek geannuleerd.");agent.ServerCallRingTimeout+=_=>QueueCleanup("Geen antwoord.");agent.ClientCallFailed+=(_,reason,_)=>log.Write("WRN","Uitbellen: "+reason);
   lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);keepalive=Task.Run(()=>Keepalive(lifetime.Token));
   var reg=options.Sip.Register;if(reg.Enabled){var password=secrets.Read(reg.PasswordSecretRef)??throw new InvalidOperationException("Registrarwachtwoord ontbreekt.");registrar=new(transport,reg.User,password,reg.Registrar,reg.ExpirySeconds);if(reg.OutboundProxy.Length>0)registrar.OutboundProxy=SIPEndPoint.ParseSIPEndPoint(reg.OutboundProxy);registrar.RegistrationSuccessful+=(_,_)=>log.Write("INF","SIP-registratie actief.");registrar.RegistrationFailed+=(_,_,message)=>DeviceWarning("Registratie mislukt: "+message);registrar.Start();}
   log.Write("INF",$"SIP luistert op UDP {options.Sip.Port}; RTP {options.Rtp.Port}/{options.Rtp.RtcpPort}.");Change(CallState.Idle);
  }catch{transport?.Shutdown();transport=null;agent?.Dispose();agent=null;throw;}finally{commands.Release();}
 }
 async Task OnRequest(SIPEndPoint local,SIPEndPoint remote,SIPRequest request)
 {
  // In-dialog requests are validated and handled by the SIP library.
  if(request.Header.To?.ToTag is not null)return;
  if(request.Method==SIPMethodsEnum.INVITE && State!=CallState.Idle){await transport!.SendResponseAsync(SIPResponse.GetResponse(request,SIPResponseStatusCodesEnum.BusyHere,null));return;}
  if(request.Method==SIPMethodsEnum.OPTIONS){if(options.Security.AllowlistCidrs.Length>0&&!options.Security.AllowlistCidrs.Any(c=>Ipv4Cidr.Parse(c).Contains(remote.Address)))return;var ok=SIPResponse.GetResponse(request,SIPResponseStatusCodesEnum.Ok,null);ok.Header.Allow="INVITE, ACK, BYE, CANCEL, OPTIONS";await transport!.SendResponseAsync(ok);}
  else if(request.Method is not (SIPMethodsEnum.INVITE or SIPMethodsEnum.ACK or SIPMethodsEnum.CANCEL)){var response=SIPResponse.GetResponse(request,SIPResponseStatusCodesEnum.MethodNotAllowed,null);await transport!.SendResponseAsync(response);}
 }
 void OnIncoming(SIPUserAgent userAgent,SIPRequest request)=>_=HandleIncoming(request);
 async Task HandleIncoming(SIPRequest request)
 {
  if(transport is null)return;var address=request.RemoteSIPEndPoint.Address;var admit=security!.Check(request,address);if(admit==Admission.Drop)return;
  if(admit==Admission.Challenge){var response=SIPResponse.GetResponse(request,SIPResponseStatusCodesEnum.Unauthorised,null);response.Header.AuthenticationHeaders=[security.Challenge(address)];await transport.SendResponseAsync(response);return;}
  if(Interlocked.CompareExchange(ref incomingPending,1,0)!=0 || State!=CallState.Idle){await transport.SendResponseAsync(SIPResponse.GetResponse(request,SIPResponseStatusCodesEnum.BusyHere,null));return;}
  bool autoAnswer=options.Sip.AutoAnswer;try{await commands.WaitAsync();try{
   var profile=profiles.FirstOrDefault(p=>p.AllowedCidrs.Any(c=>Ipv4Cidr.Parse(c).Contains(address)))??profiles.FirstOrDefault(p=>!string.IsNullOrEmpty(p.ExpectedFromContains)&&request.Header.From.ToString().Contains(p.ExpectedFromContains,StringComparison.OrdinalIgnoreCase));
   var effective=WithProfile(options,profile);autoAnswer=effective.Sip.AutoAnswer;var parsed=new SdpNegotiator().Parse(request.Body);var chosen=new SdpNegotiator().Choose(parsed,effective.Codecs);if(chosen is null){await transport.SendResponseAsync(SIPResponse.GetResponse(request,SIPResponseStatusCodesEnum.NotAcceptableHere,null));return;}
   media=NewMedia(effective,address);incoming=agent!.AcceptCall(request);ActiveCall=new(request.Header.CallId,profile?.Name??request.Header.From.FromName??address.ToString(),request.RemoteSIPEndPoint.ToString(),chosen.Description,DateTimeOffset.MinValue,TimeSpan.Zero);Change(CallState.Incoming);
  }finally{commands.Release();}
  if(autoAnswer)await AnswerAsync();
  }catch(Exception e){DeviceWarning("Inkomend gesprek: "+e.Message);await HangupAsync();}finally{Interlocked.Exchange(ref incomingPending,0);}
 }
 CodecMediaSession NewMedia(SoftCodecOptions config,IPAddress? peer)
 {
  IPAddress advertised;if(!IPAddress.TryParse(config.Identity.AdvertisedIp,out advertised!)){using var route=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);route.Connect(peer??IPAddress.Parse("1.1.1.1"),5060);advertised=((IPEndPoint)route.LocalEndPoint!).Address;log.Write("WRN","Geen publiek adres ingesteld; SDP gebruikt routeadres "+advertised);}
  var session=new CodecMediaSession(config,devices,log,advertised,peer);session.AlarmRaised+=alarm=>AlarmRaised?.Invoke(alarm);session.Faulted+=message=>{AlarmRaised?.Invoke(new("MEDIA",message,DateTimeOffset.Now));QueueCleanup(message);};return session;
 }

 void OnReinvite(UASInviteTransaction transaction)
 {
  var current=media;if(current is null)return;
  try{
   var answer=current.PrepareReinvite(SIPSorcery.Net.SDP.ParseSDPDescription(transaction.TransactionRequest.Body));
   transaction.OnAckReceived+=async(_,_,_,_)=>{try{await current.CommitReinviteAsync();if(ActiveCall is not null&&media==current){ActiveCall=ActiveCall with{NegotiatedCodec=current.Negotiated!.Description};StateChanged?.Invoke(State,ActiveCall);}}catch(Exception e){DeviceWarning("Heronderhandeling: "+e.Message);QueueCleanup("Heronderhandeling mislukt.");}return SocketError.Success;};
   transaction.UASInviteTransactionFailed+=(_,_)=>current.CancelReinvite();
   transaction.SendFinalResponse(transaction.GetOkResponse("application/sdp",answer.ToString()));
  }catch(Exception e)when(e is FormatException or ArgumentException or InvalidOperationException){current.CancelReinvite();transaction.SendFinalResponse(SIPResponse.GetResponse(transaction.TransactionRequest,SIPResponseStatusCodesEnum.NotAcceptableHere,null));}
 }
 public async Task AnswerAsync()
 {
  // Do not hold command lock while waiting for the remote ACK.
  SIPServerUserAgent uas;CodecMediaSession session;await commands.WaitAsync();try{if(State!=CallState.Incoming||incoming is null||media is null)return;uas=incoming;session=media;Change(CallState.Establishing);}finally{commands.Release();}
  try{bool answered=await agent!.Answer(uas,session);await commands.WaitAsync();try{if(media!=session)return;if(answered){ActiveCall=ActiveCall! with{ConnectedAt=DateTimeOffset.Now,NegotiatedCodec=session.Negotiated!.Description};Change(CallState.Connected);}else await CleanupLocked("Geen ACK of gesprek geweigerd.");}finally{commands.Release();}}
  catch(Exception e){DeviceWarning(e.Message);await HangupAsync();}
 }
 public async Task RejectAsync(){await commands.WaitAsync();try{incoming?.Reject(SIPResponseStatusCodesEnum.BusyHere,"Operator declined");await CleanupLocked("Gesprek geweigerd.");}finally{commands.Release();}}
 public async Task PlaceCallAsync(Uri target,ReporterProfile? profile=null)
 {
  if(target.Scheme!="sip")throw new ArgumentException("Gebruik een sip:-URI.");CodecMediaSession session;await commands.WaitAsync();try{if(agent is null||State!=CallState.Idle)throw new InvalidOperationException("Start de ontvanger en beëindig het huidige gesprek.");session=media=NewMedia(WithProfile(options,profile),null);ActiveCall=new(Guid.NewGuid().ToString(),profile?.Name??target.ToString(),target.ToString(),"Onderhandelen",DateTimeOffset.MinValue,TimeSpan.Zero);Change(CallState.Outgoing);}finally{commands.Release();}
  try{var reg=options.Sip.Register;bool ok=await agent!.Call(target.ToString(),reg.Enabled?reg.User:null,reg.Enabled?secrets.Read(reg.PasswordSecretRef):null,session,30);await commands.WaitAsync();try{if(media!=session)return;if(ok){ActiveCall=ActiveCall! with{ConnectedAt=DateTimeOffset.Now,NegotiatedCodec=session.Negotiated!.Description};Change(CallState.Connected);}else await CleanupLocked("Uitgaand gesprek mislukt.");}finally{commands.Release();}}
  catch{await HangupAsync();throw;}
 }
 public async Task HangupAsync(){await commands.WaitAsync();try{if(agent?.IsCalling==true)agent.Cancel();else if(agent?.IsCallActive==true)agent.Hangup();else if(incoming is {IsUASAnswered:false})incoming.Reject(SIPResponseStatusCodesEnum.BusyHere,"Stopped");await CleanupLocked("Gesprek beëindigd.");}finally{commands.Release();}}
 void QueueCleanup(string reason)=>_=Task.Run(async()=>{await commands.WaitAsync();try{await CleanupLocked(reason);}finally{commands.Release();}});
 async Task CleanupLocked(string reason){var previous=media;media=null;incoming=null;if(previous is not null){Change(CallState.Disconnecting);try{await previous.DisposeAsync();}catch(Exception e){log.Write("ERR","Afsluiten media: "+e.Message);}}ActiveCall=null;Change(CallState.Idle);log.Write("INF",reason);}
 public async Task StopAsync(){await HangupAsync();await commands.WaitAsync();try{lifetime?.Cancel();if(keepalive is not null)await keepalive;registrar?.Stop();registrar=null;agent?.Dispose();agent=null;transport?.Shutdown();transport?.Dispose();transport=null;lifetime?.Dispose();lifetime=null;log.Write("INF","Ontvanger gestopt.");}finally{commands.Release();}}
 async Task Keepalive(CancellationToken ct){try{using var timer=new PeriodicTimer(TimeSpan.FromSeconds(20));while(await timer.WaitForNextTickAsync(ct)){var destination=agent?.Dialogue?.RemoteTarget;if(destination is not null && transport is not null){var request=SIPRequest.GetRequest(SIPMethodsEnum.OPTIONS,destination);await transport.SendRequestAsync(request);}}}catch(OperationCanceledException){}catch(Exception e){log.Write("WRN","Keepalive: "+e.Message);}}
 void Change(CallState state){State=state;log.Write("INF","Status: "+state);StateChanged?.Invoke(state,ActiveCall);}
 public void SetProfiles(IReadOnlyList<ReporterProfile> value){if(transport is not null)throw new InvalidOperationException("Stop eerst de ontvanger.");profiles=value;}
 public void ApplyConfig(SoftCodecOptions config){if(transport is not null)throw new InvalidOperationException("Stop de ontvanger voordat je instellingen wijzigt.");config.Validate();options=config;}
 static SoftCodecOptions WithProfile(SoftCodecOptions options,ReporterProfile? p)=>p is null?options:options with{Jitter=p.JitterOverride??options.Jitter,Sip=options.Sip with{AutoAnswer=p.AutoAnswer},Codecs=options.Codecs with{Priority=p.CodecPriority.Length>0?p.CodecPriority:options.Codecs.Priority,Opus=p.OpusOverride??options.Codecs.Opus}};
 public async ValueTask DisposeAsync(){await StopAsync();devices.Warning-=DeviceWarning;devices.Dispose();commands.Dispose();}
}
