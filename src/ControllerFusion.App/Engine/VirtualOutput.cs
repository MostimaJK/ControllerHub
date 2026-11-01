using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Y700Switch2V60Viiper;

namespace ControllerFusion.Engine;

public sealed class VirtualOutput : IAsyncDisposable
{
    internal Func<string,IEnumerable<string>,CancellationToken,Task<string>> CommandRunner=Processes.Run;
    Process? broker;
    ChildProcessJob? job;
    ViiperProtocolClient? client;
    ViiperDevice? device;
    ViiperDeviceStream? stream;
    uint? bus;
    string? usbip;
    int usbPort;
    string? leaseFile;
    CancellationTokenSource? lifetime;
    Task? reader,writer;
    readonly RealtimeFrameBuffer frames=new();
    long maximumWriteTicks,writeTimeouts;
    internal long WaitingWakeups;
    public object PipelineStatistics=>new {Buffer=frames.Snapshot(),Written=Interlocked.Read(ref Written),WriteTimeouts=Interlocked.Read(ref writeTimeouts),WaitingWakeups=Interlocked.Read(ref WaitingWakeups),MaximumWriteMilliseconds=Interlocked.Read(ref maximumWriteTicks)*1000d/Stopwatch.Frequency};
    public event Action<byte,byte>? Rumble;
    public int ImportedPort {get;private set;}=-1;
    public Func<byte[],bool>? AdvancedFeedback { get; set; }
    public event Action<string>? Fault;
    public event Action<string>? Log;
    public long Written;
    int disposed,failed,ready;
    public bool Ready { get=>Volatile.Read(ref ready)!=0;private set=>Volatile.Write(ref ready,value?1:0); }
    public VirtualOutput() { }
    internal VirtualOutput(bool undrainedDiagnostic) {Ready=undrainedDiagnostic;}
    void FailOnce(string message)
    {
        if(Interlocked.Exchange(ref failed,1)!=0)return;
        Ready=false;frames.Complete();
        _=Task.Run(()=> {try {Fault?.Invoke(message);}finally {try {lifetime?.Cancel();}catch(ObjectDisposedException){}}});
    }
    public string Mode { get; private set; }="Xbox";
    public uint[] PriorXbox { get; private set; }=Array.Empty<uint>();

