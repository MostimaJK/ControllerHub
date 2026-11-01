using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ControllerFusion.Engine;

// Recovery authority is limited to the exact loopback endpoint recorded before
// our import. Never detach every USB/IP port or clean another live host's lease.
public sealed record OutputLease(int HostPid,long HostStartUtcTicks,int UsbPort,uint Bus,string Device)
{
    static string DirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ControllerFusion","OutputLeases");
    public static string Record(int port,uint bus,string device)
    {
        Directory.CreateDirectory(DirectoryPath);
        using var host=Process.GetCurrentProcess();
        var lease=new OutputLease(host.Id,host.StartTime.ToUniversalTime().Ticks,port,bus,device);
        var path=Path.Combine(DirectoryPath,Guid.NewGuid().ToString("N")+".json");
        ProfileStore.AtomicWrite(path,JsonSerializer.Serialize(lease)); return path;
    }
    public static int[] Ports(string text,int port,uint bus,string device)
    {
        var found=new List<int>();
        foreach(Match m in Regex.Matches(text,@"(?ms)^\s*Port\s+(\d+)\s*:(.*?)(?=^\s*Port\s+\d+\s*:|\z)"))
        {
            string block=m.Groups[2].Value;
            bool matches=false;
            foreach(Match endpoint in Regex.Matches(block,@"usbip://[^\s]+",RegexOptions.IgnoreCase))
            {
                if(!Uri.TryCreate(endpoint.Value.TrimEnd(')',','),UriKind.Absolute,out var uri) ||
                    !uri.IsLoopback || uri.Port!=port)continue;
                string id=uri.AbsolutePath.Trim('/');var parts=id.Split('-');
                bool exact=parts.Length==2 && uint.TryParse(parts[0],out var actualBus) &&
                    uint.TryParse(parts[1],out var actualDevice) && uint.TryParse(device,out var expectedDevice) &&
                    actualBus==bus && actualDevice==expectedDevice;
                bool padded=id.Length==0 && Regex.IsMatch(block,@"remote\s+bus/dev\s+0*"+bus+@"/0*"+Regex.Escape(device)+@"(?:\D|$)",RegexOptions.IgnoreCase);
                matches|=exact || padded;
            }
            if(matches && int.TryParse(m.Groups[1].Value,out int number))found.Add(number);
        }
        return found.ToArray();
    }
    static bool HostAlive(OutputLease lease)
    {
        try { using var p=Process.GetProcessById(lease.HostPid); return p.StartTime.ToUniversalTime().Ticks==lease.HostStartUtcTicks; }
        catch(ArgumentException) { return false; }
        catch { return true; } // Uncertain process identity is not cleanup authority.
    }
    public static async Task Recover(string usbip,Action<string>? log,CancellationToken token)
    {
        if(!Directory.Exists(DirectoryPath)) return;
        foreach(var file in Directory.EnumerateFiles(DirectoryPath,"*.json"))
        {
            try
            {
                var lease=JsonSerializer.Deserialize<OutputLease>(File.ReadAllText(file));
                if(lease==null || lease.HostPid<=0 || lease.UsbPort<1024 || lease.UsbPort>65535 || lease.Bus==0 || !uint.TryParse(lease.Device,out var dev) || dev==0) { log?.Invoke("略過無效的輸出復原紀錄。"); continue; }
                if(HostAlive(lease)) continue;
                var ports=Ports(await Processes.Run(usbip,new[]{"port"},token),lease.UsbPort,lease.Bus,lease.Device);
                if(ports.Length==0) {File.Delete(file);log?.Invoke("上次輸出已不存在，已清除本程式的復原紀錄。");continue;}
                // Reserve the dead loopback endpoint during cleanup, so a
                // different service cannot reuse it between check and detach.
                var reservation=new TcpListener(IPAddress.Loopback,lease.UsbPort);
                reservation.Server.ExclusiveAddressUse=true;
                try
                {
                    try {reservation.Start();}
                    catch(SocketException) {log?.Invoke("舊輸出 endpoint 尚被佔用，保留紀錄，不自動移除。");continue;}
                    foreach(int port in ports) await Processes.Run(usbip,new[]{"detach","-p",port.ToString()},token);
                    var remaining=Ports(await Processes.Run(usbip,new[]{"port"},token),lease.UsbPort,lease.Bus,lease.Device);
                    if(remaining.Length==0) {File.Delete(file);log?.Invoke("已移除上次異常退出留下的本程式虛擬連接埠。");}
                }
                finally {reservation.Stop();}
            }
            catch(OperationCanceledException) { throw; }
            catch(Exception e) { log?.Invoke("輸出復原紀錄保留待重試："+e.Message); }
        }
    }
}
