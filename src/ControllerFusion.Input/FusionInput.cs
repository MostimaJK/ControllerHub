// ControllerFusion integration facade. Distributed under GPL-3.0-or-later.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DS4Windows;

namespace ControllerFusion.Input;

public sealed record InputDevice(int Slot, string Model, string Transport, int Battery, bool Charging);
public sealed record PairCandidate(int Id, string Model, bool Remembered);

// Compiled inside the pinned DS4Windows assembly to keep its internal API private.
public sealed partial class FusionInput : IDisposable
{
    internal static FusionInput Current;
    public static bool InputOnly => Current != null;
    private string targetController="",targetTransport="";
    private bool Wants(string controller,string transport)=>
        (targetController=="" || targetController==controller) && (targetTransport=="" || targetTransport==transport);
    internal static bool EnableSwitch2Usb=>Current==null || Current.Wants("Switch 2 Pro","USB 有線");
    internal static bool EnableSwitch2Bluetooth=>Current==null || Current.Wants("Switch 2 Pro","藍牙");
    public void Select(string controller,string transport)
    {
        if(service?.running==true) throw new IOException("請先停止目前的實體輸入工作階段。");
        targetController=controller;targetTransport=transport;
    }
    private void ApplySelection()=>Global.DeviceOptions.DualSenseOpts.Enabled=targetController=="" || targetController=="PS5 DualSense";
    public static bool IsUsbipDevice(string path,int importedPort) => importedPort>=0 && Global.TryResolveUsbIpWin2Device(path,out bool ancestor,out int port) && ancestor && port==importedPort;
    public static bool IsUsbipDevice(string path) => Global.TryResolveUsbIpWin2Device(path,out bool ancestor,out _) && ancestor;
    public static DS4Windows.Switch2.Switch2Advertisement NormalizeKnownProWake(
        DS4Windows.Switch2.Switch2Advertisement advertisement)
    {
        if(advertisement.IsWake && advertisement.IsForThisHost &&
            advertisement.Model==DS4Windows.Switch2.Switch2ControllerModel.ProController2)
            return new DS4Windows.Switch2.Switch2Advertisement(advertisement.Model,
                advertisement.ProductId,false,advertisement.Host);
        return advertisement;
    }
    private ControlService service;
    private readonly Func<bool> shutdownOverride;
    public FusionInput() { }
    internal FusionInput(Func<bool> shutdownOverride) {this.shutdownOverride=shutdownOverride;}
    private EventHandler<DebugEventArgs> audioLog;
    private bool stopFailed;
    private readonly object traceGate=new();
    private string lastAdvertisement="";
    private long lastAdvertisementTicks;
    internal static void TraceAdvertisement(string model,bool wake,string host)
    {
        var owner=Current;if(owner==null)return;
        string detail=$"Switch 2 Bluetooth advertisement: model={model}; wake={wake}; host={host}.";
        lock(owner.traceGate)
        {
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            if(owner.lastAdvertisement==detail && System.Diagnostics.Stopwatch.GetElapsedTime(owner.lastAdvertisementTicks,now).TotalSeconds<3)return;
            owner.lastAdvertisement=detail;owner.lastAdvertisementTicks=now;
        }
        NLog.LogManager.GetLogger("ControllerFusion.Input").Info(detail);owner.Log?.Invoke(detail);
    }
    public event Action<int, DS4State> Frame;
    public event Action<int, DS4State, long> TimedFrame;
    public event Action<string> Log;

