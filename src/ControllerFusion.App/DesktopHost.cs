using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ControllerFusion;

public sealed partial class MainWindow
{
    bool desktopMode;
    int revision;
    readonly List<FrameworkElement> desktopControls = new();
    readonly List<ConnectionBadge> badges = new();
    readonly List<TextBlock> liveText = new();
    string lastTelemetry = "";
    static readonly string[] DesktopPages = { "手柄", "震動", "背鍵 / C 鍵", "體感與進階", "遊戲配置", "診斷與設定" };

    void PrepareDesktop()
    {
        desktopMode = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Opacity = 0;
        IsHitTestVisible = false;
        // Leave the window measured for drawing controller artwork and owning dialogs.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.WorkArea.Right - 1;
        Top = SystemParameters.WorkArea.Bottom - 1;
        Console.OutputEncoding = new UTF8Encoding(false);
        Loaded += async (_, _) =>
        {
            while (!closing)
            {
                var line = await Task.Run(Console.ReadLine);
                if (line == null || line == "close") { Close(); break; }
                HandleDesktopCommand(line);
            }
        };
    }

    void HandleDesktopCommand(string line)
    {
        if (line.StartsWith("page ") && int.TryParse(line.AsSpan(5), out var index)
            && (uint)index < DesktopPages.Length && IsEnabled)
        { page = DesktopPages[index]; Draw(); }
        else if (line.StartsWith("{") && IsEnabled)
        {
            using var json = JsonDocument.Parse(line);
            var command = json.RootElement;
            var id = command.GetProperty("id").GetInt32();
            if (command.GetProperty("revision").GetInt32() != revision ||
                (uint)id >= desktopControls.Count || !desktopControls[id].IsEnabled) return;
            var value = command.GetProperty("value");
            switch (desktopControls[id])
            {
                case Button b: b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); break;
                case ComboBox c when value.TryGetInt32(out var n) && (uint)n < c.Items.Count: c.SelectedIndex = n; break;
                case CheckBox c: c.IsChecked = value.GetBoolean(); break;
                case Slider s: s.Value = Math.Clamp(value.GetDouble(), s.Minimum, s.Maximum); break;
                case TextBox t when !t.IsReadOnly: t.Text = value.GetString(); break;
            }
        }
    }

    object? Describe(UIElement element)
    {
        if (element.Visibility != Visibility.Visible) return null;
        if(element is StackPanel {Tag: List<ConnectionStep> steps})
            return new {kind="connectionSteps",steps};
        if (element is ConnectionBadge badge)
        {
            badges.Add(badge);
            return new {kind="connectionStatus", state=badge.State, label=badge.Label, slot=badges.Count-1};
        }
        if (element is TextBlock t)
        {
            liveText.Add(t);
            return new { kind="text", hint=Equals(t.Tag,"connection-hint"), text=t.Text, size=t.FontSize, bold=t.FontWeight.ToOpenTypeWeight()>=600, live=liveText.Count-1 };
        }
        if (element is Control control && control is System.Windows.Controls.Button or CheckBox or ComboBox or Slider or TextBox)
        {
            int id=desktopControls.Count;
            desktopControls.Add(control);
            object data=control switch {
                Button b => new { content=b.Content is UIElement e ? Describe(e) : b.Content, selected=Equals((b.Background as SolidColorBrush)?.Color,Selected), primary=Equals((b.Background as SolidColorBrush)?.Color,Accent) },
                ComboBox c => new { options=c.Items.Cast<object>().Select(i=>(i as ComboBoxItem)?.Content?.ToString()??i.ToString()), value=c.SelectedIndex },
                CheckBox c => new { label=c.Content?.ToString(), value=c.IsChecked==true },
                Slider s => new { min=s.Minimum,max=s.Maximum,step=s.TickFrequency,value=s.Value },
                TextBox box => new { value=box.Text,readOnly=box.IsReadOnly },
                _ => throw new InvalidOperationException()
            };
            return new {kind=control.GetType().Name,id,data,disabled=!control.IsEnabled};
        }
        if (element is Border border && border.Child!=null)
            return new { kind=border.CornerRadius.TopLeft>=15?"card":"group", children=new[]{Describe(border.Child)} };
        if (element is ScrollViewer scroll && scroll.Content is UIElement content) return Describe(content);
        if (element is Panel panel)
        {
            int columns=panel is Grid grid ? grid.ColumnDefinitions.Count : panel is UniformGrid uniform ? uniform.Columns : 0;
            var children=panel.Children.Cast<UIElement>().Select(child=>new { node=Describe(child),col=Grid.GetColumn(child)+1,row=Grid.GetRow(child)+1 }).ToArray();
            return new {kind="layout",columns,grid=panel is Grid, horizontal=panel is StackPanel stack && stack.Orientation==Orientation.Horizontal,children};
        }
        if(element is ControllerDrawing drawing)
        {
            string asset = drawing.IsDualSense ? "DualSense" : drawing.Rear ? "pro-rear" : "pro-front";
            return new {kind="art", asset};
        }
        return null;
    }

    void PublishDesktop(Grid main)
    {
        if (!desktopMode) return;
        revision++; desktopControls.Clear(); liveText.Clear(); badges.Clear();
        var body=Describe(main);
        SendDesktop("HUB",JsonSerializer.Serialize(new {labels=DesktopPages.Select(UiText.Get),index=Array.IndexOf(DesktopPages,page),language=UiText.Current,tray=preferences.MinimizeToTray,revision,body}));
    }
    void PublishReadings()
    {
        if (!desktopMode) return;
        var packet=JsonSerializer.Serialize(new {revision,badges=badges.Select(b=>new {state=b.State,label=b.Label}),text=liveText.Select(t=>t.Text)});
        if(packet!=lastTelemetry){SendDesktop("LIVE",packet);lastTelemetry=packet;}
    }
}
