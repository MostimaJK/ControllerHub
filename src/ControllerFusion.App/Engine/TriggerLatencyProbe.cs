using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using DS4Windows;

namespace ControllerFusion.Engine;

// Explicit finite diagnostic: Windows notification receipt -> XInput readback.
// This cannot measure finger-to-radio time, display latency or game physics.
internal static class TriggerLatencyProbe
{
    [DllImport("winmm.dll",EntryPoint="timeBeginPeriod",ExactSpelling=true)] static extern uint BeginPeriod(uint milliseconds);
    [DllImport("winmm.dll",EntryPoint="timeEndPeriod",ExactSpelling=true)] static extern uint EndPeriod(uint milliseconds);
    readonly record struct Edge(long Received,long Arrived,byte LT,byte RT,bool Release);
    internal static async Task<string> Run(ControllerEngine engine,string path)
    {
        if(!engine.HasOutput || engine.XboxState==null)throw new IOException("請先連接 Xbox 輸出模式。");
        var incoming=new ConcurrentQueue<Edge>();var samples=new List<object>();var latencies=new List<double>();var releaseLatencies=new List<double>();
        var previous=engine.LastFrame;byte l=previous?.L2??0,r=previous?.R2??0;
        int ambiguous=0,unobserved=0;double maxSourceAge=0;
        void Capture(DS4State frame)
        {
            byte nl=frame.L2,nr=frame.R2;if(nl==l && nr==r)return;
            bool release=(l>0 && nl==0)||(r>0 && nr==0);l=nl;r=nr;
            incoming.Enqueue(new(engine.LatestInputTimestamp,Stopwatch.GetTimestamp(),nl,nr,release));
        }
        engine.FrameReceived+=Capture;bool precision=BeginPeriod(1)==0;var pollGaps=new List<double>();long previousPoll=Stopwatch.GetTimestamp();
        var watch=Stopwatch.StartNew();Edge? pending=null;bool pendingAmbiguous=false;
        try
        {
            while(watch.Elapsed<TimeSpan.FromSeconds(30))
            {
                long poll=Stopwatch.GetTimestamp();pollGaps.Add(Stopwatch.GetElapsedTime(previousPoll,poll).TotalMilliseconds);previousPoll=poll;
                if(!engine.HasOutput)throw new IOException("測試期間輸出已中斷，未完成延遲驗證。");
                while(incoming.TryDequeue(out var next))
                {
                    if(pending!=null){unobserved++;ambiguous++;pendingAmbiguous=true;}
                    pending=next;
                }
                var xbox=engine.XboxState;
                if(pending is {} edge && xbox is {} state && state.LT==edge.LT && state.RT==edge.RT)
                {
                    long now=Stopwatch.GetTimestamp();double local=Stopwatch.GetElapsedTime(edge.Arrived,now).TotalMilliseconds;
                    double age=Stopwatch.GetElapsedTime(edge.Received,edge.Arrived).TotalMilliseconds;
                    maxSourceAge=Math.Max(maxSourceAge,age);
                    if(!pendingAmbiguous){latencies.Add(local);if(edge.Release)releaseLatencies.Add(local);}
                    samples.Add(new {edge.LT,edge.RT,edge.Release,NotificationToAppMilliseconds=age,AppToXInputObservedMilliseconds=local,Ambiguous=pendingAmbiguous});pending=null;pendingAmbiguous=false;
                }
                await Task.Delay(2).ConfigureAwait(false);
            }
        }
        finally {engine.FrameReceived-=Capture;if(precision)EndPeriod(1);}
        if(pending!=null)unobserved++;
        double? P95(List<double> values)=>values.Count==0?null:values.Order().ElementAt((int)Math.Ceiling(values.Count*.95)-1);
        var result=new {Transport=engine.ActiveTransport,HighResolutionTimerAcquired=precision,P95ActualPollingMilliseconds=P95(pollGaps),MaximumPollingMilliseconds=pollGaps.Count==0?0:pollGaps.Max(),ObservedTransitions=latencies.Count,ReleaseTransitions=releaseLatencies.Count,
            P95AppToXInputMilliseconds=P95(latencies),P95ReleaseAppToXInputMilliseconds=P95(releaseLatencies),
            MaximumReleaseAppToXInputMilliseconds=releaseLatencies.Count==0?(double?)null:releaseLatencies.Max(),
            MaximumNotificationToAppMilliseconds=maxSourceAge,UnobservedTransitions=unobserved,AmbiguousTransitions=ambiguous,
            engine.InputStatistics,engine.OutputStatistics,Samples=samples,
            Note="Software timestamps and actual polling interval reported. 1 ms timer resolution is scoped to this 30 s probe and restored on exit; not hard real time. Excludes physical button/radio/game/display latency. Sparse or unobserved transitions cannot prove game behavior."};
        File.WriteAllText(path+".trigger-latency.json",JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        return $"觀測 {latencies.Count} 次變化／{releaseLatencies.Count} 次鬆鍵；鬆鍵 P95 {P95(releaseLatencies):0.0} ms；未觀測 {unobserved} 次。";
    }
}
