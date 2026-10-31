using System.Diagnostics;

namespace ControllerFusion.Engine;

public sealed class InputStatistics
{
    readonly object gate=new();
    readonly long[] buckets=new long[1001];
    long previous,count;
    double maximum;
    public void Reset() {lock(gate){Array.Clear(buckets);previous=count=0;maximum=0;}}
    public void Observe(long timestamp)
    {
        lock(gate)
        {
            if(previous!=0)
            {
                double ms=Stopwatch.GetElapsedTime(previous,timestamp).TotalMilliseconds;
                if(ms>=0) {buckets[(int)Math.Min(1000,Math.Ceiling(ms))]++;count++;maximum=Math.Max(maximum,ms);}
            }
            previous=timestamp;
        }
    }
    public object Snapshot()
    {
        lock(gate)
        {
            double? Percentile(double q)
            {
                if(count==0)return null;
                long threshold=(long)Math.Ceiling(count*q),sum=0;
                for(int i=0;i<buckets.Length;i++){sum+=buckets[i];if(sum>=threshold)return i<1000?i:Math.Ceiling(maximum);}
                return null;
            }
            return new {GapSamples=count,P95UpperMilliseconds=Percentile(.95),P99UpperMilliseconds=Percentile(.99),MaximumGapMilliseconds=maximum,HistogramOverflowAtMilliseconds=1000,Note="Received report gaps, not end-to-end input latency."};
        }
    }
}
