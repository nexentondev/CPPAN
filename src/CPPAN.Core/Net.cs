using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
namespace CPPAN;

public class Prog
{
    public Action<string> Log = _ => { };
    public Action<int> Percent = _ => { };
    public Action<string> Status = _ => { };
}

public static class Net
{
    public static readonly HttpClient Http = CreateClient();
    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("CPPAN/1.0");
        return c;
    }

    public static async Task DownloadAsync(string url, string dest, Prog p, CancellationToken ct)
    {
        using var r = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        r.EnsureSuccessStatusCode();
        long total = r.Content.Headers.ContentLength ?? -1, read = 0;
        await using var s = await r.Content.ReadAsStreamAsync(ct);
        await using var f = File.Create(dest);
        var buf = new byte[81920]; int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            await f.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (total > 0) p.Percent((int)(read * 100 / total));
        }
    }

    // ---- Registry (mod index) ----
    static string IndexCache => Path.Combine(Config.Data, "index.json");
    public static RepoIndex LoadCachedIndex() => Json.Load<RepoIndex>(IndexCache);

    public static async Task<RepoIndex> UpdateIndexAsync(Prog p, CancellationToken ct)
    {
        p.Log("Loading cached repository data");
        p.Log("Updating repositories...");
        p.Log($"Downloading {Config.IndexUrl} ...");
        try
        {
            var tmp = IndexCache + ".tmp";
            await DownloadAsync(Config.IndexUrl, tmp, p, ct);
            JsonSerializer.Deserialize<RepoIndex>(File.ReadAllText(tmp), Json.O); // validate before replacing cache
            File.Move(tmp, IndexCache, true);
            p.Log("Loading modules from downloaded CPPAN-default repository...");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            p.Log("Could not update repository (" + e.Message + "). Using cached data.");
        }
        return LoadCachedIndex();
    }

    // ---- App self-update via GitHub Releases ----
    public static async Task<(string tag, string url, string notes)?> CheckAppUpdateAsync(string suffix = ".zip")
    {
        try
        {
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{Config.Owner}/{Config.AppRepo}/releases/latest");
            using var d = JsonDocument.Parse(json);
            var tag = d.RootElement.GetProperty("tag_name").GetString() ?? "";
            var cur = typeof(Net).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            if (Ver.Cmp(tag, cur) <= 0) return null;
            foreach (var a in d.RootElement.GetProperty("assets").EnumerateArray())
                if ((a.GetProperty("name").GetString() ?? "").EndsWith(suffix))
                    return (tag, a.GetProperty("browser_download_url").GetString()!, d.RootElement.GetProperty("body").GetString() ?? "");
        }
        catch { }
        return null;
    }

    public static async Task ApplyAppUpdateAsync(string url, Prog p)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "cppan-update");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        var zip = Path.Combine(tmp, "u.zip");
        await DownloadAsync(url, zip, p, default);
        var ex = Path.Combine(tmp, "x");
        ZipFile.ExtractToDirectory(zip, ex);
        var app = AppContext.BaseDirectory.TrimEnd('\\');
        var exe = Environment.ProcessPath!;
        var bat = Path.Combine(tmp, "u.bat");
        File.WriteAllText(bat, $"@echo off\r\ntimeout /t 2 /nobreak >nul\r\nxcopy /e /y /q \"{ex}\\*\" \"{app}\\\"\r\nstart \"\" \"{exe}\"\r\n");
        Process.Start(new ProcessStartInfo(bat) { CreateNoWindow = true, UseShellExecute = false });
        Environment.Exit(0);
    }
}
