using System.Text.Json;
namespace CPPAN;

public static class Config
{
    // >>> CHANGE THIS to your GitHub username <<<
    public const string Owner = "nexentondev";
    public const string AppRepo = "CPPAN";        // this app
    public const string MetaRepo = "CPPAN-meta";  // the mod registry (index.json)
    public const string CoreId = "people-api";    // mandatory mod
    public const int SteamAppId = 1118200;        // People Playground on Steam
    public static string IndexUrl => $"https://raw.githubusercontent.com/{Owner}/{MetaRepo}/main/index.json";
    public static string Data => Directory.CreateDirectory(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CPPAN")).FullName;
    public static string Cache => Directory.CreateDirectory(Path.Combine(Data, "cache")).FullName;
}

public static class Json
{
    public static readonly JsonSerializerOptions O = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static T Load<T>(string path) where T : new()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), O) ?? new T() : new T(); }
        catch { return new T(); }
    }
    public static void Save(string path, object o)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(o, O));
    }
}

public static class Ver
{
    public static int Cmp(string a, string b)
    {
        int[] P(string s) => s.TrimStart('v', 'V').Split('.', '-', '+')
            .Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        var x = P(a); var y = P(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }
}

public class ModVersion
{
    public string Version { get; set; } = "0.0.0";
    public string MinGameVersion { get; set; } = "";
    public string[] Urls { get; set; } = Array.Empty<string>(); // multiple mirrors => old/removed mods survive
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public DateTime Released { get; set; }
    public string Changelog { get; set; } = "";
}

public class ModEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Abstract { get; set; } = "";
    public string License { get; set; } = "";
    public string Homepage { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] Depends { get; set; } = Array.Empty<string>();
    public string[] Recommends { get; set; } = Array.Empty<string>();
    public string[] Conflicts { get; set; } = Array.Empty<string>();
    public bool Removed { get; set; } // removed from Steam Workshop / original host, archived by CPPAN
    public List<ModVersion> Versions { get; set; } = new();
    public ModVersion? Latest => Versions.OrderByDescending(v => v.Version, Comparer<string>.Create(Ver.Cmp)).FirstOrDefault();
}

public class RepoIndex
{
    public int Schema { get; set; } = 1;
    public List<ModEntry> Mods { get; set; } = new();
    public List<string> BlockedHashes { get; set; } = new(); // known-malicious SHA256s
    public ModEntry? Find(string id) => Mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

public class InstalledMod
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime InstalledOn { get; set; } = DateTime.Now;
    public List<string> Files { get; set; } = new();
}

public class GameInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string LaunchMode { get; set; } = "Exe"; // "Exe" or "Steam"
    public string GameVersion { get; set; } = "";
    public string ModsDirOverride { get; set; } = "";
    public string Dir => Path.GetDirectoryName(ExePath) ?? "";
    public string ModsDir => string.IsNullOrWhiteSpace(ModsDirOverride) ? Path.Combine(Dir, "Mods") : ModsDirOverride;
    string DbPath => Path.Combine(Config.Data, "instances", Id, "installed.json");
    public Dictionary<string, InstalledMod> LoadInstalled() => Json.Load<Dictionary<string, InstalledMod>>(DbPath);
    public void SaveInstalled(Dictionary<string, InstalledMod> d) => Json.Save(DbPath, d);
}

public class Settings
{
    public List<GameInstance> Instances { get; set; } = new();
    public string CurrentId { get; set; } = "";
    public bool UseDefender { get; set; } = true;
    public bool CheckAppUpdates { get; set; } = true;
    public GameInstance? Current => Instances.FirstOrDefault(i => i.Id == CurrentId) ?? Instances.FirstOrDefault();
    static string P => Path.Combine(Config.Data, "settings.json");
    public static Settings Load() => Json.Load<Settings>(P);
    public void Save() => Json.Save(P, this);
}
