using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace CPPAN;

public class Plan
{
    public List<(ModEntry m, ModVersion v)> Install = new();
    public List<string> Remove = new();
}

public static class Installer
{
    public static Plan MakePlan(RepoIndex idx, Dictionary<string, InstalledMod> inst,
        IEnumerable<string> want, IEnumerable<string> remove, ISet<string> updating)
    {
        var plan = new Plan(); var seen = new HashSet<string>(); var rem = remove.ToList();
        void Add(string id)
        {
            if (!seen.Add(id)) return;
            var m = idx.Find(id) ?? throw new Exception($"'{id}' was not found in the registry.");
            var v = m.Latest ?? throw new Exception($"'{id}' has no versions.");
            foreach (var d in m.Depends) Add(d);
            if (!inst.ContainsKey(id) || updating.Contains(id)) plan.Install.Add((m, v));
        }
        Add(Config.CoreId);
        foreach (var w in want) Add(w);
        foreach (var r in rem)
        {
            if (r == Config.CoreId) throw new Exception("People API is required and cannot be removed.");
            var deps = inst.Keys.Where(k => k != r && !rem.Contains(k) && idx.Find(k)?.Depends.Contains(r) == true).ToList();
            if (deps.Count > 0) throw new Exception($"Cannot remove '{r}': needed by {string.Join(", ", deps)}.");
            plan.Remove.Add(r);
        }
        var final = inst.Keys.Except(plan.Remove).Concat(plan.Install.Select(x => x.m.Id)).ToHashSet();
        foreach (var id in final)
            foreach (var c in idx.Find(id)?.Conflicts ?? Array.Empty<string>())
                if (final.Contains(c)) throw new Exception($"'{id}' conflicts with '{c}'.");
        return plan;
    }

    public static async Task ApplyAsync(GameInstance g, RepoIndex idx, Plan plan, Dictionary<string, InstalledMod> db,
        bool defender, Func<string, ScanReport, bool> confirmWarning, Prog p, CancellationToken ct)
    {
        int step = 0, steps = plan.Remove.Count + plan.Install.Count;
        foreach (var id in plan.Remove)
        {
            p.Status($"Removing {id}"); Uninstall(g, db, id); p.Log($"Removed {id}");
            g.SaveInstalled(db); p.Percent(++step * 100 / steps);
        }
        foreach (var (m, v) in plan.Install)
        {
            p.Status($"Installing {m.Name} {v.Version}");
            var zip = await FetchAsync(m, v, p, ct);
            var rep = Scanner.ScanZip(zip, idx.BlockedHashes, defender);
            foreach (var f in rep.Findings) p.Log("  " + f);
            if (rep.Verdict == Verdict.Blocked) throw new Exception($"{m.Name} was blocked by the safety scan.");
            if (rep.Verdict == Verdict.Warning && !confirmWarning(m.Name, rep)) throw new Exception($"Install of {m.Name} cancelled.");
            if (db.ContainsKey(m.Id)) Uninstall(g, db, m.Id);
            var files = Extract(zip, Path.Combine(g.ModsDir, m.Id));
            db[m.Id] = new InstalledMod { Id = m.Id, Version = v.Version, Files = files };
            g.SaveInstalled(db);
            p.Log($"Installed {m.Name} {v.Version}"); p.Percent(++step * 100 / steps);
        }
    }

    static async Task<string> FetchAsync(ModEntry m, ModVersion v, Prog p, CancellationToken ct)
    {
        var path = Path.Combine(Config.Cache, $"{m.Id}-{v.Version}.zip");
        if (File.Exists(path) && (v.Sha256 == "" || Scanner.Hash(path).Equals(v.Sha256, StringComparison.OrdinalIgnoreCase))) return path;
        Exception? last = null;
        foreach (var url in v.Urls) // try each mirror
        {
            try
            {
                p.Log($"Downloading {url}");
                await Net.DownloadAsync(url, path, p, ct);
                if (v.Sha256 != "" && !Scanner.Hash(path).Equals(v.Sha256, StringComparison.OrdinalIgnoreCase))
                { File.Delete(path); throw new Exception("SHA256 mismatch - file was tampered with or changed."); }
                if (v.Sha256 == "") p.Log("  WARN   registry entry has no SHA256 hash");
                return path;
            }
            catch (Exception e) when (e is not OperationCanceledException) { last = e; p.Log("  failed: " + e.Message); }
        }
        throw new Exception($"All mirrors failed for {m.Name}: {last?.Message}");
    }

    public static List<string> Extract(string zipPath, string target)
    {
        var files = new List<string>();
        Directory.CreateDirectory(target);
        var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
        using var z = ZipFile.OpenRead(zipPath);
        var mj = z.Entries.FirstOrDefault(e => Regex.IsMatch(e.FullName.Replace('\\', '/'), @"^([^/]+/)?mod\.json$", RegexOptions.IgnoreCase));
        var prefix = mj != null && mj.FullName.Contains('/') ? mj.FullName[..(mj.FullName.IndexOf('/') + 1)] : "";
        foreach (var e in z.Entries)
        {
            var rel = e.FullName.Replace('\\', '/');
            if (prefix != "" && !rel.StartsWith(prefix)) continue;
            rel = rel[prefix.Length..];
            if (rel == "" || rel.EndsWith('/')) continue;
            var dest = Path.GetFullPath(Path.Combine(target, rel));
            if (!dest.StartsWith(root)) throw new Exception("Blocked path traversal: " + rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, true);
            files.Add(Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(target))!, dest));
        }
        return files;
    }

    public static void Uninstall(GameInstance g, Dictionary<string, InstalledMod> db, string id)
    {
        if (db.TryGetValue(id, out var im))
        {
            foreach (var f in im.Files) { var p = Path.Combine(g.ModsDir, f); if (File.Exists(p)) File.Delete(p); }
            var dir = Path.Combine(g.ModsDir, id);
            if (Directory.Exists(dir))
                foreach (var d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                    if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            db.Remove(id);
        }
    }
}
