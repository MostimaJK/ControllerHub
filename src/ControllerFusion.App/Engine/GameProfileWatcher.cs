using System.Diagnostics;
using System.IO;

namespace ControllerFusion.Engine;

public static class GameProfileWatcher
{
    public static (SavedProfile? Profile,bool Ambiguous) FindRunning(IEnumerable<SavedProfile> profiles)
    {
        var matches=new List<SavedProfile>();
        foreach(var profile in profiles.Where(p=>!string.IsNullOrWhiteSpace(p.ExecutablePath)))
        {
            var expected=Path.GetFullPath(profile.ExecutablePath!);
            foreach(var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(expected)))
            {
                using(process)
                {
                    try { if(string.Equals(process.MainModule?.FileName,expected,StringComparison.OrdinalIgnoreCase)) {matches.Add(profile);break;} }
                    catch(System.ComponentModel.Win32Exception) { }
                    catch(InvalidOperationException) { }
                }
            }
        }
        // Two matching games are ambiguous. Keep the user's current profile.
        return (matches.Count==1?matches[0]:null,matches.Count>1);
    }
}
