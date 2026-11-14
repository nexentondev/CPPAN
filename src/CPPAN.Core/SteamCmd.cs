using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.RegularExpressions;
namespace CPPAN;

/// SteamCMD integration: downloads Valve's official SteamCMD, then fetches Workshop items like a "Workshop downloader".
public static class SteamCmd
{
    const string WinUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
    const string LinuxUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";

    public static string Dir => Directory.CreateDirectory(Path.Combine(Config.Data, "steamcmd")).FullName;
    public static string Exe => Path.Combine(Dir, OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh");
    public static bool Installed => File.Exists(Exe);

    /// Accepts a full Workshop link or a bare item id.
    public static string? ParseItemId(string input)
    {
        input = (input ?? "").Trim();
        if (Regex.IsMatch(input, @"^\d{5,}$")) return input;
        var m = Regex.Match(input, @"[?&]id=(\d{5,})");
        if (!m.Success) m = Regex.Match(input, @"filedetails/(\d{5,})");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string Command(string itemId) => $"workshop_download_item {Config.SteamAppId} {itemId}";
    public static string ContentDir(string itemId) => Path.Combine(Dir, "steamapps", "workshop", "content", Config.SteamAppId.ToString(), itemId);

    public static async Task EnsureInstalledAsync(Prog p, CancellationToken ct)
    {
        if (Installed) { p.Log("SteamCMD already installed: " + Dir); return; }
        var win = OperatingSystem.IsWindows();
        var archive = Path.Combine(Dir, win ? "steamcmd.zip" : "steamcmd_linux.tar.gz");
        p.Status("Installing SteamCMD"); p.Log("Downloading SteamCMD from Valve...");
        await Net.DownloadAsync(win ? WinUrl : LinuxUrl, archive, p, ct);
        if (win) ZipFile.ExtractToDirectory(archive, Dir, true);
        else
        {
            await using var fs = File.OpenRead(archive);
            await using var gz = new GZipStream(fs, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gz, Dir, true);
            foreach (var f in new[] { Exe, Path.Combine(Dir, "linux32", "steamcmd") })
                if (File.Exists(f)) File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        File.Delete(archive);
        p.Log("SteamCMD installed. It updates itself the first time it starts.");
    }

    /// Opens SteamCMD with NO commands and downloads nothing: you type the commands yourself.
    public static Process? OpenConsole() =>
        Process.Start(new ProcessStartInfo(Exe) { WorkingDirectory = Dir, UseShellExecute = OperatingSystem.IsWindows() });

    /// Optional no-typing mode: +login anonymous +workshop_download_item ... +quit
    public static async Task<bool> AutoDownloadAsync(string itemId, Prog p, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Exe) { WorkingDirectory = Dir, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var a in new[] { "+login", "anonymous", "+" + Command(itemId), "+quit" }) psi.ArgumentList.Add(a);
        p.Log("Running SteamCMD: login anonymous, " + Command(itemId));
        using var proc = Process.Start(psi)!;
        var denied = false;
        while (await proc.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            p.Log(line);
            if (Regex.IsMatch(line, "Access Denied|No subscription|Failure|ERROR", RegexOptions.IgnoreCase)) denied = true;
        }
        await proc.WaitForExitAsync(ct);
        if (denied || !HasContent(itemId))
        {
            p.Log("Download failed. People Playground is a paid game, so Steam may refuse anonymous downloads.");
            p.Log("Use the guided mode instead and log in with your own Steam account in the SteamCMD window (login <username>).");
            return false;
        }
        return true;
    }

    public static bool HasContent(string itemId)
    {
        var d = ContentDir(itemId);
        return Directory.Exists(d) && Directory.EnumerateFileSystemEntries(d).Any();
    }

    /// Counts files+bytes so callers can tell when a download has finished growing.
    public static string Signature(string itemId)
    {
        var d = ContentDir(itemId);
        if (!Directory.Exists(d)) return "";
        var files = Directory.GetFiles(d, "*", SearchOption.AllDirectories);
        return files.Length + ":" + files.Sum(f => new FileInfo(f).Length);
    }

    /// Scans a downloaded Workshop item (the temp zip it makes is returned for installing).
    public static ScanReport ScanItem(string itemId, IEnumerable<string> blocked, bool defender, out string zip)
    {
        if (!HasContent(itemId)) throw new Exception("Nothing downloaded for item " + itemId + " yet.");
        var d = ContentDir(itemId);
        var only = Directory.GetFileSystemEntries(d);
        if (only.Length == 1 && only[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        { zip = Path.Combine(Path.GetTempPath(), "cppan-" + Guid.NewGuid().ToString("N") + ".zip"); File.Copy(only[0], zip); return Scanner.ScanZip(zip, blocked, defender); }
        return Scanner.ScanFolder(d, blocked, defender, out zip);
    }
}
