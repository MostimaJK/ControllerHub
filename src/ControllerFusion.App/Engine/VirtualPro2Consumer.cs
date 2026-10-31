using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;
using ControllerFusion.Input;

namespace ControllerFusion.Engine;

// Test consumer for the exact USB/IP output port. Never admits a physical device.
internal sealed class VirtualPro2Consumer : IDisposable
{
    readonly SafeFileHandle file;
    IntPtr usb;
    VirtualPro2Consumer(SafeFileHandle file,IntPtr usb){this.file=file;this.usb=usb;}
    public static async Task<VirtualPro2Consumer> EnableReports(int importedPort)
    {
        const string query="System.Devices.InterfaceClassGuid:=\"{7D8F1E5C-9D89-4E0D-A2F0-6D10F1F89A8F}\"";
        string? path=null;
        for(int attempt=0;attempt<20 && path==null;attempt++)
        {
            var interfaces=await DeviceInformation.FindAllAsync(query);
            path=interfaces.Select(d=>d.Id).SingleOrDefault(id=>id.Contains("vid_057e&pid_2069&mi_01",StringComparison.OrdinalIgnoreCase) && FusionInput.IsUsbipDevice(id,importedPort));
            if(path==null)await Task.Delay(100);
        }
        if(path==null)throw new IOException("找不到本次虛擬 Pro2 的 WinUSB 命令介面。");
        var file=CreateFileW(path,0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
        if(file.IsInvalid){file.Dispose();throw new Win32Exception(Marshal.GetLastWin32Error(),"無法開啟虛擬 Pro2 命令介面。");}
        IntPtr usb=IntPtr.Zero;
        try
        {
            if(!WinUsb_Initialize(file,out usb))throw new Win32Exception(Marshal.GetLastWin32Error());
            uint timeout=1000;
            if(!WinUsb_SetPipePolicy(usb,2,3,4,ref timeout))throw new Win32Exception(Marshal.GetLastWin32Error());
            var owner=new VirtualPro2Consumer(file,usb);
            foreach(var item in new[]{(Command:(byte)0x0c,Sub:(byte)2,Value:(byte)0x27),(Command:(byte)0x0c,Sub:(byte)4,Value:(byte)0x27),(Command:(byte)3,Sub:(byte)0x0a,Value:(byte)5),(Command:(byte)3,Sub:(byte)0x0d,Value:(byte)1)})
            {
                var packet=new byte[]{item.Command,0x91,0,item.Sub,0,8,0,0,item.Value,0,0xff,0xff,0xff,0xff,0xff,0xff};
                if(!WinUsb_WritePipe(usb,2,packet,(uint)packet.Length,out uint written,IntPtr.Zero) || written!=packet.Length)
                    throw new IOException("虛擬 Pro2 初始化命令未完成。");
            }
            return owner;
        }
        catch {if(usb!=IntPtr.Zero)WinUsb_Free(usb);file.Dispose();throw;}
    }
    public void Dispose(){if(usb==IntPtr.Zero)return;var handle=usb;usb=IntPtr.Zero;try {if(!WinUsb_Free(handle))throw new Win32Exception(Marshal.GetLastWin32Error());}finally {file.Dispose();}}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("winusb.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool WinUsb_Initialize(SafeFileHandle file,out IntPtr usb);
    [DllImport("winusb.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool WinUsb_Free(IntPtr usb);
    [DllImport("winusb.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool WinUsb_SetPipePolicy(IntPtr usb,byte pipe,uint policy,uint size,ref uint value);
    [DllImport("winusb.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool WinUsb_WritePipe(IntPtr usb,byte pipe,byte[] data,uint size,out uint written,IntPtr overlapped);
}
