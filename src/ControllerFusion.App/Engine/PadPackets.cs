using System.Buffers.Binary;
using DS4Windows;

namespace ControllerFusion.Engine;

// VIIPER wire layouts from the pinned PRO2 source. Face buttons use physical
// positions: Nintendo B(south) becomes Xbox A / PS Cross.
public static class PadPackets
{
    public static DS4State Map(DS4State raw, Preferences p, DS4State? destination=null)
    {
        var s = destination ?? new DS4State(); raw.CopyTo(s);
        s.Motion=MotionPipeline.Map(raw.Motion,p);
        if (raw.BLP) Apply(s, p.GL);
        if (raw.BRP) Apply(s, p.GR);
        if (raw.Switch2RawInputStatus.CButton) Apply(s, p.C);
        if(p.GL!="不映射") s.BLP=false;
        if(p.GR!="不映射") s.BRP=false;
        if(p.C!="不映射") s.Switch2RawInputStatus.CButton=false;
        return s;
    }
    static void Apply(DS4State s, string target)
    {
        foreach (var part in target.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part)
            {
                case "A": s.Circle = true; break;
                case "B": s.Cross = true; break;
                case "X": s.Triangle = true; break;
                case "Y": s.Square = true; break;
                case "L": s.L1 = true; break;
                case "R": s.R1 = true; break;
                case "ZL": s.L2Btn = true; s.L2 = 255; break;
                case "ZR": s.R2Btn = true; s.R2 = 255; break;
                case "左搖桿按下": s.L3 = true; break;
                case "右搖桿按下": s.R3 = true; break;
            }
        }
    }
    public static int PacketLength(string mode)=>mode=="Xbox"?20:mode=="PS5"?33:28;
    public static byte[] Encode(DS4State s, string mode)
    {
        var packet=new byte[PacketLength(mode)];EncodeInto(s,mode,packet);return packet;
    }
    public static void EncodeInto(DS4State s,string mode,Span<byte> b)
    {
        if(b.Length!=PacketLength(mode))throw new ArgumentException("Invalid output packet length.",nameof(b));
        b.Clear();
        uint buttons = 0;
        void Bit(bool on, uint mask) { if (on) buttons |= mask; }
        if (mode == "Xbox")
        {
            Bit(s.DpadUp,1); Bit(s.DpadDown,2); Bit(s.DpadLeft,4); Bit(s.DpadRight,8);
            Bit(s.Options,16); Bit(s.Share,32); Bit(s.L3,64); Bit(s.R3,128);
            Bit(s.L1,256); Bit(s.R1,512); Bit(s.PS,1024);
            Bit(s.Cross,0x1000); Bit(s.Circle,0x2000); Bit(s.Square,0x4000); Bit(s.Triangle,0x8000);
            U32(b,0,buttons); b[4]=s.L2; b[5]=s.R2;
            var hi=s.Switch2RawInputStatus;
            I16(b,6,hi.IsValid?hi.LeftStickX:Axis(s.LX)); I16(b,8,hi.IsValid?-hi.LeftStickY:-Axis(s.LY)); I16(b,10,hi.IsValid?hi.RightStickX:Axis(s.RX)); I16(b,12,hi.IsValid?-hi.RightStickY:-Axis(s.RY));
        }
        else if (mode == "PS5")
        {
            b[0]=unchecked((byte)(s.LX-128)); b[1]=unchecked((byte)(s.LY-128));
            b[2]=unchecked((byte)(s.RX-128)); b[3]=unchecked((byte)(s.RY-128));
            Bit(s.Square,16); Bit(s.Cross,32); Bit(s.Circle,64); Bit(s.Triangle,128);
            Bit(s.L1,256); Bit(s.R1,512); Bit(s.L2Btn,1024); Bit(s.R2Btn,2048);
            Bit(s.Share,4096); Bit(s.Options,8192); Bit(s.L3,16384); Bit(s.R3,32768);
            Bit(s.PS,65536); Bit(s.TouchButton || s.Capture,131072);
            Bit(s.Mute,262144);
            U32(b,4,buttons);
            b[8]=(byte)((s.DpadUp?1:0)|(s.DpadDown?2:0)|(s.DpadLeft?4:0)|(s.DpadRight?8:0));
            b[9]=s.L2; b[10]=s.R2;
            U16(b,11,Math.Clamp((int)s.TrackPadTouch0.X,0,1919)); U16(b,13,Math.Clamp((int)s.TrackPadTouch0.Y,0,1079)); b[15]=s.TrackPadTouch0.IsActive?(byte)1:(byte)0;
            U16(b,16,Math.Clamp((int)s.TrackPadTouch1.X,0,1919)); U16(b,18,Math.Clamp((int)s.TrackPadTouch1.Y,0,1079)); b[20]=s.TrackPadTouch1.IsActive?(byte)1:(byte)0;
            if(s.Motion is {} m) {
                double factor=s.Switch2RawInputStatus.IsValid?1.024:1;
                I16(b,21,(int)Math.Round(m.gyroPitchFull*factor)); I16(b,23,(int)Math.Round(-m.gyroYawFull*factor)); I16(b,25,(int)Math.Round(-m.gyroRollFull*factor));
                I16(b,27,-m.accelXFull); I16(b,29,-m.accelYFull); I16(b,31,m.accelZFull);
            } else I16(b,29,8192);
        }
        else
        {
            Bit(s.Cross,1); Bit(s.Circle,2); Bit(s.Square,4); Bit(s.Triangle,8);
            Bit(s.R1,16); Bit(s.R2Btn || s.R2>127,32); Bit(s.Options,64); Bit(s.R3,128);
            Bit(s.DpadDown,256); Bit(s.DpadRight,512); Bit(s.DpadLeft,1024); Bit(s.DpadUp,2048);
            Bit(s.L1,4096); Bit(s.L2Btn || s.L2>127,8192); Bit(s.Share,16384); Bit(s.L3,32768);
            Bit(s.PS,65536); Bit(s.Capture || s.TouchButton,131072);
            if (mode == "Steam / Pro2") { Bit(s.BRP,0x40000); Bit(s.BLP,0x80000); Bit(s.Switch2RawInputStatus.CButton,0x100000); }
            U32(b,0,buttons);
            var hi=s.Switch2RawInputStatus;
            U16(b,4,hi.IsValid?Signed12(hi.LeftStickX):Axis12(s.LX)); U16(b,6,hi.IsValid?Signed12(-hi.LeftStickY):Axis12Y(s.LY)); U16(b,8,hi.IsValid?Signed12(hi.RightStickX):Axis12(s.RX)); U16(b,10,hi.IsValid?Signed12(-hi.RightStickY):Axis12Y(s.RY));
            if(s.Motion is {} m) {
                I16(b,12,-m.accelXFull/2); I16(b,14,-m.accelZFull/2); I16(b,16,-m.accelYFull/2);
                double factor=s.Switch2RawInputStatus.IsValid?1.024:1;
                I16(b,18,(int)Math.Round(m.gyroPitchFull*factor)); I16(b,20,(int)Math.Round(m.gyroRollFull*factor)); I16(b,22,(int)Math.Round(-m.gyroYawFull*factor));
            } else I16(b,16,4096);
            U32(b,24,unchecked((uint)s.totalMicroSec));
        }
    }
    static int Axis(byte a) => a>=128 ? (a-128)*32767/127 : (a-128)*256;
    static int Signed12(int a)=>Math.Clamp(a>=0?2048+a*2047/32767:2048+a*2048/32768,0,4095);
    static int Axis12(byte a) => a>=128 ? 2048+(a-128)*2047/127 : a*16;
    static int Axis12Y(byte a) => a==128 ? 2048 : a<128 ? 2048+(128-a)*2047/128 : (255-a)*2048/127;
    static void U16(Span<byte> b,int o,int v)=>BinaryPrimitives.WriteUInt16LittleEndian(b[o..], (ushort)v);
    static void I16(Span<byte> b,int o,int v)=>BinaryPrimitives.WriteInt16LittleEndian(b[o..],(short)Math.Clamp(v,-32768,32767));
    static void U32(Span<byte> b,int o,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(b[o..],v);
}
