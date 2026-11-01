using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ControllerFusion.Engine;

namespace ControllerFusion;

public sealed class Preferences
{
    public string Language { get; set; } = "zh-Hant";
    public string Controller { get; set; } = "Switch 2 Pro";
    public string Output { get; set; } = "Xbox";
    public string Transport { get; set; } = "藍牙";
    public string GL { get; set; } = "B";
    public string GR { get; set; } = "A";
    public string C { get; set; } = "不映射";
    public double Rumble { get; set; } = 65;
    public bool HdHaptics { get; set; } = true;
    public bool AdaptiveTriggers { get; set; } = true;
    public bool MotionEnabled { get; set; }
    public string GyroCalibrationController { get; set; } = "";
    public DateTimeOffset? GyroCalibratedAt { get; set; }
    public double GyroBiasPitch { get; set; }
    public double GyroBiasYaw { get; set; }
    public double GyroBiasRoll { get; set; }
    public bool AudioHaptics { get; set; }
    public bool AudioReplace { get; set; }
    public string AudioEndpointId { get; set; } = "";
    public int AudioGain { get; set; } = 70;
    public bool AutoProfiles { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public bool AutoTransport { get; set; }
    public bool HidePhysical { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool SimulateFailure { get; set; }
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        if(args.Contains("--crash-test-output"))
        {
            var path=args.SkipWhile(a=>a!="--crash-test-output").Skip(1).FirstOrDefault()??"crash-output-ready.txt";
            try { WindowsOutputChecks.HoldForCrashTest(path).GetAwaiter().GetResult(); }
            catch(Exception ex) {File.WriteAllText(path,ex.ToString());Environment.ExitCode=1;}
            return;
        }
        if(args.Contains("--test-windows-output"))
        {
            var path=args.SkipWhile(a=>a!="--test-windows-output").Skip(1).FirstOrDefault()??"windows-output-test.txt";
            try { WindowsOutputChecks.Run(path).GetAwaiter().GetResult(); }
            catch(Exception ex) { File.AppendAllText(path,Environment.NewLine+ex); Environment.ExitCode=1; }
            return;
        }
        if(args.Contains("--performance-check"))
        {
            var path=args.SkipWhile(a=>a!="--performance-check").Skip(1).First();
            try {PerformanceChecks.Run(path).GetAwaiter().GetResult();}
            catch(Exception ex){File.WriteAllText(path,ex.ToString());Environment.ExitCode=1;}
            return;
        }
        if(args.Contains("--self-test"))
        {
            var path=args.SkipWhile(a=>a!="--self-test").Skip(1).FirstOrDefault()??"self-test.txt";
            try { IntegrationChecks.Run(path).GetAwaiter().GetResult(); }
            catch(Exception ex) { File.WriteAllText(path,ex.ToString()); Environment.ExitCode=1; }
            return;
        }
        if (args.Contains("--probe-input"))
        {
            var output = args.SkipWhile(a => a != "--probe-input").Skip(1).FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory,"input-probe.txt");
            try
            {
                var probe = Task.Run(async () =>
                {
                    using var input = new Input.FusionInput();
                    if(args.Contains("--dualsense")) input.Select("PS5 DualSense","USB 有線");
                    input.Log += s => File.AppendAllText(output+".progress",s+Environment.NewLine);
                    long reports=0; var keys=new HashSet<string>(); var gate=new object(); int minLX=255,maxLX=0,minLY=255,maxLY=0;
                    input.Frame += (slot,s) => { Interlocked.Increment(ref reports); lock(gate) { if(s.PS) keys.Add("PS"); if(s.Share) keys.Add("Create / Share"); if(s.BLP) keys.Add("GL"); if(s.BRP) keys.Add("GR"); if(s.Switch2RawInputStatus.CButton) keys.Add("C"); if(s.Cross||s.Circle||s.Square||s.Triangle) keys.Add("face"); minLX=Math.Min(minLX,s.LX);maxLX=Math.Max(maxLX,s.LX);minLY=Math.Min(minLY,s.LY);maxLY=Math.Max(maxLY,s.LY); } };
                    input.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ControllerFusion","Input"));
                    for(int i=0;i<(args.Contains("--interactive")?300:args.Contains("--long")?60:15);i++) { input.Rescan(); await Task.Delay(1000); lock(gate) File.WriteAllText(output,JsonSerializer.Serialize(new { Devices=input.Devices(),Reports=reports,Keys=keys.ToArray(),minLX,maxLX,minLY,maxLY },new JsonSerializerOptions{WriteIndented=true})); if(i==20 && args.Contains("--rumble")) { foreach(var d in input.Devices()) input.Rumble(d.Slot,120,75); await Task.Delay(500); foreach(var d in input.Devices()) input.Rumble(d.Slot,0,0); } if(File.Exists(output+".stop")) break; }
                    File.WriteAllText(output,JsonSerializer.Serialize(new { Devices=input.Devices(),Reports=reports,Keys=keys.ToArray(),minLX,maxLX,minLY,maxLY },new JsonSerializerOptions{WriteIndented=true}));
                });
                _ = probe.ContinueWith(t => { if(t.Exception!=null) { File.WriteAllText(output,t.Exception.ToString()); Environment.ExitCode=1; } app.Dispatcher.BeginInvoke(new Action(()=>app.Shutdown())); });
                app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
                app.Run();
            }
            catch(Exception ex) { File.WriteAllText(output,ex.ToString()); Environment.ExitCode=1; }
            return;
        }
        using var mutex=new Mutex(true,"Local\\ControllerFusion.Desktop",out bool first);
        if(!first && !args.Contains("--smoke")) {
            try { UiText.Use(ProfileStore.LoadPreferences(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ControllerFusion","preferences.json"),out _).Language); } catch { }
            MessageBox.Show(UiText.Get("ControllerHub 已在執行，請切換至原視窗。"),"ControllerHub"); return;
        }
        if(args.Contains("--test-release-lifecycle"))
        {
            var path=args.SkipWhile(a=>a!="--test-release-lifecycle").Skip(1).First();
            app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
            _=Task.Run(async ()=>
            {
                try {await LifecycleChecks.Run(path,args.Contains("--dualsense")?"PS5 DualSense":"Switch 2 Pro",args.Contains("--bluetooth")?"藍牙":"USB 有線",args.Contains("--with-haptics"));}
                catch(Exception ex) {File.AppendAllText(path,Environment.NewLine+ex);Environment.ExitCode=1;}
                finally {_ = app.Dispatcher.BeginInvoke(new Action(app.Shutdown));}
            });
            app.Run();return;
        }
        app.Run(new MainWindow(args));
    }
}

public sealed class MainWindow : Window
{
    static readonly Color Ink = Color.FromRgb(24, 32, 49);
    static readonly Color Muted = Color.FromRgb(99, 110, 128);
    static readonly Color Canvas = Color.FromRgb(248, 249, 252);
    static readonly Color Line = Color.FromRgb(228, 232, 240);
    static readonly Color Selected = Color.FromRgb(238, 242, 255);
    static readonly Color Accent = Color.FromRgb(0, 47, 167);
    readonly Grid shell = new();
    readonly List<string> logs = new();
    readonly string[] args;
    readonly string preferencesPath;
    readonly ControllerEngine engine = new();
    readonly ProfileStore profileStore;
    readonly List<SavedProfile> savedProfiles;
    readonly DispatcherTimer metrics = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer profilePolling = new() { Interval = TimeSpan.FromSeconds(2) };
    TextBlock? frequencyText, inputStateText, motionText, hapticsText;
    BatteryIndicator? batteryIndicator;
    ConnectionBadge? headerBadge, connectionBadge;
    bool closing;
    bool closeReady;
    TrayHost? tray;
    bool viewDirty;
    bool inspectingGame;
    string? automaticProfile;
    Preferences? beforeGame;
    string? profileBeforeGame;

    readonly HashSet<string> observedKeys=new();
    readonly object observedGate=new();
    Preferences preferences = new();
    bool gameHapticsTesting;
    string page = "手柄", phase = "尚未連接", note = "選擇手柄與連接方式。", selectedKey = "GL";
    CancellationTokenSource? connection;
    bool connected;
    int generation;
    string? selectedProfile;
    readonly List<string> profiles = new() { "預設配置", "動作遊戲", "模擬駕駛" };

    public MainWindow(string[] arguments)
    {
        args = arguments;
        Title = "ControllerHub";
        Width = 1220; Height = 850; MinWidth = 960; MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush(Canvas); Foreground = Brush(Ink);
        FontFamily = new FontFamily("Segoe UI, Microsoft JhengHei UI"); FontSize = 14;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        var smoke = args.Contains("--smoke");
        preferencesPath = Path.Combine(smoke ? Path.Combine(Path.GetTempPath(), "ControllerFusion-Smoke-" + Guid.NewGuid().ToString("N")) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerFusion"), "preferences.json");
        profileStore = new ProfileStore(Path.GetDirectoryName(preferencesPath)!);
        try { savedProfiles=profileStore.Load(); if(profileStore.LoadWarning!=null)note=profileStore.LoadWarning; } catch { savedProfiles=new(); note="配置清單無法讀取，原檔案保留，已載入預設配置。"; }
        if(savedProfiles.Count==0) foreach(var name in profiles) savedProfiles.Add(new(name,new Preferences()));
        profiles.Clear(); profiles.AddRange(savedProfiles.Select(p=>p.Name));
        try {preferences=ProfileStore.LoadPreferences(preferencesPath,out var warning);if(warning!=null)note=warning;}
        catch { preferences=new(); note = "無法讀取配置，已使用預設值；原檔案未覆寫。"; }
        if(args.Contains("--hardware-live")) { preferences.AudioHaptics=false;preferences.Controller=args.Contains("--dualsense")?"PS5 DualSense":"Switch 2 Pro"; preferences.Transport=args.Contains("--bluetooth")?"藍牙":"USB 有線"; preferences.Output=args.Contains("--bounded-game-haptics") || args.Contains("--bounded-game-triggers") || args.Contains("--bounded-motion")?"PS5":"Xbox";if(args.Contains("--bounded-motion"))preferences.MotionEnabled=true;if(args.Contains("--bounded-game-triggers"))preferences.AdaptiveTriggers=true; }
        if(args.Contains("--bounded-audio") || args.Contains("--bounded-trigger") || args.Contains("--bounded-game-haptics") || args.Contains("--bounded-game-triggers") || args.Contains("--bounded-motion") || args.Contains("--close-after-connect")){preferences.AudioHaptics=false;preferences.AutoReconnect=false;preferences.AutoTransport=false;preferences.AutoProfiles=false;preferences.MinimizeToTray=false;}
        if(args.Contains("--manual-latency")) {page="手柄";preferences.Output="Xbox";preferences.AudioHaptics=false;preferences.AutoReconnect=false;preferences.AutoTransport=false;preferences.AutoProfiles=false;preferences.MinimizeToTray=false;}
        if(args.Contains("--manual-haptics")) {page="震動";preferences.Output="PS5";preferences.AudioHaptics=false;preferences.AutoReconnect=false;preferences.AutoTransport=false;preferences.AutoProfiles=false;preferences.MinimizeToTray=false;}
        selectedProfile = profiles[0];
        Resources.Add(typeof(Button), ButtonStyle());
        Resources.Add(typeof(ComboBox), new Style(typeof(ComboBox)) { Setters = { new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)), new Setter(Control.MinHeightProperty, 36.0), new Setter(Control.ForegroundProperty, Brush(Ink)), new Setter(Control.FontSizeProperty, 14.0) } });
        Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="ComboBoxItem">
            <Setter Property="Padding" Value="12,9"/><Setter Property="Foreground" Value="#182031"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem">
              <Border x:Name="ItemSurface" CornerRadius="5" Padding="{TemplateBinding Padding}" Background="Transparent"><ContentPresenter/></Border>
              <ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="ItemSurface" Property="Background" Value="#EEF2FF"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter Property="FontWeight" Value="SemiBold"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBox">
            <Setter Property="MinHeight" Value="38"/><Setter Property="Foreground" Value="#182031"/><Setter Property="FontSize" Value="13"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
              <Grid>
                <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                  <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border x:Name="Surface" CornerRadius="8" BorderBrush="#8F9BAF" BorderThickness="1" Background="#FFFFFF"><Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="30"/></Grid.ColumnDefinitions><ContentPresenter Margin="11,8,0,8" HorizontalAlignment="Left" VerticalAlignment="Center"/><TextBlock Grid.Column="1" Text="⌄" Foreground="#636E80" VerticalAlignment="Center" HorizontalAlignment="Center"/></Grid></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="#EEF2FF"/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
                  <TextBlock Text="{Binding SelectionBoxItem, RelativeSource={RelativeSource TemplatedParent}}"/>
                </ToggleButton>
                <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="None">
                  <Border Background="White" CornerRadius="8" BorderBrush="#8F9BAF" BorderThickness="1" Padding="4" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" Margin="0,4,0,0"><ScrollViewer MaxHeight="310" CanContentScroll="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border>
                </Popup>
              </Grid>
              <ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
        </ResourceDictionary>
        """));
        Resources.Remove(typeof(ComboBox));
        Content = new Border { Background = Brush(Canvas), Child = shell };
        Icon = new DrawingImage(new GeometryDrawing(Brush(Accent), null, Geometry.Parse("M2,2 L30,2 L30,30 L2,30 Z M8,14 L14,14 L14,8 L18,8 L18,14 L24,14 L24,18 L18,18 L18,24 L14,24 L14,18 L8,18 Z")));
        DispatcherTimer? manualDeadline=null;
        if(args.Contains("--manual-haptics")) {
            manualDeadline=new DispatcherTimer {Interval=TimeSpan.FromSeconds(120)};
            manualDeadline.Tick+=(_,_)=> {manualDeadline.Stop();Close();};
            Loaded+=(_,_)=>manualDeadline.Start();
        }
        Closing += async (_,e) =>
        {
            if(closeReady || smoke) return;
            e.Cancel=true;
            if(closing) return;
            closing=true; IsEnabled=false; CancelConnection(false); metrics.Stop();profilePolling.Stop();manualDeadline?.Stop();
            try { await engine.DisposeAsync(); } catch(Exception ex) { Log(ex.Message);Environment.ExitCode=2; }
            if(args.Contains("--hardware-live"))
            {
                if(!engine.CleanupSucceeded)Environment.ExitCode=2;
                var index=Array.IndexOf(args,"--hardware-live");
                if(index+1<args.Length)
                    try {File.WriteAllText(args[index+1]+".shutdown-result.json",JsonSerializer.Serialize(new {engine.CleanupSucceeded,engine.Status,engine.Reports,engine.HasInput,engine.HasOutput,ExitCode=Environment.ExitCode}));}
                    catch(Exception ex){Log("無法儲存退出結果："+ex.Message);Environment.ExitCode=2;}
            }
            tray?.Dispose(); tray=null;
            closeReady=true;
            Close();
        };
        engine.Log += s => Dispatcher.BeginInvoke(new Action(()=>Log(s)));
        engine.FrameReceived += s => { lock(observedGate) { if(s.PS)observedKeys.Add("PS");if(s.Share)observedKeys.Add("Create / Share");if(s.BLP)observedKeys.Add("GL");if(s.BRP)observedKeys.Add("GR");if(s.Switch2RawInputStatus.CButton)observedKeys.Add("C");if(s.Cross||s.Circle||s.Square||s.Triangle)observedKeys.Add("face"); } };
        engine.Changed += s => Dispatcher.BeginInvoke(new Action(()=> { if(closing)return; phase=s.Phase; note=s.Detail; connected=s.OutputReady; Draw(); }));
        metrics.Tick += (_,_) =>
        {
            if(frequencyText!=null) frequencyText.Text=engine.HasInput?$"{engine.Hertz:0} Hz":"— Hz";
            batteryIndicator?.Update(engine.Battery,engine.Charging,engine.HasInput);
            headerBadge?.Update(engine.HasInput,engine.HasOutput,engine.HasInput?phase:"等待連接");
            connectionBadge?.Update(engine.HasInput,engine.HasOutput,phase);
            if(inputStateText!=null) { var s=engine.LastFrame; inputStateText.Text=s==null || !engine.HasInput?UiText.Get("等待實體按鍵輸入"):preferences.Controller == "PS5 DualSense" ? $"L {s.LX},{s.LY} · R {s.RX},{s.RY}   PS {(s.PS?"●":"○")}  Create {(s.Share?"●":"○")}  L2 {s.L2}  R2 {s.R2}" : $"L {s.LX},{s.LY} · R {s.RX},{s.RY}   GL {(s.BLP?"●":"○")}  GR {(s.BRP?"●":"○")}  C {(s.Switch2RawInputStatus.CButton?"●":"○")}"; }
            if(hapticsText!=null) hapticsText.Text=UiText.Get(engine.HapticsStatus);
            if(motionText!=null) { var m=engine.LastFrame?.Motion; motionText.Text=UiText.Get(engine.HasInput && m!=null?$"角速度 °/s   P {m.angVelPitch:F2}   Y {m.angVelYaw:F2}   R {m.angVelRoll:F2}\n加速度 g   X {m.accelXG:F2}   Y {m.accelYG:F2}   Z {m.accelZG:F2}":"等待有效體感資料"); }
            var liveAt=Array.IndexOf(args,"--hardware-live");
            if(liveAt>=0 && liveAt+1<args.Length) { try { string[] keys;lock(observedGate)keys=observedKeys.ToArray(); ProfileStore.AtomicWrite(args[liveAt+1],JsonSerializer.Serialize(new{engine.Status,engine.Reports,engine.FeedbackReports,engine.Hertz,engine.HasInput,engine.HasOutput,engine.Battery,engine.Charging,engine.HapticsStatus,engine.HapticsStatistics,engine.NativeHdAvailable,engine.ActiveTransport,engine.InputStatistics,engine.OutputStatistics,Motion=engine.LastFrame?.Motion is {} motion?new{motion.angVelPitch,motion.angVelYaw,motion.angVelRoll,motion.accelXG,motion.accelYG,motion.accelZG}:null,engine.BluetoothStatus,engine.Candidates,engine.XboxState,Keys=keys})); } catch { } }
        };
        profilePolling.Tick+=(_,_)=> {if(preferences.AutoProfiles && !inspectingGame && !closing)_=ObserveGameProfile();};
        if(!smoke) {metrics.Start();profilePolling.Start();}
        StateChanged+=(_,_)=> {
            if(closing)return;
            if(WindowState==WindowState.Minimized)metrics.Stop();
            else {if(!smoke)metrics.Start();if(viewDirty)Draw();}
            if(smoke || WindowState!=WindowState.Minimized || !preferences.MinimizeToTray)return;
            tray??=new TrayHost(()=>Dispatcher.BeginInvoke(new Action(()=>{Show();WindowState=WindowState.Normal;Activate();})),()=>Dispatcher.BeginInvoke(new Action(Close)));
            Hide();
        };
        Log("已載入實體輸入與虛擬輸出模組；按連接後啟動。");
        Draw();
        SizeChanged += (_, e) => { if (e.HeightChanged && (e.PreviousSize.Height <= 760) != (e.NewSize.Height <= 760)) Draw(); };
        Loaded += async (_, _) =>
        {
            try
            {
                var at = Array.IndexOf(args, "--capture");
                if (smoke) await Smoke(at >= 0 && at + 1 < args.Length ? args[at + 1] : "captures");
                if(args.Contains("--hardware-live"))
                {
                    _=ConnectReal();
                    if(args.Contains("--bounded-audio")) {
                        using var deadline=new CancellationTokenSource(args.Contains("--bluetooth")?35000:8000);
                        while(!engine.HasOutput)await Task.Delay(100,deadline.Token);
                        var result=await engine.RunBoundedAudioProbe();
                        var path=Array.IndexOf(args,"--hardware-live");
                        if(path>=0 && path+1<args.Length)File.WriteAllText(args[path+1]+".audio-result.txt",result);
                        Close();
                    }
                    if(args.Contains("--bounded-motion")) {
                        using var deadline=new CancellationTokenSource(args.Contains("--bluetooth")?35000:8000);
                        while(!engine.HasOutput)await Task.Delay(100,deadline.Token);
                        var result=await engine.TestMotionForwarding();
                        var path=Array.IndexOf(args,"--hardware-live");
                        if(path>=0 && path+1<args.Length)File.WriteAllText(args[path+1]+".motion-result.txt",result);
                        Close();
                    }
                    if(args.Contains("--bounded-game-triggers")) {
                        using var deadline=new CancellationTokenSource(args.Contains("--bluetooth")?35000:8000);
                        while(!engine.HasOutput)await Task.Delay(100,deadline.Token);
                        await engine.TestRumble();
                        await Task.Delay(300);
                        await engine.TestGameTriggers(5000);
                        await Task.Delay(500);
                        var path=Array.IndexOf(args,"--hardware-live");
                        if(path>=0 && path+1<args.Length)File.WriteAllText(args[path+1]+".game-triggers-result.txt",engine.HapticsStatus+"\nAdvanced feedback frames="+engine.FeedbackReports+"\n"+JsonSerializer.Serialize(engine.HapticsStatistics));
                        Close();
                    }
                    if(args.Contains("--bounded-game-haptics")) {
                        using var deadline=new CancellationTokenSource(args.Contains("--bluetooth")?35000:8000);
                        while(!engine.HasOutput)await Task.Delay(100,deadline.Token);
                        await Task.Delay(1000);
                        await engine.TestGameHd();
                        await Task.Delay(500);
                        var path=Array.IndexOf(args,"--hardware-live");
                        if(path>=0 && path+1<args.Length)File.WriteAllText(args[path+1]+".game-haptics-result.txt",engine.HapticsStatus+"\nAdvanced feedback frames="+engine.FeedbackReports);
                        Close();
                    }
                    if(args.Contains("--bounded-trigger")) {
                        using var deadline=new CancellationTokenSource(args.Contains("--bluetooth")?35000:8000);
                        while(!engine.HasOutput)await Task.Delay(100,deadline.Token);
                        await engine.TestGameRumble();
                        await Task.Delay(500);
                        await engine.TestTriggers(3000);
                        await Task.Delay(500);
                        var path=Array.IndexOf(args,"--hardware-live");
                        if(path>=0 && path+1<args.Length)File.WriteAllText(args[path+1]+".trigger-result.txt","Xbox rumble cue submitted; 3 second trigger preview completed; preview release requested. Physical resistance requires user confirmation.");
                        Close();
                    }
                    if(args.Contains("--hd-test")) {
                        using var deadline=new CancellationTokenSource(60000);
                        while(!engine.HasOutput) await Task.Delay(100,deadline.Token);
                        engine.TestHd();Log("原生 HD 測試已提交。請確認實體感受。");
                    }
                    if(args.Contains("--close-after-connect"))
                    {
                        using var timeout=new CancellationTokenSource(8000);
                        while(!engine.HasOutput) await Task.Delay(100,timeout.Token);
                        await Task.Delay(1500);
                        var live=Array.IndexOf(args,"--hardware-live");
                        if(live>=0 && live+1<args.Length) Capture(args[live+1]+".png");
                        Close();
                    }
                }
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-error.txt"), ex.ToString()); Environment.ExitCode = 1; }
            finally { if (smoke || args.Contains("--close-after-connect") || args.Contains("--bounded-audio") || args.Contains("--bounded-trigger") || args.Contains("--bounded-game-haptics") || args.Contains("--bounded-motion") || args.Contains("--bounded-game-triggers")) Close(); }
        };
    }

    static SolidColorBrush Brush(Color c) => new(c);
    static Style ButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush(Ink)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(Line)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 10, 16, 10)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter); template.VisualTree = border;
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Control.BorderBrushProperty, Brush(Accent)) } });
        style.Triggers.Add(new Trigger { Property = ButtonBase.IsPressedProperty, Value = true, Setters = { new Setter(UIElement.OpacityProperty, .88) } });
        style.Triggers.Add(new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true, Setters = { new Setter(Control.BorderBrushProperty, Brush(Accent)), new Setter(Control.BorderThicknessProperty, new Thickness(2)) } });
        style.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(UIElement.OpacityProperty, .42) } });
        return style;
    }

    TextBlock Text(string value, double size = 14, bool bold = false, bool muted = false) => new() { Text = UiText.Get(value), FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Brush(muted ? Muted : Ink), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    Button Button(string label, Action action, bool primary = false, string? id = null)
    {
        var b = new Button { Content = UiText.Get(label), Tag = id, Background = primary ? Brush(Accent) : Brushes.White, Foreground = primary ? Brushes.White : Brush(Ink), MinHeight = 40 };
        System.Windows.Automation.AutomationProperties.SetName(b,UiText.Get(string.IsNullOrWhiteSpace(label)?id??"操作":label));
        b.Click += (_, _) => action(); return b;
    }
    static StackPanel Column(params UIElement[] children) { var p = new StackPanel(); foreach (var c in children) p.Children.Add(c); return p; }
    static StackPanel Row(params UIElement[] children) { var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var c in children) p.Children.Add(c); return p; }
    static void Space(FrameworkElement e, double bottom = 12) => e.Margin = new Thickness(0, 0, 0, bottom);
    Border Card(UIElement child, Thickness? padding = null) => new() { Background = Brushes.White, BorderBrush = Brush(Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = padding ?? new Thickness(22), Child = child };
    Border Pill(string text, bool warning = false) => new() { Background = warning ? new SolidColorBrush(Color.FromRgb(251, 242, 218)) : Brush(Selected), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 5), Child = Text(text, 12) };
    ComboBox Combo(string[] values, string value, Action<string> changed)
    {
        var items = values.Select(v => new ComboBoxItem { Content = UiText.Get(v), Tag = v }).ToArray();
        var combo = new ComboBox { ItemsSource = items, SelectedItem = items.FirstOrDefault(i => Equals(i.Tag, value)) ?? items[0] };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string selected }) changed(selected); }; return combo;
    }
    void Log(string s)
    {
        var line=$"{DateTime.Now:HH:mm:ss.fff}  {s}";
        logs.Insert(0,line); if(logs.Count>80)logs.RemoveAt(logs.Count-1);
        try
        {
            var path=Path.Combine(Path.GetDirectoryName(preferencesPath)!,"session.log");
            if(File.Exists(path) && new FileInfo(path).Length>1024*1024) File.Move(path,path+".previous",true);
            File.AppendAllText(path,line+Environment.NewLine);
        }
        catch { /* A log failure must not break controller input. */ }
    }
    void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!); ProfileStore.SavePreferences(preferencesPath,preferences); engine.Update(preferences); note = "設定已儲存並套用。"; Log(note); } catch (Exception ex) { note = "儲存失敗：" + ex.Message; } }

    void Draw()
    {
        if(IsLoaded && (WindowState==WindowState.Minimized || !IsVisible)){viewDirty=true;return;}
        viewDirty=false;
        UiText.Use(preferences.Language);
        FontFamily = UiText.Font;
        tray?.ApplyLanguage();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(UiText.Current);
        frequencyText=null;batteryIndicator=null;inputStateText=null;headerBadge=null;connectionBadge=null;motionText=null;hapticsText=null;
        shell.Children.Clear(); shell.RowDefinitions.Clear(); shell.ColumnDefinitions.Clear(); shell.Margin = new Thickness(0);
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        shell.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new Grid { Margin = new Thickness(16, 25, 16, 20) };
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sidebar.RowDefinitions.Add(new RowDefinition());
        sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var logo = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Brush(Accent), Margin = new Thickness(0, 0, 9, 0), Child = new TextBlock { Text = "C", FontSize = 23, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        sidebar.Children.Add(Row(logo, Column(Text("Controller", 15, true), Text("Hub", 15, true))));
        var nav = new StackPanel { Margin = new Thickness(0, 42, 0, 0) };
        var glyphs = new[] { "\uE7FC", "\uE9D9", "\uE765", "\uE9CE", "\uE7C1", "\uE713" };
        var titles = new[] { "手柄", "震動", "背鍵 / C 鍵", "體感與進階", "遊戲配置", "診斷與設定" };
        for (int i = 0; i < titles.Length; i++)
        {
            var title = titles[i]; bool active = page == title;
            var icon = new TextBlock { Text = glyphs[i], FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 17, Foreground = Brush(active ? Accent : Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) };
            var label = Text(title, 13, active); label.Foreground = Brush(active ? Accent : Muted);
            var tab = Button("", () => { page = title; note = ""; Draw(); }, id: "nav-" + title);
            System.Windows.Automation.AutomationProperties.SetName(tab,UiText.Get(title));
            tab.Content = Row(icon, label); tab.Background = active ? Brush(Selected) : Brushes.Transparent; tab.BorderThickness = new Thickness(0);
            tab.HorizontalContentAlignment = HorizontalAlignment.Left; tab.Padding = new Thickness(12, 13, 8, 13); tab.Margin = new Thickness(0, 0, 6, 6); nav.Children.Add(tab);
        }
        Grid.SetRow(nav, 1); sidebar.Children.Add(nav);
        var bottom = Column(Text("個人工作台", 12, true), Text("Windows 11  /  v0.3.4", 11, muted: true)); Grid.SetRow(bottom, 3); sidebar.Children.Add(bottom);
        shell.Children.Add(new Border { Background = Brushes.White, BorderBrush = Brush(Line), BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar });
        var main = new Grid { Margin = new Thickness(28, 25, 28, 18) }; main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetColumn(main, 1); shell.Children.Add(main);
        var header = new Grid { Margin = new Thickness(0, 0, 0, 24) }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = page switch { "手柄" => "Controller Hub", "震動" => "震動與回饋", "背鍵 / C 鍵" => "按鍵映射", _ => page };
        var subText = page switch { "手柄" => "連接你的手柄，設定遊戲中的輸入方式。", "震動" => "調整每一次操作的回饋。", "背鍵 / C 鍵" => "把常用動作，放在順手的位置。", "遊戲配置" => "管理不同遊戲的操作習慣。", _ => "查看連接記錄與應用設定。" };
        var heading = Text(titleText, 26, true); var subtitle = Text(subText, 13, muted: true); subtitle.Margin = new Thickness(0, 7, 0, 0); header.Children.Add(Column(heading, subtitle));
        var badge = headerBadge = new ConnectionBadge(); badge.Update(engine.HasInput,engine.HasOutput,"等待連接"); badge.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(badge, 1); header.Children.Add(badge); main.Children.Add(header);
        UIElement content = page switch { "震動" => RumblePage(), "背鍵 / C 鍵" => MappingPage(), "遊戲配置" => ProfilesPage(), "診斷與設定" => DiagnosticsPage(), "體感與進階" => AdvancedPage(), _ => HomePage() };
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 3, 0) }; Grid.SetRow(scroll, 1); main.Children.Add(scroll);
        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(Text(string.IsNullOrEmpty(note) ? "設定保存在本機；修改輸出模式會停止當前連接。" : note, 11, muted: true)); var f = Text("ControllerHub", 10, muted: true); Grid.SetColumn(f, 1); footer.Children.Add(f); Grid.SetRow(footer, 2); main.Children.Add(footer);
    }

    UIElement HomePage()
    {
        var root = new StackPanel();
        var workspace = new Grid(); workspace.ColumnDefinitions.Add(new ColumnDefinition()); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        var device = new Grid { Margin = new Thickness(0, 0, 25, 0) }; device.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); device.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Height <= 760 ? 145 : 250) }); device.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var deviceHeader = new Grid(); deviceHeader.ColumnDefinitions.Add(new ColumnDefinition()); deviceHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var deviceName = Text(preferences.Controller, 23, true); var modelHint = Text(preferences.Controller == "Switch 2 Pro" ? "NINTENDO  /  PRO CONTROLLER" : "SONY  /  DUALSENSE", 10, muted: true); modelHint.Margin = new Thickness(0, 6, 0, 0); deviceHeader.Children.Add(Column(deviceName, modelHint));
        var selector = Combo(new[] { "Switch 2 Pro", "PS5 DualSense" }, preferences.Controller, value => { CancelConnection(false); preferences.Controller = value; Save(); Draw(); }); selector.Width = 145; selector.Height = 38;
        // On compact windows the selector moves below the name rather than squeezing it.
        var titleGroup = Column(deviceHeader, selector); selector.HorizontalAlignment = HorizontalAlignment.Left; selector.Margin = new Thickness(0, 13, 0, 0); device.Children.Add(titleGroup);
        var art = new ControllerDrawing(preferences.Controller == "PS5 DualSense") { Margin = new Thickness(8, 10, 8, 0), ToolTip = UiText.Get(preferences.Controller == "PS5 DualSense" ? "DualSense 圖示：Al. Lopez / Gamepad Asset Pack" : "Switch 2 Pro 圖示：NS2ProCon") }; Grid.SetRow(art, 1); device.Children.Add(art);
        var stats = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
        frequencyText=Text(engine.HasInput?$"{engine.Hertz:0} Hz":"— Hz",17,true);
        batteryIndicator=new BatteryIndicator { Margin=new Thickness(0,6,0,0) }; batteryIndicator.Update(engine.Battery,engine.Charging,engine.HasInput);
        frequencyText.Margin=new Thickness(0,6,0,0);
        var deviceState=Text(engine.HasInput?"有實體輸入":"未連接",17,true); deviceState.Margin=new Thickness(0,6,0,0);
        stats.Children.Add(Column(Text("輸入頻率",11,muted:true),frequencyText));
        stats.Children.Add(Column(Text("手柄電量",11,muted:true),batteryIndicator));
        stats.Children.Add(Column(Text("裝置狀態",11,muted:true),deviceState)); inputStateText=Text("等待實體按鍵輸入",11,muted:true); inputStateText.Margin=new Thickness(0,12,0,0); Grid.SetRow(stats, 2); device.Children.Add(Column(stats,inputStateText)); Grid.SetRow(device.Children[^1],2); workspace.Children.Add(device);
        var connectionPanel = new StackPanel { Margin = new Thickness(24, 2, 0, 0) }; connectionPanel.Children.Add(Text("建立連接", 17, true)); var connHint = Text(engine.HasInput && engine.HasOutput ? "手柄已連接，可開始遊戲。" : engine.HasInput ? "實體輸入已連接，可測試按鍵與震動。" : phase=="等待手柄重連" ? "關聯已完成。現在短按 Home 喚醒。" : preferences.Transport == "USB 有線" ? (preferences.Controller=="Switch 2 Pro"?"接上資料線後短按 Home 喚醒，勿按 SYNC。":"接上資料線後短按 PS 喚醒。") : preferences.Controller == "Switch 2 Pro" ? "已關聯請按 Home；首次配對長按 SYNC。" : "先在 Windows 配對；已配對請按 PS 喚醒。", 12, muted: true); connHint.Margin = new Thickness(0, 8, 0, 16); connectionPanel.Children.Add(connHint);
        var transport = Combo(new[] { "藍牙", "USB 有線" }, preferences.Transport, value => { CancelConnection(false); preferences.Transport = value; Save(); Draw(); }); transport.IsEnabled = connection == null; Space(transport, 22); connectionPanel.Children.Add(transport);
        var steps = new[] { "搜尋裝置", "建立連線", "初始化輸入" }; if (Height <= 760) { var compactSteps = Text("搜尋 → 連線 → 初始化", 12, muted: true); Space(compactSteps, 15); connectionPanel.Children.Add(compactSteps); } else for (int i = 0; i < steps.Length; i++) { bool active = phase == steps[i] || connected; var dot = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Brush(connected ? ConnectionBadge.Emerald : active ? Accent : Color.FromRgb(241, 243, 248)), Margin = new Thickness(0, 0, 10, 0), Child = new TextBlock { Text = connected ? "✓" : (i + 1).ToString(), Foreground = active ? Brushes.White : Brush(Muted), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 } }; var line = Row(dot, Text(steps[i], 12, active)); Space(line, 13); connectionPanel.Children.Add(line); }
        var status = connectionBadge = new ConnectionBadge(); status.Update(engine.HasInput,engine.HasOutput,phase); status.HorizontalAlignment = HorizontalAlignment.Left; status.Margin = new Thickness(0, 8, 0, 17); connectionPanel.Children.Add(status);
        connectionPanel.Children.Add(Button(connection != null ? "取消 / 中斷連接" : "連接手柄  →", () => { if (connection != null) { CancelConnection(true); Draw(); } else _ = ConnectReal(); }, true, "connect"));
        var inputOnly=Button("僅測試實體輸入",()=>{ if(connection==null) _=ConnectReal(true); }); inputOnly.Margin=new Thickness(0,8,0,0); connectionPanel.Children.Add(inputOnly);
        var separate = new Border { BorderBrush = Brush(Line), BorderThickness = new Thickness(1, 0, 0, 0), Child = connectionPanel }; Grid.SetColumn(separate, 1); workspace.Children.Add(separate);
        var hero = Card(workspace, new Thickness(25)); Space(hero, 25); root.Children.Add(hero);
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.Children.Add(Text("輸出模式", 17, true)); var n = Text(engine.HasOutput?"虛擬手柄已建立":"需要 USB/IP 驅動", 11, muted: true); Grid.SetColumn(n, 1); row.Children.Add(n); Space(row, 12); root.Children.Add(row);
        var modes = new UniformGrid { Columns = 4 }; var descriptions = new Dictionary<string, string> { ["Steam / Pro2"] = "Steam 輸入配置", ["Pro1"] = "一代 Pro 兼容", ["Xbox"] = "一般 PC 遊戲", ["PS5"] = "依遊戲支援" };
        foreach (var name in descriptions.Keys)
        {
            bool selected = preferences.Output == name; var title = Text(name, 15, true); title.Foreground = Brush(selected ? Accent : Ink);
            var detail = Text(descriptions[name], 11, muted: true); detail.Margin = new Thickness(0, 7, 0, 0); var label = Column(title, detail);
            var b = Button("", () => { if(preferences.Output!=name) CancelConnection(false); preferences.Output = name; Save(); Draw(); }, id: "mode-" + name); b.Content = label; b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Padding = new Thickness(16, 17, 12, 17); b.MinHeight = 84; b.Margin = new Thickness(0, 0, name == "PS5" ? 0 : 9, 0); b.BorderBrush = Brush(selected ? Accent : Line); b.Background = selected ? Brush(Selected) : Brushes.White; modes.Children.Add(b);
        }
        if(args.Contains("--manual-latency"))
        {
            var latency=Button("開始 ZL／ZR 延遲測試（30 秒）",async ()=>
            {
                if(gameHapticsTesting)return;gameHapticsTesting=true;
                try
                {
                    note="測試進行中：請反覆按下、鬆開 ZL／ZR，各停留約半秒。";Draw();
                    int at=Array.IndexOf(args,"--hardware-live");
                    string result=await TriggerLatencyProbe.Run(engine,args[at+1]);Log(result);note=result;
                    Draw();
                }
                catch(Exception ex){note=ex.Message;Log(ex.Message);Draw();}
                finally {gameHapticsTesting=false;}
            },true,id:"trigger-latency");latency.IsEnabled=engine.HasOutput && !gameHapticsTesting;
            latency.Margin=new Thickness(0,12,0,12);root.Children.Add(latency);
        }
        root.Children.Add(modes); var current = Text($"{preferences.Controller}   →   {preferences.Output}   ·   設定保存在本機", 11, muted: true); current.Margin = new Thickness(0, 14, 0, 0); root.Children.Add(current); return root;
    }

    async Task ConnectReal(bool inputOnly=false)
    {
        if(args.Contains("--smoke")) { await ConnectDemo(); return; }
        if(connection!=null) return;
        try { Validate(preferences); } catch(Exception ex) { note=ex.Message; Draw(); return; }
        lock(observedGate) observedKeys.Clear();
        var source=new CancellationTokenSource(); connection=source;
        try { await engine.Run(ProfileStore.Copy(preferences),source.Token,inputOnly); }
        finally { if(ReferenceEquals(connection,source)) connection=null; source.Dispose(); connected=false; if(!closing)Draw(); }
    }
    async Task ConnectDemo()
    {
        if (connection != null) return;
        connection = new CancellationTokenSource(); var token = connection.Token; int current = ++generation; connected = false;
        try
        {
            foreach (var step in new[] { "搜尋裝置", "建立連線", "初始化輸入" })
            {
                phase = step; note = "演示流程，不會搜尋真實藍牙裝置。"; Log("[示範] " + step); Draw(); await Task.Delay(args.Contains("--smoke") ? 40 : 1050, token);
                if (current != generation) return;
                if (preferences.SimulateFailure && step == "搜尋裝置") { phase = "演示連接失敗"; note = "示範：搜尋逾時。可檢查配對模式，或至診斷頁關閉失敗演示後重試。"; Log("[示範] 搜尋逾時，沒有建立虛擬裝置。"); return; }
            }
            connected = true; phase = "演示完成"; note = "演示完成。輸入頻率與電量保持未知，不產生虛構硬件數值。"; Log("[示範] 流程完成；實體連接未實作。");
        }
        catch (OperationCanceledException) { if (current == generation) phase = "已取消"; }
        finally { if (current == generation) { connection?.Dispose(); connection = null; Draw(); } }
    }
    void CancelConnection(bool notify)
    {
        generation++; connection?.Cancel(); engine.Cancel(); connected = false; phase = "尚未連接";
        if(args.Contains("--smoke")) { connection?.Dispose(); connection=null; }
        if (notify) { phase = "正在中斷"; note = "正在停止震動並釋放虛擬手柄。"; Log(note); }
    }

    UIElement RumblePage()
    {
        bool manual=args.Contains("--manual-haptics");
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(270) });
        var left = new StackPanel(); left.Children.Add(Text("震動強度", 20, true)); var help = Text("調整震動強度，確認手柄回饋。", 13, muted: true); help.Margin = new Thickness(0, 8, 0, 20); left.Children.Add(help);
        left.Children.Add(new ControllerDrawing(preferences.Controller == "PS5 DualSense") { Height = 225 });
        var value = Text($"震動強度   {preferences.Rumble:0}%", 17, true); Space(value); left.Children.Add(value);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = preferences.Rumble, TickFrequency = 5, IsSnapToTickEnabled = true, Margin = new Thickness(0, 5, 0, 20) };
        slider.IsEnabled=!manual;
        slider.ValueChanged += (_, _) => { preferences.Rumble = slider.Value; value.Text = UiText.Get($"震動強度   {slider.Value:0}%"); }; left.Children.Add(slider);
        var saveRumble=Button("儲存震動設定", () => { Save(); Draw(); }, true, "save-rumble");saveRumble.IsEnabled=!manual;left.Children.Add(saveRumble);
        var c = Card(left); c.Margin = new Thickness(0, 0, 16, 0); grid.Children.Add(c);
        var test = Column(Text("回饋測試", 18, true)); var text = Text(manual?"握好手柄，按藍色按鈕開始。應有兩段短震動，中間停頓；結束後程式自動退出。":engine.HasInput?"發送 0.5 秒普通震動，結束後自動停止。":"先在手柄頁連接，或選擇僅測試實體輸入。", 14, muted: true); text.Margin = new Thickness(0, 12, 0, 20); test.Children.Add(text);
        var b = Button("測試普通震動", async () => { try { await engine.TestRumble(); note="震動測試指令已發送，請確認實體感受。"; } catch(Exception e) { note=e.Message; } Draw(); }, id: "test-rumble"); b.IsEnabled = engine.HasInput && !manual; Space(b, 24); test.Children.Add(b);
        var gameTest=Button("測試 Xbox 回饋鏈路",async ()=> {try {await engine.TestGameRumble();note="Windows 遊戲回饋測試已發送，請確認實體震動。";} catch(Exception e){note=e.Message;}Draw();},id:"test-game-rumble"); gameTest.IsEnabled=!manual && engine.HasOutput && preferences.Output=="Xbox"; Space(gameTest,20); test.Children.Add(gameTest);
        var hd=Button("測試原生 HD 震動",()=> {try {engine.TestHd();note="已提交原生 HD 辨識脈衝，請確認實體感受。";}catch(Exception e){note=e.Message;} Draw();}); hd.IsEnabled=!manual && engine.HasInput && preferences.Controller=="Switch 2 Pro"; Space(hd,12); test.Children.Add(hd);
        var hdGame=Button(manual?"開始兩段 HD 震動測試":"測試 PS5 觸覺回饋鏈路",async ()=> {
            if(gameHapticsTesting)return;gameHapticsTesting=true;
            if(manual)IsEnabled=false;
            try {
                await engine.TestGameHd();await Task.Delay(500);
                note="觸覺測試結束，請確認兩段震動。";
                if(manual) {var i=Array.IndexOf(args,"--hardware-live");if(i>=0 && i+1<args.Length)File.WriteAllText(args[i+1]+".game-haptics-result.txt",engine.HapticsStatus+"\n"+JsonSerializer.Serialize(engine.HapticsStatistics));}
            }
            catch(Exception e){note=e.Message;if(manual)Environment.ExitCode=1;}
            finally {gameHapticsTesting=false;if(manual)Close();else Draw();}
        },primary:manual);hdGame.IsEnabled=engine.HasOutput && preferences.Output=="PS5" && !gameHapticsTesting;Space(hdGame,12);test.Children.Add(hdGame);
        var triggers=Button("測試扳機阻力（1.5 秒）",async ()=> {try {await engine.TestTriggers();note="扳機測試結束，已釋放測試效果。";}catch(Exception e){note=e.Message;}Draw();}); triggers.IsEnabled=!manual && engine.HasInput && preferences.Controller=="PS5 DualSense"; Space(triggers,20); test.Children.Add(triggers);
        test.Children.Add(Text("功能狀態", 13, true)); foreach (var s in new[] { "普通震動      已接入，可測試", "HD 震動        已接入，待實機", "自適應扳機    PS5 模式待實機" }) { var t = Text(s, 13, muted: true); t.Margin = new Thickness(0, 12, 0, 0); test.Children.Add(t); }
        var right = Card(test); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }

    UIElement MappingPage()
    {
        if (preferences.Controller == "PS5 DualSense") return Card(Column(Text("這款手柄沒有背鍵", 25, true), new ControllerDrawing(true) { Height = 240 }, Text("普通 DualSense 沒有 GL / GR 和 C 鍵。請在手柄頁切換至 Switch 2 Pro。", 14, muted: true), Button("選擇 Switch 2 Pro", () => { preferences.Controller = "Switch 2 Pro"; Save(); Draw(); }, true)));
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(270) });
        var left = new StackPanel(); left.Children.Add(Text("Switch 2 Pro", 22, true)); var caption = Text("先選按鍵，再設定它在遊戲中的動作。", 13, muted: true); caption.Margin = new Thickness(0, 8, 0, 10); left.Children.Add(caption);
        left.Children.Add(new ControllerDrawing(false) { Height = 280, Rear = true });
        var keys = new UniformGrid { Columns = 3 }; foreach (var key in new[] { "GL", "GR", "C" }) { var b = Button(key == "C" ? "C 鍵" : key + " 背鍵", () => { selectedKey = key; Draw(); }, id: "key-" + key); b.Margin = new Thickness(0, 0, 8, 0); b.Background = selectedKey == key ? Brush(Selected) : Brushes.White; keys.Children.Add(b); } left.Children.Add(keys);
        var summary = Text($"GL → {preferences.GL}      GR → {preferences.GR}      C → {preferences.C}", 14, true); summary.Margin = new Thickness(0, 24, 0, 5); left.Children.Add(summary); left.Children.Add(Text("這是按鍵位置示意，不代表已寫入手柄。", 12, muted: true)); var lc = Card(left); lc.Margin = new Thickness(0, 0, 16, 0); grid.Children.Add(lc);
        string selected = selectedKey == "GL" ? preferences.GL : selectedKey == "GR" ? preferences.GR : preferences.C;
        var editor = Column(Text(selectedKey == "C" ? "C 鍵設定" : selectedKey + " 背鍵設定", 18, true)); var label = Text("映射至", 13, muted: true); label.Margin = new Thickness(0, 24, 0, 8); editor.Children.Add(label);
        editor.Children.Add(Combo(new[] { "不映射", "A", "B", "X", "Y", "L", "R", "ZL", "ZR", "左搖桿按下", "右搖桿按下", "A + B", "L + R" }, selected, s => { if (selectedKey == "GL") preferences.GL = s; else if (selectedKey == "GR") preferences.GR = s; else preferences.C = s; }));
        var description = Text("儲存後立即套用於虛擬輸出。A／B／X／Y 使用 Nintendo 鍵位名稱；設定保存在電腦。", 13, muted: true); description.Margin = new Thickness(0, 16, 0, 22); editor.Children.Add(description); editor.Children.Add(Button("儲存按鍵設定", () => { Save(); Draw(); }, true, "save-mapping"));
        var reset = Button("恢復這個按鍵", () => { if (selectedKey == "GL") preferences.GL = "B"; else if (selectedKey == "GR") preferences.GR = "A"; else preferences.C = "不映射"; Save(); Draw(); }); reset.Margin = new Thickness(0, 10, 0, 0); editor.Children.Add(reset);
        var rc = Card(editor); Grid.SetColumn(rc, 1); grid.Children.Add(rc); return grid;
    }

    CheckBox Toggle(string label,bool initial,Action<bool> change)
    {
        var box=new CheckBox {Content=UiText.Get(label),IsChecked=initial,Margin=new Thickness(0,10,0,10),FontSize=14};
        box.Checked+=(_,_)=>change(true); box.Unchecked+=(_,_)=>change(false); return box;
    }
    UIElement AdvancedPage()
    {
        var root=new StackPanel();
        var motion=Column(Text("體感與靜置校準",20,true));
        motion.Children.Add(Text("PS5、Pro1 與 Pro2 可轉發體感；Xbox XInput 沒有體感通道。",13,muted:true));
        motion.Children.Add(Toggle("轉發陀螺儀與加速度",preferences.MotionEnabled,v=>{preferences.MotionEnabled=v;Save();}));
        motionText=Text("等待有效體感資料",13,muted:true); Space(motionText,12); motion.Children.Add(motionText);
        motion.Children.Add(Button("靜置校準（約 2 秒）",async ()=> {try {note="請保持手柄平放靜止。";var b=await engine.CalibrateMotion();preferences.GyroBiasPitch=b.Pitch;preferences.GyroBiasYaw=b.Yaw;preferences.GyroBiasRoll=b.Roll;preferences.GyroCalibrationController=preferences.Controller;preferences.GyroCalibratedAt=DateTimeOffset.Now;Save();note="體感靜態偏差已儲存。";}catch(Exception e){note=e.Message;}Draw();}));
        if(preferences.GyroCalibratedAt is {} at && preferences.GyroCalibrationController==preferences.Controller) {var result=Pill($"✓ 校準已儲存 · {at:HH:mm:ss}");result.Background=Brush(Color.FromRgb(236,253,245));((TextBlock)result.Child).Foreground=Brush(Color.FromRgb(6,95,70)); result.Margin=new Thickness(0,10,0,0); motion.Children.Add(result);}
        var bias=Text($"偏差 °/s   P {preferences.GyroBiasPitch:F2} / Y {preferences.GyroBiasYaw:F2} / R {preferences.GyroBiasRoll:F2}",12,muted:true); bias.Margin=new Thickness(0,10,0,0); motion.Children.Add(bias);
        var a=Card(motion);Space(a);root.Children.Add(a);
        var feedback=Column(Text("進階回饋",20,true));
        feedback.Children.Add(Toggle("啟用原生 HD／PS5 觸覺資料",preferences.HdHaptics,v=>{preferences.HdHaptics=v;Save();}));
        feedback.Children.Add(Toggle("轉發 PS5 遊戲的自適應扳機",preferences.AdaptiveTriggers,v=>{preferences.AdaptiveTriggers=v;Save();}));
        feedback.Children.Add(Text("需要遊戲實際提供對應回饋。Switch 2 Pro 沒有可產生阻力的扳機；Xbox 普通震動不會自動變成原生觸覺。",13,muted:true));
        hapticsText=Text(engine.HapticsStatus,12,muted:true);hapticsText.Margin=new Thickness(0,12,0,0);feedback.Children.Add(hapticsText);
        var bcard=Card(feedback);Space(bcard);root.Children.Add(bcard);
        var audio=Column(Text("音訊轉震動",20,true));
        audio.Children.Add(Text("使用系統播放音訊合成觸覺；只在開啟後開始擷取，不保存音訊。",13,muted:true));
        try {
            var sources=Input.FusionInput.AudioSources();
            var choices=new[]{"系統預設播放裝置"}.Concat(sources.Select((x,i)=>$"{i+1}. {x.Name}")).ToArray();
            int selected=Array.FindIndex(sources,x=>x.Id==preferences.AudioEndpointId)+1;
            audio.Children.Add(Combo(choices,choices[selected],async name=> {var i=Array.IndexOf(choices,name);preferences.AudioEndpointId=i>0?sources[i-1].Id:"";Save();try {await Task.Run(engine.RefreshAudio);}catch(Exception e){note=e.Message;}Draw();}));
        } catch(Exception e) {audio.Children.Add(Text("音訊來源無法列舉："+e.Message,12,muted:true));}
        audio.Children.Add(Toggle("啟用系統音訊轉震動",preferences.AudioHaptics,async v=>{preferences.AudioHaptics=v;Save();try {await Task.Run(engine.RefreshAudio);}catch(Exception e){note=e.Message;}Draw();}));
        audio.Children.Add(Toggle("取代遊戲觸覺（關閉則混合）",preferences.AudioReplace,async v=>{preferences.AudioReplace=v;Save();try {await Task.Run(engine.RefreshAudio);}catch(Exception e){note=e.Message;}}));
        audio.Children.Add(Button("播放固定低音測試（1 秒）",async ()=> {try {await engine.TestAudio();note="低音測試結束，請確認是否有震動。";}catch(Exception e){note=e.Message;}Draw();}));
        audio.Children.Add(Text($"音訊增益 {preferences.AudioGain}%",13,true));
        var gain=new Slider {Minimum=0,Maximum=200,Value=preferences.AudioGain,TickFrequency=10,IsSnapToTickEnabled=true,Margin=new Thickness(0,12,0,12)};
        gain.ValueChanged+=(_,_)=>preferences.AudioGain=(int)gain.Value; audio.Children.Add(gain);
        audio.Children.Add(Button("套用音訊設定",async ()=>{Save();try {await Task.Run(engine.RefreshAudio);note=engine.HapticsStatus;}catch(Exception e){note=e.Message;}Draw();}));
        var c=Card(audio);Space(c);root.Children.Add(c);
        var connection=Column(Text("連接與背景運行",20,true));
        connection.Children.Add(Toggle("輸入中斷後自動重連（最多 3 次）",preferences.AutoReconnect,v=>{preferences.AutoReconnect=v;Save();}));
        connection.Children.Add(Toggle("自動選擇 USB／藍牙（優先 USB）",preferences.AutoTransport,v=>{preferences.AutoTransport=v;Save();}));
        connection.Children.Add(Toggle("最小化時縮到系統匣",preferences.MinimizeToTray,v=>{preferences.MinimizeToTray=v;if(!v){tray?.Dispose();tray=null;}Save();}));
        root.Children.Add(Card(connection));
        var hiding=Column(Text("避免遊戲雙重輸入",20,true));
        hiding.Children.Add(Text(Input.FusionInput.HidingInstalled?"HidHide 驅動已安裝":"HidHide 未安裝（可選）",13,muted:true));
        hiding.Children.Add(Toggle("連接虛擬輸出後隱藏實體 HID 手柄",preferences.HidePhysical,async v=>{preferences.HidePhysical=v;Save();await Task.Run(engine.RefreshHiding);note=engine.HidingStatus;Draw();}));
        hiding.Children.Add(Text(engine.HidingStatus,12,muted:true));
        hiding.Children.Add(Button("安裝 HidHide…",async ()=>{try {using var process=BackendFiles.InstallHidHide();if(process!=null)await process.WaitForExitAsync();note="HidHide 安裝程式已關閉，請按提示重啟後再啟用隱藏。";}catch(Exception e){note=e.Message;}Draw();}));
        var open=Button("開啟 HidHide 設定",()=> { try {
            var roots=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"D:\Program Files"};
            var cli=roots.Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(r=>new[]{Path.Combine(r,"Nefarius Software Solutions","HidHide","HidHideClient.exe"),Path.Combine(r,"Nefarius Software Solutions","HidHide","x64","HidHideClient.exe")}).FirstOrDefault(File.Exists);
            if(cli!=null)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cli){UseShellExecute=true});
            else {note="找不到 HidHide 設定程式，請先完成安裝。";Draw();}
        }catch(Exception e){note="無法開啟 HidHide 設定："+e.Message;Draw();}});open.Margin=new Thickness(0,8,0,0);hiding.Children.Add(open);
        hiding.Children.Add(Text("首次使用需在 HidHide 設定中啟用裝置隱藏。僅管理本次實體手柄，連接停止時還原；不隱藏虛擬輸出。",12,muted:true));
        var hidCard=Card(hiding);hidCard.Margin=new Thickness(0,16,0,0);root.Children.Add(hidCard);return root;
    }

    UIElement ProfilesPage()
    {
        var root = new StackPanel(); root.Children.Add(Text("我的配置", 20, true)); var intro = Text("手動套用不同遊戲的映射、震動與輸出設定。切換配置會停止當前連接。", 13, muted: true); intro.Margin = new Thickness(0, 9, 0, 22); root.Children.Add(intro);
        root.Children.Add(Toggle("遊戲啟動時自動套用已綁定配置",preferences.AutoProfiles,v=>{preferences.AutoProfiles=v;Save();}));
        foreach (var p in profiles)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); var stored=savedProfiles.First(s=>s.Name==p); row.Children.Add(Column(Text(p, 17, true), Text($"{stored.Settings.Controller} → {stored.Settings.Output} · GL {stored.Settings.GL} / GR {stored.Settings.GR}", 13, muted: true)));
            var b = Button(selectedProfile == p ? "重新套用" : "套用配置", () => { CancelConnection(false); selectedProfile = p; var auto=preferences.AutoProfiles; var language=preferences.Language; preferences=ProfileStore.Copy(stored.Settings); preferences.Language=language; preferences.AutoProfiles=auto; if(automaticProfile!=null)beforeGame=null; Save(); note="已套用「"+p+"」。請重新連接手柄。"; Draw(); }, selectedProfile == p, "profile-" + p); Grid.SetColumn(b, 1); row.Children.Add(b); var card = Card(row); Space(card); root.Children.Add(card);
        }
        var bindings=Row(Button("綁定遊戲 EXE…",()=> {
            var dialog=new Microsoft.Win32.OpenFileDialog {Filter=UiText.Get("遊戲程式|*.exe")};
            if(dialog.ShowDialog(this)==true && selectedProfile!=null) {var i=savedProfiles.FindIndex(p=>p.Name==selectedProfile);if(i>=0){savedProfiles[i]=savedProfiles[i] with {ExecutablePath=dialog.FileName};profileStore.Save(savedProfiles);note="已綁定 "+Path.GetFileName(dialog.FileName);Draw();}}
        }),Button("解除遊戲綁定",()=> {var i=savedProfiles.FindIndex(p=>p.Name==selectedProfile);if(i>=0){savedProfiles[i]=savedProfiles[i] with {ExecutablePath=null};profileStore.Save(savedProfiles);note="已解除綁定。";Draw();}}),Button("刪除配置",()=> {if(savedProfiles.Count<=1){note="至少保留一個配置。";Draw();return;}savedProfiles.RemoveAll(p=>p.Name==selectedProfile);profileStore.Save(savedProfiles);profiles.Clear();profiles.AddRange(savedProfiles.Select(p=>p.Name));selectedProfile=profiles[0];Draw();}));
        bindings.Margin=new Thickness(0,8,0,8);root.Children.Add(bindings);
        if(savedProfiles.FirstOrDefault(p=>p.Name==selectedProfile)?.ExecutablePath is {} game) root.Children.Add(Text("綁定遊戲："+Path.GetFileName(game),12,muted:true));
        var name=new TextBox { Text=selectedProfile??UiText.Get("新配置"),Padding=new Thickness(10),Margin=new Thickness(0,8,0,8) }; root.Children.Add(name);
        root.Children.Add(Button("將目前設定儲存為此配置",()=> { try { var n=name.Text.Trim(); if(n.Length==0 || n.Length>60) throw new Exception("配置名稱請輸入 1 至 60 個字。"); var old=savedProfiles.FirstOrDefault(p=>p.Name==n); savedProfiles.RemoveAll(p=>p.Name==n); savedProfiles.Add(new(n,ProfileStore.Copy(preferences),old?.ExecutablePath)); profileStore.Save(savedProfiles); profiles.Clear(); profiles.AddRange(savedProfiles.Select(p=>p.Name)); selectedProfile=n; note="配置已儲存。"; } catch(Exception e) { note=e.Message; } Draw(); },true));
        var actions=Row(Button("匯出目前設定…",()=> { var d=new Microsoft.Win32.SaveFileDialog {FileName="controller-profile.json",Filter=UiText.Get("配置|*.json")}; if(d.ShowDialog(this)==true) { try { File.WriteAllText(d.FileName,JsonSerializer.Serialize(preferences,new JsonSerializerOptions {WriteIndented=true})); } catch(Exception e) {note=e.Message;} } }),Button("匯入設定…",()=> { var d=new Microsoft.Win32.OpenFileDialog {Filter=UiText.Get("配置|*.json")}; if(d.ShowDialog(this)==true) { try { var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(d.FileName)) ?? throw new Exception("配置內容無效。"); Validate(p); CancelConnection(false); p.Language=preferences.Language; preferences=p; Save(); } catch(Exception e) {note=e.Message;} Draw(); } })); actions.Margin=new Thickness(0,12,0,0); root.Children.Add(actions); return root;
    }

    async Task ObserveGameProfile()
    {
        inspectingGame=true;
        try
        {
            var bound=savedProfiles.ToArray();
            var match=await Task.Run(()=>GameProfileWatcher.FindRunning(bound));
            if(match.Ambiguous)return;
            var selected=match.Profile;
            if(closing || !preferences.AutoProfiles) return;
            if(selected?.Name==automaticProfile) return;
            Preferences? next=null;
            if(selected!=null)
            {
                if(automaticProfile==null) {beforeGame=ProfileStore.Copy(preferences);profileBeforeGame=selectedProfile;}
                next=ProfileStore.Copy(selected.Settings);selectedProfile=selected.Name;automaticProfile=selected.Name;
            }
            else if(automaticProfile!=null)
            {next=beforeGame;selectedProfile=profileBeforeGame;automaticProfile=null;beforeGame=null;}
            if(next==null)return;
            Validate(next);next.AutoProfiles=true;
            bool resume=connection!=null;
            CancelConnection(false);
            var until=DateTime.UtcNow.AddSeconds(12);
            while(connection!=null && DateTime.UtcNow<until && !closing) await Task.Delay(50);
            if(connection!=null || closing) {note="上一個連接仍在釋放，請手動套用配置。";return;}
            next.Language=preferences.Language;preferences=next;Save();Log(automaticProfile==null?"遊戲已退出，還原先前設定。":"遊戲配置已自動套用："+automaticProfile);Draw();
            if(resume) _=ConnectReal();
        }
        catch(Exception e) {Log("遊戲配置："+e.Message);}
        finally {inspectingGame=false;}
    }

    UIElement DiagnosticsPage()
    {
        var root = new StackPanel();
        var languageLabel = Column(Text("Language / 語言", 17, true), Text("立即套用，無需重新啟動。", 12, muted: true));
        var languageItems = UiText.Languages.Select((code, index) => new ComboBoxItem { Tag = code, Content = UiText.LanguageNames[index] }).ToArray();
        var languageChoice = new ComboBox { ItemsSource = languageItems, SelectedItem = languageItems.First(i => Equals(i.Tag, UiText.Normalize(preferences.Language))), Width = 220, Height = 38, HorizontalAlignment = HorizontalAlignment.Right };
        languageChoice.Tag = "language-selector";
        System.Windows.Automation.AutomationProperties.SetName(languageChoice, UiText.Get("介面語言"));
        languageChoice.SelectionChanged += (_, _) => {
            if (languageChoice.SelectedItem is not ComboBoxItem { Tag: string code } || preferences.Language == code) return;
            preferences.Language = code; UiText.Use(code); Save(); note = "語言已更新。"; Draw();
        };
        var languageRow = new Grid(); languageRow.ColumnDefinitions.Add(new()); languageRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        languageRow.Children.Add(languageLabel); Grid.SetColumn(languageChoice, 1); languageRow.Children.Add(languageChoice);
        var languageCard = Card(languageRow, new Thickness(20, 16, 20, 16)); Space(languageCard, 18); root.Children.Add(languageCard);
        root.Children.Add(Text("連接診斷", 20, true)); var intro = Text($"實體輸入：{(engine.HasInput?"有報告":"未連接")}　虛擬輸出：{(engine.HasOutput?"已建立":"未建立")}　累計報告：{engine.Reports}", 13, muted: true); intro.Margin = new Thickness(0, 8, 0, 20); root.Children.Add(intro);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(260) });
        var items = Column(Text("連接記錄", 17, true)); var log = new TextBox { Text = string.Join(Environment.NewLine + Environment.NewLine, logs.Select(UiText.Get)), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 270, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Brush(Muted), FontFamily = FontFamily, FontSize = 13, Margin = new Thickness(0, 14, 0, 14) }; items.Children.Add(log);
        items.Children.Add(Button("預覽與匯出診斷…", ShowDiagnosticPreview, id: "preview-diagnostics")); var l = Card(items); l.Margin = new Thickness(0, 0, 16, 0); grid.Children.Add(l);
        var settings = Column(Text("後端與驅動", 17, true)); var backend = Text($"VIIPER：隨程式附帶\nUSB/IP：{(Processes.FindUsbip()==null?"未找到，請安裝":"已找到，等待檢測")}",13,muted:true); backend.Margin=new Thickness(0,16,0,16); settings.Children.Add(backend);
        settings.Children.Add(Button("檢測 USB/IP 驅動",async ()=> { try {var exe=Processes.FindUsbip()??throw new Exception("找不到 usbip.exe。"); using var timeout=new CancellationTokenSource(6000); await Processes.Run(exe,new[]{"port"},timeout.Token); note="USB/IP 命令與驅動檢測成功。";} catch(Exception e){note=e.Message;} Log(note); Draw(); }));
        var install=Button("安裝 USB/IP…",async ()=> { try { using var process=BackendFiles.InstallUsbip(); if(process!=null) await process.WaitForExitAsync(); note="安裝程式已關閉；請檢測驅動，並按安裝程式提示重啟。"; } catch(Exception e) {note=e.Message;} Draw(); }); install.Margin=new Thickness(0,8,0,12); settings.Children.Add(install);
        settings.Children.Add(Button("選擇 usbip.exe…",()=> {var d=new Microsoft.Win32.OpenFileDialog{Filter="usbip.exe|usbip.exe"}; if(d.ShowDialog(this)==true) { Processes.UsbipOverride=d.FileName; note="本次工作階段使用指定的 USB/IP。"; Draw(); }}));
        var windows=Button("Windows 藍牙設定",()=>Processes.Open("ms-settings:bluetooth")); windows.Margin=new Thickness(0,12,0,12); settings.Children.Add(windows);
        settings.Children.Add(Text("安裝驅動會顯示 Windows 權限提示。若遊戲同時讀到實體與虛擬手柄，可用 HidHide 或 Steam Input 管理可見性。",12,muted:true)); var credits = Text("輸入 DS4Windows · GPL-3.0+\n輸出 PRO2 / VIIPER\n手柄圖 NS2ProCon / Al. Lopez · MIT",11,muted:true); credits.Margin=new Thickness(0,16,0,0); settings.Children.Add(credits); var r=Card(settings); Grid.SetColumn(r,1); grid.Children.Add(r); root.Children.Add(grid); return root;
    }

    void ShowDiagnosticPreview()
    {
        var report = DiagnosticReport.Create(engine, preferences, logs);
        var preview = new Window { Owner=this, Title=UiText.Get("診斷報告預覽"), FontFamily=FontFamily, Width=720, Height=620, MinWidth=480, MinHeight=380, WindowStartupLocation=WindowStartupLocation.CenterOwner, Background=Brush(Canvas) };
        var layout = new DockPanel { Margin=new Thickness(20) };
        var help=Text("報告包含連接狀態與最近記錄；已遮蔽常見裝置識別資訊。儲存前可在下方檢查。",13,muted:true); help.Margin=new Thickness(0,0,0,12); DockPanel.SetDock(help,Dock.Top); layout.Children.Add(help);
        var save=Button("儲存報告…",()=> {
            var dialog=new Microsoft.Win32.SaveFileDialog { FileName="ControllerHub-diagnostics.json", Filter=UiText.Get("診斷報告|*.json") };
            if(dialog.ShowDialog(preview)==true) { try { File.WriteAllText(dialog.FileName,report); note="診斷報告已匯出。"; preview.Close(); } catch(Exception e) { MessageBox.Show(preview,UiText.Get(e.Message),UiText.Get("儲存失敗")); } }
        },true); save.Margin=new Thickness(0,12,0,0); DockPanel.SetDock(save,Dock.Bottom); layout.Children.Add(save);
        layout.Children.Add(new TextBox { Text=report, IsReadOnly=true, FontFamily=new FontFamily("Consolas"), FontSize=12, TextWrapping=TextWrapping.Wrap, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, Padding=new Thickness(12) });
        preview.Content=new Border { Background=Brush(Canvas), Child=layout }; preview.ShowDialog(); Draw();
    }

    static void Validate(Preferences p)=>PreferenceRules.Validate(p);

    Button? FindButton(string tag, DependencyObject? parent = null)
    {
        parent ??= shell;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var c = VisualTreeHelper.GetChild(parent, i); if (c is Button b && Equals(b.Tag, tag)) return b; var found = FindButton(tag, c); if (found != null) return found; } return null;
    }
    void Click(string tag) { UpdateLayout(); var b = FindButton(tag) ?? throw new InvalidOperationException("Missing button " + tag); b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
    async Task Settle() { await Dispatcher.InvokeAsync(() => { UpdateLayout(); }, DispatcherPriority.Render); await Task.Delay(110); }
    void Capture(string path, FrameworkElement? target = null)
    {
        UpdateLayout(); target ??= (FrameworkElement)Content; var bitmap = new RenderTargetBitmap((int)target.ActualWidth, (int)target.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(target); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var f = File.Create(path); png.Save(f);
    }
    async Task Smoke(string output)
    {
        Directory.CreateDirectory(output); await Settle(); Capture(Path.Combine(output, "01-home.png"));
        Click("mode-PS5"); if (preferences.Output != "PS5") throw new Exception("Output selection failed");
        Click("connect"); Click("connect"); await Task.Delay(90); if (connected || connection != null) throw new Exception("Cancellation failed");
        preferences.SimulateFailure = true; await ConnectDemo(); if (phase != "演示連接失敗") throw new Exception("Failure route failed"); await Settle(); Capture(Path.Combine(output, "02-failure.png"));
        preferences.SimulateFailure = false; await ConnectDemo(); if (!connected) throw new Exception("Demo completion failed"); CancelConnection(false);
        Click("nav-背鍵 / C 鍵"); Click("key-GR"); preferences.GR = "L + R"; Click("save-mapping"); if (!File.Exists(preferencesPath)) throw new Exception("Settings not saved"); var restored = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(preferencesPath)); if (restored?.GR != "L + R") throw new Exception("Persistence failed"); await Settle(); Capture(Path.Combine(output, "03-mapping.png"));
        Click("nav-震動"); await Settle(); if (FindButton("test-rumble")?.IsEnabled != false) throw new Exception("Unwired rumble must be disabled"); Capture(Path.Combine(output, "04-rumble.png"));
        Click("nav-體感與進階"); await Settle(); Capture(Path.Combine(output,"05-advanced.png"));
        Click("nav-遊戲配置"); Click("profile-動作遊戲"); if (selectedProfile != "動作遊戲") throw new Exception("Profile selection failed"); await Settle(); Capture(Path.Combine(output, "05-profiles.png"));
        Click("nav-診斷與設定"); await Settle(); Capture(Path.Combine(output, "06-diagnostics.png"));
        _ = Dispatcher.BeginInvoke(new Action(()=> { var preview=Application.Current.Windows.OfType<Window>().Single(w=>w.Owner==this); preview.UpdateLayout(); Capture(Path.Combine(output,"06-diagnostic-preview.png"),(FrameworkElement)preview.Content); preview.Close(); }), DispatcherPriority.ApplicationIdle);
        Click("preview-diagnostics");
        preferences.Controller = "PS5 DualSense"; page = "手柄"; Draw(); await Settle(); Capture(Path.Combine(output, "07-dualsense-home.png")); page = "背鍵 / C 鍵"; Draw(); await Settle(); Capture(Path.Combine(output, "07-dualsense.png"));
        preferences.Controller = "Switch 2 Pro"; preferences.Output = "Xbox"; page = "手柄"; Width = 960; Height = 720; Draw(); await Settle(); Capture(Path.Combine(output, "08-small.png"));
        Width=1220; Height=850; page="手柄"; Draw(); await Settle();
        headerBadge!.Update(true,true,"視覺預覽"); connectionBadge!.Update(true,true,"視覺預覽"); batteryIndicator!.Update(90,false,true);
        await Settle(); Capture(Path.Combine(output,"09-connected-battery-preview.png"));
        batteryIndicator.Update(12,true,true); await Settle(); Capture(Path.Combine(output,"10-charging-preview.png"));
        batteryIndicator.Update(14,false,true); await Settle(); Capture(Path.Combine(output,"11-low-battery-preview.png"));
        Width=960; Height=720; Draw(); await Settle(); headerBadge!.Update(true,true,"視覺預覽"); connectionBadge!.Update(true,true,"視覺預覽"); batteryIndicator!.Update(100,true,true); await Settle(); Capture(Path.Combine(output,"12-connected-small-preview.png"));
        // Exercise the real settings selector and persistence using the smoke
        // profile, never the user's live settings or hardware connection.
        var originalMapping = (preferences.GL, preferences.GR, preferences.C, preferences.Transport, preferences.Output);
        var localeReview = new List<object>();
        int untranslatedCount=0;
        IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
            yield return parent;
            for (int i=0; i<VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(parent,i))) yield return child;
        }
        Width=1220; Height=850;
        foreach (var code in UiText.Languages) {
            page="診斷與設定"; Draw(); await Settle();
            var selector=Descendants(shell).OfType<ComboBox>().Single(c=>Equals(c.Tag,"language-selector"));
            selector.SelectedItem=selector.Items.Cast<ComboBoxItem>().Single(i=>Equals(i.Tag,code));
            await Settle();
            var loaded=ProfileStore.LoadPreferences(preferencesPath,out _);
            if(loaded.Language!=code || UiText.Current!=code) throw new Exception("Language selection did not persist: "+code);
            if(originalMapping!=(preferences.GL,preferences.GR,preferences.C,preferences.Transport,preferences.Output)) throw new Exception("Language change altered input settings.");
            if(code=="en") {
                var face=new Typeface(FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
                if(!face.TryGetGlyphTypeface(out var glyph) || !glyph.FamilyNames.Values.Any(n=>n=="Google Sans")) throw new Exception("Bundled Google Sans was not resolved.");
                File.WriteAllText(Path.Combine(output,"english-font.txt"),glyph.FontUri.ToString());
            }
            foreach(var targetPage in new[]{"手柄","震動","背鍵 / C 鍵","體感與進階","遊戲配置","診斷與設定"}) {
                page=targetPage; Draw(); await Settle();
                var strings=Descendants(shell).OfType<TextBlock>().Select(t=>t.Text).ToArray();
                var untranslated=code=="en" ? strings.Where(t=>t.Any(c=>c>='\u3400'&&c<='\u9fff') && !UiText.LanguageNames.Contains(t)).Distinct().ToArray() : Array.Empty<string>();
                untranslatedCount+=untranslated.Length;
                localeReview.Add(new {Language=code,Page=targetPage,Untranslated=untranslated});
                Capture(Path.Combine(output,$"language-{code}-{Array.IndexOf(new[]{"手柄","震動","背鍵 / C 鍵","體感與進階","遊戲配置","診斷與設定"},targetPage)}.png"));
            }
        }
        File.WriteAllText(Path.Combine(output,"language-review.json"),JsonSerializer.Serialize(localeReview,new JsonSerializerOptions{WriteIndented=true}));
        if(untranslatedCount!=0) throw new Exception("Untranslated English UI strings; inspect language-review.json.");
        preferences.Language="en"; preferences.Controller="PS5 DualSense"; page="手柄"; Draw(); await Settle(); Capture(Path.Combine(output,"language-en-dualsense.png"));
        preferences.Controller="Switch 2 Pro"; Width=960; Height=720;
        foreach(var target in new[]{"手柄","診斷與設定"}) { page=target; Draw(); await Settle(); Capture(Path.Combine(output,target=="手柄"?"language-en-small-home.png":"language-en-small-settings.png")); }
        Width=1220; Height=850;
        preferences.Language="zh-Hant"; UiText.Use(preferences.Language); Save(); page="手柄"; Draw();
        metrics.Start();profilePolling.Start();WindowState=WindowState.Minimized;await Settle();
        if(metrics.IsEnabled || !profilePolling.IsEnabled)throw new Exception("Minimizing must stop UI metrics while retaining automatic profile polling.");
        Draw();if(!viewDirty)throw new Exception("Hidden view update was not deferred.");
        WindowState=WindowState.Normal;await Settle();
        if(viewDirty)throw new Exception("Restored view did not consume deferred update.");
        metrics.Stop();profilePolling.Stop();
        File.WriteAllText(Path.Combine(output,"background-ui-checks.txt"),"PASS: minimized UI timer pauses; profile polling remains enabled; deferred view refreshes on restore.");
        File.WriteAllText(Path.Combine(output, "smoke-result.txt"), "PASS: output selection; cancel; simulated timeout; simulated completion; GL/GR/C settings persistence; unwired rumble disabled; navigation; profile selection; DualSense capability state.\nNative WPF rendered at 1220x850 and 960x720 logical window sizes.\nPASS: all three language selectors persist; mapping / transport / output stay unchanged; bundled Google Sans resolves.\nNo physical controller, driver or latency tests performed.");
    }
}

public sealed class ControllerDrawing : FrameworkElement
{
    readonly bool dualSense;
    static readonly BitmapSource DualSenseImage = LoadDualSense();
    public bool Rear { get; set; }
    public ControllerDrawing(bool dualSense) { this.dualSense = dualSense; RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality); }
    static BitmapSource LoadDualSense()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/Assets/DualSense.png", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!dualSense) { ProControllerArtwork.Draw(dc, ActualWidth, ActualHeight, Rear); return; }
        double scale = Math.Min(ActualWidth / DualSenseImage.PixelWidth, ActualHeight / DualSenseImage.PixelHeight);
        if (scale <= 0) return;
        double width = DualSenseImage.PixelWidth * scale, height = DualSenseImage.PixelHeight * scale;
        dc.DrawImage(DualSenseImage, new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height));
    }
}
