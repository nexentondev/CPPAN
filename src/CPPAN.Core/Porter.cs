using System.Text.Json;
using System.Text.Json.Nodes;
namespace CPPAN;

/// Makes old mods loadable on the instance's game version.
/// Honest scope: this fixes the manifest (mod.json / GameVersion / thumbnail). It cannot rewrite code
/// that relies on game features that no longer exist.
public static class Porter
{
    static readonly JsonDocumentOptions Doc = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public static List<string> Port(GameInstance g, string dir, string id, string version, ScanReport rep, string? thumbRel)
    {
        var log = new List<string>();
        var path = Path.Combine(dir, "mod.json");
        var gv = g.GameVersion;
        JsonObject obj;
        if (!File.Exists(path))
        {
            obj = new JsonObject
            {
                ["Name"] = id, ["Description"] = "Old-style mod, wrapped by CPPAN", ["Author"] = "unknown",
                ["ModVersion"] = version, ["GameVersion"] = gv == "" ? "1.0.0" : gv
            };
            log.Add("No mod.json found: generated one so the game can load this old-style mod.");
        }
        else
        {
            File.Copy(path, path + ".cppan-orig", true);
            obj = JsonNode.Parse(File.ReadAllText(path), null, Doc) as JsonObject ?? new JsonObject();
            var key = obj.Select(kv => kv.Key).FirstOrDefault(k => k.Equals("GameVersion", StringComparison.OrdinalIgnoreCase));
            if (gv == "") log.Add("Instance game version is unknown: not porting. Set it in Instances.");
            else if (key == null) { obj["GameVersion"] = gv; log.Add($"mod.json had no GameVersion: set to {gv}."); }
            else
            {
                var old = obj[key]?.ToString() ?? "";
                if (old != "" && Ver.Cmp(old, gv) < 0) { obj[key] = gv; log.Add($"Ported from game {old} to {gv}: manifest updated (original saved as mod.json.cppan-orig)."); }
                else if (old != "" && Ver.Cmp(old, gv) > 0) log.Add($"Mod targets a newer game ({old}) than this instance ({gv}): left unchanged, it may not work.");
                else log.Add("Game version already matches.");
            }
        }
        if (thumbRel != null && !obj.Any(kv => kv.Key.Equals("ThumbnailPath", StringComparison.OrdinalIgnoreCase)))
        { obj["ThumbnailPath"] = thumbRel; log.Add($"Thumbnail set to {thumbRel}."); }
        File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return log;
    }
}
