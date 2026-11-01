using DS4Windows;
namespace ControllerFusion.Engine;
internal sealed class InputPacketMapper
{
    readonly DS4State mapped=new();
    // One synchronous owner; mapped state never escapes this method.
    internal int Write(DS4State raw,Preferences preferences,string mode,Span<byte> destination)
    {
        int length=PadPackets.PacketLength(mode);
        PadPackets.EncodeInto(PadPackets.Map(raw,preferences,mapped),mode,destination[..length]);return length;
    }
}
