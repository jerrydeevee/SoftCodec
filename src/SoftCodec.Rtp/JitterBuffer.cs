using SoftCodec.Core;
namespace SoftCodec.Rtp;
public enum PacketStatus { Waiting, Received, Missing, RecoverFec }
/// <summary>Fixed memory packet ring. A short lock publishes a complete slot across receive/decode threads.</summary>
public sealed class AdaptiveJitterBuffer
{
 sealed class Slot { public readonly byte[] Bytes=new byte[RtpPacket.MaxPacketSize];public uint Timestamp;public int Length;public bool Filled; }
 readonly Slot[] slots;readonly object gate=new();readonly int step,frameMs,min,max;readonly bool adaptive;
 bool started;uint next;long startedAt;long nextDue;int target;int buffered;long late,discarded;long lastDecrease;
 public AdaptiveJitterBuffer(JitterSettings settings,int frameMs,int clockRate)
 {
  this.frameMs=frameMs;step=clockRate*frameMs/1000;min=settings.MinMs;max=settings.MaxMs;target=settings.TargetMs;adaptive=settings.Mode=="adaptive";
  slots=Enumerable.Range(0,Math.Max(16,max/frameMs+64)).Select(_=>new Slot()).ToArray();
 }
 public long Late=>Interlocked.Read(ref late);public long Discarded=>Interlocked.Read(ref discarded);public int TargetMs=>Volatile.Read(ref target);public int DepthMs{get{lock(gate)return buffered*frameMs;}}
 int Index(uint ts)=>(int)((ts/(uint)step)%(uint)slots.Length);
 public void Insert(in RtpHeader h,ReadOnlySpan<byte> payload,long nowMs)
 {
  lock(gate){if(payload.Length>RtpPacket.MaxPacketSize){discarded++;return;}if(!started){started=true;next=h.Timestamp;startedAt=nowMs;nextDue=nowMs+target;}
   var delta=unchecked((int)(h.Timestamp-next));if(delta<0){late++;return;}if(delta%step!=0 || delta/step>=slots.Length){discarded++;return;}
   // Index relative to the first timestamp; RTP timestamps are randomized, not frame aligned.
   int idx=(int)((unchecked(h.Timestamp-next)/(uint)step+readIndex)%(uint)slots.Length);var slot=slots[idx];
   if(slot.Filled){discarded++;return;}payload.CopyTo(slot.Bytes);slot.Timestamp=h.Timestamp;slot.Length=payload.Length;slot.Filled=true;buffered++;
  }
 }
 uint readIndex;
 public PacketStatus TakeDue(long nowMs,Span<byte> destination,out int length)
 {
  lock(gate){length=0;if(!started || nowMs<nextDue)return PacketStatus.Waiting;nextDue+=frameMs;
   var slot=slots[readIndex];var status=PacketStatus.Missing;
   if(slot.Filled && slot.Timestamp==next){length=slot.Length;slot.Bytes.AsSpan(0,length).CopyTo(destination);slot.Filled=false;buffered--;status=PacketStatus.Received;}
   else {var following=slots[(readIndex+1)%(uint)slots.Length];if(following.Filled && following.Timestamp==unchecked(next+(uint)step)){length=following.Length;following.Bytes.AsSpan(0,length).CopyTo(destination);status=PacketStatus.RecoverFec;}}
   next=unchecked(next+(uint)step);readIndex=(readIndex+1)%(uint)slots.Length;return status;
  }
 }
 public void Adapt(double jitterMs,bool underrun,long nowMs)
 {
  if(!adaptive)return;lock(gate){int desired=Math.Clamp((int)Math.Ceiling(jitterMs*4)+min,min,max);
   if(underrun)desired=Math.Max(desired,target+frameMs);
   if(desired>target){int increase=Math.Min(frameMs,desired-target);target+=increase;if(started)nextDue+=increase;}
   else if(desired<target && nowMs-lastDecrease>10000){int lower=Math.Max(desired,target-frameMs);if(started)nextDue-=target-lower;target=lower;lastDecrease=nowMs;}
  }
 }
}
public sealed class DriftController
{
 double integral;public double Ratio{get;private set;}=1;
 public double Update(double fillSeconds,double targetSeconds,double dt)
 {if(dt<=0||!double.IsFinite(fillSeconds)||!double.IsFinite(targetSeconds))throw new ArgumentException("Ongeldige driftmeting.");double error=fillSeconds-targetSeconds;integral=Math.Clamp(integral+error*dt,-.05,.05);return Ratio=Math.Clamp(1+.15*error+.02*integral,.9997,1.0003);}
}
