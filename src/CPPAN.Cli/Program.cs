using CPPAN;

// cppan - command-line CPPAN for Linux, macOS and Windows (same engine as the Windows GUI).
internal static class Program
{
    static readonly Settings st = Settings.Load();
    static RepoIndex idx = Net.LoadCachedIndex();
    static readonly Prog pr = new() { Log = Console.WriteLine, Status = s => Console.WriteLine(s) };
    static GameInstance? Inst => st.Current;

    const string Help = @"CPPAN - Comprehensive People Playground Archive Network (command line)

  instances                          list game instances
  add <path-to-exe> [steam]          add an instance ('steam' = launch through Steam)
  detect-steam                       find a Steam install of People Playground
  use <number|name>                  choose the active instance
  update                             refresh the mod list from GitHub
  list [words] [filters]             search mods   (filters: is:installed is:updatable is:portable is:compatible
                                                   is:removed is:core tag:x source:workshop|local|registry kind:legacy|contraption)
  info <id>                          show details of a mod
  install <id...>   remove <id...>   install / remove mods (dependencies and People API are handled)
  upgrade                            update every installed mod
  install-file <zip>                 install a mod or saved structure from a zip file
  workshop <link|id> [--auto]        add a mod from the Steam Workshop through SteamCMD
  scan <zip|folder>                  run the safety scan
  play                               launch the game
  log                                show where the log file is
  check-update                       look for a newer CPPAN";

    static async Task<int> Main(string[] a)
    {
        try { return await Run(a); }
        catch (Exception e) { Console.Error.WriteLine("Error: " + e.Message); return 1; }
    }

    static async Task<int> Run(string[] a)
    {
        if (a.Length == 0 || a[0] is "help" or "--help" or "-h") { Console.WriteLine(Help); return 0; }
        var rest = a.Skip(1).ToArray();
        switch (a[0])
        {
            case "instances":
                for (int i = 0; i < st.Instances.Count; i++)
                { var g = st.Instances[i]; Console.WriteLine($"{(g.Id == st.Current?.Id ? "*" : " ")} {i + 1}. {g.Name}  [PP {(g.GameVersion == "" ? "?" : g.GameVersion)}, {g.LaunchMode}]  {g.ExePath}"); }
                return 0;
            case "add":
                if (rest.Length < 1) throw new Exception("Usage: cppan add <path-to-exe> [steam]");
                AddInstance(InstanceTools.FromExe(Path.GetFullPath(rest[0]), rest.Contains("steam") ? "Steam" : "Exe")); return 0;
            case "detect-steam":
                var p = InstanceTools.FindSteamInstall() ?? throw new Exception("No Steam install of People Playground found.");
                AddInstance(InstanceTools.FromExe(p, "Steam")); return 0;
            case "use":
                var g2 = st.Instances.ElementAtOrDefault(int.TryParse(rest.FirstOrDefault(), out var n) ? n - 1 : -1)
                         ?? st.Instances.FirstOrDefault(x => x.Name.Equals(rest.FirstOrDefault(), StringComparison.OrdinalIgnoreCase))
                         ?? throw new Exception("No such instance.");
                st.CurrentId = g2.Id; st.Save(); Console.WriteLine("Using " + g2.Name); return 0;
            case "update": idx = await Net.UpdateIndexAsync(pr, default); Console.WriteLine($"{idx.Mods.Count} mods in the registry."); return 0;
            case "list": List(rest); return 0;
            case "info": Info(rest.FirstOrDefault() ?? ""); return 0;
            case "install": await Change(rest, Array.Empty<string>(), false); return 0;
            case "remove": await Change(Array.Empty<string>(), rest, false); return 0;
            case "upgrade": await Change(Array.Empty<string>(), Array.Empty<string>(), true); return 0;
            case "install-file": await InstallFile(rest.FirstOrDefault() ?? throw new Exception("Usage: cppan install-file <zip>")); return 0;
            case "workshop": await Workshop(rest); return 0;
            case "scan": Scan(rest.FirstOrDefault() ?? throw new Exception("Usage: cppan scan <zip|folder>")); return 0;
            case "play": Require(); InstanceTools.Launch(Inst!); return 0;
            case "log": Console.WriteLine(AppLog.FilePath); return 0;
            case "check-update":
                var u = await Net.CheckAppUpdateAsync(OperatingSystem.IsWindows() ? ".zip" : ".tar.gz");
                Console.WriteLine(u == null ? "CPPAN is up to date." : $"CPPAN {u.Value.tag} is available:\n{u.Value.url}\n\n{u.Value.notes}");
                return 0;
            default: Console.WriteLine(Help); return 1;
        }
    }