    static int FreePort() { var l=new TcpListener(IPAddress.Loopback,0); l.Start(); int p=((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
    public async Task Start(string mode,CancellationToken token,bool attach=true)
    {
        Mode=mode;
        usbip=Processes.FindUsbip();
        if(attach && usbip==null) throw new IOException("尚未安裝 USB/IP 驅動。請到「診斷與設定」安裝，或指定 usbip.exe。");
        if(attach) await CommandRunner(usbip!,new[]{"port"},token);
        if(attach) await OutputLease.Recover(usbip!,s=>Log?.Invoke(s),token);
        if(attach) PriorXbox=XInputEndpoint.Connected();
        int apiPort=FreePort(); do { usbPort=FreePort(); } while(usbPort==apiPort);
        var exe=BackendFiles.Viiper();
        var info=new ProcessStartInfo(exe) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"server",$"--api.addr=127.0.0.1:{apiPort}",$"--usb.addr=127.0.0.1:{usbPort}","--api.device-handler-connect-timeout=60s","--api.auto-attach-local-client=false","--update-notify=none"}) info.ArgumentList.Add(arg);
        broker=Process.Start(info) ?? throw new IOException("無法啟動虛擬輸出服務。");
        job=new ChildProcessJob(broker);
        broker.OutputDataReceived+=(_,e)=>{if(e.Data!=null)Log?.Invoke(e.Data);};
        broker.ErrorDataReceived+=(_,e)=>{if(e.Data!=null)Log?.Invoke(e.Data);};
        broker.BeginOutputReadLine(); broker.BeginErrorReadLine();
        // Lifetime belongs only to this process; disconnect never stops another app's broker.
        client=new ViiperProtocolClient("127.0.0.1",apiPort);
        var deadline=Stopwatch.StartNew();
        while(true)
        {
            token.ThrowIfCancellationRequested();
            if(broker.HasExited) throw new IOException("VIIPER 啟動後立即退出，請匯出診斷日誌。");
            try { await client.PingAsync(token); break; }
            catch(SocketException) when(deadline.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(100,token); }
        }
        bus=await client.BusCreateAsync(token);
        var type=mode switch {"Xbox"=>"xbox360","PS5"=>"dualsensehaptic","Pro1"=>"nspro",_=>"ns2pro"};
        device=await client.AddDeviceAsync(bus.Value,type,new Dictionary<string,object?> { ["source_paced"]=true,["serial_number"]="CFUSION-01",["input_interval_ms"]=4,["protocol_clock_resample"]=true },token);
        if(attach)
        {
            leaseFile=OutputLease.Record(usbPort,device.BusId,device.DevId);
            await CommandRunner(usbip!,new[]{"--tcp-port",usbPort.ToString(),"attach","-r","127.0.0.1","-b",$"{device.BusId}-{device.DevId}"},token);
            // Confirm the import exists before declaring output ready.
            var ports=await CommandRunner(usbip!,new[]{"port"},token);
            ImportedPort=FindOwnedPorts(ports).SingleOrDefault(-1);
            if(ImportedPort<0) throw new IOException("USB/IP 回報成功，但沒有找到本次虛擬手柄的連接埠。");
        }
        stream=await client.OpenStreamAsync(device.BusId,device.DevId,token);
        lifetime=CancellationTokenSource.CreateLinkedTokenSource(token);
        var ioToken=lifetime.Token;
        writer=Task.Run(()=>WriteLoop(ioToken));
        reader=Task.Run(()=>ReadLoop(ioToken));
        Ready=Volatile.Read(ref failed)==0;
    }
    public bool Submit(ReadOnlySpan<byte> frame,long? receivedTimestamp=null)
    {
        if(!Ready) return false;
        if(frames.TryWrite(frame,Mode,receivedTimestamp)) return true;
        // Do not silently drop press/release edges under backpressure.
        FailOnce("虛擬輸出佇列壅塞，已停止本次連接，避免遺失放開按鍵的事件。");
        return false;
    }
    internal bool TryReadPendingFrame(out byte[] frame) => frames.TryRead(out frame!);
    Task WriteLoop(CancellationToken token)=>RunWriter((frame,ct)=>stream!.WriteAsync(frame,ct),token);
    internal async Task RunWriter(Func<byte[],CancellationToken,Task> send,CancellationToken token)
    {
        var frame=new byte[PadPackets.PacketLength(Mode)];
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            while(!token.IsCancellationRequested && !frames.Completed)
            {
                if(frames.TryReadInto(frame,out _) || frames.TrySafetyNeutralInto(frame,out _))
                {
                    deadline.CancelAfter(100);
                    long began=Stopwatch.GetTimestamp();
                    try {await send(frame,deadline.Token).ConfigureAwait(false);}
                    catch(OperationCanceledException) when(!token.IsCancellationRequested)
                    {Interlocked.Increment(ref writeTimeouts);throw new IOException("虛擬輸出寫入逾時，已停止連接以免重播舊按鍵。");}
                    deadline.CancelAfter(Timeout.Infinite);
                    if(deadline.IsCancellationRequested && !token.IsCancellationRequested)
                    {Interlocked.Increment(ref writeTimeouts);throw new IOException("虛擬輸出寫入逾時，已停止連接以免重播舊按鍵。");}
                    Interlocked.Exchange(ref maximumWriteTicks,Math.Max(Interlocked.Read(ref maximumWriteTicks),Stopwatch.GetTimestamp()-began));
                    Interlocked.Increment(ref Written);
                }
                else {await frames.WaitAsync(token).ConfigureAwait(false);Interlocked.Increment(ref WaitingWakeups);}
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception e) { FailOnce("虛擬輸出中斷："+e.Message); }
    }
    async Task ReadLoop(CancellationToken token)
    {
        int size=Mode switch {"Xbox"=>2,"PS5"=>388,"Pro1"=>10,_=>34};
        try
        {
            while(!token.IsCancellationRequested)
            {
                var frame=await stream!.ReadExactAsync(size,token);
                if(AdvancedFeedback?.Invoke(frame)==true) continue;
                var motors=DecodeFeedback(Mode,frame);
                if(motors is {} m) Rumble?.Invoke(m.Large,m.Small);
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception e) { FailOnce("遊戲回饋通道中斷："+e.Message); }
    }
    public static (byte Large,byte Small)? DecodeFeedback(string mode,byte[] f)
    {
        if(mode=="Xbox" && f.Length==2) return (f[0],f[1]);
        if(mode=="PS5" && f.Length==388)
        {
            int len=f[1]|f[2]<<8;
            if(len>384 || len<5 || f[0]!=1 || f[4]!=2) return null; // Audio haptics is a separate capability.
            bool ordinary=(f[5]&3)!=0 || (len>39 && (f[43]&4)!=0);
            return ordinary ? (f[8],f[7]) : null;
        }
        if(mode=="Pro1" && f.Length==10 && (f[8]&1)!=0)
            return (SwitchAmplitude(f.AsSpan(0,4)),SwitchAmplitude(f.AsSpan(4,4)));
        if(mode=="Steam / Pro2" && f.Length==34 && (f[32]&1)!=0)
            return (Pro2Amplitude(f.AsSpan(0,16)),Pro2Amplitude(f.AsSpan(16,16)));
        return null;
    }
    // Ordinary motor approximation only. Native HD waveforms are not forwarded.
    static byte SwitchAmplitude(ReadOnlySpan<byte> f)
    {
        int high=f[1]>>1,low=f[3]>=64?(f[3]-64)*2+(f[2]>>7):0;
        int code=Math.Max(high,low);
        double level=code==0?0:code<16?Math.Pow(2,(code-16)/4.0)*.117:code<32?Math.Pow(2,(code-32)/16.0)*.225:Math.Pow(2,(code-32)/32.0)*.225;
        return (byte)Math.Clamp((int)Math.Round(level*255),0,255);
    }
    static byte Pro2Amplitude(ReadOnlySpan<byte> f)
    {
        if((f[0]&0xf0)!=0x50) return 0;
        ulong max=0;
        for(int j=0;j<3;j++) { ulong bits=0; for(int k=0;k<5;k++) bits|=(ulong)f[1+j*5+k]<<(8*k); max=Math.Max(max,Math.Max((bits>>10)&1023,(bits>>30)&1023)); }
        return (byte)(max*255/1023);
    }
    IEnumerable<int> FindOwnedPorts(string output)
    {
        if(device==null) yield break;
        foreach(int port in OutputLease.Ports(output,usbPort,device.BusId,device.DevId)) yield return port;
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0) return;
        Ready=false;
        frames.Complete();
        lifetime?.Cancel();
        foreach(var task in new[]{reader,writer}) if(task!=null) { try { await task; } catch { } }
        var failures=new List<Exception>();
        if(stream!=null) { try { using var neutral=new CancellationTokenSource(500); await stream.WriteAsync(PadPackets.Encode(new DS4Windows.DS4State(),Mode),neutral.Token); } catch { } try {await stream.DisposeAsync();}catch(Exception ex){failures.Add(ex);} }
        string? portFailure=null;
        async Task<bool> DetachAndVerify()
        {
            if(usbip==null || device==null)return true;
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));
                var ports=await CommandRunner(usbip,new[]{"port"},timeout.Token);
                foreach(int port in FindOwnedPorts(ports))
                {
                    try {await CommandRunner(usbip,new[]{"detach","-p",port.ToString()},timeout.Token);}
                    catch(Exception ex){portFailure=ex.Message;Log?.Invoke("移除本次虛擬連接埠失敗："+ex.Message);}
                }
                for(int attempt=0;attempt<3;attempt++)
                {
                    if(!FindOwnedPorts(await CommandRunner(usbip,new[]{"port"},timeout.Token)).Any())
                    {if(leaseFile!=null)File.Delete(leaseFile);return true;}
                    await Task.Delay(100,timeout.Token);
                }
                portFailure??="USB/IP 仍列出本次虛擬連接埠。";
            }
            catch(Exception ex){portFailure=ex.Message;Log?.Invoke("虛擬連接埠驗證失敗："+ex.Message);}
            return false;
        }
        bool released=await DetachAndVerify();
        if(bus.HasValue && client!=null) {try {using var timeout=new CancellationTokenSource(1500);await client.RemoveBusAsync(bus.Value,timeout.Token);}catch(Exception ex){Log?.Invoke("移除本次虛擬匯流排："+ex.Message);}}
        if(broker!=null)
        {
            try {if(!broker.HasExited){broker.Kill(true);using var timeout=new CancellationTokenSource(1500);await broker.WaitForExitAsync(timeout.Token);}}
            catch(Exception ex){failures.Add(ex);}
            finally {broker.Dispose();}
        }
        try {job?.Dispose();}catch(Exception ex){failures.Add(ex);}
        lifetime?.Dispose();
        // A stopped server can allow a previously busy driver import to retire.
        // Re-check only the endpoint owned by this session, retaining its lease
        // until absence is proven. Never report a swallowed detach as success.
        if(!released)released=await DetachAndVerify();
        if(!released)failures.Add(new IOException("虛擬輸出尚未完成釋放，復原紀錄已保留。"+portFailure));
        if(failures.Count!=0)throw new AggregateException("虛擬輸出清理未完成。",failures);
    }
}
