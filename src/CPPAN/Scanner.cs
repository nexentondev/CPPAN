using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace CPPAN;

public enum Verdict { Clean, Warning, Blocked }
public enum ModKind { Unknown, Modern, Legacy, Contraption }

public class ScanReport
{
    public Verdict Verdict = Verdict.Clean;
    public List<string> Findings = new();
    public bool IsMod;
    public ModKind Kind;
    public string Root = "";
    public List<string> Thumbnails = new();
    public string ModJson = "";
    public string Sha256 = "";
    public void Warn(string s) { Findings.Add("WARN   " + s); if (Verdict < Verdict.Warning) Verdict = Verdict.Warning; }
    public void Block(string s) { Findings.Add("BLOCK  " + s); Verdict = Verdict.Blocked; }
    public void Info(string s) => Findings.Add("INFO   " + s);
    public string Text => string.Join("\r\n", Findings);
}

/// Static safety checks. This is NOT a replacement for antivirus: it blocks obviously dangerous
/// packages and flags suspicious behaviour so a human can decide. Windows Defender is also run if present.
public static class Scanner
{
    // Edit these lists if the game uses other extensions.
    static readonly string[] LegacyExt = { ".outline", ".jaap" };
    static readonly string[] LegacyDirs = { "content" };
    static readonly string[] ContraptionExt = { ".phy", ".contraption" };
    static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg" };

