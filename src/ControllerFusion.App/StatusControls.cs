using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace ControllerFusion;

public sealed class ConnectionBadge : Border
{
    public static readonly Color Emerald = Color.FromRgb(4, 120, 87);
    readonly TextBlock symbol = new() { FontSize=14, FontWeight=FontWeights.Bold, Margin=new Thickness(0,0,7,0), VerticalAlignment=VerticalAlignment.Center };
    readonly TextBlock label = new() { FontSize=12, FontWeight=FontWeights.SemiBold, VerticalAlignment=VerticalAlignment.Center };
    (bool Input, bool Output, string Phase)? previous;

    public ConnectionBadge()
    {
        CornerRadius=new CornerRadius(18); Padding=new Thickness(13,6,13,6);
        var row=new StackPanel { Orientation=Orientation.Horizontal };
        row.Children.Add(symbol); row.Children.Add(label); Child=row;
    }
    public void Update(bool input, bool output, string phase)
    {
        var state=(input,output,phase); if(previous==state) return; previous=state;
        bool ready=input && output;
        bool warning=(!input && output) || phase.Contains("失敗") || phase.Contains("未完成");
        string text=ready ? "已連接 · 遊戲輸出就緒" : input ? "已連接 · 實體輸入就緒" : output ? "實體輸入已中斷" : phase;
        Background=new SolidColorBrush(ready ? Emerald : input ? Color.FromRgb(236,253,245) : warning ? Color.FromRgb(255,247,237) : Color.FromRgb(239,243,255));
        var foreground=ready ? Brushes.White : new SolidColorBrush(input ? Color.FromRgb(6,95,70) : warning ? Color.FromRgb(154,52,18) : Color.FromRgb(51,65,85));
        symbol.Text=input ? "✓" : warning ? "!" : "●"; symbol.Foreground=foreground;
        text=UiText.Get(text); label.Text=text; label.Foreground=foreground;
        AutomationProperties.SetName(this,text);
    }
}

public sealed class BatteryIndicator : StackPanel
{
    readonly BatteryIcon icon = new() { Width=32, Height=18, Margin=new Thickness(0,0,9,0), VerticalAlignment=VerticalAlignment.Center };
    readonly TextBlock value = new() { FontSize=18, FontWeight=FontWeights.SemiBold, Foreground=new SolidColorBrush(Color.FromRgb(24,32,49)) };
    (int Level, bool Charging, bool Available)? previous;

    public BatteryIndicator()
    {
        Orientation=Orientation.Horizontal;
        Children.Add(icon);
        var text=new StackPanel(); text.Children.Add(value); Children.Add(text);
    }
    public void Update(int level, bool charging, bool available)
    {
        var state=(level,charging,available); if(previous==state) return; previous=state;
        bool known=available && level>=0 && level<=100;
        bool low=known && level<=20 && !charging;
        icon.Level=known ? level : -1;
        icon.Charging=known && charging; icon.Width=icon.Charging ? 42 : 32;
        icon.Foreground=new SolidColorBrush(low ? Color.FromRgb(180,83,9) : ConnectionBadge.Emerald);
        icon.InvalidateVisual();
        value.Text=known ? $"{level}%" : "—";
        value.Foreground=new SolidColorBrush(low ? Color.FromRgb(180,83,9) : Color.FromRgb(24,32,49));
        var detail=!available ? "未連接" : !known ? "電量未回報" : charging ? "充電中" : low ? "低電量" : "電池供電";
        ToolTip=UiText.Get(known ? $"手柄回報電量 {level}% · {detail}" : detail);
        AutomationProperties.SetName(this,UiText.Get($"手柄電量 {value.Text}，{detail}"));
    }

    sealed class BatteryIcon : FrameworkElement
    {
        public int Level { get; set; }=-1;
        public bool Charging { get; set; }
        public Brush Foreground { get; set; }=Brushes.Gray;
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var edge=new SolidColorBrush(Color.FromRgb(102,112,133));
            dc.DrawRoundedRectangle(null,new Pen(edge,1.5),new Rect(1,2,26,14),3,3);
            dc.DrawRoundedRectangle(edge,null,new Rect(29,6,2,6),1,1);
            if(Level>0) dc.DrawRoundedRectangle(Foreground,null,new Rect(4,5,20*Level/100.0,8),1,1);
            if(Charging) { var bolt=new StreamGeometry(); using(var g=bolt.Open()) { g.BeginFigure(new Point(39,2),false,false); g.LineTo(new Point(34,9),true,false); g.LineTo(new Point(40,9),true,false); g.LineTo(new Point(35,16),true,false); } dc.DrawGeometry(null,new Pen(Foreground,1.8),bolt); }
            if(Level<0) dc.DrawLine(new Pen(edge,1.5),new Point(11,9),new Point(17,9));
        }
    }
}
