using System.Buffers.Binary;
using System.Diagnostics;
using DS4Windows;

namespace ControllerFusion.Engine;

internal static class RealtimeFrameChecks
{
    internal static async Task Run(Action<bool,string> check)
    {
        long Tick(int ms)=>Stopwatch.Frequency*ms/1000;
        byte[] Xbox(byte l=0,byte r=0,int x=0)=>PadPackets.Encode(new DS4State {L2=l,R2=r,LX=(byte)(128+x)},"Xbox");
        long now=0;
        var buffer=new RealtimeFrameBuffer(()=>now);
        for(int i=0;i<1000;i++)buffer.TryWrite(Xbox(r:255,x:i%20),"Xbox");
        check(buffer.TryRead(out var latest) && BinaryPrimitives.ReadInt16LittleEndian(latest.AsSpan(6))!=0 && !buffer.TryRead(out _),"held/analog reports coalesce to the latest axes");
        buffer.TryWrite(Xbox(l:255),"Xbox");buffer.TryWrite(Xbox(),"Xbox");buffer.TryWrite(Xbox(r:255),"Xbox");
        check(buffer.TryRead(out var a) && a[4]==255 && buffer.TryRead(out var b) && b[4]==0 && b[5]==0 && buffer.TryRead(out var c) && c[5]==255,"fresh ZL release and ZR press edges preserve order");
        buffer.TryWrite(Xbox(r:255),"Xbox");now=Tick(70);buffer.TryWrite(Xbox(),"Xbox");
        check(buffer.TryRead(out var release) && release[5]==0 && !buffer.TryRead(out _),"expired held history cannot delay a fresh ZR release");
        now=Tick(80);buffer.TryWrite(Xbox(l:255,r:255),"Xbox");now=Tick(140);
        check(buffer.TryRead(out var old) && old[4]==0 && old[5]==0,"expired latest state becomes neutral rather than replaying old triggers");
        var silent=new RealtimeFrameBuffer(()=>now);silent.TryWrite(Xbox(l:255,r:255),"Xbox");silent.TryRead(out _);
        now=Tick(289);check(!silent.TrySafetyNeutral(out _),"brief Bluetooth jitter does not force an early neutral");
        now=Tick(290);check(silent.TrySafetyNeutral(out var neutral) && neutral[4]==0 && neutral[5]==0 && !silent.TrySafetyNeutral(out _),"150 ms source silence releases both triggers once");
        now=Tick(291);silent.TryWrite(Xbox(r:255),"Xbox");check(silent.TryRead(out var resumed) && resumed[5]==255,"fresh input resumes after a safety neutral");
        var taps=new RealtimeFrameBuffer(()=>now);
        taps.TryWrite(Xbox(r:255),"Xbox");taps.TryWrite(Xbox(),"Xbox");taps.TryWrite(Xbox(r:255),"Xbox");
        check(taps.TryRead(out var down1) && down1[5]==255 && taps.TryRead(out var up) && up[5]==0 && taps.TryRead(out var down2) && down2[5]==255,"rapid fresh press/release/press taps are not swallowed");
        var owned=new RealtimeFrameBuffer(()=>now);var mutable=Xbox(r:255);owned.TryWrite(mutable,"Xbox");mutable[5]=0;
        check(owned.TryRead(out var ownedBytes) && ownedBytes[5]==255,"queued packets own their bytes despite caller buffer reuse");
        var notification=new RealtimeFrameBuffer(()=>now);
        notification.TryWrite(Xbox(l:255),"Xbox",Tick(200));
        check(notification.TryRead(out var stale) && stale[4]==0,"old Bluetooth completion time is never freshened by delayed app delivery");
        notification.TryWrite(Xbox(),"Xbox",Tick(280));notification.TryWrite(Xbox(r:255),"Xbox",Tick(250));
        check(notification.TryRead(out var ordered) && ordered[5]==0 && !notification.TryRead(out _),"out of order old held input cannot overwrite a newer release");
        foreach(var mode in new[]{"PS5","Pro1","Steam / Pro2"})
        {
            var queue=new RealtimeFrameBuffer(()=>now);
            for(int i=0;i<50;i++)queue.TryWrite(PadPackets.Encode(new DS4State {R2=255,R2Btn=true,LX=(byte)(128+i%20)},mode),mode);
            queue.TryWrite(PadPackets.Encode(new DS4State(),mode),mode);
            int count=0;while(queue.TryRead(out _))count++;
            check(count==2,"coalescing preserves trigger release in "+mode);
        }
        // Drive the actual output writer with one temporarily held transport
        // write. This reproduces backpressure without a controller or driver.
        await using(var output=new VirtualOutput(true))
        {
            using var stop=new CancellationTokenSource(3000);
            var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allow=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var deliveredRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sent=new List<byte[]>();
            async Task Send(byte[] packet,CancellationToken token)
            {
                sent.Add((byte[])packet.Clone());
                if(sent.Count==1){entered.SetResult();await allow.Task.WaitAsync(token);check(packet[5]==255,"in-flight packet cannot be overwritten by producer slot reuse");}
                if(packet[5]==0)deliveredRelease.TrySetResult();
            }
            output.Submit(Xbox(r:255));var worker=output.RunWriter(Send,stop.Token);
            await entered.Task.WaitAsync(stop.Token);
            for(int i=0;i<100;i++)output.Submit(Xbox(r:255));output.Submit(Xbox());allow.SetResult();
            await deliveredRelease.Task.WaitAsync(stop.Token);stop.Cancel();await worker;
            check(sent.FindIndex(p=>p[5]==0)<=2,"actual writer forwards release within two pending writes after backpressure");
        }
        await using(var blocked=new VirtualOutput(true))
        {
            using var stop=new CancellationTokenSource(3000);int faults=0;
            var failure=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            blocked.Fault+=_=> {Interlocked.Increment(ref faults);failure.TrySetResult();};
            blocked.Submit(Xbox(r:255));
            var worker=blocked.RunWriter((_,token)=>Task.Delay(Timeout.Infinite,token),stop.Token);
            await failure.Task.WaitAsync(stop.Token);await worker;
            check(!blocked.Ready && faults==1,"blocked transport stops once at its write deadline instead of replaying input");
        }
        // The safety wake must still arrive with event-driven waiting and no
        // periodic polling, and must re-arm after fresh input resumes.
        await using(var quiet=new VirtualOutput(true))
        {
            using var stop=new CancellationTokenSource(3000);
            int neutrals=0;
            var first=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var second=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task Send(byte[] packet,CancellationToken token)
            {
                if(packet[5]==0){if(Interlocked.Increment(ref neutrals)==1)first.TrySetResult();else second.TrySetResult();}
                return Task.CompletedTask;
            }
            quiet.Submit(Xbox(r:255));var worker=quiet.RunWriter(Send,stop.Token);
            await first.Task.WaitAsync(stop.Token);quiet.Submit(Xbox(r:255));
            await second.Task.WaitAsync(stop.Token);stop.Cancel();await worker;
            check(neutrals==2,"event driven silence guard wakes and re-arms without input polling");
        }
        foreach(var mode in new[]{"Xbox","PS5","Pro1","Steam / Pro2"})
        {
            var mapper=new InputPacketMapper();var bytes=new byte[33];bool equivalent=true;
            for(int i=0;i<20;i++)
            {
                var raw=new DS4State {Cross=i%2==0,R2=(byte)(i%2*255),R2Btn=i%2==1,BLP=i%3==0,LX=(byte)(120+i)};
                var settings=new Preferences {GL="ZL"};int length=mapper.Write(raw,settings,mode,bytes);
                equivalent &= bytes.AsSpan(0,length).SequenceEqual(PadPackets.Encode(PadPackets.Map(raw,settings),mode));
            }
            check(equivalent,"reused mapping clears previous buttons/trigger state "+mode);
        }
        // Keep key edges distinct under pressure: overflowing fresh edges is a
        // visible connection fault, never a silent drop of only release.
        var full=new RealtimeFrameBuffer(()=>now);bool accepted=true;
        for(int i=0;i<33;i++){var frame=Xbox();frame[0]=(byte)(i%2);accepted=full.TryWrite(frame,"Xbox");}
        check(!accepted,"distinct fresh edge overflow is explicit");
    }
}
