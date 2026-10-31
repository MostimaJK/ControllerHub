using DS4Windows;

namespace ControllerFusion.Engine;

public static class MotionPipeline
{
    public static SixAxis? Map(SixAxis? source,Preferences p)
    {
        if(!p.MotionEnabled || source==null) return null;
        bool calibrated=p.GyroCalibrationController==p.Controller;
        return new SixAxis(
            -source.gyroYawFull+(int)Math.Round((calibrated?p.GyroBiasYaw:0)*16),
            source.gyroPitchFull-(int)Math.Round((calibrated?p.GyroBiasPitch:0)*16),
            -source.gyroRollFull+(int)Math.Round((calibrated?p.GyroBiasRoll:0)*16),
            -source.accelXFull,-source.accelYFull,source.accelZFull,source.elapsed);
    }

    public static (double Pitch,double Yaw,double Roll) Calibrate(IReadOnlyList<SixAxis> samples)
    {
        if(samples.Count<50) throw new InvalidOperationException("有效體感資料不足；請保持連接後重試。");
        double p=samples.Average(s=>s.angVelPitch),y=samples.Average(s=>s.angVelYaw),r=samples.Average(s=>s.angVelRoll);
        foreach(var s in samples)
        {
            double g=Math.Sqrt(s.accelXG*s.accelXG+s.accelYG*s.accelYG+s.accelZG*s.accelZG);
            if(g<.8 || g>1.2 || Math.Abs(s.angVelPitch-p)>2 || Math.Abs(s.angVelYaw-y)>2 || Math.Abs(s.angVelRoll-r)>2)
                throw new InvalidOperationException("校準期間偵測到移動或無效重力資料，請把手柄放平靜置後重試。");
        }
        if(Math.Max(Math.Abs(p),Math.Max(Math.Abs(y),Math.Abs(r)))>10)
            throw new InvalidOperationException("校準時手柄仍在旋轉，未儲存偏差。");
        return(p,y,r);
    }
}
