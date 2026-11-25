using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Windows;

namespace ControllerFusion;

public sealed partial class MainWindow
{
    WebView2? web;
    bool pageReady;
    void SendDesktop(string topic, string payload)
    {
        if (web == null) Console.WriteLine(topic+" "+payload);
        else if (pageReady) web.CoreWebView2.PostWebMessageAsJson("{\"topic\":\""+topic+"\",\"data\":"+payload+"}");
    }

    void PrepareWebView()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(1200, Math.Floor(area.Width * .9));
        Height = Math.Min(800, Math.Floor(area.Height * .9));
        MinWidth = Math.Min(860, Width);
        MinHeight = Math.Min(600, Height);
        var nativeContent=Content;
        desktopMode=true;
        var view=new WebView2();
        web=view;
        Content=view;
        Closed+=(_,_)=>view.Dispose();
        Loaded+=async (_,_)=>{
            try
            {
                var cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ControllerFusion","WebView2");
                await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,cache));
                const string address="https://controllerhub.local/index.html";
                var core=view.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping("controllerhub.local",Path.Combine(AppContext.BaseDirectory,"Web"),CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting+=(_,e)=>{if(e.Uri!=address)e.Cancel=true;else pageReady=false;};
                core.NewWindowRequested+=(_,e)=>e.Handled=true;
                core.WebMessageReceived+=(_,e)=>{
                    if(e.Source!=address || closing)return;
                    string command=e.TryGetWebMessageAsString();
                    if(command=="ready"){pageReady=true;Draw();}
                    else if(pageReady)HandleDesktopCommand(command);
                };
                core.Navigate(address);
            }
            catch(Exception ex)
            {
                desktopMode=false;web=null;Content=nativeContent;view.Dispose();
                note="WebView2: "+ex.Message;Draw();
            }
        };
    }
}
