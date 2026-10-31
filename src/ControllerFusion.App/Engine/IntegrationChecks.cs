using System.Buffers.Binary;
using System.IO;
using DS4Windows;
using DS4Windows.Switch2;
using ControllerFusion.Input;

namespace ControllerFusion.Engine;

public static class IntegrationChecks
{
    public static async Task Run(string path)
    {
        var results=new List<string>();
        void Check(bool value,string name) { if(!value) throw new Exception("FAIL: "+name); results.Add("PASS: "+name); }
        await using(var stagedOutput=new VirtualOutput(true))
        {
            var engine=new ControllerEngine();
            typeof(ControllerEngine).GetField("output",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(engine,stagedOutput);
            Check(!engine.HasOutput,"USB backend readiness alone cannot advertise game output readiness");
            typeof(ControllerEngine).GetMethod("Set",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(engine,new object[]{"遊戲輸出已就緒","synthetic XInput slot resolved",true,true});
            Check(engine.HasOutput,"game output is ready after its consumer slot is resolved");
            await stagedOutput.DisposeAsync();
            Check(!engine.HasOutput,"disposed transport revokes game output readiness");
        }
        long Tick(int milliseconds)=>(long)Math.Round(milliseconds*System.Diagnostics.Stopwatch.Frequency/1000d);
        var ready=new InputReadiness();ready.Observe(Tick(0));
        Check(!ready.IsReady(Tick(200)),"a single cached input report cannot establish a connection");
        ready.Reset();for(int i=0;i<20;i++)ready.Observe(Tick(i));
        Check(!ready.IsReady(Tick(200)),"a short buffered input burst cannot establish a connection");
        ready.Reset();for(int i=0;i<=60;i++)ready.Observe(Tick(i*4));
        Check(ready.IsReady(Tick(245)),"continuous USB input establishes readiness");
        Check(!ready.IsReady(Tick(400)),"stalled input loses readiness");
        ready.Observe(Tick(500));Check(!ready.IsReady(Tick(500)),"a returning single report must build a new stable window");
        ready.Reset();for(int i=0;i<=16;i++)ready.Observe(Tick(i*16));
        Check(ready.IsReady(Tick(260)),"continuous Bluetooth input establishes readiness");
        var privateLog = @"Peer AA:BB:CC:DD:EE:FF; HID\VID_054C&PID_0CE6#private-serial; path C:\Users\ExamplePerson\AppData\Local\ControllerFusion";
        var redacted = DiagnosticReport.Redact(privateLog);
        Check(!redacted.Contains("AA:BB") && !redacted.Contains("private-serial") && !redacted.Contains("ExamplePerson"),"diagnostic export removes address, device ID and user profile");
        Check(DiagnosticReport.Redact("已停止：USB/IP port 4; 250 Hz; PS=False") == "已停止：USB/IP port 4; 250 Hz; PS=False","diagnostic export retains useful errors and measurements");
        var offline = new ControllerEngine();
        using(var report=System.Text.Json.JsonDocument.Parse(DiagnosticReport.Create(offline,new Preferences(),new[]{privateLog})))
            Check(report.RootElement.GetProperty("Input").GetProperty("Battery").ValueKind==System.Text.Json.JsonValueKind.Null && report.RootElement.GetProperty("Buttons").ValueKind==System.Text.Json.JsonValueKind.Null,"disconnected diagnostic snapshot has no stale battery or buttons");
        await offline.DisposeAsync();
        var pcmSource=new byte[512*8];
        for(int i=0;i<pcmSource.Length;i+=8) {BinaryPrimitives.WriteInt16LittleEndian(pcmSource.AsSpan(i,2),30000);BinaryPrimitives.WriteInt16LittleEndian(pcmSource.AsSpan(i+2,2),-30000);BinaryPrimitives.WriteInt16LittleEndian(pcmSource.AsSpan(i+4,2),16384);BinaryPrimitives.WriteInt16LittleEndian(pcmSource.AsSpan(i+6,2),-8192);}
        var converter=new PcmHapticsResampler();var windows=new List<byte[]>();
        converter.Push(pcmSource.AsSpan(0,800),100,windows.Add); Check(windows.Count==0,"partial PCM window waits for enough samples");
        converter.Push(pcmSource.AsSpan(800),100,windows.Add); Check(windows.Count==1 && windows[0].Where((_,i)=>i%2==0).All(x=>x==64) && windows[0].Where((_,i)=>i%2==1).All(x=>unchecked((sbyte)x)==-32),"48 kHz PCM preserves left/right signed haptic channels");
        for(int i=0;i<pcmSource.Length;i+=8)pcmSource.AsSpan(i+4,4).Clear();windows.Clear();converter.Push(pcmSource,100,windows.Add);
        Check(windows.Single().All(x=>x==0),"speaker-only PCM never creates haptic samples");
        converter.Push(pcmSource.AsSpan(0,800),100,_=>{});converter.Reset();windows.Clear();converter.Push(pcmSource.AsSpan(800),100,windows.Add);Check(windows.Count==0,"PCM reset discards incomplete stale window");
        var still=Enumerable.Range(0,80).Select(_=>new SixAxis(-32,16,0,0,0,8192,.004)).ToArray();
        var bias=MotionPipeline.Calibrate(still);Check(bias.Pitch==1 && bias.Yaw==2 && bias.Roll==0,"stationary gyro calibration retains measured axis bias");
        var corrected=MotionPipeline.Map(still[0],new Preferences {MotionEnabled=true,GyroCalibrationController="Switch 2 Pro",GyroBiasPitch=1,GyroBiasYaw=2});Check(corrected!.gyroPitchFull==0 && corrected.gyroYawFull==0 && still[0].gyroYawFull==32,"gyro calibration corrects copy without mutating physical sample");
        bool movingRejected=false;try {MotionPipeline.Calibrate(still.Concat(new[]{new SixAxis(5000,0,0,0,0,8192,.004)}).ToArray());}catch(InvalidOperationException){movingRejected=true;}Check(movingRejected,"calibration rejects a moving controller");
        var motionDisabled=PadPackets.Map(new DS4State {Motion=still[0]},new Preferences());Check(motionDisabled.Motion==null,"disabled motion is not forwarded");
        using(var process=System.Diagnostics.Process.GetCurrentProcess()) {
            string executable=process.MainModule!.FileName;
            var game=new SavedProfile("Fixture",new Preferences(),executable);
            Check(GameProfileWatcher.FindRunning(new[]{game}).Profile?.Name=="Fixture","game profile matches running exact executable path");
            Check(GameProfileWatcher.FindRunning(new[]{game,game with {Name="Other"}}).Ambiguous,"multiple matching game profiles remain ambiguous");
        }
        await using(var stalled=new VirtualOutput(true))
        {
            int failures=0;var faultSignal=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            stalled.Fault+=_=>{Interlocked.Increment(ref failures);faultSignal.TrySetResult();};
            var held=new byte[20];for(int i=0;i<10000;i++)stalled.Submit(held);
            await faultSignal.Task.WaitAsync(TimeSpan.FromSeconds(3));await Task.Delay(30);
            Check(!stalled.Ready && failures==1,"blocked output consumer latches one fault without a log storm");
        }
        var movingState=new DS4State {Motion=new SixAxis(-320,160,-480,-8192,0,0,.004)};
        var sonyMotion=PadPackets.Encode(PadPackets.Map(movingState,new Preferences {MotionEnabled=true}),"PS5");
        Check(BinaryPrimitives.ReadInt16LittleEndian(sonyMotion.AsSpan(21))==160 && BinaryPrimitives.ReadInt16LittleEndian(sonyMotion.AsSpan(23))==-320 && BinaryPrimitives.ReadInt16LittleEndian(sonyMotion.AsSpan(27))==-8192,"Sony motion preserves sensor domain and declared wire axes");
        movingState.Switch2RawInputStatus=new(){IsValid=true};
        var switchMotion=PadPackets.Encode(PadPackets.Map(movingState,new Preferences {MotionEnabled=true}),"Steam / Pro2");
        Check(BinaryPrimitives.ReadInt16LittleEndian(switchMotion.AsSpan(12))==-4096 && BinaryPrimitives.ReadInt16LittleEndian(switchMotion.AsSpan(18))==164,"Switch motion converts acceleration and gyro scales for Nintendo wire domain");
        var host=new byte[]{1,2,3,4,5,6};
        var advertisement=new byte[24];advertisement[0]=1;advertisement[2]=3;advertisement[3]=0x7e;advertisement[4]=5;advertisement[5]=0x69;advertisement[6]=0x20;advertisement[8]=1;advertisement[9]=0x81;advertisement[16]=15;
        host.Reverse().ToArray().CopyTo(advertisement,10);
        Check(Switch2AdvertisementCodec.TryDecode(0x0553,advertisement,host,out var wake),"decode captured-shape Pro2 Home advertisement");
        Switch2BluetoothPeerToken.TryDerive(Enumerable.Repeat((byte)7,32).ToArray(),1,0x010203040506,out var peer);
        var baseline=new Switch2BluetoothCandidateRegistry();baseline.TryBeginScan(1);
        Check(baseline.Observe(1,peer,1,wake).Disposition==Switch2BluetoothObservationDisposition.IgnoredWake,"reproduce upstream rejection of known-host Home wake");
        var admitted=FusionInput.NormalizeKnownProWake(wake);
        Check(baseline.Observe(1,peer,2,admitted).Disposition==Switch2BluetoothObservationDisposition.RememberedThisHost,"known-host Pro2 Home reaches remembered reconnect admission");
        Switch2AdvertisementCodec.TryDecode(0x0553,advertisement,new byte[]{9,9,9,9,9,9},out var foreign);
        var foreignRegistry=new Switch2BluetoothCandidateRegistry();foreignRegistry.TryBeginScan(1);
        Check(!foreignRegistry.Observe(1,peer,1,FusionInput.NormalizeKnownProWake(foreign)).IsConnectionCandidate,"foreign-host wake remains excluded");
        advertisement[5]=0x67;
        Switch2AdvertisementCodec.TryDecode(0x0553,advertisement,host,out var joycon);
        var joyconRegistry=new Switch2BluetoothCandidateRegistry();joyconRegistry.TryBeginScan(1);
        Check(!joyconRegistry.Observe(1,peer,1,FusionInput.NormalizeKnownProWake(joycon)).IsConnectionCandidate,"unverified Joy-Con wake behavior remains excluded");
        var neutral=new DS4State();
        foreach(var mode in new[]{"Xbox","PS5","Pro1","Steam / Pro2"})
        {
            var b=PadPackets.Encode(neutral,mode);
            Check(b.Length==(mode=="Xbox"?20:mode=="PS5"?33:28),mode+" wire length");
            Check(BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(mode=="PS5"?4:0))==0,mode+" neutral buttons");
            if(mode=="Xbox") Check(b.AsSpan(4,10).ToArray().All(v=>v==0),"Xbox centered axes and triggers");
            if(mode=="PS5") Check(b.AsSpan(0,4).ToArray().All(v=>v==0) && b[8]==0,"PS5 centered axes and dpad");
            if(mode is "Pro1" or "Steam / Pro2") Check(BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4))==2048 && BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6))==2048,mode+" centered axes");
        }
        var raw=new DS4State {BLP=true,BRP=true,Switch2RawInputStatus=new(){IsValid=true,CButton=true}};
        var mapped=PadPackets.Map(raw,new Preferences{GL="A + B",GR="L + R",C="ZR"});
        Check(mapped.Cross && mapped.Circle && mapped.L1 && mapped.R1 && mapped.R2==255,"GL/GR/C combination mapping");
        Check(!raw.Cross && raw.R2==0,"mapping never mutates physical report");
        Check(!mapped.BLP && !mapped.BRP && !mapped.Switch2RawInputStatus.CButton,"mapped native extras are not emitted a second time");
        var released=PadPackets.Map(new DS4State(),new Preferences{GL="A + B"});
        Check(!released.Cross && !released.Circle,"release has no sticky mapping");
        var physical=new DS4State {Cross=true,BLP=true};
        var touchState=new DS4State {Mute=true,TrackPadTouch0=new(){IsActive=true,X=1234,Y=567}};
        var touchWire=PadPackets.Encode(touchState,"PS5");
        Check((BinaryPrimitives.ReadUInt32LittleEndian(touchWire.AsSpan(4))&0x40000)!=0 && BinaryPrimitives.ReadUInt16LittleEndian(touchWire.AsSpan(11))==1234 && BinaryPrimitives.ReadUInt16LittleEndian(touchWire.AsSpan(13))==567 && touchWire[15]==1,"PS5 touch position and mute button passthrough");
        Check(PadPackets.Encode(new DS4State(),"PS5")[15]==0,"PS5 touch release does not manufacture a contact");
        Check(PadPackets.Map(physical,new Preferences{GL="不映射"}).Cross,"unmapped rear key preserves physical face button");
        var extremes=PadPackets.Encode(new DS4State{LX=0,LY=0,RX=255,RY=255},"Xbox");
        Check(BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(6))==-32768 && BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(8))==32767 && BinaryPrimitives.ReadInt16LittleEndian(extremes.AsSpan(10))==32767,"axis endpoints and vertical inversion");
        var precision=PadPackets.Encode(new DS4State {Switch2RawInputStatus=new(){IsValid=true,LeftStickX=1201}},"Xbox");
        Check(BinaryPrimitives.ReadInt16LittleEndian(precision.AsSpan(6))==1201,"Switch 2 calibrated stick precision survives 8-bit UI projection");
        Check(VirtualOutput.DecodeFeedback("Xbox",new byte[]{120,60})==(120,60),"Xbox motor ordering");
        var feedback=new byte[388]; feedback[0]=1;feedback[1]=48;feedback[4]=2;feedback[5]=3;feedback[7]=44;feedback[8]=88;
        Check(VirtualOutput.DecodeFeedback("PS5",feedback)==(88,44),"PS5 ordinary rumble extraction");
        feedback[5]=0; Check(VirtualOutput.DecodeFeedback("PS5",feedback)==null,"PS5 LED-only feedback does not cancel active rumble");feedback[5]=3;
        feedback[0]=2; Check(VirtualOutput.DecodeFeedback("PS5",feedback)==null,"audio haptics not mistaken for motor bytes");
        Check(VirtualOutput.DecodeFeedback("Pro1",new byte[10])==null,"LED-only feedback does not stop motors");
        const string ports="Port 01: device in use\n -> usbip://127.0.0.1:41000/1-1\n -> remote bus/dev 001/001\nPort 02: device in use\n -> usbip://127.0.0.1:42000/1-1\n -> remote bus/dev 001/001\n";
        Check(OutputLease.Ports(ports,41000,1,"1").SequenceEqual(new[]{1}),"cleanup matches exact owned endpoint only");
        Check(OutputLease.Ports(ports,43000,1,"1").Length==0,"cleanup never falls back to matching all virtual devices");
        var gaps=new InputStatistics();long tick=System.Diagnostics.Stopwatch.GetTimestamp();gaps.Observe(tick);
        for(int i=0;i<99;i++){tick+=System.Diagnostics.Stopwatch.Frequency*4/1000;gaps.Observe(tick);}tick+=System.Diagnostics.Stopwatch.Frequency/2;gaps.Observe(tick);
        using(var gapJson=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(gaps.Snapshot()))) {Check(gapJson.RootElement.GetProperty("P95UpperMilliseconds").GetDouble()<=5 && gapJson.RootElement.GetProperty("MaximumGapMilliseconds").GetDouble()>=499,"input gap statistics distinguish usual cadence from an isolated stall");}
        var folder=Path.Combine(Path.GetTempPath(),"FusionChecks-"+Guid.NewGuid().ToString("N"));
        var store=new ProfileStore(folder); store.Save(new(){new("Roundtrip",new Preferences{GL="ZL",Output="Pro1"})});
        Check(store.Load().Single().Settings.GL=="ZL","profile atomic roundtrip");
        store.Save(new(){new("Bound game",new Preferences {GL="ZR"},@"C:\Games\Game.exe")});
        Check(store.Load().Single().ExecutablePath==@"C:\Games\Game.exe","game executable binding persists");
        File.WriteAllText(Path.Combine(folder,"profiles.json"),"{broken");
        Check(store.Load().Single().Name=="Roundtrip" && store.LoadWarning!=null,"corrupt profile list recovers the previous valid backup");
        var prefPath=Path.Combine(folder,"preferences.json");ProfileStore.SavePreferences(prefPath,new Preferences {Rumble=40});ProfileStore.SavePreferences(prefPath,new Preferences {Rumble=80});File.WriteAllText(prefPath,"{broken");
        Check(ProfileStore.LoadPreferences(prefPath,out var recovery).Rumble==40 && recovery!=null,"corrupt preferences recover the previous valid backup");
        foreach(var mode in new[]{"Xbox","PS5","Pro1","Steam / Pro2"})
        {
            await using var output=new VirtualOutput();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await StartWithPausedUi(output,mode,timeout.Token);
            Check(output.Ready,"real VIIPER create/open "+mode+" (no USB import)");
            Check(output.Submit(PadPackets.Encode(neutral,mode)),"stream accepts "+mode);
            for(int i=0;i<30 && Interlocked.Read(ref output.Written)==0;i++) await Task.Delay(20);
            Check(Interlocked.Read(ref output.Written)>0,"real TCP write while UI context is paused "+mode);
        }
        results.Add("No driver installation or physical-game compatibility assertions are part of these checks.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllLines(path,results);
    }
    static async Task StartWithPausedUi(VirtualOutput output,string mode,CancellationToken token)
    {
        var context=new PausedUiContext();var previous=SynchronizationContext.Current;
        Task startup;
        SynchronizationContext.SetSynchronizationContext(context);
        try {startup=output.Start(mode,token,attach:false);}
        finally {SynchronizationContext.SetSynchronizationContext(previous);}
        while(!startup.IsCompleted) {context.Pump();await Task.Delay(5,token).ConfigureAwait(false);}
        await startup.ConfigureAwait(false);
        // No more UI pumping. The transport must continue on its own worker.
    }
    sealed class PausedUiContext : SynchronizationContext
    {
        readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback,object? State)> work=new();
        public override void Post(SendOrPostCallback callback,object? state)=>work.Enqueue((callback,state));
        public void Pump()
        {
            for(int i=0;i<32 && work.TryDequeue(out var item);i++)
            {
                var previous=Current;SetSynchronizationContext(this);
                try {item.Callback(item.State);}finally {SetSynchronizationContext(previous);}
            }
        }
    }

}
