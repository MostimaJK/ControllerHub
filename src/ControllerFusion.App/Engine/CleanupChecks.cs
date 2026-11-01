using System.IO;
using System.Reflection;
using Y700Switch2V60Viiper;
namespace ControllerFusion.Engine;
internal static class CleanupChecks
{
    const string Ports="Port 01: device in use\n -> usbip://127.0.0.1:41000/1-1\n -> remote bus/dev 001/001\n";
    static VirtualOutput Fixture(Func<string,IEnumerable<string>,CancellationToken,Task<string>> run,string lease)
    {
        var output=new VirtualOutput {CommandRunner=run};
        void Set(string name,object value)=>typeof(VirtualOutput).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(output,value);
        Set("usbip","fixture-usbip.exe");Set("usbPort",41000);Set("device",new ViiperDevice(1,"1","fixture","fixture","xbox360"));Set("leaseFile",lease);
        return output;
    }
    internal static async Task Run(Action<bool,string> check)
    {
        await using(var engine=new ControllerEngine())
        {
            typeof(ControllerEngine).GetProperty(nameof(ControllerEngine.CleanupSucceeded))!.SetValue(engine,false);
            await engine.Run(new Preferences(),CancellationToken.None);
            check(engine.Status.Phase=="釋放未完成" && !engine.HasOutput && !engine.HasInput,"failed cleanup blocks a new connection before hardware access");
        }
        check(OutputLease.Ports(Ports.Replace("1-1","1-10"),41000,1,"1").Length==0,"cleanup never matches a longer remote device prefix");
        check(OutputLease.Ports(Ports.Replace("127.0.0.1","192.0.2.10"),41000,1,"1").Length==0,"cleanup never matches a foreign host with the same port");
        check(OutputLease.Ports(Ports,41000,1,"1").SequenceEqual(new[]{1}),"cleanup matches its exact loopback endpoint");
        check(OutputLease.Ports(Ports.Replace("127.0.0.1","[::1]"),41000,1,"1").SequenceEqual(new[]{1}),"cleanup accepts IPv6 loopback endpoint");
        check(OutputLease.Ports(Ports.Replace("/1-1","/"),41000,1,"1").SequenceEqual(new[]{1}),"cleanup accepts padded bus/device with empty URI path");
        string lease=Path.Combine(Path.GetTempPath(),"ControllerHub-cleanup-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(lease,"fixture");
        try
        {
            var output=Fixture((_,args,_)=>args.First()=="detach"?Task.FromException<string>(new IOException("Injected detach failure")):Task.FromResult(Ports),lease);
            bool failed=false;try {await output.DisposeAsync();}catch(IOException){failed=true;}catch(AggregateException){failed=true;}
            check(failed && File.Exists(lease),"failed driver detach is reported and retains exact recovery lease");
        }
        finally {File.Delete(lease);}
        foreach(string scenario in new[]{"success","retained","transient"})
        {
            File.WriteAllText(lease,"fixture");int detaches=0;
            try
            {
                var output=Fixture((_,args,_)=>
                {
                    if(args.First()=="detach"){detaches++;return Task.FromResult("");}
                    return Task.FromResult(scenario=="retained" || detaches==0 || (scenario=="transient" && detaches<2)?Ports:"");
                },lease);
                bool failed=false;try{await output.DisposeAsync();}catch(AggregateException){failed=true;}
                check(scenario=="retained"?failed && File.Exists(lease):!failed && !File.Exists(lease),"driver detach outcome verified: "+scenario);
            }
            finally {File.Delete(lease);}
        }
    }
}
