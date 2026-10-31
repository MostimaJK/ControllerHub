using System.Diagnostics;
using System.IO;
using DS4Windows;
using ControllerFusion.Input;

namespace ControllerFusion.Engine;

internal static class VirtualHidEndpoint
{
    public static async Task<HidDevice> Wait(string mode,int importedPort,CancellationToken token)
    {
        var (vid,pid)=mode switch {"PS5"=>(0x054c,0x0ce6),"Pro1"=>(0x057e,0x2009),"Steam / Pro2"=>(0x057e,0x2069),_=>throw new ArgumentOutOfRangeException(nameof(mode))};
        var elapsed=Stopwatch.StartNew();
        while(elapsed.ElapsedMilliseconds<4000)
        {
            token.ThrowIfCancellationRequested();
            foreach(var candidate in HidDevices.Enumerate(vid,pid))
            {
                if(candidate.Capabilities.UsagePage==1 && candidate.Capabilities.Usage==5 && FusionInput.IsUsbipDevice(candidate.DevicePath,importedPort))return candidate;
                candidate.CloseDevice();
            }
            await Task.Delay(100,token);
        }
        throw new IOException("Windows 的本次虛擬 HID 手柄尚未就緒："+mode);
    }
}
