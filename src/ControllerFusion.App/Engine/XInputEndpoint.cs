using System.Runtime.InteropServices;
using System.IO;

namespace ControllerFusion.Engine;

public readonly record struct XboxObservation(ushort Buttons,byte LT,byte RT,short LX,short LY,short RX,short RY);
public static class XInputEndpoint
{
    [StructLayout(LayoutKind.Sequential)] struct State { public uint Packet; public ushort Buttons; public byte LT,RT; public short LX,LY,RX,RY; }
    [StructLayout(LayoutKind.Sequential)] struct Vibration { public ushort Large,Small; }
    [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint user,out State state);
    [DllImport("xinput1_4.dll")] static extern uint XInputSetState(uint user,ref Vibration motors);
    public static uint[] Connected()=>Enumerable.Range(0,4).Select(i=>(uint)i).Where(i=>Read(i)!=null).ToArray();
    public static XboxObservation? Read(uint index)=>XInputGetState(index,out var s)==0?new(s.Buttons,s.LT,s.RT,s.LX,s.LY,s.RX,s.RY):null;
    public static void Rumble(uint index,ushort large,ushort small)
    {
        var motors=new Vibration{Large=large,Small=small};
        if(XInputSetState(index,ref motors)!=0) throw new IOException("Windows XInput 震動指令失敗，請重新連接手柄。");
    }
}
