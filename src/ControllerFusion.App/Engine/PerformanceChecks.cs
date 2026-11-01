using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DS4Windows;

namespace ControllerFusion.Engine;
internal static class PerformanceChecks
{
    static DS4State? snapshotSink;
    internal static async Task Run(string path)
    {
        const int iterations=20000,repeats=5;
        object Measure(string name,Action action)
        {
            for(int i=0;i<10000;i++)action();
            var samples=new List<object>();var times=new List<double>();var allocations=new List<double>();
            for(int repeat=0;repeat<repeats;repeat++)
            {
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                long allocated=GC.GetAllocatedBytesForCurrentThread();int g0=GC.CollectionCount(0);
                long began=Stopwatch.GetTimestamp();
                for(int i=0;i<iterations;i++)action();
                double ns=Stopwatch.GetElapsedTime(began).TotalNanoseconds/iterations;
                double bytes=(GC.GetAllocatedBytesForCurrentThread()-allocated)/(double)iterations;
                times.Add(ns);allocations.Add(bytes);samples.Add(new {NanosecondsPerOperation=ns,AllocatedBytesPerOperation=bytes,Gen0Collections=GC.CollectionCount(0)-g0});
            }
            return new {Name=name,Iterations=iterations,Repeats=repeats,MedianNanosecondsPerOperation=times.Order().ElementAt(repeats/2),MedianAllocatedBytesPerOperation=allocations.Order().ElementAt(repeats/2),Samples=samples};
        }
        var raw=new DS4State {R2=255,R2Btn=true,LX=140,LY=110};var preferences=new Preferences();
        var mapper=new InputPacketMapper();var destination=new byte[33];
        var mapping=Measure("MapAndEncodeXbox",()=>mapper.Write(raw,preferences,"Xbox",destination));
        var buffer=new RealtimeFrameBuffer(()=>0);var held=PadPackets.Encode(raw,"Xbox");var output=new byte[33];
        var roundtrip=Measure("BufferRoundtrip",()=>{buffer.TryWrite(held,"Xbox");buffer.WaitAsync(CancellationToken.None).GetAwaiter().GetResult();if(!buffer.TryReadInto(output,out _))throw new Exception("No queued frame");});
        int n=0;var burst=new RealtimeFrameBuffer(()=>0);
        var coalesce=Measure("HeldCoalesce",()=>{burst.TryWrite(held,"Xbox");if(++n%32==0)burst.TryReadInto(output,out _);});
        var copy=Measure("SourceSnapshot",()=>{var target=new DS4State();raw.CopyTo(target);snapshotSink=target;});
        long wakes;double cpu;long writes;
        await using(var idle=new VirtualOutput(true))
        {
            using var stop=new CancellationTokenSource();using var process=Process.GetCurrentProcess();
            double began=process.TotalProcessorTime.TotalMilliseconds;
            var worker=idle.RunWriter((_,_)=>Task.CompletedTask,stop.Token);
            await Task.Delay(1000).ConfigureAwait(false);stop.Cancel();await worker.ConfigureAwait(false);
            process.Refresh();cpu=process.TotalProcessorTime.TotalMilliseconds-began;wakes=idle.WaitingWakeups;writes=idle.Written;
        }
        var result=new {Runtime=Environment.Version.ToString(),Framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),Scenarios=new[]{mapping,roundtrip,coalesce,copy},
            Idle=new{SampleMilliseconds=1000,WriterWaitCompletions=wakes,CpuMilliseconds=cpu,Writes=writes},
            Note="Offline synthetic CPU/allocation benchmark after warmup; no radio, USB driver, game, audio, or end-to-end latency measurement. Tiny CPU/wall-time differences require repeated runs."};
        File.WriteAllText(path,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
    }
}
