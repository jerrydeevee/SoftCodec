using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Diagnostics;
using EventLog=SoftCodec.Core.EventLog;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoftCodec.Core;
using SoftCodec.Sip;
using SoftCodec.Windows;
namespace SoftCodec.App;
public partial class MainViewModel : ObservableObject,IAsyncDisposable
{
 readonly WasapiDeviceGraph devices=new();readonly WindowsSecrets secrets;readonly EventLog log;readonly JsonStore<SoftCodecOptions> configStore;readonly JsonStore<List<ReporterProfile>> profileStore;
 SoftCodecHost host;SoftCodecOptions options;readonly DispatcherTimer timer;readonly string directory;
 public SettingsViewModel Settings{get;}=new();public ObservableCollection<string> Logs{get;}=[];public ObservableCollection<ContactViewModel> Profiles{get;}=[];
 public IReadOnlyList<AudioDevice> RenderDevices{get;}public IReadOnlyList<AudioDevice> CaptureDevices{get;}
 [ObservableProperty] private string status="GESTOPT";
 [ObservableProperty] private string remoteName="Klaar voor een reportage";
 [ObservableProperty] private string codec="Geen actieve verbinding";
 [ObservableProperty] private string duration="00:00:00";
 [ObservableProperty] private string listener="";
 [ObservableProperty] private string alarm="Stel je audioapparaten, publieke IP-adres en beveiliging in.";
 [ObservableProperty] private string metrics="Nog geen media ontvangen";
 [ObservableProperty] private string target="sip:";
 [ObservableProperty] private string logFilter="";
 [ObservableProperty] private double remoteLeft=-96;
 [ObservableProperty] private double remoteRight=-96;
 [ObservableProperty] private double returnLeft=-96;
 [ObservableProperty] private double returnRight=-96;
 [ObservableProperty] private bool connected;
 [ObservableProperty] private double remoteLevel;
 [ObservableProperty] private double returnLevel;
 [ObservableProperty] private string remoteDb="−96 dBFS";
 [ObservableProperty] private string returnDb="−96 dBFS";
 [ObservableProperty] private bool running;
 [ObservableProperty] private bool muted;
 [ObservableProperty] private bool alwaysOnTop;
 [ObservableProperty] private ContactViewModel? selectedProfile;
 [ObservableProperty] private System.Windows.Media.PointCollection bufferPoints=[];
 readonly Queue<double> history=new();
 public string Version=>$"SoftCodec 0.1.0 · .NET {Environment.Version} · Windows x64";
 public MainViewModel()
 {
  directory=ChooseDirectory();secrets=new(Path.Combine(directory,"secrets"));log=new(Path.Combine(directory,"logs"));configStore=new(Path.Combine(directory,"config.json"));profileStore=new(Path.Combine(directory,"profiles.json"));
  options=configStore.Load(out var recovery);if(recovery is not null)Alarm=recovery;RenderDevices=devices.GetDevices(false);CaptureDevices=devices.GetDevices(true);Settings.Load(options,RenderDevices,CaptureDevices);
  foreach(var p in profileStore.Load(out _))Profiles.Add(ContactViewModel.From(p));host=CreateHost();log.Written+=OnLog;
  timer=new(TimeSpan.FromMilliseconds(100),DispatcherPriority.Background,(_,_)=>Refresh(),System.Windows.Application.Current.Dispatcher);timer.Start();
 }
 static string ChooseDirectory()
 {
  foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)})try{var d=Path.Combine(root,"DVC-it","SoftCodec");Directory.CreateDirectory(d);var p=Path.Combine(d,".write-test-"+Guid.NewGuid());File.WriteAllText(p,"");File.Delete(p);return d;}catch(UnauthorizedAccessException){}throw new IOException("Geen schrijfbare configuratiemap.");
 }
 SoftCodecHost CreateHost(){var h=new SoftCodecHost(options,devices,secrets,log,Profiles.Select(p=>p.Build()).ToArray());h.AlarmRaised+=a=>System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>Alarm=a.Message);return h;}
 void OnLog(string line)=>System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>{if(LogFilter.Length==0||line.Contains(LogFilter,StringComparison.OrdinalIgnoreCase)){Logs.Add(line);while(Logs.Count>1000)Logs.RemoveAt(0);}});
 partial void OnLogFilterChanged(string value){Logs.Clear();foreach(var line in log.Snapshot().Where(l=>l.Contains(value,StringComparison.OrdinalIgnoreCase)))Logs.Add(line);}
 partial void OnMutedChanged(bool value)=>host.Muted=value;
 void Refresh()
 {
  Status=host.State switch{CallState.Idle=>Running?"WACHTEN OP GESPREK":"GESTOPT",CallState.Incoming=>"INKOMENDE REPORTAGE",CallState.Outgoing=>"UITBELLEN",CallState.Establishing=>"VERBINDING OPBOUWEN",CallState.Connected=>"VERBONDEN",CallState.Disconnecting=>"AFSLUITEN",_=>"FOUT"};
  RemoteName=host.ActiveCall?.RemoteDisplay??"Klaar voor een reportage";Codec=host.ActiveCall?.NegotiatedCodec??"Geen actieve verbinding";Duration=host.ActiveCall is {ConnectedAt:var at}&&at!=DateTimeOffset.MinValue?(DateTimeOffset.Now-at).ToString(@"hh\:mm\:ss"):"00:00:00";
  var m=host.Metrics;RemoteLeft=m.LevelRemoteLeftDbfs;RemoteRight=m.LevelRemoteRightDbfs;ReturnLeft=m.LevelReturnLeftDbfs;ReturnRight=m.LevelReturnRightDbfs;Connected=host.State==CallState.Connected;RemoteLevel=Math.Clamp(m.LevelRemoteDbfs+60,0,60);ReturnLevel=Math.Clamp(m.LevelReturnDbfs+60,0,60);RemoteDb=$"{m.LevelRemoteDbfs:F1} dBFS";ReturnDb=$"{m.LevelReturnDbfs:F1} dBFS";
  Metrics=$"Buffer {m.BufferDepthMs:F0} / {m.TargetBufferMs:F0} ms    ·    Loss {m.PacketLossPercent:F2}%    ·    Jitter {m.JitterMs:F1} ms    ·    RTT {(m.RttMs is {} r?$"{r:F0} ms":"—")}\nRX {m.PacketsReceived}    TX {m.PacketsSent}    Late {m.PacketsLate}    ·    {m.BitrateDownKbps:F0} / {m.BitrateUpKbps:F0} kbit/s    ·    Drift {devices.DriftPpm:+0;-0;0} ppm";
  history.Enqueue(m.BufferDepthMs);while(history.Count>600)history.Dequeue();BufferPoints=new(history.Select((v,i)=>new System.Windows.Point(i*900d/600,90-Math.Min(90,v*.2))));
  Listener=$"sip:studio@{(options.Identity.AdvertisedIp.Length>0?options.Identity.AdvertisedIp:"lokaal adres")}  ·  UDP {options.Sip.Port} / {options.Rtp.Port} / {options.Rtp.RtcpPort}";
 }
 async Task Run(Func<Task> action){try{await action();}catch(Exception e){Alarm=e.Message;log.Write("ERR",e.Message);}}

 [RelayCommand] async Task DetectIp()=>await Run(async()=>{Settings.AdvertisedIp=(await SoftCodec.Rtp.StunClient.DiscoverAsync(Settings.StunServer)).ToString();Alarm="Publiek adres via STUN gevonden. Stel ook de portforwards in.";});
 [RelayCommand] async Task TestTone()=>await Run(async()=>{if(Running)throw new InvalidOperationException("Stop de ontvanger voor de audiotest.");try{devices.Open(Settings.Build().Audio,48000,2);var samples=new float[1920];for(int frame=0;frame<960;frame++)samples[frame*2]=samples[frame*2+1]=.12589254f*MathF.Sin(2*MathF.PI*1000*frame/48000);for(int i=0;i<100;i++){devices.Write(samples,2,48000);await Task.Delay(20);}Alarm="Testtoon voltooid: 1 kHz, −18 dBFS.";}finally{devices.Close();}});
 public async Task AutoStartAsync(){if(File.Exists(configStore.PathName))await Start();}
 [RelayCommand] async Task Start()=>await Run(async()=>{if(Running)return;await host.StartAsync();Running=true;Alarm="Ontvanger actief.";});
 [RelayCommand] async Task Stop()=>await Run(async()=>{await host.StopAsync();Running=false;});
 [RelayCommand] async Task Answer()=>await Run(host.AnswerAsync);
 [RelayCommand] async Task Reject()=>await Run(host.RejectAsync);
 [RelayCommand] async Task Hangup()=>await Run(async()=>{if(host.State!=CallState.Idle&&System.Windows.MessageBox.Show("De reportageverbinding verbreken?","Ophangen",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)await host.HangupAsync();});
 [RelayCommand] async Task PlaceCall()=>await Run(()=>host.PlaceCallAsync(new Uri(Target)));
 [RelayCommand] async Task CallProfile()=>await Run(()=>SelectedProfile is null?Task.CompletedTask:host.PlaceCallAsync(new Uri(SelectedProfile.SipUri),SelectedProfile.Build()));
 [RelayCommand] void AddProfile(){var p=new ContactViewModel{Name="Nieuwe verslaggever",SipUri="sip:"};Profiles.Add(p);SelectedProfile=p;}
 [RelayCommand] void RemoveProfile(){if(SelectedProfile is not null)Profiles.Remove(SelectedProfile);}
 public void SetPasswords(string digest,string register){if(Running)throw new InvalidOperationException("Stop eerst de ontvanger.");if(digest.Length>0)secrets.Write("sip-digest",digest);if(register.Length>0)secrets.Write("sip-register",register);}
 [RelayCommand] async Task Save()=>await Run(async()=>{if(Running)throw new InvalidOperationException("Stop de ontvanger voordat je instellingen opslaat.");var next=Settings.Build();next.Validate();var profiles=Profiles.Select(p=>p.Build()).ToList();foreach(var p in profiles){foreach(var cidr in p.AllowedCidrs)_=Ipv4Cidr.Parse(cidr);if(p.SipUri is not null&&p.SipUri.Length>0&&(!Uri.TryCreate(p.SipUri,UriKind.Absolute,out var uri)||uri.Scheme!="sip"))throw new ArgumentException("Ongeldige SIP-URI in profiel "+p.Name);}configStore.Save(next);profileStore.Save(profiles);await host.StopAsync();options=next;host.ApplyConfig(next);host.SetProfiles(profiles);Alarm="Instellingen opgeslagen.";});
 [RelayCommand] void OpenFolder()=>Process.Start(new ProcessStartInfo(directory){UseShellExecute=true});
 [RelayCommand] void Diagnostics()
 {
  try{var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"SoftCodec-diagnose-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip");using var zip=ZipFile.Open(path,ZipArchiveMode.Create);var info=zip.CreateEntry("systeem.txt");using(var writer=new StreamWriter(info.Open()))writer.WriteLine(Version+Environment.NewLine+Environment.OSVersion);var config=zip.CreateEntry("config.json");using(var writer=new StreamWriter(config.Open()))writer.Write(System.Text.Json.JsonSerializer.Serialize(options,JsonStore<SoftCodecOptions>.Json));var entry=zip.CreateEntry("recent.log");using(var writer=new StreamWriter(entry.Open()))foreach(var line in log.Snapshot())writer.WriteLine(line);Alarm="Diagnose opgeslagen: "+path;}catch(Exception e){Alarm=e.Message;}
 }
 public async ValueTask DisposeAsync(){timer.Stop();log.Written-=OnLog;await host.DisposeAsync();}
}
public partial class ContactViewModel : ObservableObject
{
 public Guid Id{get;set;}=Guid.NewGuid();
 [ObservableProperty]private string name="";
 [ObservableProperty]private string sipUri="";
 [ObservableProperty]private string allowedCidrs="";
 [ObservableProperty]private string expectedFrom="";
 [ObservableProperty]private string codecPriority="opus,g722,pcma,pcmu";
 [ObservableProperty]private string notes="";
 [ObservableProperty]private bool autoAnswer=true;
 public ReporterProfile Build()=>new(){Id=Id,Name=Name,SipUri=SipUri,AllowedCidrs=AllowedCidrs.Split([',','\n','\r'],StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries),ExpectedFromContains=ExpectedFrom,CodecPriority=CodecPriority.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries),AutoAnswer=AutoAnswer,Notes=Notes};
 public static ContactViewModel From(ReporterProfile p)=>new(){Id=p.Id,Name=p.Name,SipUri=p.SipUri??"",AllowedCidrs=string.Join(",",p.AllowedCidrs),ExpectedFrom=p.ExpectedFromContains??"",CodecPriority=string.Join(",",p.CodecPriority),AutoAnswer=p.AutoAnswer,Notes=p.Notes};
}
