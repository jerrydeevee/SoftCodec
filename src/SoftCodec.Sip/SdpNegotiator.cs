using System.Globalization;
using System.Net;
using SoftCodec.Core;
namespace SoftCodec.Sip;
public sealed record SdpAudio(IPAddress Address,int Port,int RtcpPort,IReadOnlyList<CodecFormat> Formats,string Direction);
public sealed class SdpNegotiator
{
 static readonly Dictionary<int,CodecFormat> Static=new(){[0]=new("PCMU",0,8000,8000,1,20),[8]=new("PCMA",8,8000,8000,1,20),[9]=new("G722",9,8000,16000,1,20),[10]=new("L16",10,44100,44100,2,4),[11]=new("L16",11,44100,44100,1,4)};
 public SdpAudio Parse(string sdp)
 {
  if(string.IsNullOrEmpty(sdp)||sdp.Length>32768)throw new FormatException("SDP ontbreekt of is te groot.");
  string[] lines=sdp.Replace("\r","").Split('\n',StringSplitOptions.RemoveEmptyEntries);
  foreach(var required in new[]{"v=","o=","s=","t=","m="})if(!lines.Any(l=>l.StartsWith(required,StringComparison.Ordinal)))throw new FormatException("SDP mist "+required);
  if(!lines.Contains("v=0"))throw new FormatException("SDP-versie niet ondersteund.");
  IPAddress? sessionAddress=null,address=null;int port=0,rtcp=0,ptime=20;string direction="sendrecv";bool selected=false,inAudio=false;
  var payloads=new List<int>();var map=new Dictionary<int,CodecFormat>();var fmtp=new Dictionary<int,string>();
  foreach(var line in lines)
  {
   if(line.StartsWith("m=")){inAudio=false;if(!selected && line.StartsWith("m=audio ")){var m=line[2..].Split(' ',StringSplitOptions.RemoveEmptyEntries);if(m.Length<4||m[2]!="RTP/AVP"||!int.TryParse(m[1].Split('/')[0],out port)||port is <1 or >65535)throw new FormatException("Ongeldige audio-m-line.");foreach(var p in m[3..]){if(!int.TryParse(p,out int pt)||pt is <0 or >127)throw new FormatException("Ongeldig payloadtype.");payloads.Add(pt);}selected=inAudio=true;}continue;}
   if(line.StartsWith("c=")){var c=line[2..].Split(' ',StringSplitOptions.RemoveEmptyEntries);if(c.Length!=3||c[0]!="IN"||c[1]!="IP4"||!IPAddress.TryParse(c[2].Split('/')[0],out var ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)throw new FormatException("Alleen unicast IPv4 ondersteund.");if(!selected)sessionAddress=ip;else if(inAudio)address=ip;}
   if(!inAudio)continue;
   if(line.StartsWith("a=rtcp:")){var x=line[7..].Split(' ')[0];if(!int.TryParse(x,out rtcp)||rtcp is <1 or >65535)throw new FormatException("Ongeldige RTCP-poort.");}
   else if(line.StartsWith("a=ptime:")){if(!double.TryParse(line[8..],CultureInfo.InvariantCulture,out double p)||p<1||p>120||p!=Math.Floor(p))throw new FormatException("Niet-ondersteunde ptime.");ptime=(int)p;}
   else if(line.StartsWith("a=rtpmap:")){var x=line[9..].Split(' ',2);if(x.Length!=2||!int.TryParse(x[0],out int pt))throw new FormatException("Ongeldige rtpmap.");var f=x[1].Split('/');if(f.Length is <2 or >3 ||!int.TryParse(f[1],out int clock)||clock<=0)throw new FormatException("Ongeldige codecklok.");int ch=f.Length==3?int.Parse(f[2],CultureInfo.InvariantCulture):1;if(ch is <1 or >2)continue;map[pt]=new(f[0].ToUpperInvariant(),pt,clock,f[0].Equals("G722",StringComparison.OrdinalIgnoreCase)?16000:clock,ch,ptime);}
   else if(line.StartsWith("a=fmtp:")){var f=line[7..].Split(' ',2);if(f.Length==2&&int.TryParse(f[0],out int pt))fmtp[pt]=f[1];}
   else if(line is "a=sendrecv" or "a=sendonly" or "a=recvonly" or "a=inactive")direction=line[2..];
  }
  if(!selected)throw new FormatException("Geen actieve audio aangeboden.");var remote=address??sessionAddress??throw new FormatException("SDP mist connection-adres.");
  if(remote.Equals(IPAddress.Any)||remote.GetAddressBytes()[0]>=224)throw new FormatException("Hold/multicast-adres niet ondersteund.");
  var formats=new List<CodecFormat>();foreach(int pt in payloads){if(!map.TryGetValue(pt,out var f)&&!Static.TryGetValue(pt,out f))continue;if(Static.TryGetValue(pt,out var standard)&&(f.Name!=standard.Name||f.ClockRate!=standard.ClockRate||f.Channels!=standard.Channels))continue;formats.Add(f with{PtimeMs=ptime,Fmtp=fmtp.GetValueOrDefault(pt,"")});}
  return new(remote,port,rtcp==0?checked(port+1):rtcp,formats,direction);
 }
 public CodecFormat? Choose(SdpAudio offer,CodecOptions options)
 {
  if(offer.Direction!="sendrecv")return null;
  foreach(var priority in options.Priority)foreach(var f in offer.Formats)
  {
   bool match=priority.ToLowerInvariant() switch {"opus"=>f.Name=="OPUS"&&f.ClockRate==48000&&f.Channels==2&&f.PtimeMs is 10 or 20 or 40,"g722"=>f.Name=="G722"&&f.ClockRate==8000&&f.Channels==1,"pcma"=>f.Name=="PCMA"&&f.ClockRate==8000&&f.Channels==1,"pcmu"=>f.Name=="PCMU"&&f.ClockRate==8000&&f.Channels==1,"l16-48"=>f.Name=="L16"&&f.ClockRate==48000,"l16-32"=>f.Name=="L16"&&f.ClockRate==32000,"l24-48"=>f.Name=="L24"&&f.ClockRate==48000,_=>false};
   if(!match)continue;
   if(f.Name=="OPUS")return f with{Fmtp=$"minptime=10;useinbandfec={(options.Opus.UseInBandFec?1:0)};stereo={(options.Opus.Stereo&&Parameter(f.Fmtp,"stereo")=="1"?1:0)};sprop-stereo={(options.Opus.Stereo?1:0)};maxaveragebitrate={options.Opus.MaxAverageBitrate}"};
   return f;
  }return null;
 }
 public static string? Parameter(string text,string name)=>text.Split(';',StringSplitOptions.TrimEntries).Select(x=>x.Split('=',2)).Where(x=>x.Length==2&&x[0].Equals(name,StringComparison.OrdinalIgnoreCase)).Select(x=>x[1]).FirstOrDefault();
 public IReadOnlyList<CodecFormat> CreateFormats(CodecOptions options)
 {
  var all=new[]{new CodecFormat("OPUS",111,48000,48000,2,options.Opus.PtimeMs,$"useinbandfec={(options.Opus.UseInBandFec?1:0)};stereo={(options.Opus.Stereo?1:0)};sprop-stereo={(options.Opus.Stereo?1:0)};maxaveragebitrate={options.Opus.MaxAverageBitrate}"),new("G722",9,8000,16000,1,20),new("PCMA",8,8000,8000,1,20),new("PCMU",0,8000,8000,1,20),new("L16",96,48000,48000,2,4),new("L16",97,32000,32000,2,4),new("L24",98,48000,48000,2,4)};
  var key=new[]{"opus","g722","pcma","pcmu","l16-48","l16-32","l24-48"};return options.Priority.SelectMany(p=>all.Where((_,i)=>key[i]==p.ToLowerInvariant())).ToArray();
 }
 public string Build(IPAddress advertised,int port,int rtcp,IReadOnlyList<CodecFormat> formats,ulong sessionId,ulong version)
 {
  if(formats.Count==0)throw new ArgumentException("Geen codecs.");
  var lines=new List<string>{"v=0",$"o=softcodec {sessionId} {version} IN IP4 {advertised}","s=SoftCodec","c=IN IP4 "+advertised,"t=0 0",$"m=audio {port} RTP/AVP "+string.Join(' ',formats.Select(f=>f.PayloadType)),"a=rtcp:"+rtcp,"a=sendrecv"};
  foreach(var f in formats){lines.Add($"a=rtpmap:{f.PayloadType} {(f.Name=="OPUS"?"opus":f.Name)}/{f.ClockRate}"+(f.Channels==1?"":"/"+f.Channels));if(f.Fmtp.Length>0)lines.Add($"a=fmtp:{f.PayloadType} {f.Fmtp}");}lines.Add("a=ptime:"+formats[0].PtimeMs);return string.Join("\r\n",lines)+"\r\n";
 }
}
