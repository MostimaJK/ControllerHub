using System;

namespace ControllerFusion;

public sealed class TrayHost : IDisposable
{
    readonly System.Windows.Forms.NotifyIcon icon;
    public TrayHost(Action show,Action exit)
    {
        icon=new System.Windows.Forms.NotifyIcon {
            Text="ControllerHub",
            Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            Visible=true
        };
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(UiText.Get("顯示工作台"),null,(_,_)=>show());
        menu.Items.Add(UiText.Get("退出並釋放手柄"),null,(_,_)=>exit());
        icon.ContextMenuStrip=menu;
        icon.DoubleClick+=(_,_)=>show();
    }
    public void Dispose() {icon.Visible=false;icon.ContextMenuStrip?.Dispose();icon.Dispose();}
    public void ApplyLanguage()
    {
        if (icon.ContextMenuStrip is not { } menu) return;
        menu.Items[0].Text=UiText.Get("顯示工作台");
        menu.Items[1].Text=UiText.Get("退出並釋放手柄");
    }
}
