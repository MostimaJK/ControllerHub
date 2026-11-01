using System.IO;

namespace ControllerFusion.Engine;

// Explicit, bounded physical-hardware acceptance. Never enables audio or hiding
// and never changes preferences. Reuses one engine, as disconnect/mode changes do.
public static class LifecycleChecks
{
    public static async Task Run(string path,string controller,string transport,bool testHaptics=false)
    {
        File.WriteAllText(path,"Release lifecycle acceptance; audio/hiding disabled.\n");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await using var engine=new ControllerEngine();
        engine.Log+=s=> {lock(path) File.AppendAllText(path,s+Environment.NewLine);};
        foreach(string mode in new[]{"PS5","Xbox","PS5","Pro1","Steam / Pro2","Xbox","PS5","Xbox"})
        {
            using var cycle=CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            cycle.CancelAfter(TimeSpan.FromSeconds(15));
            var run=engine.Run(new Preferences {Controller=controller,Transport=transport,Output=mode,
                AudioHaptics=false,AutoReconnect=false,AutoTransport=false,HidePhysical=false},cycle.Token);
            try
            {
                while(!engine.HasOutput)
                {
                    if(run.IsCompleted) throw new IOException("Connection ended before readiness: "+engine.Status);
                    await Task.Delay(50,cycle.Token);
                }
                if(testHaptics && controller=="Switch 2 Pro") engine.TestHd();
                await Task.Delay(testHaptics?5000:500,cycle.Token);
            }
            finally {cycle.Cancel();await run;}
            if(!engine.CleanupSucceeded || engine.Status.Phase=="釋放未完成")
                throw new IOException("FAIL release: "+mode+"; "+engine.Status);
            if(engine.HasInput || engine.HasOutput) throw new IOException("FAIL stale readiness after release.");
            File.AppendAllText(path,$"PASS connect/disconnect {mode}; reports={engine.Reports}\n");
        }
        File.AppendAllText(path,"PASS all 8 lifecycle cycles on the same engine.\n");
    }
}
