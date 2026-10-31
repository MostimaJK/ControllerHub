// GPL-3.0-or-later. VIIPER's 48 kHz, signed-16, four-channel PCM to
// the native 3 kHz signed-8 stereo haptics window. Channels 0/1 are speakers.
using System;
using System.Buffers.Binary;

namespace ControllerFusion.Input;

public sealed class PcmHapticsResampler
{
    readonly byte[] window=new byte[64];
    int count,sumLeft,sumRight,length;
    public void Reset() {count=sumLeft=sumRight=length=0;Array.Clear(window);}
    public void Push(ReadOnlySpan<byte> pcm,int gain,Action<byte[]> emit)
    {
        if(pcm.Length%8!=0) throw new ArgumentException("Incomplete four-channel PCM frame.",nameof(pcm));
        gain=Math.Clamp(gain,0,100);
        for(int i=0;i<pcm.Length;i+=8)
        {
            sumLeft+=BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i+4,2));
            sumRight+=BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i+6,2));
            if(++count!=16)continue;
            window[length++]=unchecked((byte)(sbyte)Math.Clamp(sumLeft/16/256*gain/100,-128,127));
            window[length++]=unchecked((byte)(sbyte)Math.Clamp(sumRight/16/256*gain/100,-128,127));
            sumLeft=sumRight=count=0;
            if(length==window.Length) {length=0;emit((byte[])window.Clone());}
        }
    }
}
