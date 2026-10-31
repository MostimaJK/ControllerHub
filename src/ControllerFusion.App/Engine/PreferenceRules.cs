namespace ControllerFusion.Engine;

public static class PreferenceRules
{
    public static void Validate(Preferences p)
    {
        p.Language = UiText.Normalize(p.Language);
        if(!new[]{"Switch 2 Pro","PS5 DualSense"}.Contains(p.Controller) || !new[]{"Xbox","PS5","Pro1","Steam / Pro2"}.Contains(p.Output) || !new[]{"藍牙","USB 有線"}.Contains(p.Transport) || !double.IsFinite(p.Rumble) || p.Rumble<0 || p.Rumble>100) throw new System.IO.InvalidDataException("配置包含不支援的手柄、輸出或震動值。");
        if(p.AudioGain<0 || p.AudioGain>200 || new[]{p.GyroBiasPitch,p.GyroBiasYaw,p.GyroBiasRoll}.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1000)) throw new System.IO.InvalidDataException("進階配置數值無效。");
        var keys=new[]{"不映射","A","B","X","Y","L","R","ZL","ZR","左搖桿按下","右搖桿按下","A + B","L + R"};
        if(!keys.Contains(p.GL)||!keys.Contains(p.GR)||!keys.Contains(p.C)) throw new System.IO.InvalidDataException("配置包含無效的背鍵映射。");
    }

}
