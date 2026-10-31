using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ControllerFusion.Engine;

// Windows closes this handle on a host crash too, preventing an orphan broker.
internal sealed class ChildProcessJob : IDisposable
{
    readonly SafeFileHandle handle;
    [StructLayout(LayoutKind.Sequential)] struct Basic { public long Time1,Time2; public uint Flags; public nuint Min,Max; public uint Active; public nuint Affinity; public uint Priority,Scheduling; }
    [StructLayout(LayoutKind.Sequential)] struct Io { public ulong R1,R2,R3,R4,R5,R6; }
    [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public Io Io; public nuint ProcessMemory,JobMemory,PeakProcess,PeakJob; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateJobObjectW(nint security,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(SafeFileHandle handle,int informationClass,ref Extended information,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(SafeFileHandle handle,nint process);
    public ChildProcessJob(Process process)
    {
        handle=CreateJobObjectW(0,null);
        var info=new Extended {Basic=new Basic { Flags=0x2000 }}; // KILL_ON_JOB_CLOSE
        if(handle.IsInvalid || !SetInformationJobObject(handle,9,ref info,(uint)Marshal.SizeOf<Extended>()) || !AssignProcessToJobObject(handle,process.Handle))
        { int error=Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error,"無法建立後端程序生命週期。"); }
    }
    public void Dispose()=>handle.Dispose();
}
