using System.IO;
using System.Text.Json;

namespace ControllerFusion.Engine;

public sealed record SavedProfile(string Name,Preferences Settings,string? ExecutablePath=null);
public sealed class ProfileStore
{
    readonly string path;
    public ProfileStore(string directory) { Directory.CreateDirectory(directory); path=Path.Combine(directory,"profiles.json"); }
    public string? LoadWarning { get; private set; }
    static List<SavedProfile> Read(string file)
    {
        var profiles=JsonSerializer.Deserialize<List<SavedProfile>>(File.ReadAllText(file)) ?? throw new InvalidDataException("配置清單為空值。");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in profiles)
        {
            if(p==null || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length>60 || !names.Add(p.Name) || p.Settings==null) throw new InvalidDataException("配置名稱或內容無效。");
            PreferenceRules.Validate(p.Settings);
            if(p.ExecutablePath!=null && (!Path.IsPathFullyQualified(p.ExecutablePath) || !p.ExecutablePath.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("遊戲綁定路徑無效。");
        }
        return profiles;
    }
    public List<SavedProfile> Load()
    {
        if(!File.Exists(path))return new();
        try {return Read(path);}
        catch(Exception ex) when(ex is JsonException or InvalidDataException)
        {
            LoadWarning="配置清單損壞，保留原檔；";
            if(File.Exists(path+".bak")) {var recovered=Read(path+".bak");LoadWarning+="已載入上一份有效備份。";return recovered;}
            throw;
        }
    }
    public static Preferences Copy(Preferences p)=>JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(p))!;
    public static Preferences LoadPreferences(string file,out string? warning)
    {
        warning=null;
        if(!File.Exists(file))return new();
        Preferences ReadSettings(string path)
        {
            var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path)) ?? throw new InvalidDataException("設定內容為空值。");
            PreferenceRules.Validate(p);return p;
        }
        try {return ReadSettings(file);}
        catch(Exception ex) when(ex is JsonException or InvalidDataException)
        {
            warning="設定檔損壞，保留原檔。";
            if(File.Exists(file+".bak")) {warning+="已載入上一份有效備份。";return ReadSettings(file+".bak");}
            throw;
        }
    }
    public static void SavePreferences(string file,Preferences p)
    {
        PreferenceRules.Validate(p);
        if(File.Exists(file))
        {
            try {var old=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file));if(old!=null){PreferenceRules.Validate(old);File.Copy(file,file+".bak",true);}}
            catch(Exception ex) when(ex is JsonException or InvalidDataException) { }
        }
        AtomicWrite(file,JsonSerializer.Serialize(p,new JsonSerializerOptions {WriteIndented=true}));
    }
    public static void AtomicWrite(string path,string text)
    { var temp=path+".tmp"; File.WriteAllText(temp,text); File.Move(temp,path,true); }
    public void Save(List<SavedProfile> profiles)
    {
        if(File.Exists(path))
        {
            try {Read(path);File.Copy(path,path+".bak",true);}
            catch(Exception ex) when(ex is JsonException or InvalidDataException) { }
        }
        AtomicWrite(path,JsonSerializer.Serialize(profiles,new JsonSerializerOptions{WriteIndented=true}));
    }
}
