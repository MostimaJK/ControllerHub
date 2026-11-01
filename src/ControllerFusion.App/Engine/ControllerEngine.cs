using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using ControllerFusion.Input;
using DS4Windows;

namespace ControllerFusion.Engine;

public sealed record EngineStatus(string Phase,string Detail,bool InputReady=false,bool OutputReady=false);

public sealed class ControllerEngine : IAsyncDisposable
{
    readonly FusionInput input=new();
    readonly InputStatistics statistics=new();
    readonly object mappingGate=new();
    readonly InputPacketMapper packetMapper=new();
    readonly InputReadiness readiness=new();
    public object InputStatistics => statistics.Snapshot();
    readonly SemaphoreSlim lifecycle=new(1,1);
    Task? startup;
    CancellationTokenSource? session;
    VirtualOutput? output;
    object? lastOutputStatistics;
    public object? OutputStatistics=>output?.PipelineStatistics ?? lastOutputStatistics;
    FusionFeedback? feedback;
    public bool CleanupSucceeded { get; private set; }=true;
    public bool NativeHdAvailable => feedback?.NativeHdAvailable==true;
    public string ActiveTransport { get; private set; }="";
    public string HidingStatus {get;private set;}="尚未套用 HidHide";
    public object? HapticsStatistics=>feedback?.Statistics;
    public string HapticsStatus => feedback?.Status ?? "尚未連接進階回饋";
    int selectedSlot=-1;
    uint? xboxIndex;
    long lastReport,reportCount,windowStart=Stopwatch.GetTimestamp(),windowReports;
    long feedbackCount;
    int generation;
    string? lastFeedbackError; long lastFeedbackErrorAt;
    Preferences settings=new();
    public EngineStatus Status { get; private set; }=new("尚未連接","選擇手柄與連接方式。");
    public event Action<EngineStatus>? Changed;
    public event Action<string>? Log;
    public event Action<DS4State>? FrameReceived;
    string? fault;
    CancellationTokenSource? runCancellation;
    bool retryable,transportChanged;
    public DS4State? LastFrame { get; private set; }
    internal long LatestInputTimestamp=>Interlocked.Read(ref lastReport);
    public long Reports => Interlocked.Read(ref reportCount);
    public long FeedbackReports => Interlocked.Read(ref feedbackCount);
    public double Hertz { get; private set; }
    public int Battery { get; private set; }=-1;
    public bool Charging { get; private set; }
    public XboxObservation? XboxState => xboxIndex.HasValue ? XInputEndpoint.Read(xboxIndex.Value) : null;
    public bool HasInput => selectedSlot>=0 && Stopwatch.GetElapsedTime(Interlocked.Read(ref lastReport)).TotalSeconds < 2;
    public bool HasOutput => output?.Ready==true && Status.OutputReady;
    public string BluetoothStatus => input.BluetoothStatus;
    public PairCandidate[] Candidates => startup?.IsCompletedSuccessfully==true ? input.Candidates().Where(c=>c.Model.Contains("Pro")).ToArray() : Array.Empty<PairCandidate>();
    public ControllerEngine()
    {
        input.Log+=s=>Log?.Invoke(s);
        input.TimedFrame+=OnFrame;
    }
    public void Update(Preferences p)
    {
        var previous=Volatile.Read(ref settings);Volatile.Write(ref settings,ProfileStore.Copy(p));
        if(previous.HdHaptics!=p.HdHaptics || previous.AdaptiveTriggers!=p.AdaptiveTriggers)
            _=Task.Run(()=>{var current=Volatile.Read(ref settings);feedback?.SetPolicy(current.HdHaptics,current.AdaptiveTriggers);});
    }
    void Set(string phase,string detail,bool hasInput=false,bool hasOutput=false)
    { Status=new(phase,detail,hasInput,hasOutput); Log?.Invoke(phase+"："+detail); Changed?.Invoke(Status); }
    public Task Run(Preferences p,CancellationToken external,bool inputOnly=false)=>Task.Run(()=>RunCore(p,external,inputOnly),external);
    async Task RunCore(Preferences p,CancellationToken external,bool inputOnly)
    {
        if(!CleanupSucceeded){Set("釋放未完成","上次連接資源尚未完整釋放，請重新啟動程式，讓復原流程完成後再連接。");return;}
        using var run=CancellationTokenSource.CreateLinkedTokenSource(external);
        runCancellation=run;
        Update(p);
        try
        {
            int switches=0;
            for(int attempt=0;;attempt++)
            {
                run.Token.ThrowIfCancellationRequested();
                p=ProfileStore.Copy(Volatile.Read(ref settings));
                if(p.AutoTransport) p.Transport=await Task.Run(()=>FusionInput.HasUsbDevice(p.Controller),run.Token)?"USB 有線":"藍牙";
                await RunOnce(p,run.Token,inputOnly);
                if(!retryable || run.IsCancellationRequested) break;
                var live=Volatile.Read(ref settings);
                if(transportChanged && live.AutoTransport && ++switches<=4) { attempt--; continue; }
                if(!live.AutoReconnect || attempt>=3) break;
                Set("等待重新連接",$"輸入中斷；準備第 {attempt+1}/3 次重試。可隨時取消。");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(8,2<<attempt)),run.Token);
            }
        }
        catch(OperationCanceledException) {Set("已停止","自動連接已取消。");}
        finally { if(ReferenceEquals(runCancellation,run))runCancellation=null; }
    }
    async Task RunOnce(Preferences p,CancellationToken external,bool inputOnly)
    {
        await lifecycle.WaitAsync(external);
        if(!CleanupSucceeded)
        {
            lifecycle.Release();
            Set("釋放未完成","上次連接資源尚未完整釋放，請重新啟動程式，讓復原流程完成後再連接。");
            return;
        }
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(external);
        try
        {
            session=linked;CleanupSucceeded=true;lastOutputStatistics=null;
            var token=linked.Token;
            Update(p); ActiveTransport=p.Transport; selectedSlot=-1; fault=null; retryable=false; transportChanged=false;
            Interlocked.Increment(ref generation);
            Interlocked.Exchange(ref reportCount,0); Interlocked.Exchange(ref feedbackCount,0);
            statistics.Reset();readiness.Reset();
            windowReports=0; windowStart=Stopwatch.GetTimestamp(); Hertz=0; LastFrame=null; Battery=-1; Charging=false;
            Set("準備連接","啟動隔離的手柄輸入引擎。");
            input.Select(p.Controller,p.Transport);
            startup = Task.Run(()=>input.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ControllerFusion","Input")));
            await startup.WaitAsync(token);
            if(p.Controller=="Switch 2 Pro" && p.Transport=="藍牙") await Task.Run(input.PrepareBluetooth,token);
            Set("搜尋裝置",p.Transport=="藍牙" ? (p.Controller=="Switch 2 Pro"?"已關聯請短按 Home；只有首次配對才長按 SYNC。":"請先在 Windows 完成配對，已配對請按 PS 喚醒。") : p.Controller=="Switch 2 Pro"?"用 USB 資料線連接後，短按 Home 喚醒；不要按 SYNC。":"用 USB 資料線連接後，短按 PS 喚醒。");
            var wait=Stopwatch.StartNew();
            var lastScan=TimeSpan.FromSeconds(-3);
            bool paired=false;
            bool awaitingReconnect=false;
            var searchLimit=TimeSpan.FromSeconds(35);
            using var searchDeadline=CancellationTokenSource.CreateLinkedTokenSource(token);
            searchDeadline.CancelAfter(TimeSpan.FromSeconds(35));
            var searchToken=searchDeadline.Token;
            using var timeoutNotice=searchToken.Register(()=>
            {
                if(!token.IsCancellationRequested) Set("正在清理連線",awaitingReconnect?"等待重新廣播已逾時，正在釋放藍牙資源。":"搜尋期限已到，正在等待 Windows 釋放連線資源。");
            });
            try
            {
            while(true)
            {
                searchToken.ThrowIfCancellationRequested();
                if(wait.Elapsed-lastScan>TimeSpan.FromSeconds(2)) { await Task.Run(input.Rescan,searchToken); lastScan=wait.Elapsed; }
                var model=p.Controller=="Switch 2 Pro"?"Switch2Pro":"DualSense";
                var matches=input.Devices().Where(d=>d.Model==model && d.Transport==p.Transport).ToArray();
                if(matches.Length>1) throw new IOException("找到多個同型號手柄，請暫時只保留一個，再連接。");
                if(matches.Length==1) { if(selectedSlot!=matches[0].Slot)readiness.Reset();Volatile.Write(ref selectedSlot,matches[0].Slot); Battery=matches[0].Battery; if(HasInput && readiness.IsReady(Stopwatch.GetTimestamp())) break; }
                if(p.Controller=="Switch 2 Pro" && p.Transport=="藍牙" && !paired)
                {
                    var candidates=Candidates;
                    if(candidates.Length==1)
                    {
                        paired=true;
                        Set(candidates[0].Remembered?"重新連線":"藍牙配對",candidates[0].Remembered?"正在連接已記住的 Switch 2 Pro。":"正在配對找到的 Switch 2 Pro。");
                        await input.Associate(candidates[0].Id,searchToken);
                        awaitingReconnect=true;
                        searchLimit=TimeSpan.FromMinutes(2);
                        wait.Restart(); lastScan=TimeSpan.FromSeconds(-3);
                        searchDeadline.CancelAfter(searchLimit);
                        Set("等待手柄重連","關聯指令已完成，等待有效輸入；若手柄已休眠，請按一下 Home 喚醒。");
                    }
                    else if(candidates.Length>1) throw new IOException("找到多個 Switch 2 Pro 配對候選，請只讓目標手柄進入同步模式。");
                }
                if(wait.Elapsed>searchLimit) throw new TimeoutException(awaitingReconnect?"關聯已完成，但未收到手柄重新廣播；請短按 Home 喚醒後重試。":"35 秒內未收到目標手柄的有效輸入。請確認連接方式、資料線或配對狀態，再重試。");
                await Task.Delay(100,searchToken);
            }
            }
            catch(OperationCanceledException) when(!token.IsCancellationRequested) { throw new TimeoutException(awaitingReconnect?"關聯已完成，但兩分鐘內未收到有效輸入；請按 Home 喚醒後重試。":"搜尋或配對超過 35 秒。請確認手柄已喚醒／進入配對模式。藍牙掃描："+BluetoothStatus); }
            searchDeadline.CancelAfter(Timeout.InfiniteTimeSpan);
            Set("輸入已就緒","已收到實體手柄報告；正在建立虛擬輸出。",true);
            feedback=input.OpenFeedback(selectedSlot,p.Output);
            feedback.SetPolicy(p.HdHaptics,p.AdaptiveTriggers);
            try {feedback.ConfigureAudio(p.AudioHaptics,p.AudioGain,p.AudioReplace,p.AudioEndpointId);}catch(Exception e){Log?.Invoke(e.Message);}
            if(!inputOnly)
            {
            var activeOutput=new VirtualOutput(); output=activeOutput;
            output.Log+=s=>Log?.Invoke(s);
            var activeFeedback=feedback;
            output.AdvancedFeedback=frame=> {
                if(!ReferenceEquals(output,activeOutput)) return true;
                var current=Volatile.Read(ref settings);
                try { bool handled=activeFeedback.Route(frame,(int)current.Rumble,current.AdaptiveTriggers,current.HdHaptics); if(handled) Interlocked.Increment(ref feedbackCount); return handled; }
                catch(Exception ex) { var now=Stopwatch.GetTimestamp(); if(lastFeedbackError!=ex.Message || Stopwatch.GetElapsedTime(lastFeedbackErrorAt,now).TotalSeconds>5) {lastFeedbackError=ex.Message;lastFeedbackErrorAt=now;Log?.Invoke("進階回饋："+ex.Message);} return true; }
            };
            output.Fault+=s=>{if(!ReferenceEquals(output,activeOutput))return;fault=s;Log?.Invoke(s);try {linked.Cancel();}catch(ObjectDisposedException){}};
            output.Rumble+=(large,small)=> { if(!ReferenceEquals(output,activeOutput)) return; Interlocked.Increment(ref feedbackCount); SendRumble(large,small); };
            await output.Start(p.Output,token);
            var priorXbox=output.PriorXbox;
            if(p.Output=="Xbox")
            {
                for(int i=0;i<40;i++)
                {
                    var added=XInputEndpoint.Connected().Except(priorXbox).ToArray();
                    if(added.Length==1) {xboxIndex=added[0];break;}
                    if(added.Length>1) throw new IOException("多個 Xbox 裝置同時到達，無法確認本次輸出；請重試。");
                    await Task.Delay(100,token);
                }
                if(xboxIndex==null) throw new IOException("虛擬 USB 已建立，但 XInput 沒有可用控制器槽位；請檢查其他 Xbox 手柄與驅動。");
            }
            if(p.Output!="Xbox")
            {
                var endpoint=await VirtualHidEndpoint.Wait(p.Output,output.ImportedPort,token);
                endpoint.CloseDevice();
            }
            if(p.HidePhysical) HidingStatus=input.ConfigureHiding(selectedSlot,true);
            Set("遊戲輸出已就緒",$"{p.Controller} → {p.Output}；普通震動回饋已接入。",true,true);
            }
            else Set("輸入測試中","僅讀取實體手柄；可到震動頁測試普通震動。",true);
            long transportProbe=Stopwatch.GetTimestamp();
            while(true)
            {
                await Task.Delay(250,token);
                if(!HasInput) {retryable=true;throw new IOException("實體輸入超過 2 秒沒有更新，已停止虛擬輸出。");}
                if(p.Transport=="藍牙" && Volatile.Read(ref settings).AutoTransport && Stopwatch.GetElapsedTime(transportProbe).TotalSeconds>=2)
                {
                    transportProbe=Stopwatch.GetTimestamp();
                    if(await Task.Run(()=>FusionInput.HasUsbDevice(p.Controller),token)) {transportChanged=retryable=true;Set("切換 USB","正在停止藍牙來源並切換至 USB。");break;}
                }
                var device=input.Devices().FirstOrDefault(d=>d.Slot==selectedSlot);
                Battery=device?.Battery ?? -1; Charging=device?.Charging ?? false;
            }
        }
        catch(OperationCanceledException) { Set(fault==null?"已停止":"連接未完成",fault??"連接已取消，正在釋放虛擬手柄。",HasInput); }
        catch(Exception e) { if(e is TimeoutException)retryable=true; Set("連接未完成",e.Message,HasInput); }
        finally
        {
            var current=output; output=null;
            try
            {
                try { SendRumble(0,0); } catch(Exception ex) {CleanupSucceeded=false;Log?.Invoke("停止震動："+ex.Message);}
                try {if(current!=null) await current.DisposeAsync();} catch(Exception ex) {CleanupSucceeded=false;Log?.Invoke("虛擬輸出清理："+ex.Message);} finally {if(current!=null)lastOutputStatistics=current.PipelineStatistics;}
                try {feedback?.Dispose();}
                catch(AggregateException ex) when(ex.Flatten().InnerExceptions.All(e=>e is DeferredFeedbackRetirementException))
                {Log?.Invoke("進階回饋停止已交由實體輸入生命週期完成。");}
                catch(Exception ex) {CleanupSucceeded=false;Log?.Invoke("進階回饋清理："+ex.Message);} finally {feedback=null;}
                if(startup!=null) { try {await startup;} catch { } }
                try { await Task.Run(input.Stop); } catch(Exception ex) {CleanupSucceeded=false;retryable=false;Set("釋放未完成",ex.Message);}
            }
            finally
            {
                if(!CleanupSucceeded)retryable=false;
                selectedSlot=-1; xboxIndex=null; session=null; Battery=-1; Charging=false; Hertz=0; LastFrame=null;
                lifecycle.Release();
                Status=Status with { InputReady=false,OutputReady=false };
                if(CleanupSucceeded && Status.Phase=="已停止")
                    Status=Status with {Detail="手柄已斷開，資源已釋放。"};
                else if(!CleanupSucceeded && Status.Phase!="釋放未完成")
                    Status=new("釋放未完成","部分連接資源未完整釋放，請到診斷與設定查看記錄。");
                Changed?.Invoke(Status);
            }
        }
    }
    void OnFrame(int slot,DS4State state,long receivedTimestamp)
    {
        if(slot!=Volatile.Read(ref selectedSlot)) return;
        var now=Stopwatch.GetTimestamp(); readiness.Observe(now);statistics.Observe(now); Interlocked.Exchange(ref lastReport,receivedTimestamp);
        Interlocked.Increment(ref reportCount); windowReports++;
        if(Stopwatch.GetElapsedTime(windowStart,now).TotalSeconds>=1) { Hertz=windowReports/Stopwatch.GetElapsedTime(windowStart,now).TotalSeconds; windowStart=now; windowReports=0; }
        LastFrame=state;
        var p=Volatile.Read(ref settings);
        var target=output;
        if(target!=null)
        {
            lock(mappingGate)
            {
                Span<byte> packet=stackalloc byte[33];
                int length=packetMapper.Write(state,p,target.Mode,packet);
                target.Submit(packet[..length],receivedTimestamp);
            }
        }
        try { FrameReceived?.Invoke(state); } catch { /* Diagnostics cannot break input delivery. */ }
    }
    void SendRumble(byte large,byte small)
    {
        double gain=Math.Clamp(Volatile.Read(ref settings).Rumble/100,0,1);
        input.Rumble(Volatile.Read(ref selectedSlot),(byte)(large*gain),(byte)(small*gain));
    }
    public void RefreshHiding()
    {
        var p=Volatile.Read(ref settings);
        HidingStatus=!HasOutput && p.HidePhysical?"請先建立虛擬輸出，再套用隱藏設定。":input.ConfigureHiding(selectedSlot,p.HidePhysical);
        Log?.Invoke(HidingStatus);
    }
    public async Task<string> RunBoundedAudioProbe()
    {
        var original=ProfileStore.Copy(Volatile.Read(ref settings));
        var probe=ProfileStore.Copy(original);probe.AudioHaptics=true;probe.AutoReconnect=false;
        try
        {
            Update(probe);await Task.Run(RefreshAudio);
            await TestAudio();await Task.Delay(200);
            return HapticsStatus;
        }
        finally {Update(original);await Task.Run(RefreshAudio);}
    }
    public async Task<string> TestMotionForwarding()
    {
        var current=output;
        if(!HasInput || !HasOutput || current?.Mode!="PS5" || !Volatile.Read(ref settings).MotionEnabled)
            throw new InvalidOperationException("請先啟用體感並連接 PS5 輸出模式。");
        using var token=CancellationTokenSource.CreateLinkedTokenSource(runCancellation?.Token??CancellationToken.None);
        token.CancelAfter(4000);
        var expected=new Queue<byte[]>();var gate=new object();
        void Capture(DS4State state)
        {
            if(!ReferenceEquals(output,current) || state.Motion==null)return;
            var wire=PadPackets.Encode(PadPackets.Map(state,Volatile.Read(ref settings)),"PS5");
            lock(gate){expected.Enqueue(wire.AsSpan(21,12).ToArray());while(expected.Count>128)expected.Dequeue();}
        }
        FrameReceived+=Capture;
        try
        {
            await Task.Delay(150,token.Token);
            HidDevice? hid=null;
            try
            {
                hid=await VirtualHidEndpoint.Wait("PS5",current.ImportedPort,token.Token);
                hid.OpenDevice(false);
                var data=new byte[hid.Capabilities.InputReportByteLength];int matched=0;
                while(matched<3)
                {
                    token.Token.ThrowIfCancellationRequested();
                    if(!ReferenceEquals(output,current))throw new IOException("體感測試時連接已改變。");
                    var result=await Task.Run(()=>hid.ReadFile(data,300),token.Token);
                    if(result!=HidDevice.ReadStatus.Success || data[0]!=1)continue;
                    bool match;lock(gate)match=expected.Any(sample=>data.AsSpan(16,12).SequenceEqual(sample));
                    if(match)matched++;
                }
                return "Windows 已讀回 3 份與實體來源相符的六軸資料。";
            }
            finally {hid?.CloseDevice();}
        }
        finally {FrameReceived-=Capture;}
    }

    public async Task TestGameHd()
    {
        var current=output;
        if(current?.Ready!=true || current.Mode!="PS5")throw new InvalidOperationException("請先連接 PS5 輸出模式。");
        using var token=CancellationTokenSource.CreateLinkedTokenSource(runCancellation?.Token??CancellationToken.None);
        token.CancelAfter(5000);
        await Task.Run(()=>FusionAudioTests.PlayVirtualHaptics(current.ImportedPort,token.Token),token.Token);
    }
    public async Task TestAudio()
    {
        var p=Volatile.Read(ref settings);
        if(!HasInput || !p.AudioHaptics)throw new InvalidOperationException("請先連接手柄並啟用音訊轉震動。");
        using var token=CancellationTokenSource.CreateLinkedTokenSource(runCancellation?.Token??CancellationToken.None);
        token.CancelAfter(5000);
        await Task.Run(()=>FusionAudioTests.PlaySystemTone(p.AudioEndpointId,token.Token),token.Token);
    }
    public void TestHd()
    {
        if(!HasInput || feedback?.TestHd()!=true) throw new InvalidOperationException("請先連接 Switch 2 Pro；原生 HD 測試尚未獲得輸出通道。");
    }
    public async Task TestGameTriggers(int milliseconds=3000)
    {
        var current=output;
        if(!HasInput || !HasOutput || current?.Mode!="PS5" || feedback?.AdaptiveTriggersAvailable!=true || !Volatile.Read(ref settings).AdaptiveTriggers)
            throw new InvalidOperationException("請先連接 PS5 手柄及 PS5 輸出，並啟用自適應扳機。");
        using var token=CancellationTokenSource.CreateLinkedTokenSource(runCancellation?.Token??CancellationToken.None);
        token.CancelAfter(6500);
        HidDevice? hid=null;
        try
        {
            hid=await VirtualHidEndpoint.Wait("PS5",current.ImportedPort,token.Token);
            hid.OpenDevice(false);
            byte[] Packet(bool enabled) {var data=new byte[hid.Capabilities.OutputReportByteLength];var report=Input.FusionInput.CreateTriggerTestReport(enabled);report.AsSpan(0,Math.Min(report.Length,data.Length)).CopyTo(data);return data;}
            try
            {
                if(!await Task.Run(()=>hid.WriteOutputReportViaInterrupt(Packet(true),500),token.Token))throw new IOException("虛擬扳機指令未完成。");
                await Task.Delay(Math.Clamp(milliseconds,500,5000),token.Token);
            }
            finally {if(!await Task.Run(()=>hid.WriteOutputReportViaInterrupt(Packet(false),500)))throw new IOException("虛擬扳機解除指令未完成。");}
        }
        finally {hid?.CloseDevice();}
    }

    public async Task TestTriggers(int milliseconds=1500)
    {
        var target=feedback;
        if(!HasInput || target?.AdaptiveTriggersAvailable!=true) throw new InvalidOperationException("請先連接 PS5 DualSense。");
        using var token=CancellationTokenSource.CreateLinkedTokenSource(runCancellation?.Token??CancellationToken.None);
        token.CancelAfter(4500);
        try {await Task.Run(()=>target.SetTriggerPreview(true),token.Token);await Task.Delay(Math.Clamp(milliseconds,500,3000),token.Token);}
        finally {await Task.Run(()=>target.SetTriggerPreview(false));}
    }
    public void RefreshAudio() { var p=Volatile.Read(ref settings); feedback?.ConfigureAudio(p.AudioHaptics,p.AudioGain,p.AudioReplace,p.AudioEndpointId); }
    public async Task<(double Pitch,double Yaw,double Roll)> CalibrateMotion()
    {
        if(!HasInput) throw new InvalidOperationException("請先連接手柄。");
        var epoch=Volatile.Read(ref generation);
        var samples=new List<SixAxis>(); var gate=new object();
        void Capture(DS4State s) { if(s.Motion is {} m) lock(gate) samples.Add(m); }
        FrameReceived+=Capture;
        try { await Task.Delay(1800); }
        finally { FrameReceived-=Capture; }
        if(!HasInput || epoch!=Volatile.Read(ref generation)) throw new InvalidOperationException("校準時連接已改變，未套用。");
        lock(gate) return MotionPipeline.Calibrate(samples);
    }
    public async Task TestRumble()
    {
        if(!HasInput) throw new InvalidOperationException("請先連接實體手柄。");
        var epoch=Volatile.Read(ref generation);
        try { SendRumble(150,90); await Task.Delay(500); }
        finally { if(epoch==Volatile.Read(ref generation) && HasInput) SendRumble(0,0); }
    }
    public async Task TestGameRumble()
    {
        var index=xboxIndex;
        var activeOutput=output;
        if(index==null || !HasOutput) throw new InvalidOperationException("請先連接 Xbox 輸出模式。");
        try { XInputEndpoint.Rumble(index.Value,30000,15000); await Task.Delay(500); }
        finally { if(ReferenceEquals(output,activeOutput) && HasOutput) XInputEndpoint.Rumble(index.Value,0,0); }
    }
    public void Cancel()
    {
        try { Volatile.Read(ref runCancellation)?.Cancel(); Volatile.Read(ref session)?.Cancel(); }
        catch(ObjectDisposedException) { /* Completion won the cancellation race. */ }
    }
    public async ValueTask DisposeAsync()
    {
        Cancel();
        await lifecycle.WaitAsync();
        try { if(startup!=null) { try { await startup; } catch { } } await Task.Run(input.Dispose); }
        catch {CleanupSucceeded=false;throw;}
        finally { lifecycle.Release(); }
    }
}
