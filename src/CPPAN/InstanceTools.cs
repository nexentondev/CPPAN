using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace CPPAN;

public static class InstanceTools
{
    public static string? FindSteamInstall()
    {
        var roots = new List<string>();
        try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"); if (k?.GetValue("SteamPath") is string s) roots.Add(s.Replace('/', '\\')); } catch { }
        roots.Add(@"C:\Program Files (x86)\Steam");
        foreach (var root in roots.ToList())
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    roots.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        foreach (var r in roots.Distinct())
        {
            var exe = Path.Combine(r, "steamapps", "common", "People Playground", "People Playground.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    public static GameInstance FromExe(string exe, string mode)
    {
        string ver = "";
        try { ver = FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? ""; } catch { }
        return new GameInstance
        {
            Name = new DirectoryInfo(Path.GetDirectoryName(exe)!).Name + (mode == "Steam" ? " (Steam)" : ""),
            ExePath = exe, LaunchMode = mode, GameVersion = ver
        };
    }

    public static void Launch(GameInstance g)
    {
        if (g.LaunchMode == "Steam")
            Process.Start(new ProcessStartInfo($"steam://rungameid/{Config.SteamAppId}") { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo(g.ExePath) { WorkingDirectory = g.Dir, UseShellExecute = true });
    }
}
