// GPL-3.0-or-later. Recover only additions made by this app after an unclean exit.
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text.Json;
using DS4Windows;
using DS4WinWPF.DS4Control;

namespace ControllerFusion.Input;

internal static class FusionHidingJournal
{
    sealed record Claim(int Process,long Started,string[] Devices);
    static readonly string FilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ControllerFusion","hiding-lease.json");
    internal static void Record(DS4Device device)
    {
        using var api=new HidHideAPIDevice(false);
        if(!api.TryGetBlacklist(out var previous))throw new IOException("無法備份 HidHide 現有裝置清單，未開始隱藏。");
        var added=HidHideDeviceIdentity.Resolve(Global.GetInstanceIdFromDevicePath(device.HidDevice.DevicePath)).Except(previous,StringComparer.OrdinalIgnoreCase).ToArray();
        using var process=Process.GetCurrentProcess();
        if(File.Exists(FilePath))
        {
            var retained=JsonSerializer.Deserialize<Claim>(File.ReadAllText(FilePath));
            if(retained!=null && retained.Process==process.Id && retained.Started==process.StartTime.ToUniversalTime().Ticks)added=added.Union(retained.Devices,StringComparer.OrdinalIgnoreCase).ToArray();
            else throw new IOException("前次 HidHide 還原記錄尚未處理，未新增隱藏設定。");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        File.WriteAllText(FilePath+".tmp",JsonSerializer.Serialize(new Claim(process.Id,process.StartTime.ToUniversalTime().Ticks,added)));
        File.Move(FilePath+".tmp",FilePath,true);
    }
    internal static void Recover()
    {
        if(!File.Exists(FilePath))return;
        var claim=JsonSerializer.Deserialize<Claim>(File.ReadAllText(FilePath));
        if(claim==null)throw new IOException("HidHide 還原記錄無效，請檢查診斷資料。");
        try {using var process=Process.GetProcessById(claim.Process);if(!process.HasExited && process.StartTime.ToUniversalTime().Ticks==claim.Started)return;}
        catch(ArgumentException) { }
        using var api=new HidHideAPIDevice();
        if(!api.IsOpen())return;
        if(!api.TryGetBlacklist(out var current))throw new IOException("無法讀取 HidHide 清單，未更改其他裝置。");
        var remaining=current.Where(id=>!claim.Devices.Contains(id,StringComparer.OrdinalIgnoreCase)).ToList();
        if(remaining.Count!=current.Count && !api.SetBlacklist(remaining))throw new IOException("前次 HidHide 設定還原失敗，請使用 HidHide 設定解除本次實體手柄隱藏。");
        File.Delete(FilePath);
    }
    internal static void Complete()
    {
        if(!File.Exists(FilePath))return;
        var claim=JsonSerializer.Deserialize<Claim>(File.ReadAllText(FilePath));
        using var process=Process.GetCurrentProcess();
        if(claim==null || claim.Process!=process.Id || claim.Started!=process.StartTime.ToUniversalTime().Ticks)return;
        using var api=new HidHideAPIDevice(false);
        if(api.TryGetBlacklist(out var current) && !claim.Devices.Any(id=>current.Contains(id,StringComparer.OrdinalIgnoreCase)))File.Delete(FilePath);
    }
}
