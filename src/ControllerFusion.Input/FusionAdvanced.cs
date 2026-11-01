// GPL-3.0-or-later. Exact-device feedback leases over the pinned native owners.
using System;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Buffers.Binary;
using DS4Windows;
using DS4Windows.Switch2;
using DS4Windows.InputDevices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ControllerFusion.Input;

// A session stop can retain exact USB I/O. The enclosing physical owner must
// finish it; only a failed physical stop makes this a final cleanup failure.
public sealed class DeferredFeedbackRetirementException : InvalidOperationException
{
    public DeferredFeedbackRetirementException() : base("原生遊戲回饋通道尚未完成退役，交由實體輸入生命週期繼續清理。") { }
}

public sealed record FusionAudioSource(string Id,string Name);
public sealed record FusionHapticsStatistics(long AcceptedPcmWindows,long RejectedPcmWindows,long RejectedNeutralWindows,string LastRejection);
public sealed partial class FusionInput
{
    public static FusionAudioSource[] AudioSources()
    {
        using var enumerator=new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active).Where(e=>!e.FriendlyName.Contains("Wireless Controller",StringComparison.OrdinalIgnoreCase) && !e.FriendlyName.Contains("DualSense",StringComparison.OrdinalIgnoreCase) && !DualSenseAudioPassthrough.TryGetEndpointUsbipPort(e,out _)).Select(e=>new FusionAudioSource(e.ID,e.FriendlyName)).ToArray();
    }
    public static byte[] CreateTriggerTestReport(bool enabled)
    {
        var effect=TriggerLabEffectEncoder.Encode(new TriggerLabEffectSettings {ForcePercent=35,StartPercent=25},enabled);
        DualSenseDevice.TriggerEffectData data=default;
        data.ChangeRaw(effect.Mode,effect.ZoneMaskLow,effect.ZoneMaskHigh,effect.Data0,effect.Data1,effect.Data2,effect.Data3,effect.Frequency);
        var report=new byte[63];report[0]=2;report[1]=12;
        DualSenseDsxOverlay.WriteTrigger(report,11,data);
        DualSenseDsxOverlay.WriteTrigger(report,22,data);
        return report;
    }
    public FusionFeedback OpenFeedback(int slot, string output)
    {
        var device=service?.DS4Controllers.ElementAtOrDefault(slot) ?? throw new InvalidOperationException("手柄已離線。");
        return new FusionFeedback(device,slot,output);
    }
}

public sealed class FusionFeedback : IDisposable
{
    readonly object gate=new();
    readonly DS4Device device;
    readonly int slot;
    readonly string mode;
    readonly AudioHapticsService audio=new();
    Switch2VirtualFeedbackSession nintendo;
    BufferedWaveProvider pcm;
    WasapiOut player;
    MMDevice endpoint;
    readonly PcmHapticsResampler resampler=new();
    long lastPcm,nextEndpointRetry,pcmAccepted,pcmRejected,pcmNeutralRejected;
    string lastPcmRejection="";
    public FusionHapticsStatistics Statistics=>new(Interlocked.Read(ref pcmAccepted),Interlocked.Read(ref pcmRejected),Interlocked.Read(ref pcmNeutralRejected),Volatile.Read(ref lastPcmRejection));
    bool disposed,replaceAudio,policyHd=true,policyAdaptive=true;
    string status="尚未收到進階回饋";
    bool audioEnabled;
    public string Status {get=>status+(pcmAccepted+pcmRejected>0?$"\nHD 片段：成功 {Interlocked.Read(ref pcmAccepted)}／略過 {Interlocked.Read(ref pcmRejected)}":"")+(audioEnabled?"\n"+audio.GetFusionDiagnostics(slot):"");private set=>status=value;}
    public bool NativeHdAvailable=>nintendo!=null;
    public bool AdaptiveTriggersAvailable=>device is DualSenseDevice;

