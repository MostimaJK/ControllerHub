using System.Diagnostics;

namespace ControllerFusion.Engine;

// Connection readiness needs a live stream; one cached USB report is insufficient.
internal sealed class InputReadiness
{
    readonly object gate=new();
    long first,last,count;
    public void Reset(){lock(gate){first=last=count=0;}}
    public void Observe(long timestamp)
    {
        lock(gate)
        {
            if(count==0 || timestamp<last || Stopwatch.GetElapsedTime(last,timestamp).TotalMilliseconds>150)
            {first=timestamp;count=0;}
            last=timestamp;count++;
        }
    }
    public bool IsReady(long timestamp)
    {
        lock(gate) return count>=8 && timestamp>=last &&
            Stopwatch.GetElapsedTime(first,last).TotalMilliseconds>=200 &&
            Stopwatch.GetElapsedTime(last,timestamp).TotalMilliseconds<=150;
    }
}
