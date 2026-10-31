// GPL-3.0-or-later. Finite, explicitly requested diagnostics using Windows audio.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using DS4Windows;

namespace ControllerFusion.Input;

public static class FusionAudioTests
{
    public static async Task PlaySystemTone(string endpointId,CancellationToken token)
    {
        using var enumerator=new MMDeviceEnumerator();
        using var endpoint=string.IsNullOrEmpty(endpointId)?enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia):enumerator.GetDevice(endpointId);
        if(endpoint.FriendlyName.Contains("Wireless Controller",StringComparison.OrdinalIgnoreCase)||endpoint.FriendlyName.Contains("DualSense",StringComparison.OrdinalIgnoreCase)||DualSenseAudioPassthrough.TryGetEndpointUsbipPort(endpoint,out _))throw new InvalidOperationException("測試音請選擇一般揚聲器／耳機。");
        var tone=new OffsetSampleProvider(new SignalGenerator(48000,2) {Gain=.15,Frequency=90,Type=SignalGeneratorType.Sin}) {Take=TimeSpan.FromSeconds(1)};
        using var output=new WasapiOut(endpoint,AudioClientShareMode.Shared,true,20);
        output.Init(tone);output.Play();
        try {await Task.Delay(1200,token).ConfigureAwait(false);}finally {output.Stop();}
    }

    public static async Task PlayVirtualHaptics(int importedPort,CancellationToken token)
    {
        using var enumerator=new MMDeviceEnumerator();
        var matches=enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active).Where(e=>e.AudioClient.MixFormat.Channels==4 && DualSenseAudioPassthrough.TryGetEndpointUsbipPort(e,out int port) && port==importedPort).ToArray();
        if(matches.Length!=1)throw new InvalidOperationException("本次虛擬 PS5 的四聲道音訊端點尚未就緒，請稍後重試。");
        using var endpoint=matches[0];
        using var output=new WasapiOut(endpoint,AudioClientShareMode.Shared,true,10);
        output.Init(FusionAudioFormat.ForEndpoint(new HapticTone(),endpoint.AudioClient.MixFormat));output.Play();
        try {await Task.Delay(1300,token).ConfigureAwait(false);}finally {output.Stop();}
    }

    sealed class HapticTone : ISampleProvider
    {
        int frame;
        public WaveFormat WaveFormat {get;}=WaveFormat.CreateIeeeFloatWaveFormat(48000,4);
        public int Read(float[] buffer,int offset,int count)
        {
            int frames=Math.Min(count/4,48000-frame);
            for(int i=0;i<frames;i++,frame++)
            {
                buffer[offset++]=0;buffer[offset++]=0;
                double gain=frame<19200 || frame>=28800?.22:0;
                buffer[offset++]=(float)(gain*Math.Sin(2*Math.PI*110*frame/48000));
                buffer[offset++]=(float)(gain*Math.Sin(2*Math.PI*180*frame/48000));
            }
            return frames*4;
        }
    }
}

// Shared endpoint adapter; its contract is tested without opening audio devices.
internal static class FusionAudioFormat
{
    internal static IWaveProvider ForEndpoint(ISampleProvider source,WaveFormat mixFormat)
    {
        if(source.WaveFormat.Channels!=mixFormat.Channels)throw new InvalidOperationException("觸覺來源與端點聲道數不一致。");
        if(source.WaveFormat.SampleRate!=mixFormat.SampleRate)source=new WdlResamplingSampleProvider(source,mixFormat.SampleRate);
        var standard=mixFormat is WaveFormatExtensible extensible?extensible.ToStandardWaveFormat():mixFormat;
        IWaveProvider bytes=standard.Encoding==WaveFormatEncoding.IeeeFloat && standard.BitsPerSample==32
            ?source.ToWaveProvider()
            :standard.Encoding==WaveFormatEncoding.Pcm && standard.BitsPerSample==16
                ?source.ToWaveProvider16():throw new NotSupportedException("不支援此觸覺端點取樣格式："+mixFormat);
        return new ExactFormat(bytes,mixFormat);
    }
    sealed class ExactFormat:IWaveProvider
    {
        readonly IWaveProvider source;
        public WaveFormat WaveFormat {get;}
        public ExactFormat(IWaveProvider source,WaveFormat format){this.source=source;WaveFormat=format;}
        public int Read(byte[] buffer,int offset,int count)=>source.Read(buffer,offset,count);
    }
}