    internal FusionFeedback(DS4Device device,int slot,string mode)
    {
        this.device=device;this.slot=slot;this.mode=mode;
        if(device is Switch2RuntimeInputDevice target && target.TryGetFeedbackBinding(out var dg,out var tg))
        {
            var source=mode switch {"Xbox"=>ControllerFeedbackSource.Xbox360VirtualDevice,"PS5"=>ControllerFeedbackSource.DualSenseVirtualDevice,"Steam / Pro2"=>ControllerFeedbackSource.Switch2VirtualDevice,_=>ControllerFeedbackSource.Invalid};
            if(source!=ControllerFeedbackSource.Invalid) target.TryCreateVirtualFeedbackSession(source,dg,tg,out nintendo);
            Status=nintendo!=null?"原生 HD 回饋通道已就緒":"此模式使用普通震動相容通道";
        }
    }

    public void ConfigureAudio(bool enabled,int gain,bool replace,string endpointId)
    {
        try {ConfigureAudioCore(enabled,gain,replace,endpointId);}
        catch(Exception e) {lock(gate){audioEnabled=replaceAudio=false;try {audio.Stop(slot);}catch { }Status="音訊轉震動啟動失敗："+e.Message;}throw;}
    }
    void ConfigureAudioCore(bool enabled,int gain,bool replace,string endpointId)
    {
        lock(gate)
        {
            if(disposed) return;
            replaceAudio=enabled && replace;audioEnabled=enabled;
            if(!enabled) { audio.Stop(slot); Status="音訊轉震動已停用"; return; }
            using(var enumerator=new MMDeviceEnumerator()) using(var source=string.IsNullOrEmpty(endpointId)?enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia):enumerator.GetDevice(endpointId))
                if(source.FriendlyName.Contains("Wireless Controller",StringComparison.OrdinalIgnoreCase) || source.FriendlyName.Contains("DualSense",StringComparison.OrdinalIgnoreCase) || DualSenseAudioPassthrough.TryGetEndpointUsbipPort(source,out _))
                    throw new InvalidOperationException("請先把 Windows 預設播放裝置選為電腦揚聲器／耳機，再啟用系統音訊轉震動，避免回授循環。");
            var settings=new AudioHapticsProfileSettings {Enabled=true,Source=AudioHapticsSourceKind.SystemAudio,SourceEndpointId=endpointId??string.Empty,Mode=replace?AudioHapticsMode.Replace:AudioHapticsMode.Mix,GainPercent=Math.Clamp(gain,0,200)};
            audio.Start(slot,device,settings,OutContType.None,null);
            Status="音訊轉震動";
        }
    }

    public void SetPolicy(bool hd,bool adaptive)
    {
        lock(gate)
        {
            if(disposed)return;
            if(policyHd && !hd) {nintendo?.TryPublish(default(ControllerFeedbackActuatorState));pcm?.ClearBuffer();resampler.Reset();if(device is DualSenseDevice sony && device.ConnectionType==ConnectionType.BT)sony.WriteBluetoothHapticsSamples(new byte[64],0,64);}
            if(policyAdaptive && !adaptive && device is DualSenseDevice target) {var off=new byte[63];off[0]=2;off[1]=12;off[11]=off[22]=5;target.WriteRawOutputReportFromGame(off,0,off.Length);}
            policyHd=hd;policyAdaptive=adaptive;
        }
    }

    public bool Route(byte[] wire,int gain,bool adaptive,bool haptics)
    {
        lock(gate)
        {
            if(disposed) return true;
            gain=Math.Clamp(gain,0,100);
            bool replace=replaceAudio && audio.GetStatus(slot).Active;
            if(replace && (mode!="PS5" || wire.Length!=388 || wire[0]!=1)) return true;
            if(mode=="Xbox" && wire.Length==2 && nintendo!=null)
            {nintendo.TryPublish(new ControllerFeedbackActuatorState((ushort)(wire[0]*257),(ushort)(wire[1]*257),0,0),bodyStrengthPercent:gain);return true;}
            if(mode=="Steam / Pro2" && wire.Length==34 && nintendo!=null && (wire[32]&1)!=0 &&
                Switch2HdRumbleGroupCodec.TryDecode(wire.AsSpan(0,16),out _,out var l) &&
                Switch2HdRumbleGroupCodec.TryDecode(wire.AsSpan(16,16),out _,out var r))
            {
                bool neutral=IsNeutral(l)&&IsNeutral(r);
                if(!haptics) {nintendo.TryPublish(new ControllerFeedbackActuatorState(Amplitude(l),Amplitude(r),0,0),bodyStrengthPercent:gain);return true;}
                bool ok=neutral?nintendo.TryPublish(default(ControllerFeedbackActuatorState),bodyStrengthPercent:gain):nintendo.TryPublishSourcePreserved(new ControllerFeedbackActuatorState(1,0,0,0),Switch2HdRumbleFeedbackFidelity.NativeSwitch2PassThrough,l,r,bodyStrengthPercent:gain);
                Status=ok?"Nintendo 原生 HD 回饋":"Nintendo HD 寫入被拒絕";
                return true;
            }
            if(mode!="PS5" || wire.Length!=388) return false;
            int size=BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(1,2));
            if(size>384) return true;
            if(wire[0]==2) { if(haptics) WritePcm48(wire.AsSpan(4,size),gain); return true; }
            if(wire[0]==1 && nintendo!=null && size>=5 && wire[4]==2)
            {
                bool ordinary=(wire[5]&3)!=0 || (size>39 && (wire[43]&4)!=0);
                if(ordinary) nintendo.TryPublish(new ControllerFeedbackActuatorState((ushort)(wire[8]*257),(ushort)(wire[7]*257),0,0),bodyStrengthPercent:gain);
                return true;
            }
            if(wire[0]==1 && size>=48 && wire[4]==2 && device is DualSenseDevice sony)
            {
                var report=new byte[63];wire.AsSpan(4,Math.Min(size,report.Length)).CopyTo(report);
                if(!adaptive) report[1]&=0xF3;
                if(replace) {report[1]&=0xFC;report[3]=report[4]=0;}
                report[3]=(byte)(report[3]*gain/100); report[4]=(byte)(report[4]*gain/100);
                Status=sony.WriteRawOutputReportFromGame(report,0,report.Length)?"PS5 原生回饋已接入":"PS5 原生回饋寫入被拒絕";
                return true;
            }
            return false;
        }
    }

    void WritePcm48(ReadOnlySpan<byte> payload,int gain)
    {
        if(payload.Length==0 || payload.Length%8!=0) return;
        long now=Stopwatch.GetTimestamp();
        if(lastPcm!=0 && Stopwatch.GetElapsedTime(lastPcm,now).TotalMilliseconds>40) {resampler.Reset();pcm?.ClearBuffer();}
        lastPcm=now;
        if(device is DualSenseDevice && device.ConnectionType!=ConnectionType.BT)
        {
            if(player==null) { if(now<nextEndpointRetry)return; nextEndpointRetry=now+5*Stopwatch.Frequency; StartPcmOutput(); }
            var data=payload.ToArray();
            for(int i=0;i<data.Length;i+=8)
            {
                data.AsSpan(i,4).Clear(); // Speaker samples never become haptics or audible output.
                for(int c=4;c<8;c+=2) BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i+c,2),(short)(BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(i+c,2))*gain/100));
            }
            if(pcm.BufferedDuration.TotalMilliseconds>30) pcm.ClearBuffer();
            pcm.AddSamples(data,0,data.Length); Status="PS5 USB 原生觸覺 PCM"; return;
        }
        resampler.Push(payload,gain,DeliverPcmWindow);
    }
    void DeliverPcmWindow(byte[] samples)
    {
        if(nintendo!=null && DualSenseHapticsTranslator.TryTranslatePcmToSwitch2Groups(samples,out var l,out var r) && ControllerFeedbackClock.TryGetTimestampMicroseconds(out var t))
        {
            bool neutral=IsNeutral(l)&&IsNeutral(r);
            bool accepted=neutral?nintendo.TryPublish(default(ControllerFeedbackActuatorState)):nintendo.TryPublishSourcePreserved(new ControllerFeedbackActuatorState(1,0,0,0),Switch2HdRumbleFeedbackFidelity.DualSensePcmDualBand,l,r,expiresAtMicroseconds:t+40000);
            if(accepted){Interlocked.Increment(ref pcmAccepted);Status="PS5 觸覺 → Nintendo HD";}
            else
            {
                Interlocked.Increment(ref pcmRejected);
                if(neutral)Interlocked.Increment(ref pcmNeutralRejected);
                Volatile.Write(ref lastPcmRejection,nintendo.DescribeWirePublication());
                if(Interlocked.Read(ref pcmAccepted)==0)Status="PS5 觸覺尚未成功寫入";
            }
        }
        else if(device is DualSenseDevice sony) Status=sony.WriteBluetoothHapticsSamples(samples,0,64)?"PS5 藍牙觸覺 PCM":"PS5 藍牙觸覺寫入被拒絕";
    }

    void StartPcmOutput()
    {
        using var enumerator=new MMDeviceEnumerator();
        var candidates=enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active)
            .Where(e=>e.AudioClient.MixFormat.Channels==4 && (e.FriendlyName.Contains("Wireless Controller",StringComparison.OrdinalIgnoreCase)||e.FriendlyName.Contains("DualSense",StringComparison.OrdinalIgnoreCase)) && !DualSenseAudioPassthrough.TryGetEndpointUsbipPort(e,out _)).ToArray();
        if(candidates.Length!=1) throw new InvalidOperationException("無法唯一識別實體 DualSense 四聲道音訊端點。");
        endpoint=candidates[0];
        pcm=new BufferedWaveProvider(new WaveFormat(48000,16,4)) {BufferDuration=TimeSpan.FromMilliseconds(60),ReadFully=true,DiscardOnBufferOverflow=true};
        var samples=pcm.ToSampleProvider();
        player=new WasapiOut(endpoint,AudioClientShareMode.Shared,true,10);
        player.Init(FusionAudioFormat.ForEndpoint(samples,endpoint.AudioClient.MixFormat));player.Play();
    }

    static ushort Amplitude(Switch2HdRumbleGroup g)
    {
        int amplitude=new[]{g.First.Oscillator0AmplitudeCode,g.First.Oscillator1AmplitudeCode,g.Second.Oscillator0AmplitudeCode,g.Second.Oscillator1AmplitudeCode,g.Third.Oscillator0AmplitudeCode,g.Third.Oscillator1AmplitudeCode}.Max();
        return (ushort)(amplitude*65535/1023);
    }
    static bool IsNeutral(Switch2HdRumbleGroup g)=>g.First.Oscillator0AmplitudeCode==0 && g.First.Oscillator1AmplitudeCode==0 && g.Second.Oscillator0AmplitudeCode==0 && g.Second.Oscillator1AmplitudeCode==0 && g.Third.Oscillator0AmplitudeCode==0 && g.Third.Oscillator1AmplitudeCode==0;
    public bool TestHd()
    {
        lock(gate) return !disposed && device is Switch2RuntimeInputDevice s && s.TryStartIdentificationHaptic();
    }
    public void SetTriggerPreview(bool enabled)
    {
        lock(gate)
        {
            if(disposed || device is not DualSenseDevice sony) return;
            foreach(var id in new[]{TriggerId.LeftTrigger,TriggerId.RightTrigger}) TriggerLabEffectEncoder.ApplyToDevice(sony,id,new TriggerLabEffectSettings {ForcePercent=35,StartPercent=25},enabled);
        }
    }
    public void Dispose()
    {
        lock(gate)
        {
            if(disposed)return;disposed=true;
            var failures=new System.Collections.Generic.List<Exception>();
            void Release(Action action) {try {action();}catch(Exception e){failures.Add(e);}}
            Release(audio.Dispose);
            Release(()=> {if(nintendo?.TryRetire()==false)throw new DeferredFeedbackRetirementException();});
            Release(()=>player?.Stop());
            Release(()=>player?.Dispose());
            Release(()=>endpoint?.Dispose());
            if(device is DualSenseDevice sony)
            {
                Release(()=>sony.WriteBluetoothHapticsSamples(new byte[64],0,64));
                Release(sony.ReleaseNativeGameOutputOwnership);
                foreach(var id in new[]{TriggerId.LeftTrigger,TriggerId.RightTrigger})
                    Release(()=>TriggerLabEffectEncoder.ApplyToDevice(sony,id,new TriggerLabEffectSettings(),false));
            }
            if(failures.Count!=0)throw new AggregateException("進階回饋清理："+string.Join("；",failures.Select(e=>e.Message)),failures);
        }
    }
}
