using System.Buffers.Binary;
using System.Net;
using SoftCodec.Audio;
using SoftCodec.Core;
using SoftCodec.Rtp;
using SoftCodec.Sip;
using Xunit;
namespace SoftCodec.Tests;
public class ProtocolTests
{
 static string Offer(string formats,string attributes="")=>$"v=0\r\no=peer 1 1 IN IP4 127.0.0.1\r\ns=peer\r\nc=IN IP4 127.0.0.1\r\nt=0 0\r\nm=audio 6014 RTP/AVP {formats}\r\n{attributes}";
 [Fact]public void NegotiatesStaticG722WithEightKhzClock(){var n=new SdpNegotiator();var f=n.Choose(n.Parse(Offer("9")),new());Assert.NotNull(f);Assert.Equal(8000,f.ClockRate);Assert.Equal(16000,f.SampleRate);Assert.Equal(160,f.TimestampStep);Assert.Equal(320,f.FrameSamples);}
 [Theory][InlineData(0,"PCMU")][InlineData(8,"PCMA")][InlineData(9,"G722")]
 public void StaticPayloadWithoutRtpmap(int pt,string name){var f=new SdpNegotiator().Parse(Offer(pt.ToString())).Formats.Single();Assert.Equal(name,f.Name);}
 [Fact]public void UnknownOpusFmtpIsRemoved(){var n=new SdpNegotiator();var f=n.Choose(n.Parse(Offer("111","a=rtpmap:111 opus/48000/2\r\na=fmtp:111 stereo=1;strange=123\r\na=ebuacip:version 0\r\n")),new());Assert.NotNull(f);Assert.DoesNotContain("strange",f.Fmtp);Assert.Contains("stereo=1",f.Fmtp);}
 [Fact]public void RejectsOpusWithWrongClock(){var n=new SdpNegotiator();Assert.Null(n.Choose(n.Parse(Offer("111","a=rtpmap:111 opus/8000/2\r\n")),new()));}
 [Fact]public void LocalCodecPriorityWins(){var n=new SdpNegotiator();var f=n.Choose(n.Parse(Offer("8 9")),new(){Priority=["g722","pcma"]});Assert.Equal("G722",f!.Name);}
 [Fact]public void IgnoresVideoAttributesAndUsesAudioConnection(){var n=new SdpNegotiator();var s=Offer("8","c=IN IP4 192.0.2.5\r\na=rtcp:5011\r\n")+"m=video 9000 RTP/AVP 96\r\nc=IN IP4 192.0.2.9\r\na=rtpmap:96 VP8/90000\r\n";var a=n.Parse(s);Assert.Equal("192.0.2.5",a.Address.ToString());Assert.Equal(5011,a.RtcpPort);Assert.Single(a.Formats);}
 [Fact]public void AnswerHasSinglePayloadAndCrLf(){var n=new SdpNegotiator();var f=n.Choose(n.Parse(Offer("8 9")),new())!;string answer=n.Build(IPAddress.Parse("203.0.113.40"),5004,5005,[f],1,1);Assert.Contains("m=audio 5004 RTP/AVP 9\r\n",answer);Assert.Contains("a=sendrecv\r\n",answer);Assert.Contains("a=rtpmap:9 G722/8000",answer);}
 [Theory][InlineData("")][InlineData("v=0\r\n")][InlineData("v=1\r\no=a\r\ns=a\r\nt=0 0\r\nm=audio 4 RTP/AVP 8\r\n")]
 public void RejectsMalformedSdp(string s)=>Assert.Throws<FormatException>(()=>new SdpNegotiator().Parse(s));
 [Fact]public void RtpRoundtripAtSequenceAndTimestampWrap(){var b=new byte[100];var h=new RtpHeader(65535,uint.MaxValue,12,111,true);int n=RtpPacket.Write(b,h,new byte[]{1,2,3});Assert.True(RtpPacket.TryParse(b.AsSpan(0,n),out var parsed,out int o,out int len));Assert.Equal(h,parsed);Assert.Equal(12,o);Assert.Equal(3,len);}
 [Fact]public void RtpRejectsTruncatedExtensions(){byte[] b=new byte[16];b[0]=0x90;b[1]=111;b[14]=255;Assert.False(RtpPacket.TryParse(b,out _,out _,out _));}
 [Fact]public void RtpHandlesCsrcExtensionAndPadding(){var b=new byte[31];b[0]=0xb1;b[1]=8;BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(18),1);b[24]=1;b[25]=2;b[30]=5;Assert.True(RtpPacket.TryParse(b,out _,out int o,out int length));Assert.Equal(24,o);Assert.Equal(2,length);}

 [Fact]public void DuplicateSequenceDoesNotInflateReceived(){var stats=new RtpReceptionStatistics(8000);var h=new RtpHeader(20,123,1,8,false);Assert.True(stats.Add(h,0));Assert.False(stats.Add(h,.02));Assert.Equal(1,stats.Received);Assert.Equal(0,stats.Lost);}
 [Fact]public void ReorderedPacketRecoversLoss(){var stats=new RtpReceptionStatistics(8000);stats.Add(new(10,0,1,8,false),0);stats.Add(new(12,320,1,8,false),.04);Assert.Equal(1,stats.Lost);stats.Add(new(11,160,1,8,false),.05);Assert.Equal(0,stats.Lost);}
 [Fact]public void RtpParserToleratesRandomMalformedPackets(){var random=new Random(42);for(int i=0;i<10000;i++){var bytes=new byte[random.Next(0,300)];random.NextBytes(bytes);RtpPacket.TryParse(bytes,out _,out _,out _);}}
 [Fact]public void AdaptiveJitterActuallyDelaysPlayoutOnUnderrun(){var j=new AdaptiveJitterBuffer(new(){MinMs=0,TargetMs=20,MaxMs=200},20,8000);j.Insert(new(1,123,1,8,false),new byte[]{1},0);var b=new byte[10];Assert.Equal(PacketStatus.Received,j.TakeDue(20,b,out _));j.Adapt(0,true,20);j.Insert(new(2,283,1,8,false),new byte[]{2},30);Assert.Equal(40,j.TargetMs);Assert.Equal(PacketStatus.Waiting,j.TakeDue(40,b,out _));Assert.Equal(PacketStatus.Received,j.TakeDue(60,b,out _));}
 [Fact]public void StunParsesXorMappedIpv4(){var b=new byte[32];BinaryPrimitives.WriteUInt16BigEndian(b,0x0101);BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(2),12);BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4),0x2112a442);BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(20),0x0020);BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(22),8);b[25]=1;BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(28),0xcb007128^0x2112a442);Assert.Equal("203.0.113.40",StunClient.ParseResponse(b,new byte[12]).ToString());b[8]=1;Assert.Throws<FormatException>(()=>StunClient.ParseResponse(b,new byte[12]));}
 [Fact]public void SequenceWrapDoesNotCreateLoss(){var s=new RtpReceptionStatistics(8000);s.Add(new(65535,1000,1,8,false),0);s.Add(new(0,1160,1,8,false),.02);Assert.Equal(2,s.Received);Assert.Equal(0,s.Lost);Assert.Equal(65536u,s.ExtendedHighestSequence);}
 [Fact]public void TimestampWrapDoesNotCreateHugeJitter(){var s=new RtpReceptionStatistics(48000);s.Add(new(1,uint.MaxValue-479,1,111,false),0);s.Add(new(2,480,1,111,false),.02);Assert.InRange(s.JitterMs,0,.01);}
 [Fact]public void JitterReordersWithoutAllocatingPerSlot(){var j=new AdaptiveJitterBuffer(new(){MinMs=20,TargetMs=40,MaxMs=200},20,48000);j.Insert(new(1,123,1,111,false),new byte[]{1},0);j.Insert(new(3,2043,1,111,false),new byte[]{3},20);j.Insert(new(2,1083,1,111,false),new byte[]{2},30);var b=new byte[10];Assert.Equal(PacketStatus.Waiting,j.TakeDue(39,b,out _));for(int i=0;i<3;i++){Assert.Equal(PacketStatus.Received,j.TakeDue(40+i*20,b,out int len));Assert.Equal(1,len);Assert.Equal(i+1,b[0]);}}
 [Fact]public void JitterFecPreservesFollowingPacket(){var j=new AdaptiveJitterBuffer(new(){MinMs=0,TargetMs=0,MaxMs=200},20,48000);j.Insert(new(1,900,1,111,false),new byte[]{1},0);j.Insert(new(3,2820,1,111,false),new byte[]{3},0);var b=new byte[10];j.TakeDue(0,b,out _);Assert.Equal(PacketStatus.RecoverFec,j.TakeDue(20,b,out _));Assert.Equal(3,b[0]);Assert.Equal(PacketStatus.Received,j.TakeDue(40,b,out _));Assert.Equal(3,b[0]);}
 [Fact]public void JitterWrapsRandomizedTimestamp(){var j=new AdaptiveJitterBuffer(new(){MinMs=0,TargetMs=0,MaxMs=100},20,48000);var b=new byte[8];uint start=uint.MaxValue-480;for(int i=0;i<8;i++){j.Insert(new((ushort)i,unchecked(start+(uint)i*960),1,111,false),new byte[]{(byte)i},i*20);Assert.Equal(PacketStatus.Received,j.TakeDue(i*20,b,out _));Assert.Equal(i,b[0]);}}
 [Fact]public void LateAndDuplicatePacketsAreDiscarded(){var j=new AdaptiveJitterBuffer(new(){MinMs=0,TargetMs=0,MaxMs=100},20,48000);var h=new RtpHeader(1,700,1,8,false);j.Insert(h,new byte[]{1},0);j.Insert(h,new byte[]{1},0);Assert.Equal(1,j.Discarded);j.TakeDue(0,new byte[8],out _);j.Insert(h,new byte[]{1},20);Assert.Equal(1,j.Late);}
 [Fact]public void RtcpRoundtripIncludesReportAndSdes(){var b=new byte[256];var rr=new RtcpReport(44,2,3,65537,48,123,456);int n=Rtcp.WriteReport(b,55,1,2,3,rr,DateTimeOffset.UtcNow);n+=Rtcp.WriteSdes(b.AsSpan(n),55,"test@studio");Assert.True(Rtcp.TryReadReport(b.AsSpan(0,n),44,out uint last,out uint delay,out _));Assert.Equal(123u,last);Assert.Equal(456u,delay);}
 [Fact]public void MalformedRtcpDoesNotReadOutsidePacket()=>Assert.False(Rtcp.TryReadReport(new byte[]{128,200,255,255},1,out _,out _,out _));
 [Theory][InlineData("192.0.2.0/24","192.0.2.4",true)][InlineData("192.0.2.0/24","192.0.3.1",false)][InlineData("0.0.0.0/0","203.0.113.4",true)][InlineData("127.0.0.1","127.0.0.2",false)]
 public void CidrBoundaries(string cidr,string ip,bool expected)=>Assert.Equal(expected,Ipv4Cidr.Parse(cidr).Contains(IPAddress.Parse(ip)));
 [Fact]public void FailClosedWithoutDigestOrAllowlist()=>Assert.Throws<ArgumentException>(()=>new SoftCodecOptions{Security=new(){Digest=new(){Enabled=false}}}.Validate());
 [Fact]public void DriftRatioIsBoundedAndDirectionCorrect(){var d=new DriftController();Assert.InRange(d.Update(.2,.12,.02),1,1.0003);for(int i=0;i<10000;i++)d.Update(0,.12,.02);Assert.InRange(d.Ratio,.9997,1);}
 [Fact]public void PcmRingNeverFeedsRenderToCapture(){var render=new PcmRing(20);var capture=new PcmRing(20);render.Write(new float[]{1,2});var b=new float[2];Assert.Equal(0,capture.Read(b));Assert.All(b,x=>Assert.Equal(0,x));}
}
