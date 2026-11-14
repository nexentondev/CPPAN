using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace CPPAN;

public static class InstanceTools
{
    public static string? FindSteamInstall()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"); if (k?.GetValue("SteamPath") is string s) roots.Add(s.Replace('/', '\\')); } catch { }
            roots.Add(@"C:\Program Files (x86)\Steam");
        }
        else
        {
            var h = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.Add(Path.Combine(h, ".steam", "steam"));
            roots.Add(Path.Combine(h, ".local", "share", "Steam"));
            roots.Add(Path.Combine(h, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
        }
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
        string ver = DetectVersion(exe);
        return new GameInstance
        {
            Name = new DirectoryInfo(Path.GetDirectoryName(exe)!).Name + (mode == "Steam" ? " (Steam)" : ""),
            ExePath = exe, LaunchMode = mode, GameVersion = ver
        };
    }

    /// Tries the exe's file version, then folder names like "People.Playground.v1.27.5".
    public static string DetectVersion(string exe)
    {
        try
        {
            var pv = FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "";
            var m = Regex.Match(pv, @"^1\.\d{2}(\.\d+)*");
            if (m.Success) return m.Value;
        }
        catch { }
        var d = new DirectoryInfo(Path.GetDirectoryName(exe) ?? "");
        for (int i = 0; i < 4 && d != null; i++, d = d.Parent)
        {
            var m = Regex.Match(d.Name, @"v?(1\.\d{2}(?:\.\d+)*)");
            if (m.Success) return m.Groups[1].Value;
        }
        return "";
    }

    public static void Launch(GameInstance g)
    {
        if (g.LaunchMode == "Steam")
        {
            var url = $"steam://rungameid/{Config.SteamAppId}";
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else Process.Start("xdg-open", url);
        }
        else if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(g.ExePath) { WorkingDirectory = g.Dir, UseShellExecute = true });
        else // People Playground is a Windows game: on Linux run it through Wine/Proton
        {
            var psi = new ProcessStartInfo("wine") { WorkingDirectory = g.Dir, UseShellExecute = false };
            psi.ArgumentList.Add(g.ExePath);
            Process.Start(psi);
        }
    }
}
