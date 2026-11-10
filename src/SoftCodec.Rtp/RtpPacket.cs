using System.Buffers.Binary;
namespace SoftCodec.Rtp;
public readonly record struct RtpHeader(ushort Sequence,uint Timestamp,uint Ssrc,byte PayloadType,bool Marker);
public static class RtpPacket
{
 public const int MaxPacketSize=8192;
 public static bool TryParse(ReadOnlySpan<byte> packet,out RtpHeader header,out int offset,out int length)
 {
  header=default;offset=length=0;if(packet.Length<12 || packet[0]>>6!=2)return false;
  offset=12+(packet[0]&15)*4;if(offset>packet.Length)return false;
  if((packet[0]&16)!=0){if(offset+4>packet.Length)return false;int ext=BinaryPrimitives.ReadUInt16BigEndian(packet[(offset+2)..])*4;offset+=4+ext;if(offset>packet.Length)return false;}
  length=packet.Length-offset;
  if((packet[0]&32)!=0){int pad=packet[^1];if(pad==0||pad>length)return false;length-=pad;}
  header=new(BinaryPrimitives.ReadUInt16BigEndian(packet[2..]),BinaryPrimitives.ReadUInt32BigEndian(packet[4..]),BinaryPrimitives.ReadUInt32BigEndian(packet[8..]),(byte)(packet[1]&127),(packet[1]&128)!=0);return length>0;
 }
 public static int Write(Span<byte> destination,in RtpHeader h,ReadOnlySpan<byte> payload)
 {
  if(payload.Length+12>destination.Length || h.PayloadType>127)throw new ArgumentException("RTP buffer of payloadtype ongeldig.");
  destination[0]=128;destination[1]=(byte)(h.PayloadType|(h.Marker?128:0));BinaryPrimitives.WriteUInt16BigEndian(destination[2..],h.Sequence);BinaryPrimitives.WriteUInt32BigEndian(destination[4..],h.Timestamp);BinaryPrimitives.WriteUInt32BigEndian(destination[8..],h.Ssrc);payload.CopyTo(destination[12..]);return 12+payload.Length;
 }
}
public sealed class RtpReceptionStatistics(int clockRate)
{
 readonly long[] seen=Enumerable.Repeat(-1L,2048).ToArray();bool initialized;long highest,first,received;double jitter,previousTransit;uint ssrc;
 public long Received=>received;public long Expected=>initialized?highest-first+1:0;public long Lost=>Math.Max(0,Expected-received);public double JitterMs=>jitter*1000/clockRate;
 public uint ExtendedHighestSequence=>(uint)highest;public uint JitterTimestampUnits=>(uint)jitter;
 public bool Add(in RtpHeader h,double arrivalSeconds)
 {
  if(!initialized){initialized=true;highest=first=h.Sequence;ssrc=h.Ssrc;previousTransit=arrivalSeconds*clockRate-h.Timestamp;}
  if(h.Ssrc!=ssrc)return false;
  long extended=highest+unchecked((short)(h.Sequence-(ushort)highest));
  if(extended<first || extended<highest-seen.Length+1 || extended-highest>3000)return false;
  int index=(int)(extended%seen.Length);if(seen[index]==extended)return false;seen[index]=extended;
  if(extended>highest)highest=extended;received++;
  double transit=arrivalSeconds*clockRate-h.Timestamp;double d=transit-previousTransit;
  if(d>int.MaxValue)d-=4294967296d;else if(d<int.MinValue)d+=4294967296d;
  jitter+=(Math.Abs(d)-jitter)/16;previousTransit=transit;return true;
 }
}