    static readonly HashSet<string> BadExt = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".scr", ".msi", ".lnk", ".jar", ".com", ".hta", ".pif", ".reg", ".wsf", ".so", ".dylib" };

    // (pattern, blocks?, reason) - matched against "Namespace.Type::Member" in compiled DLLs
    static readonly (string pat, bool block, string why)[] DllRules =
    {
        ("System.Diagnostics.Process::Start", true, "launches external programs"),
        ("Microsoft.Win32.Registry", true, "touches the Windows registry"),
        ("System.Net.WebClient", false, "downloads/uploads over the network"),
        ("System.Net.Http.HttpClient", false, "makes network requests"),
        ("System.Net.Sockets", false, "opens raw network sockets"),
        ("System.IO.File::Delete", false, "deletes files"),
        ("System.IO.Directory::Delete", false, "deletes folders (seen in the Feb 2026 malware)"),
        ("System.Reflection.Assembly::Load", false, "loads code dynamically"),
        ("System.Environment::GetFolderPath", false, "looks at user folders"),
        ("Steamworks.SteamFriends", false, "reads/sends to your Steam friends list"),
        ("System.Convert::FromBase64String", false, "decodes hidden data (possible obfuscated payload)"),
    };

    static readonly (Regex re, bool block, string why)[] SrcRules =
    {
        (new(@"Process\s*\.\s*Start"), true, "launches external programs"),
        (new(@"Registry\s*\.\s*(Get|Set|Current|Local)"), true, "touches the Windows registry"),
        (new(@"DllImport"), true, "calls native code"),
        (new(@"WebClient|HttpClient|WebRequest|TcpClient|UdpClient|Socket\b"), false, "network access"),
        (new(@"(File|Directory)\s*\.\s*Delete"), false, "deletes files/folders"),
        (new(@"Assembly\s*\.\s*Load"), false, "loads code dynamically"),
        (new(@"SteamFriends|ISteamFriends"), false, "touches Steam friends list"),
        (new(@"FromBase64String"), false, "decodes hidden data"),
    };

    public static string Hash(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    public static ScanReport ScanZip(string zipPath, IEnumerable<string> blockedHashes, bool useDefender)
    {
        var r = new ScanReport { Sha256 = Hash(zipPath) };
        if (blockedHashes.Any(h => h.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase)))
        { r.Block("File hash is on the known-malicious list."); return r; }

        var names = new List<string>();
        ZipArchive z;
        try { z = ZipFile.OpenRead(zipPath); }
        catch { r.Block("Not a valid zip file."); return r; }

        using (z)
        {
            if (z.Entries.Count > 5000) r.Block("Too many files (possible zip bomb).");
            long total = 0;
            foreach (var e in z.Entries)
            {
                var name = e.FullName.Replace('\\', '/');
                total += e.Length;
                if (name.StartsWith("/") || name.Contains("../") || name.Contains(':'))
                { r.Block("Unsafe path (zip-slip): " + name); continue; }
                if (e.Length == 0) continue; // folder
                names.Add(name);
                var ext = Path.GetExtension(name);
                if (BadExt.Contains(ext)) r.Block($"Forbidden file type: {name}");
                else if (ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)) ScanDll(e, name, r);
                else if (ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)) ScanSource(e, name, r);
                else if (Regex.IsMatch(name, @"^([^/]+/)?mod\.json$", RegexOptions.IgnoreCase)) ReadModJson(e, r);
            }
            if (total > 1_000_000_000L) r.Block("Uncompressed size over 1 GB (possible zip bomb).");
        }

        Classify(r, names);
        if (useDefender) Defender(zipPath, r);
        if (r.Verdict == Verdict.Clean) r.Info("No suspicious behaviour found.");
        return r;
    }

    public static ScanReport ScanFolder(string folder, IEnumerable<string> blocked, bool useDefender, out string zipPath)
    {
        zipPath = Path.Combine(Path.GetTempPath(), "cppan-" + Guid.NewGuid().ToString("N") + ".zip");
        ZipFile.CreateFromDirectory(folder, zipPath, CompressionLevel.Optimal, false);
        return ScanZip(zipPath, blocked, useDefender);
    }

    public static string RootPrefix(IList<string> names)
    {
        var mj = names.FirstOrDefault(n => Regex.IsMatch(n, @"^([^/]+/)?mod\.json$", RegexOptions.IgnoreCase));
        if (mj != null) return mj.Contains('/') ? mj[..(mj.IndexOf('/') + 1)] : "";
        if (names.Count > 0 && names.All(n => n.Contains('/')))
        {
            var first = names[0][..(names[0].IndexOf('/') + 1)];
            if (names.All(n => n.StartsWith(first))) return first;
        }
        return "";
    }

    /// Works out what the package is. No mod.json does NOT mean safe or unsafe: everything is still scanned.
    static void Classify(ScanReport r, List<string> names)
    {
        r.Root = RootPrefix(names);
        var rel = names.Select(n => n.StartsWith(r.Root) ? n[r.Root.Length..] : n).ToList();
        bool Has(string[] ext) => rel.Any(n => ext.Contains(Path.GetExtension(n), StringComparer.OrdinalIgnoreCase));
        bool legacyMarker = Has(LegacyExt) || rel.Any(n => LegacyDirs.Any(d => n.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)));
        bool code = Has(new[] { ".cs", ".dll" });
        bool contr = Has(ContraptionExt);
        if (r.IsMod) r.Kind = ModKind.Modern;
        else if (contr && !legacyMarker && !code) r.Kind = ModKind.Contraption;
        else if (legacyMarker || code) r.Kind = ModKind.Legacy;
        else { r.Kind = ModKind.Unknown; r.Block("Not recognised: no mod.json, no old-style content files (.outline/.jaap/content folder), no scripts and no saved structures."); return; }

        int Count(string[] ext) => rel.Count(n => ext.Contains(Path.GetExtension(n), StringComparer.OrdinalIgnoreCase));
        r.Info($"Detected: {r.Kind} - {rel.Count} files ({Count(LegacyExt)} old content, {Count(new[] { ".cs", ".dll" })} code, {Count(ContraptionExt)} structures).");
        if (r.Kind == ModKind.Legacy && code) r.Warn("Old-style mod with code but no mod.json: its scripts will run inside your game. Only continue if you trust the source.");
        if (r.Kind != ModKind.Contraption)
            r.Thumbnails = rel.Where(n => !n.Contains('/') && ImageExt.Contains(Path.GetExtension(n), StringComparer.OrdinalIgnoreCase)).Select(n => r.Root + n).ToList();
    }

    static void ReadModJson(ZipArchiveEntry e, ScanReport r)
    {
        try
        {
            using var s = e.Open(); using var sr = new StreamReader(s);
            var txt = sr.ReadToEnd();
            using var d = JsonDocument.Parse(txt, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (d.RootElement.ValueKind == JsonValueKind.Object &&
                d.RootElement.EnumerateObject().Any(p => p.Name.Equals("Name", StringComparison.OrdinalIgnoreCase)))
            { r.IsMod = true; r.ModJson = txt; }
        }
        catch { r.Warn("mod.json could not be parsed."); }
    }

    static void ScanDll(ZipArchiveEntry e, string name, ScanReport r)
    {
        if (e.Length > 50_000_000) { r.Block($"{name}: DLL unreasonably large."); return; }
        try
        {
            using var ms = new MemoryStream(); using (var s = e.Open()) s.CopyTo(ms);
            ms.Position = 0;
            using var pe = new PEReader(ms);
            if (!pe.HasMetadata) { r.Block($"{name}: native (non-.NET) DLL - mods must be C#."); return; }
            var md = pe.GetMetadataReader();
            if (md.GetTableRowCount(TableIndex.ImplMap) > 0) r.Block($"{name}: uses P/Invoke (calls native code).");
            var hit = new HashSet<string>();
            foreach (var h in md.MemberReferences)
            {
                var m = md.GetMemberReference(h);
                if (m.Parent.Kind != HandleKind.TypeReference) continue;
                var tr = md.GetTypeReference((TypeReferenceHandle)m.Parent);
                var key = md.GetString(tr.Namespace) + "." + md.GetString(tr.Name) + "::" + md.GetString(m.Name);
                foreach (var (pat, block, why) in DllRules)
                    if (key.Contains(pat) && hit.Add(pat))
                    { if (block) r.Block($"{name}: {why} ({pat})"); else r.Warn($"{name}: {why} ({pat})"); }
            }
        }
        catch { r.Warn($"{name}: could not analyse DLL."); }
    }

    static void ScanSource(ZipArchiveEntry e, string name, ScanReport r)
    {
        if (e.Length > 5_000_000) { r.Warn($"{name}: very large source file."); return; }
        using var s = e.Open(); using var sr = new StreamReader(s);
        var txt = sr.ReadToEnd();
        foreach (var (re, block, why) in SrcRules)
            if (re.IsMatch(txt)) { if (block) r.Block($"{name}: {why}"); else r.Warn($"{name}: {why}"); }
    }

    static void Defender(string path, ScanReport r)
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        if (!File.Exists(exe)) { r.Info("Windows Defender not found - antivirus scan skipped."); return; }
        try
        {
            var psi = new ProcessStartInfo(exe, $"-Scan -ScanType 3 -File \"{path}\" -DisableRemediation")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(120_000)) { r.Warn("Defender scan timed out."); return; }
            if (p.ExitCode == 0) r.Info("Windows Defender: no threats found.");
            else if (p.ExitCode == 2) r.Block("Windows Defender flagged this file as a threat.");
            else r.Warn("Windows Defender scan returned code " + p.ExitCode);
        }
        catch (Exception ex) { r.Warn("Defender scan failed: " + ex.Message); }
    }
}
