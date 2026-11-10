using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
namespace SoftCodec.Rtp;
public static class StunClient
{
 const uint Cookie=0x2112a442;
 public static async Task<IPAddress> DiscoverAsync(string server,int port=3478,CancellationToken ct=default)
 {
  var addresses=await Dns.GetHostAddressesAsync(server,ct);var ip=addresses.FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork)??throw new SocketException((int)SocketError.HostNotFound);
  using var udp=new UdpClient(AddressFamily.InterNetwork);udp.Connect(ip,port);var request=new byte[20];request[1]=1;BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(4),Cookie);RandomNumberGenerator.Fill(request.AsSpan(8,12));
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(5));
  for(int attempt=0;attempt<3;attempt++){await udp.SendAsync(request,timeout.Token);using var retry=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);retry.CancelAfter(TimeSpan.FromMilliseconds(700*(attempt+1)));try{var response=await udp.ReceiveAsync(retry.Token);return ParseResponse(response.Buffer,request.AsSpan(8,12));}catch(OperationCanceledException)when(!timeout.IsCancellationRequested){}}
  throw new TimeoutException("Geen STUN-antwoord ontvangen.");
 }
 public static IPAddress ParseResponse(ReadOnlySpan<byte> response,ReadOnlySpan<byte> transactionId)
 {
  if(response.Length<20 || BinaryPrimitives.ReadUInt16BigEndian(response)!=0x0101 || BinaryPrimitives.ReadUInt32BigEndian(response[4..])!=Cookie || !response.Slice(8,12).SequenceEqual(transactionId))throw new FormatException("Ongeldig STUN-antwoord.");
  int length=BinaryPrimitives.ReadUInt16BigEndian(response[2..]);if(length%4!=0||20+length>response.Length)throw new FormatException("Afgebroken STUN-antwoord.");
  IPAddress? mapped=null;for(int p=20;p<20+length;){if(p+4>response.Length)throw new FormatException("Afgebroken STUN-attribuut.");ushort type=BinaryPrimitives.ReadUInt16BigEndian(response[p..]);int n=BinaryPrimitives.ReadUInt16BigEndian(response[(p+2)..]);p+=4;if(p+n>20+length)throw new FormatException("Afgebroken STUN-attribuut.");if(type is 0x0020 or 0x0001 && n==8 && response[p+1]==1){uint value=BinaryPrimitives.ReadUInt32BigEndian(response[(p+4)..]);if(type==0x0020)value^=Cookie;var bytes=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(bytes,value);var address=new IPAddress(bytes);if(type==0x0020)return address;mapped=address;}p+=(n+3)&~3;}
  return mapped??throw new FormatException("STUN bevat geen IPv4-mapping.");
 }
}
