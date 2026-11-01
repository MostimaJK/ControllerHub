using System.Buffers.Binary;
using System.Diagnostics;
using DS4Windows;

namespace ControllerFusion.Engine;

// Slots own producer data. The consumer copies into its own packet before
// unlocking, so an in-flight async write never borrows a recyclable ring slot.
internal sealed class RealtimeFrameBuffer
{
    internal const int MaximumAgeMilliseconds=40;
    internal const int InputSilenceMilliseconds=150;
    const int Capacity=32,MaximumPacketLength=33;
    readonly object gate=new();
    readonly byte[] packets=new byte[Capacity*MaximumPacketLength];
    readonly ulong[] controls=new ulong[Capacity];
    readonly long[] receivedAt=new long[Capacity];
    readonly int[] lengths=new int[Capacity];
    readonly SemaphoreSlim wake=new(0,1);
    readonly Func<long> clock;
    readonly Timer? silenceTimer;
    bool completed,hasInput,safetyNeutralSent,silenceArmed;
    long lastInput,received,coalesced,staleDropped,overflows,safetyNeutrals;
    int head,count,maximumDepth;
    double maximumResidence;
    string mode="Xbox";
    byte[] neutral=PadPackets.Encode(new DS4State(),"Xbox");
    internal RealtimeFrameBuffer(Func<long>? clock=null)
    {
        this.clock=clock??Stopwatch.GetTimestamp;
        // Fake-clock tests drive safety explicitly. Production has one reusable
        // timer, which rechecks last receipt only near its silence deadline.
        if(clock==null)silenceTimer=new Timer(OnSilence,null,Timeout.Infinite,Timeout.Infinite);
    }
    internal bool Completed {get {lock(gate)return completed;}}
    internal bool TryWrite(ReadOnlySpan<byte> bytes,string mode,long? receivedTimestamp=null)
    {
        ulong key=Controls(bytes,mode);
        lock(gate)
        {
            if(completed)return false;
            long now=clock(),timestamp=receivedTimestamp??now;received++;
            if(timestamp<0 || timestamp>now || (hasInput && timestamp<lastInput)) {staleDropped++;return true;}
            if(this.mode!=mode)
            {
                if(count!=0)throw new InvalidOperationException("Output mode changed with pending frames.");
                this.mode=mode;neutral=PadPackets.Encode(new DS4State(),mode);
            }
            lastInput=timestamp;hasInput=true;safetyNeutralSent=false;
            int tail=(head+count-1+Capacity)%Capacity,index;
            if(count!=0 && controls[tail]==key) {index=tail;coalesced++;}
            else
            {
                if(count==Capacity){overflows++;return false;}
                index=(head+count)%Capacity;count++;maximumDepth=Math.Max(maximumDepth,count);
            }
            bytes.CopyTo(packets.AsSpan(index*MaximumPacketLength,bytes.Length));
            controls[index]=key;receivedAt[index]=timestamp;lengths[index]=bytes.Length;
            if(silenceTimer!=null && !silenceArmed)
            {silenceArmed=true;silenceTimer.Change(InputSilenceMilliseconds,Timeout.Infinite);}
            SignalNoLock();return true;
        }
    }
    internal bool TryReadInto(Span<byte> destination,out int length)
    {
        lock(gate)
        {
            length=0;if(count==0)return false;
            long now=clock();double age=Stopwatch.GetElapsedTime(receivedAt[head],now).TotalMilliseconds;
            maximumResidence=Math.Max(maximumResidence,age);
            int index=head;
            if(age>MaximumAgeMilliseconds)
            {
                index=(head+count-1)%Capacity;staleDropped+=count-1;count=0;head=0;
                if(Stopwatch.GetElapsedTime(receivedAt[index],now).TotalMilliseconds>MaximumAgeMilliseconds)
                {
                    staleDropped++;neutral.CopyTo(destination);length=neutral.Length;
                    safetyNeutrals++;safetyNeutralSent=true;wake.Wait(0);return true;
                }
            }
            else {head=(head+1)%Capacity;count--;}
            length=lengths[index];packets.AsSpan(index*MaximumPacketLength,length).CopyTo(destination);
            if(count==0)wake.Wait(0);return true;
        }
    }
    // Diagnostics/tests may retain this result. Production uses caller-owned
    // storage and does not allocate a packet for each dequeue.
    internal bool TryRead(out byte[] bytes)
    {
        Span<byte> scratch=stackalloc byte[MaximumPacketLength];
        if(!TryReadInto(scratch,out int length)){bytes=null!;return false;}
        bytes=scratch[..length].ToArray();return true;
    }
    internal bool TrySafetyNeutralInto(Span<byte> destination,out int length)
    {
        lock(gate)
        {
            length=0;
            if(completed || !hasInput || safetyNeutralSent ||
                Stopwatch.GetElapsedTime(lastInput,clock()).TotalMilliseconds<InputSilenceMilliseconds)return false;
            staleDropped+=count;count=0;head=0;safetyNeutralSent=true;safetyNeutrals++;
            neutral.CopyTo(destination);length=neutral.Length;wake.Wait(0);return true;
        }
    }
    internal bool TrySafetyNeutral(out byte[] bytes)
    {
        Span<byte> scratch=stackalloc byte[MaximumPacketLength];
        if(!TrySafetyNeutralInto(scratch,out int length)){bytes=null!;return false;}
        bytes=scratch[..length].ToArray();return true;
    }
    internal Task WaitAsync(CancellationToken token)=>wake.WaitAsync(token);
    void OnSilence(object? _)
    {
        lock(gate)
        {
            if(completed || !hasInput || safetyNeutralSent){silenceArmed=false;return;}
            double remaining=InputSilenceMilliseconds-Stopwatch.GetElapsedTime(lastInput,clock()).TotalMilliseconds;
            if(remaining>0){silenceTimer!.Change((int)Math.Ceiling(remaining),Timeout.Infinite);return;}
            silenceArmed=false;SignalNoLock();
        }
    }
    // All signal producers hold gate. The count check avoids using an
    // exception as the normal notification-coalescing path.
    void SignalNoLock(){if(wake.CurrentCount==0)wake.Release();}
    internal void Complete()
    {
        lock(gate){completed=true;count=0;head=0;SignalNoLock();}
        silenceTimer?.Dispose();
    }
    internal object Snapshot() {lock(gate)return new {Received=received,Coalesced=coalesced,StaleDropped=staleDropped,
        Pending=count,MaximumDepth=maximumDepth,Overflows=overflows,SafetyNeutrals=safetyNeutrals,
        MaximumQueuedResidenceMilliseconds=maximumResidence,
        SourceAgeMilliseconds=hasInput?(double?)Stopwatch.GetElapsedTime(lastInput,clock()).TotalMilliseconds:null,
        MaximumAgeMilliseconds,InputSilenceMilliseconds,Note="Switch 2 ages start at Windows completion QPC; other controllers at facade receipt. Not radio or game latency."};}
    static ulong Controls(ReadOnlySpan<byte> b,string mode)
    {
        int expected=PadPackets.PacketLength(mode);
        if(b.Length!=expected)throw new ArgumentException("Invalid controller packet length.",nameof(b));
        if(mode=="Xbox")return BinaryPrimitives.ReadUInt32LittleEndian(b)|
            (b[4]!=0?1UL<<32:0)|(b[5]!=0?1UL<<33:0);
        if(mode=="PS5")return BinaryPrimitives.ReadUInt32LittleEndian(b[4..])|((ulong)b[8]<<32)|
            (b[9]!=0?1UL<<40:0)|(b[10]!=0?1UL<<41:0)|((ulong)b[15]<<42)|((ulong)b[20]<<43);
        return BinaryPrimitives.ReadUInt32LittleEndian(b);
    }
}
