using System.IO.Compression;
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
        bool defender, Func<string, ScanReport, bool> confirmWarning, Func<string, byte[], bool> askThumb,
        Prog p, CancellationToken ct)
    {
        int step = 0, steps = Math.Max(1, plan.Remove.Count + plan.Install.Count);
        foreach (var id in plan.Remove)
        {
            p.Status($"Removing {id}"); Uninstall(g, db, id); p.Log($"Removed {id}");
            AppLog.Write("Removed " + id);
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
            InstallPackage(g, db, zip, m.Id, v.Version, rep, askThumb, p.Log);
            p.Log($"Installed {m.Name} {v.Version}"); p.Percent(++step * 100 / steps);
        }
    }

    /// Installs a scanned package. Mods go to the Mods folder (old ones are ported); saved structures go to Contraptions.
    public static InstalledMod InstallPackage(GameInstance g, Dictionary<string, InstalledMod> db, string zip, string id,
        string version, ScanReport rep, Func<string, byte[], bool> askThumb, Action<string> log)
    {
        var im = new InstalledMod { Id = id, Version = version, Kind = rep.Kind.ToString() };
        void L(string s) { im.Log.Add($"[{DateTime.Now:HH:mm:ss}] {s}"); log(s); AppLog.Write(id + ": " + s); }
        L($"Package type: {rep.Kind} (scan: {rep.Verdict})");
        foreach (var f in rep.Findings) if (!f.StartsWith("INFO")) L(f);

        if (rep.Kind == ModKind.Contraption)
        {
            im.Files = Extract(zip, g.ContraptionsDir);
            L($"Saved structure to the Contraptions folder: {g.ContraptionsDir} ({im.Files.Count} files)");
        }
        else
        {
            string? thumbRel = null;
            if (!Regex.IsMatch(rep.ModJson ?? "", "\"ThumbnailPath\"\\s*:\\s*\"[^\"]+\"", RegexOptions.IgnoreCase))
            {
                using var z = ZipFile.OpenRead(zip);
                foreach (var cand in rep.Thumbnails)
                {
                    var ent = z.GetEntry(cand); if (ent == null) continue;
                    byte[] data;
                    using (var s = ent.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                    bool yes; try { yes = askThumb(cand, data); } catch { yes = false; }
                    if (yes) { thumbRel = cand[rep.Root.Length..]; break; }
                }
            }
            var dir = Path.Combine(g.ModsDir, id);
            im.Files = Extract(zip, dir);
            foreach (var line in Porter.Port(g, dir, id, version, rep, thumbRel)) L(line);
            var mj = Path.GetFullPath(Path.Combine(dir, "mod.json"));
            foreach (var f in new[] { mj, mj + ".cppan-orig" }) if (File.Exists(f) && !im.Files.Contains(f)) im.Files.Add(f);
            im.Thumbnail = thumbRel ?? "";
            L($"Installed to {dir}");
        }
        db[id] = im; g.SaveInstalled(db);
        return im;
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
        var prefix = Scanner.RootPrefix(z.Entries.Where(e => e.Length > 0).Select(e => e.FullName.Replace('\\', '/')).ToList());
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
            files.Add(dest);
        }
        return files;
    }

    public static void Uninstall(GameInstance g, Dictionary<string, InstalledMod> db, string id)
    {
        if (!db.TryGetValue(id, out var im)) return;
        foreach (var f in im.Files)
        {
            var p = Path.IsPathRooted(f) ? f : Path.Combine(g.ModsDir, f);
            if (File.Exists(p)) File.Delete(p);
        }
        var dir = Path.Combine(g.ModsDir, id);
        if (Directory.Exists(dir))
        {
            foreach (var d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        db.Remove(id);
    }
}
