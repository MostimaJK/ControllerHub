using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using ControllerFusion.Input;
using DS4Windows;

namespace ControllerFusion.Engine;

// Read the Windows consumer side, rather than only checking TCP writes.
public static class WindowsOutputChecks
{
    public static async Task HoldForCrashTest(string path)
    {
        await using var output=new VirtualOutput();
        using var timeout=new CancellationTokenSource(20000);
        await output.Start("Xbox",timeout.Token);
        output.Submit(PadPackets.Encode(new DS4State(),"Xbox"));
        File.WriteAllText(path,"READY: owned virtual Xbox created; this diagnostic process can now be terminated to exercise lease recovery.");
        await Task.Delay(Timeout.Infinite);
    }
    [StructLayout(LayoutKind.Sequential)] struct State { public uint Packet; public ushort Buttons; public byte LT,RT; public short LX,LY,RX,RY; }
    [StructLayout(LayoutKind.Sequential)] struct Motors { public ushort Left,Right; }
    [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint user,out State state);
    [DllImport("xinput1_4.dll")] static extern uint XInputSetState(uint user,ref Motors motors);
    [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetInputReport(Microsoft.Win32.SafeHandles.SafeFileHandle handle,[Out]byte[] data,int length);
    public static async Task Run(string path)
    {
        var lines=new List<string>();
        void Record(string line) {lines.Add(line);File.WriteAllLines(path,lines);}
        foreach(var mode in new[]{"Xbox","PS5","Pro1","Steam / Pro2"})
        {
            var before=Enumerable.Range(0,4).Where(i=>XInputGetState((uint)i,out _)==0).ToArray();
            await using var output=new VirtualOutput();
            output.Log+=s=>File.AppendAllText(path+".backend",s+Environment.NewLine);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));
            await output.Start(mode,timeout.Token);
            before=output.PriorXbox.Select(i=>(int)i).ToArray();
            Record("PASS: USB/IP attachment "+mode);
            var pressed=new DS4State{Cross=true,LX=220,Motion=new SixAxis(-320,160,-480,-8192,0,0,.004)};
            output.Submit(PadPackets.Encode(pressed,mode));
            using var producerStop=new CancellationTokenSource();
            var producer=Task.Run(async ()=> { try { while(!producerStop.IsCancellationRequested) { output.Submit(PadPackets.Encode(pressed,mode)); await Task.Delay(8,producerStop.Token); } } catch(OperationCanceledException) { } });
            try
            {
            if(mode=="Xbox")
            {
                uint? index=null;
                for(int j=0;j<70 && index==null;j++) { for(uint i=0;i<4;i++) if(!before.Contains((int)i)&&XInputGetState(i,out var s)==0 && (s.Buttons&0x1000)!=0 && s.LX>10000) {index=i;break;} await Task.Delay(100); }
                if(index==null) throw new IOException("Windows XInput 沒讀到本次虛擬 Xbox 的測試按鍵。");
                Record("PASS: Windows XInput receives A and left-stick movement");
                var feedback=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                output.Rumble+=(large,small)=>{if(large>0 && small>0)feedback.TrySetResult(true);};
                var motors=new Motors{Left=30000,Right=16000};
                XInputSetState(index.Value,ref motors);
                try { await feedback.Task.WaitAsync(TimeSpan.FromSeconds(3));Record("PASS: XInputSetState returns motor feedback through VIIPER"); }
                finally { motors=default;XInputSetState(index.Value,ref motors); }
                pressed=new DS4State(); output.Submit(PadPackets.Encode(pressed,mode));
                await Task.Delay(150);
                if(XInputGetState(index.Value,out var released)!=0 || released.Buttons!=0) throw new IOException("XInput 放開測試失敗。");
                Record("PASS: Windows XInput sees button release");
            }
            else
            {
                var hid=await VirtualHidEndpoint.Wait(mode,output.ImportedPort,timeout.Token);
                try
                {
                    hid.OpenDevice(false);
                    var data=new byte[hid.Capabilities.InputReportByteLength];
                    if(mode=="Steam / Pro2")
                    {
                        // Streaming begins only after a Nintendo-aware consumer's
                        // bulk enable command. A HID control snapshot is valid
                        // before that handshake; do not call it a stream test.
                        data[0]=0x09;
                        if(!HidD_GetInputReport(hid.SafeReadHandle,data,data.Length)) throw new IOException("Pro2 HID 控制報告讀取失敗。");
                        Record($"PASS: Windows Pro2 HID control snapshot; report={data[0]:X2}; bytes={data.Length}");
                        using var initialized=await VirtualPro2Consumer.EnableReports(output.ImportedPort);
                        var counters=new HashSet<uint>();
                        var wire=PadPackets.Encode(pressed,mode);
                        for(int attempt=0;attempt<20 && counters.Count<3;attempt++)
                        {
                            var result=await Task.Run(()=>hid.ReadFile(data,500));
                            if(result==HidDevice.ReadStatus.Success && data[0]==5 && data.AsSpan(0x31,12).SequenceEqual(wire.AsSpan(12,12)))
                                counters.Add(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(1)));
                        }
                        if(counters.Count<3)throw new IOException("Windows 未收到初始化後的 Pro2 連續六軸輸入。");
                        Record("PASS: Windows Pro2 continuous HID input after WinUSB initialization; distinct counters="+counters.Count);
                        Record("PASS: Windows HID receives six motion axes Steam / Pro2");
                    }
                    else
                    {
                        if(mode=="Pro1")
                        {
                            // Act as a Nintendo-aware consumer: streaming mode and IMU.
                            foreach(var command in new[]{(Code:(byte)3,Value:(byte)0x30),(Code:(byte)0x40,Value:(byte)1)})
                            {
                                var request=new byte[64];request[0]=1;request[10]=command.Code;request[11]=command.Value;
                                if(!hid.WriteOutputReportViaInterrupt(request,500))throw new IOException("本次虛擬 Pro1 初始化失敗。");
                            }
                        }
                        bool received=false;
                        for(int attempt=0;attempt<10 && !received;attempt++)
                        {
                            var result=await Task.Run(()=>hid.ReadFile(data,500));
                            if(result!=HidDevice.ReadStatus.Success)continue;
                            var wire=PadPackets.Encode(pressed,mode);
                            if(mode=="PS5")
                                received=data[0]==1 && (data[8]&0x20)!=0 && data.AsSpan(16,12).SequenceEqual(wire.AsSpan(21,12));
                            else if(data[0]==0x30)
                            {
                                received=true;
                                for(int axis=0;axis<6;axis++)
                                {
                                    var raw=BinaryPrimitives.ReadInt16LittleEndian(wire.AsSpan(12+axis*2));
                                    var expected=axis<3?raw:(short)Math.Round(raw/16.384*(13371.0/936.0));
                                    received &= BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(13+axis*2))==expected;
                                }
                            }
                        }
                        if(!received)throw new IOException("Windows 未讀到本次虛擬手柄的預期體感／輸入："+mode);
                        Record($"PASS: Windows HID read {mode}; report={data[0]:X2}; bytes={data.Length}");
                        Record("PASS: Windows HID receives six motion axes "+mode);
                    }
                }
                finally {hid.CloseDevice();}
            }
            }
            finally { producerStop.Cancel(); await producer; }
            await output.DisposeAsync();
            Record("PASS: detached own output "+mode);
        }
        Record("Consumer API checks do not establish compatibility with any particular game.");
    }
}
