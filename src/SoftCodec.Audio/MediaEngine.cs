using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using SoftCodec.Core;
using SoftCodec.Rtp;
namespace SoftCodec.Audio;
public sealed class MediaEngine : IAsyncDisposable
{
 readonly SoftCodecOptions options;readonly IAudioDevices devices;readonly EventLog log;readonly Socket rtp,rtcp;readonly CancellationTokenSource stop=new();
 Task[] tasks=[];CodecFormat? format;IAudioCodec? codec;AdaptiveJitterBuffer? jitter;RtpReceptionStatistics? stats;
 IPEndPoint? remote,remoteControl;IPAddress? signalingAddress;uint? remoteSsrc;uint localSsrc=RandomUInt(),timestamp=RandomUInt();ushort sequence=(ushort)RandomUInt();
 long bytesRx,bytesTx,packetsTx,lastMedia,lastRtcp,startedAt,silenceAt;uint lastSr;long lastSrAt;double? rtt;double rxLevel=-96,txLevel=-96;int disposed;
 WavRecorder? recording,returnRecording;StreamingResampler? rxResampler,txResampler;
 public event Action<string>? Faulted;public event Action<Alarm>? AlarmRaised;
 public bool IsStarted=>tasks.Length!=0;
 public bool Muted {get;set;}
 public MediaEngine(SoftCodecOptions options,IAudioDevices devices,EventLog log)
 {
  this.options=options;this.devices=devices;this.log=log;
  rtp=Bind(options.Rtp.Port);try{rtcp=Bind(options.Rtp.RtcpPort);}catch{rtp.Dispose();throw;}
 }
 Socket Bind(int port)
 {
  var socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);try{socket.Bind(new IPEndPoint(IPAddress.Parse(options.Identity.BindAddress),port));socket.ReceiveBufferSize=1<<20;try{socket.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.TypeOfService,options.Rtp.Dscp<<2);}catch(SocketException e){log.Write("WRN","DSCP niet toegepast: "+e.SocketErrorCode);}return socket;}catch{socket.Dispose();throw;}
 }
 static uint RandomUInt()=>System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(RandomNumberGenerator.GetBytes(4));
 public void Configure(CodecFormat negotiated,IPEndPoint destination,IPEndPoint control,IPAddress? peer)
 {
  if(IsStarted)throw new InvalidOperationException("Codecwijziging tijdens gesprek vereist een nieuwe mediaketen.");
  format=negotiated;remote=destination;remoteControl=control;signalingAddress=peer;
 }
 public Task StartAsync()
 {
  if(IsStarted)return Task.CompletedTask;if(format is null)throw new InvalidOperationException("Media niet onderhandeld.");
  codec=AudioCodecFactory.Create(format,options.Codecs.Opus);jitter=new(options.Jitter,format.PtimeMs,format.ClockRate);stats=new(format.ClockRate);
  devices.Open(options.Audio,48000,format.Channels);
  rxResampler=new(format.Channels,format.SampleRate,48000);txResampler=new(format.Channels,48000,format.SampleRate);
  if(options.Recording.Enabled){string name=DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6];recording=new(Path.Combine(options.Recording.Directory,name+"-remote.wav"),format.Channels,48000);if(options.Recording.RecordReturn)returnRecording=new(Path.Combine(options.Recording.Directory,name+"-retour.wav"),format.Channels,48000);}
  startedAt=lastMedia=Environment.TickCount64;silenceAt=startedAt;
  tasks=[Task.Run(ReceiveRtp),Task.Run(ReceiveRtcp),Task.Factory.StartNew(MediaLoop,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default)];return Task.CompletedTask;
 }
 async Task ReceiveRtp()
 {
  var buffer=new byte[RtpPacket.MaxPacketSize];EndPoint any=new IPEndPoint(IPAddress.Any,0);
  try{while(!stop.IsCancellationRequested){var received=await rtp.ReceiveFromAsync(buffer,SocketFlags.None,any,stop.Token);var source=(IPEndPoint)received.RemoteEndPoint;
   if(!RtpPacket.TryParse(buffer.AsSpan(0,received.ReceivedBytes),out var h,out int offset,out int length)||h.PayloadType!=format!.PayloadType)continue;
   if(!source.Address.Equals(remote!.Address)&&!source.Address.Equals(signalingAddress))continue;
   if(remoteSsrc.HasValue && (h.Ssrc!=remoteSsrc.Value || (options.Rtp.SymmetricRtp&&!source.Equals(remote))))continue;
   if(!ValidPayload(length))continue;
   if(!remoteSsrc.HasValue){remoteSsrc=h.Ssrc;if(options.Rtp.SymmetricRtp){remote=source;log.Write("INF","Symmetric RTP bevestigd: "+source);}}
   long now=Environment.TickCount64;Interlocked.Exchange(ref lastMedia,now);Interlocked.Add(ref bytesRx,length+12);
   lock(stats!)if(!stats.Add(h,Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency))continue;
   jitter!.Insert(h,buffer.AsSpan(offset,length),now);
  }}catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(Exception e){Fail("RTP ontvangen: "+e.Message);}
 }
 bool ValidPayload(int length)=>format!.Name switch{"L16"=>length==format.FrameSamples*format.Channels*2,"L24"=>length==format.FrameSamples*format.Channels*3,"PCMA" or "PCMU"=>length==format.FrameSamples,"G722"=>length==format.FrameSamples/2,"OPUS"=>length is >0 and <=1275,_=>false};
 async Task ReceiveRtcp()
 {
  var buffer=new byte[4096];EndPoint any=new IPEndPoint(IPAddress.Any,0);
  try{while(!stop.IsCancellationRequested){var result=await rtcp.ReceiveFromAsync(buffer,SocketFlags.None,any,stop.Token);var source=(IPEndPoint)result.RemoteEndPoint;if(!source.Address.Equals(remote!.Address))continue;
   if(Rtcp.TryReadReport(buffer.AsSpan(0,result.ReceivedBytes),localSsrc,out uint lsr,out uint dlsr,out uint senderSr)){if(senderSr!=0){lastSr=senderSr;lastSrAt=Environment.TickCount64;remoteControl=source;}if(lsr!=0){uint delta=unchecked(Rtcp.CompactNtp(DateTimeOffset.UtcNow)-lsr-dlsr);double ms=delta*1000d/65536;if(ms<60000)rtt=ms;}}
  }}catch(OperationCanceledException){}catch(ObjectDisposedException){}catch(Exception e){Fail("RTCP: "+e.Message);}
 }
 void MediaLoop()
 {
  var f=format!;var packet=new byte[RtpPacket.MaxPacketSize];var encoded=new byte[RtpPacket.MaxPacketSize];var decoded=new float[12000];var render=new float[24000];var capture=new float[12000];var txPcm=new float[12000];var captureRing=new PcmRing(48000*4);var report=new byte[512];
  long due=Stopwatch.GetTimestamp();long frameTicks=Stopwatch.Frequency*f.PtimeMs/1000;int deviceSamples=48000*f.PtimeMs/1000*f.Channels;
  try{while(!stop.IsCancellationRequested){long remaining=due-Stopwatch.GetTimestamp();if(remaining>0){stop.Token.WaitHandle.WaitOne(Math.Max(1,(int)(remaining*1000/Stopwatch.Frequency)));continue;}due+=frameTicks;
   // Avoid an unbounded burst of stale media after suspend or a scheduling stall.
   if(Stopwatch.GetTimestamp()-due>frameTicks*3)due=Stopwatch.GetTimestamp();
   long now=Environment.TickCount64;var status=jitter!.TakeDue(now,packet,out int packetBytes);
   if(status!=PacketStatus.Waiting){bool fec=status==PacketStatus.RecoverFec&&f.Name=="OPUS"&&options.Codecs.Opus.UseInBandFec;int n=codec!.Decode(packet.AsSpan(0,status==PacketStatus.RecoverFec&&!fec?0:packetBytes),decoded,fec);int output=rxResampler!.Process(decoded.AsSpan(0,n),render);rxLevel=AudioLevel.Dbfs(render.AsSpan(0,output));devices.Write(render.AsSpan(0,output),f.Channels,48000);recording?.Write(render.AsSpan(0,output));}
   var samples=capture.AsSpan(0,deviceSamples);devices.Read(samples,f.Channels,48000);if(Muted)samples.Clear();txLevel=AudioLevel.Dbfs(samples);returnRecording?.Write(samples);
   int converted=txResampler!.Process(samples,txPcm);captureRing.Write(txPcm.AsSpan(0,converted));
   int needed=f.FrameSamples*f.Channels;if(captureRing.Count>=needed){captureRing.Read(txPcm.AsSpan(0,needed));int count=codec!.Encode(txPcm.AsSpan(0,needed),encoded);var h=new RtpHeader(sequence++,timestamp,localSsrc,(byte)f.PayloadType,packetsTx==0);int size=RtpPacket.Write(packet,h,encoded.AsSpan(0,count));rtp.SendTo(packet.AsSpan(0,size),SocketFlags.None,remote!);timestamp=unchecked(timestamp+(uint)f.TimestampStep);Interlocked.Increment(ref packetsTx);Interlocked.Add(ref bytesTx,count);}
   if(now-lastRtcp>5000){SendReport(report,now);lastRtcp=now;}
   if(now-lastMedia>options.Sip.MediaTimeoutSeconds*1000L){Fail("Geen RTP-media binnen de ingestelde timeout.");break;}
   if(options.Sip.MaxCallSeconds>0&&now-startedAt>options.Sip.MaxCallSeconds*1000L){Fail("Maximale gespreksduur bereikt.");break;}
   if(rxLevel>options.Alarms.SilenceThresholdDbFs)silenceAt=now;else if(now-silenceAt>options.Alarms.SilenceSeconds*1000L){log.Write("WRN","Stiltealarm: remote onder drempel.");AlarmRaised?.Invoke(new("SILENCE","Stiltealarm: remote onder drempel.",DateTimeOffset.Now));silenceAt=now;}
   if(now%1000<f.PtimeMs)jitter.Adapt(stats!.JitterMs,status is PacketStatus.Missing or PacketStatus.RecoverFec,now);
  }}catch(Exception e){if(!stop.IsCancellationRequested)Fail("Audioketen: "+e.Message);}
 }
 void SendReport(Span<byte> buffer,long now)
 {
  RtcpReport? rr=null;if(remoteSsrc.HasValue){lock(stats!){rr=new(remoteSsrc.Value,(byte)Math.Min(255,stats.Expected==0?0:stats.Lost*256/stats.Expected),(int)stats.Lost,stats.ExtendedHighestSequence,stats.JitterTimestampUnits,lastSr,lastSr==0?0:(uint)((now-lastSrAt)*65536/1000));}}
  int n=Rtcp.WriteReport(buffer,localSsrc,timestamp,(uint)packetsTx,(uint)bytesTx,rr,DateTimeOffset.UtcNow);n+=Rtcp.WriteSdes(buffer[n..],localSsrc,"softcodec@studio");rtcp.SendTo(buffer[..n],SocketFlags.None,remoteControl!);
 }
 void Fail(string message){log.Write("ERR",message);stop.Cancel();Faulted?.Invoke(message);}
 public MediaMetrics Metrics
 {
  get{if(stats is null)return new();lock(stats){double seconds=Math.Max(.1,(Environment.TickCount64-startedAt)/1000d);return new(){PacketsReceived=stats.Received,PacketsLost=stats.Lost,PacketsSent=Interlocked.Read(ref packetsTx),PacketLossPercent=stats.Expected==0?0:stats.Lost*100d/stats.Expected,JitterMs=stats.JitterMs,BufferDepthMs=jitter!.DepthMs,TargetBufferMs=jitter.TargetMs,PacketsLate=jitter.Late,PacketsDiscarded=jitter.Discarded,BitrateDownKbps=bytesRx*8/seconds/1000,BitrateUpKbps=bytesTx*8/seconds/1000,RttMs=rtt,LevelRemoteDbfs=rxLevel,LevelReturnDbfs=txLevel,EstimatedOneWayMs=jitter.TargetMs+format!.PtimeMs+options.Audio.RequestedLatencyMs+(rtt??0)/2};}}
 }
 public async ValueTask DisposeAsync()
 {
  if(Interlocked.Exchange(ref disposed,1)!=0)return;stop.Cancel();rtp.Dispose();rtcp.Dispose();await Task.WhenAll(tasks);devices.Close();codec?.Dispose();rxResampler?.Dispose();txResampler?.Dispose();if(recording is not null)await recording.DisposeAsync();if(returnRecording is not null)await returnRecording.DisposeAsync();stop.Dispose();
 }
}