    static void AddInstance(GameInstance g) { st.Instances.Add(g); st.CurrentId = g.Id; st.Save(); Console.WriteLine($"Added {g.Name} (PP {(g.GameVersion == "" ? "version unknown" : g.GameVersion)})."); }
    static void Require() { if (Inst == null) throw new Exception("No game instance. Use 'cppan add <exe>' or 'cppan detect-steam'."); }
    static bool Ask(string q) { Console.Write(q + " [y/N] "); return (Console.ReadLine() ?? "").Trim().ToLowerInvariant() is "y" or "yes"; }

    static void List(string[] words)
    {
        Require(); var installed = Inst!.LoadInstalled();
        var q = idx.Mods.Where(m => m.Latest != null);
        foreach (var w0 in words)
        {
            var w = w0.ToLowerInvariant();
            if (w == "is:installed") q = q.Where(m => installed.ContainsKey(m.Id));
            else if (w == "is:updatable") q = q.Where(m => installed.TryGetValue(m.Id, out var i) && Ver.Cmp(m.Latest!.Version, i.Version) > 0);
            else if (w == "is:removed") q = q.Where(m => m.Removed);
            else if (w == "is:core") q = q.Where(m => m.Id == Config.CoreId);
            else if (w == "is:portable") q = q.Where(m => m.Latest!.MaxGameVersion != "" && Inst!.GameVersion != "" && Ver.Cmp(Inst.GameVersion, m.Latest.MaxGameVersion) > 0);
            else if (w == "is:compatible") q = q.Where(m => m.Latest!.MinGameVersion == "" || Inst!.GameVersion == "" || Ver.Cmp(Inst.GameVersion, m.Latest.MinGameVersion) >= 0);
            else if (w.StartsWith("tag:")) q = q.Where(m => m.Tags.Any(t => t.Equals(w[4..], StringComparison.OrdinalIgnoreCase)));
            else if (w.StartsWith("source:")) q = q.Where(m => (m.Id.StartsWith("workshop-") ? "workshop" : m.Id.StartsWith("local-") ? "local" : "registry") == w[7..]);
            else if (w.StartsWith("kind:")) q = q.Where(m => installed.TryGetValue(m.Id, out var i) && i.Kind.ToLowerInvariant() == w[5..]);
            else q = q.Where(m => (m.Name + " " + m.Id + " " + m.Abstract).Contains(w, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var m in q.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"[{(installed.ContainsKey(m.Id) ? "x" : " ")}] {m.Id,-28} {m.Latest!.Version,-10} {m.Name}{(m.Removed ? "  [archived]" : "")}");
        foreach (var i in installed.Values.Where(i => idx.Find(i.Id) == null)) Console.WriteLine($"[x] {i.Id,-28} {i.Version,-10} (installed from file, {i.Kind})");
    }

    static void Info(string id)
    {
        var m = idx.Find(id) ?? throw new Exception("Not found: " + id); var l = m.Latest!;
        Console.WriteLine($"{m.Name}  ({m.Id})\nAuthor: {m.Author}   License: {m.License}\nLatest: {l.Version}  Game: {(l.MinGameVersion == "" ? "any" : l.MinGameVersion)}..{(l.MaxGameVersion == "" ? "any" : l.MaxGameVersion)}\nTags: {string.Join(", ", m.Tags)}\nDepends: {string.Join(", ", m.Depends)}\nConflicts: {string.Join(", ", m.Conflicts)}\n{m.Abstract}");
    }

    static async Task Change(string[] want, string[] remove, bool upgrade)
    {
        Require(); var g = Inst!; var installed = g.LoadInstalled();
        var updating = new HashSet<string>();
        if (upgrade) foreach (var m in idx.Mods) if (m.Latest != null && installed.TryGetValue(m.Id, out var i) && Ver.Cmp(m.Latest.Version, i.Version) > 0) updating.Add(m.Id);
        var plan = Installer.MakePlan(idx, installed, want, remove, updating);
        if (plan.Install.Count == 0 && plan.Remove.Count == 0) { Console.WriteLine("Nothing to do."); return; }
        foreach (var (m, v) in plan.Install) Console.WriteLine($"  install {m.Name} {v.Version}");
        foreach (var r in plan.Remove) Console.WriteLine($"  remove  {r}");
        if (!Ask("Continue?")) return;
        await Installer.ApplyAsync(g, idx, plan, installed, st.UseDefender,
            (name, rep) => { Console.WriteLine($"\nWarnings for {name}:\n{rep.Text}"); return Ask("Install anyway?"); },
            (file, _) => Ask($"'{file}' is a picture in the main folder. Is it the thumbnail?"), pr, default);
    }

    static void Scan(string path)
    {
        var rep = Directory.Exists(path) ? Scanner.ScanFolder(path, idx.BlockedHashes, st.UseDefender, out _) : Scanner.ScanZip(path, idx.BlockedHashes, st.UseDefender);
        Console.WriteLine($"Type: {rep.Kind}   Verdict: {rep.Verdict}   SHA256: {rep.Sha256}\n{rep.Text}");
    }

    static async Task InstallFile(string zip)
    {
        Require(); var rep = Scanner.ScanZip(zip, idx.BlockedHashes, st.UseDefender); var nm = rep.NameOr(Path.GetFileNameWithoutExtension(zip));
        FinishInstall(rep, zip, "local-" + ScanReport.Slug(nm), nm); await Task.CompletedTask;
    }

    static void FinishInstall(ScanReport rep, string zip, string id, string name)
    {
        var g = Inst!; var installed = g.LoadInstalled();
        Console.WriteLine($"{name}\nDetected: {rep.Kind}   Scan: {rep.Verdict}\n{rep.Text}");
        if (rep.Verdict == Verdict.Blocked) { Console.WriteLine("Blocked by the safety scan."); return; }
        if (!Ask("Install?")) return;
        if (installed.ContainsKey(id)) Installer.Uninstall(g, installed, id);
        Installer.InstallPackage(g, installed, zip, id, id.StartsWith("workshop-") ? "workshop" : "local", rep, (f, _) => Ask($"'{f}' is a picture in the main folder. Is it the thumbnail?"), Console.WriteLine);
    }

    static async Task Workshop(string[] rest)
    {
        Require();
        var id = SteamCmd.ParseItemId(rest.FirstOrDefault() ?? "") ?? throw new Exception("Usage: cppan workshop <workshop link or item id> [--auto]");
        Console.WriteLine("Workshop mods can be malicious. CPPAN scans everything before installing.");
        await SteamCmd.EnsureInstalledAsync(pr, default);
        if (rest.Contains("--auto")) { if (!await SteamCmd.AutoDownloadAsync(id, pr, default)) return; }
        else
        {
            Console.WriteLine($"\nSteamCMD will open now (nothing is downloaded automatically).\n  1) type:  login anonymous\n  2) then paste:  {SteamCmd.Command(id)}\n  3) when it says Success, type:  quit\n");
            var proc = Process_Open();
            proc?.WaitForExit();
        }
        var rep = SteamCmd.ScanItem(id, idx.BlockedHashes, st.UseDefender, out var zip);
        try { FinishInstall(rep, zip, "workshop-" + id, rep.NameOr("Workshop item " + id)); }
        finally { try { File.Delete(zip); } catch { } }
    }

    static System.Diagnostics.Process? Process_Open() => SteamCmd.OpenConsole();
}
