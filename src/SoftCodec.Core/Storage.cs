using System.Text.Json;
using Serilog;
namespace SoftCodec.Core;
public sealed class JsonStore<T>(string path) where T : new()
{
 public static JsonSerializerOptions Json { get; }=new(JsonSerializerDefaults.Web){WriteIndented=true};
 public string PathName=>path;
 public T Load(out string? recovery)
 {
  recovery=null; if(!File.Exists(path)) return new();
  try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path),Json) ?? throw new JsonException("Leeg configuratiebestand."); }
  catch(Exception ex) when(ex is JsonException or NotSupportedException)
  { var broken=path+".broken-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"); File.Move(path,broken); recovery=$"Configuratie hersteld: {broken}"; if(File.Exists(path+".bak")) try {return JsonSerializer.Deserialize<T>(File.ReadAllText(path+".bak"),Json)??new();} catch(JsonException){} return new(); }
 }
 public void Save(T value)
 {
  if(value is SoftCodecOptions options) options.Validate();
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
  var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {
   using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {JsonSerializer.Serialize(file,value,Json);file.Flush(true);}
   if(File.Exists(path)) File.Replace(temporary,path,path+".bak"); else File.Move(temporary,path);
  } finally {if(File.Exists(temporary))File.Delete(temporary);}
 }
}
public sealed class EventLog
{
 readonly object gate=new(); readonly Queue<string> recent=new(); readonly Serilog.ILogger logger;
 public EventLog(string directory){Directory.CreateDirectory(directory);logger=new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.File(Path.Combine(directory,"softcodec-.log"),rollingInterval:Serilog.RollingInterval.Day,retainedFileCountLimit:14,fileSizeLimitBytes:10_000_000,rollOnFileSizeLimit:true,shared:true).CreateLogger();}
 public event Action<string>? Written;
 public string[] Snapshot(){lock(gate)return recent.ToArray();}
 public void Write(string level,string message)
 {
  var line=$"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message.Replace('\r',' ').Replace('\n',' ')}";
  lock(gate){recent.Enqueue(line);while(recent.Count>2000)recent.Dequeue();logger.Write(level switch{"ERR"=>Serilog.Events.LogEventLevel.Error,"WRN"=>Serilog.Events.LogEventLevel.Warning,_=>Serilog.Events.LogEventLevel.Information},"{Message}",message);}
  Written?.Invoke(line);
 }
}
