using System.Buffers.Binary;
namespace SoftCodec.Rtp;
public readonly record struct RtcpReport(uint Ssrc,byte FractionLost,int CumulativeLost,uint HighestSequence,uint Jitter,uint LastSr,uint DelaySinceSr);
public static class Rtcp
{
 public static uint CompactNtp(DateTimeOffset now){var t=now-DateTimeOffset.Parse("1900-01-01T00:00:00Z");ulong seconds=(ulong)t.TotalSeconds;return (uint)((seconds<<16)|((ulong)((t.TotalSeconds-Math.Floor(t.TotalSeconds))*65536)&65535));}
 public static int WriteReport(Span<byte> b,uint localSsrc,uint timestamp,uint packets,uint octets,RtcpReport? report,DateTimeOffset now)
 {
  int size=28+(report.HasValue?24:0);b[..size].Clear();b[0]=(byte)(128+(report.HasValue?1:0));b[1]=200;BinaryPrimitives.WriteUInt16BigEndian(b[2..],(ushort)(size/4-1));BinaryPrimitives.WriteUInt32BigEndian(b[4..],localSsrc);
  var seconds=(now-DateTimeOffset.Parse("1900-01-01T00:00:00Z")).TotalSeconds;BinaryPrimitives.WriteUInt32BigEndian(b[8..],(uint)seconds);BinaryPrimitives.WriteUInt32BigEndian(b[12..],(uint)((seconds-Math.Floor(seconds))*4294967296d));BinaryPrimitives.WriteUInt32BigEndian(b[16..],timestamp);BinaryPrimitives.WriteUInt32BigEndian(b[20..],packets);BinaryPrimitives.WriteUInt32BigEndian(b[24..],octets);
  if(report is {} r){var x=b[28..];BinaryPrimitives.WriteUInt32BigEndian(x,r.Ssrc);x[4]=r.FractionLost;int loss=Math.Clamp(r.CumulativeLost,-8388608,8388607);x[5]=(byte)(loss>>16);x[6]=(byte)(loss>>8);x[7]=(byte)loss;BinaryPrimitives.WriteUInt32BigEndian(x[8..],r.HighestSequence);BinaryPrimitives.WriteUInt32BigEndian(x[12..],r.Jitter);BinaryPrimitives.WriteUInt32BigEndian(x[16..],r.LastSr);BinaryPrimitives.WriteUInt32BigEndian(x[20..],r.DelaySinceSr);}
  return size;
 }
 public static int WriteSdes(Span<byte> b,uint ssrc,string cname)
 {var bytes=System.Text.Encoding.UTF8.GetBytes(cname);int n=Math.Min(255,bytes.Length);int size=(11+n+3)&~3;b[..size].Clear();b[0]=129;b[1]=202;BinaryPrimitives.WriteUInt16BigEndian(b[2..],(ushort)(size/4-1));BinaryPrimitives.WriteUInt32BigEndian(b[4..],ssrc);b[8]=1;b[9]=(byte)n;bytes.AsSpan(0,n).CopyTo(b[10..]);return size;}
 public static bool TryReadReport(ReadOnlySpan<byte> packet,uint ourSsrc,out uint lastSr,out uint delay,out uint senderSr)
 {
  lastSr=delay=senderSr=0;bool found=false;int pos=0;
  while(pos+4<=packet.Length){var b=packet[pos..];int size=(BinaryPrimitives.ReadUInt16BigEndian(b[2..])+1)*4;if(b[0]>>6!=2||size<4||size>b.Length)return false;int count=b[0]&31;int start=b[1]==200?28:b[1]==201?8:-1;
   if(b[1]==200 && size>=28)senderSr=(BinaryPrimitives.ReadUInt32BigEndian(b[8..])<<16)|(BinaryPrimitives.ReadUInt32BigEndian(b[12..])>>16);
   if(start>=0){if(start+count*24>size)return false;for(int i=0;i<count;i++){var r=b[(start+24*i)..];if(BinaryPrimitives.ReadUInt32BigEndian(r)==ourSsrc){lastSr=BinaryPrimitives.ReadUInt32BigEndian(r[16..]);delay=BinaryPrimitives.ReadUInt32BigEndian(r[20..]);found=true;}}}
   pos+=size;
  }return found||senderSr!=0;
 }
}