    public void Start(string directory)
    {
        FusionHidingJournal.Recover();
        if(stopFailed) throw new IOException("上次實體輸入未完整釋放，請重新啟動程式。");
        if (service != null)
        {
            ApplySelection();
            if(!service.running && !service.Start(false)) throw new IOException("無法重新啟動實體輸入服務。");
            return;
        }
        Directory.CreateDirectory(Path.Combine(directory, "Profiles"));
        Log?.Invoke("隔離配置初始化");
        Global.SaveWhere(directory);
        Global.SuppressViiperSetupPrompt = true;
        Global.Load();
        Global.VerboseStartupLogging = true;
        var config = new NLog.Config.LoggingConfiguration();
        config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, new NLog.Targets.FileTarget("fusion") { FileName=Path.Combine(directory,"engine.log") });
        NLog.LogManager.Configuration = config;
        Global.UseExclusiveMode = false;
        Global.DeviceOptions.DS4DeviceOpts.Enabled = false;
        ApplySelection();
        Global.DeviceOptions.SwitchProDeviceOpts.Enabled = false;
        Global.DeviceOptions.JoyConDeviceOpts.Enabled = false;
        Global.DeviceOptions.DS3DeviceOpts.Enabled = false;
        for (int i = 0; i < Global.DinputOnly.Length; i++)
        {
            Global.DinputOnly[i] = true;
            Global.ProfilePath[i] = Global.OlderProfilePath[i] = "FusionInputOnly";
        }
        Global.SaveAsNewProfile(0, "FusionInputOnly");
        var profile = Path.Combine(directory,"Profiles","FusionInputOnly.xml");
        var xml = new System.Xml.XmlDocument(); xml.Load(profile);
        xml.SelectSingleNode("//DinputOnly").InnerText = "True"; xml.Save(profile);
        Current = this;
        audioLog=(_,e)=> {if(e.Data.StartsWith("Audio Haptics",StringComparison.Ordinal)) {NLog.LogManager.GetLogger("ControllerFusion.Input").Info(e.Data);Log?.Invoke(e.Data);}};
        AppLogger.GuiLog+=audioLog;
        try
        {
            Log?.Invoke("建立輸入核心");
            service = new ControlService(new DS4WinWPF.ArgumentParser());
            service.Debug += (_,e) =>
            {
                if(e.Data.StartsWith("Switch 2 Pro USB",StringComparison.Ordinal) ||
                   (e.Data.StartsWith("Switch 2 ",StringComparison.Ordinal) && e.Data.Contains("Bluetooth")))
                { NLog.LogManager.GetLogger("ControllerFusion.Input").Info(e.Data); Log?.Invoke(e.Data); }
            };
            DS4Windows.Program.rootHub = service;
            DS4WinWPF.App.rootHub = service;
            Log?.Invoke("啟動輸入核心");
            if (!service.Start(false)) throw new InvalidOperationException("實體輸入引擎啟動失敗。");
            Log?.Invoke("輸入核心已啟動");
        }
        catch { Dispose(); throw; }
    }

    internal static bool Publish(DS4Device device, int slot, DS4State projected)
    {
        var owner = Current;
        if (owner == null) return false;
        var frame = new DS4State();
        (projected ?? device.getCurrentStateRef()).CopyTo(frame);
        var m=frame.Motion;
        if(m!=null) frame.Motion=new SixAxis(-m.gyroYawFull,m.gyroPitchFull,-m.gyroRollFull,-m.accelXFull,-m.accelYFull,m.accelZFull,m.elapsed);
        long received=device is DS4Windows.Switch2.Switch2RuntimeInputDevice runtime ? runtime.FusionInputTimestampQpc : 0;
        if(received<=0)received=System.Diagnostics.Stopwatch.GetTimestamp();
        try { owner.TimedFrame?.Invoke(slot,frame,received); }
        catch(Exception ex) {owner.Log?.Invoke("輸入接收失敗："+ex.Message);}
        try { owner.Frame?.Invoke(slot, frame); }
        catch (Exception ex) { owner.Log?.Invoke("輸入接收失敗：" + ex.Message); }
        return true;
    }

    public InputDevice[] Devices() => service == null ? Array.Empty<InputDevice>() :
        service.DS4Controllers.Select((d, slot) => d == null ? null :
            new InputDevice(slot, d.DeviceType.ToString(),
                d.ConnectionType == ConnectionType.BT ? "藍牙" : "USB 有線", d.Battery, d.Charging))
            .Where(d => d != null).ToArray();

    public string ConfigureHiding(int slot,bool enabled)=>service?.FusionConfigureHiding(slot,enabled) ?? "請先連接手柄。";
    public static bool HidingInstalled=>Global.IsHidHideInstalled();
    public static bool HasUsbDevice(string controller)
    {
        int vendor=controller=="Switch 2 Pro"?0x057e:0x054c, product=controller=="Switch 2 Pro"?0x2069:0x0ce6;
        foreach(var candidate in HidDevices.Enumerate(vendor,product))
        {
            try { if(candidate.DevicePath.Contains($"vid_{vendor:x4}&pid_{product:x4}",StringComparison.OrdinalIgnoreCase) && !IsUsbipDevice(candidate.DevicePath)) return true; }
            finally { candidate.CloseDevice(); }
        }
        return false;
    }
    public void Rescan() { if (service?.running == true) service.HotPlug(); }
    public void Stop()
    {
        if(service?.running==true && !service.Stop(false,true))
        {stopFailed=true;throw new IOException(targetTransport=="USB 有線"?"USB 手柄尚未完成釋放。請關閉程式，拔下 USB 等 5 秒再接回後重試。":"實體輸入尚未完成釋放，請關閉程式後重試。");}
        FusionHidingJournal.Complete();
    }
    public string BluetoothStatus
    {
        get { var s=service?.GetSwitch2BluetoothDiscoveryStatus(); return s==null ? "尚未啟動" : s.State+" / "+s.Failure; }
    }
    public void PrepareBluetooth()
    {
        var s=service;
        if(s==null) return;
        var status=s.GetSwitch2BluetoothDiscoveryStatus();
        if(status.State is DS4Windows.Switch2.Switch2BluetoothDiscoveryState.Unavailable or
            DS4Windows.Switch2.Switch2BluetoothDiscoveryState.StartFailed or
            DS4Windows.Switch2.Switch2BluetoothDiscoveryState.Interrupted or
            DS4Windows.Switch2.Switch2BluetoothDiscoveryState.Stopped)
        {
            Log?.Invoke("重新建立藍牙掃描工作階段："+BluetoothStatus);
            if(!s.Stop(false,true) || !s.Start(false)) throw new IOException("無法重建手柄搜尋工作階段；請重新啟動程式。");
        }
    }
    public PairCandidate[] Candidates() => service == null ? Array.Empty<PairCandidate>() :
        service.GetSwitch2BluetoothAssociationCandidates().Select(c =>
            new PairCandidate(c.Id, c.Model.ToString(), c.IsRemembered)).ToArray();
    public async Task Associate(int id, CancellationToken token)
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();
        var result = await service.AssociateSwitch2BluetoothAsync(id, token);
        var detail=$"藍牙關聯結果：{result.Failure}；最後指令：{result.LastCompletedStep}；耗時 {watch.Elapsed.TotalSeconds:0.0} 秒";
        NLog.LogManager.GetLogger("ControllerFusion.Input").Info(detail); Log?.Invoke(detail);
        if (!result.Succeeded) throw new IOException("藍牙配對失敗：" + result.Failure+"；最後完成："+result.LastCompletedStep);
    }
    public void Rumble(int slot, byte large, byte small)
    {
        var s = service;
        var device = s != null && (uint)slot < s.DS4Controllers.Length ? s.DS4Controllers[slot] : null;
        device?.setRumble(small, large);
    }
    public void Dispose()
    {
        bool stopped=shutdownOverride?.Invoke() ?? (service?.FusionStopAndShutDown(true) ?? true);
        if(!stopped){stopFailed=true;throw new IOException("實體輸入尚未完成關閉，未將資源標記為已釋放。");}
        service=null;
        try {FusionHidingJournal.Complete();}
        finally {if(audioLog!=null)AppLogger.GuiLog-=audioLog;audioLog=null;if(Current==this)Current=null;}
    }
}
