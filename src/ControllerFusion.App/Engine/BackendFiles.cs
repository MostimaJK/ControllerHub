using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace ControllerFusion.Engine;

public static class BackendFiles
{
    public static string Verify(string name,string expected)
    {
        var path=Path.Combine(AppContext.BaseDirectory,"Backend",name);
        using var stream=File.OpenRead(path);
        if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected,StringComparison.OrdinalIgnoreCase)) throw new IOException("後端檔案版本或雜湊不符，請重新解壓完整程式包："+name);
        return path;
    }
    public static string Viiper()=>Verify("viiper-haptic-v2.exe","1FF901E659E99D62063EBC9CF99A78A7F3E1611154F04EEF12A25641C0823D94");
    public static Process? InstallHidHide()
    {
        var exe=Verify("HidHide_1.5.230_x64.exe","F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6");
        return Process.Start(new ProcessStartInfo(exe) {UseShellExecute=true,Verb="runas",Arguments="/norestart"});
    }
    public static Process? InstallUsbip()
    {
        var exe=Verify("USBip-0.9.7.7-x64.exe","51620FA5F9F8BE5932BC9D786DEEE557CE06D5407A99CAB490DCFAC71F185FEA");
        return Process.Start(new ProcessStartInfo(exe) {UseShellExecute=true,Verb="runas",Arguments="/TYPE=compact /COMPONENTS=main,client /NORESTART"});
    }
}
