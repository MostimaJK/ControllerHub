using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ControllerFusion.Engine;

public static class Processes
{
    public static string? UsbipOverride { get; set; }
    public static string? FindUsbip()
    {
        if(UsbipOverride!=null && File.Exists(UsbipOverride)) return UsbipOverride;
        var paths = new List<string>();
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            if (!string.IsNullOrWhiteSpace(folder)) paths.Add(Path.Combine(folder.Trim('"'),"usbip.exe"));
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            foreach (var name in new[] { "USBip", "usbip-win2", "USBip-win", "USBip-win2" })
            { paths.Add(Path.Combine(root,name,"usbip.exe")); paths.Add(Path.Combine(root,name,"bin","usbip.exe")); }
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\usbip.exe");
        if (key?.GetValue(null) is string installed) paths.Insert(0,installed.Trim('"'));
        foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            try
            {
                using var registry=RegistryKey.OpenBaseKey(hive,view);
                using var uninstall=registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if(uninstall==null) continue;
                foreach(var name in uninstall.GetSubKeyNames())
                {
                    using var product=uninstall.OpenSubKey(name);
                    if(!(product?.GetValue("DisplayName") as string ?? "").Contains("USBip",StringComparison.OrdinalIgnoreCase)) continue;
                    if(product?.GetValue("InstallLocation") is string root && !string.IsNullOrWhiteSpace(root)) paths.Insert(0,Path.Combine(root,"usbip.exe"));
                }
            }
            catch(System.Security.SecurityException) { }
        }
        return paths.FirstOrDefault(File.Exists);
    }
    public static async Task<string> Run(string exe, IEnumerable<string> args, CancellationToken token)
    {
        var info=new ProcessStartInfo(exe) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var arg in args) info.ArgumentList.Add(arg);
        using var p=Process.Start(info) ?? throw new IOException("無法啟動 " + Path.GetFileName(exe));
        var stdout=p.StandardOutput.ReadToEndAsync(); var stderr=p.StandardError.ReadToEndAsync();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try { await p.WaitForExitAsync(timeout.Token); }
        catch { try { p.Kill(true); } catch { } throw; }
        var result=(await stdout + "\n" + await stderr).Trim();
        if(p.ExitCode!=0) throw new IOException($"{Path.GetFileName(exe)} 失敗 ({p.ExitCode})：{result}");
        return result;
    }
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute=true });
}
