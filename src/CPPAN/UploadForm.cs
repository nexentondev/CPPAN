using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace CPPAN;

/// Scan + package a mod for submission to the CPPAN registry (GitHub-based).
public class UploadForm : Form
{
    readonly RepoIndex idx; readonly bool defender;
    readonly TextBox path = new() { ReadOnly = true }, id = new(), nm = new(), author = new(), ver = new() { Text = "1.0.0" },
        minGame = new(), lic = new() { Text = "MIT" }, home = new(), deps = new() { Text = Config.CoreId }, tags = new(), abs = new(),
        log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
    string zip = ""; ScanReport? rep; bool tempZip;

    public UploadForm(RepoIndex idx, bool defender)
    {
        this.idx = idx; this.defender = defender;
        Text = "Upload / submit a mod"; Size = new Size(720, 640); StartPosition = FormStartPosition.CenterParent;
        int y = 10;
        void Row(string t, Control c, int w = 480) { Controls.Add(new Label { Text = t, Left = 10, Top = y + 3, Width = 150 }); c.Left = 165; c.Top = y; c.Width = w; Controls.Add(c); y += 28; }
        Row("Mod (zip or folder)", path, 330);
        var bz = new Button { Text = "Zip...", Left = 502, Top = 9, Width = 70 }; var bf = new Button { Text = "Folder...", Left = 577, Top = 9, Width = 80 };
        Controls.Add(bz); Controls.Add(bf);
        Row("Id (lowercase-dashes)", id); Row("Name", nm); Row("Author", author); Row("Version", ver); Row("Min game version", minGame);
        Row("License", lic); Row("Homepage", home); Row("Depends (comma)", deps); Row("Tags (comma)", tags); Row("Short description", abs);
        log.Left = 10; log.Top = y + 5; log.Width = 685; log.Height = 220; Controls.Add(log);
        var bs = new Button { Text = "Re-scan", Left = 10, Top = y + 235, Width = 90 };
        var bp = new Button { Text = "Create submission package + open GitHub", Left = 380, Top = y + 235, Width = 315 };
        Controls.AddRange(new Control[] { bs, bp });
        bz.Click += (_, _) => { using var d = new OpenFileDialog { Filter = "Zip|*.zip" }; if (d.ShowDialog() == DialogResult.OK) Load(d.FileName, false); };
        bf.Click += (_, _) => { using var d = new FolderBrowserDialog(); if (d.ShowDialog() == DialogResult.OK) Load(d.SelectedPath, true); };
        bs.Click += (_, _) => { if (path.Text != "") Load(path.Text, Directory.Exists(path.Text)); };
        bp.Click += (_, _) => Submit();
    }

    void Load(string p, bool folder)
    {
        Cursor = Cursors.WaitCursor; path.Text = p;
        try
        {
            if (folder) { rep = Scanner.ScanFolder(p, idx.BlockedHashes, defender, out zip); tempZip = true; }
            else { zip = p; rep = Scanner.ScanZip(p, idx.BlockedHashes, defender); tempZip = false; }
            log.Text = $"Verdict: {rep.Verdict}  (SHA256 {rep.Sha256})\r\n\r\n" + rep.Text;
            if (rep.IsMod) AutoFill(rep.ModJson);
        }
        finally { Cursor = Cursors.Default; }
    }

    void AutoFill(string json)
    {
        try
        {
            using var d = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            string G(params string[] k) => d.RootElement.EnumerateObject().FirstOrDefault(x => k.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).Value is { ValueKind: JsonValueKind.String } v ? v.GetString()! : "";
            if (nm.Text == "") nm.Text = G("Name");
            if (author.Text == "") author.Text = G("Author");
            if (abs.Text == "") abs.Text = G("Description");
            if (minGame.Text == "") minGame.Text = G("GameVersion");
            var mv = G("ModVersion", "Version"); if (mv != "") ver.Text = mv;
            if (id.Text == "") id.Text = Regex.Replace(nm.Text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        }
        catch { }
    }

    void Submit()
    {
        if (rep == null || zip == "") { MessageBox.Show("Pick a mod first."); return; }
        if (rep.Verdict == Verdict.Blocked) { MessageBox.Show("This mod failed the safety scan and cannot be submitted."); return; }
        if (!Regex.IsMatch(id.Text, "^[a-z0-9][a-z0-9-]*$")) { MessageBox.Show("Id must be lowercase letters, digits and dashes."); return; }
        if (rep.Verdict == Verdict.Warning && MessageBox.Show("The scan raised warnings (shown in the log). Maintainers will review them. Continue?", "Warnings", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        using var fd = new FolderBrowserDialog { Description = "Where to save the submission files?" };
        if (fd.ShowDialog() != DialogResult.OK) return;
        var zname = $"{id.Text}-{ver.Text}.zip";
        File.Copy(zip, Path.Combine(fd.SelectedPath, zname), true);
        string[] L(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var entry = new ModEntry
        {
            Id = id.Text, Name = nm.Text, Author = author.Text, Abstract = abs.Text, License = lic.Text, Homepage = home.Text,
            Tags = L(tags.Text), Depends = L(deps.Text),
            Versions = { new ModVersion { Version = ver.Text, MinGameVersion = minGame.Text, Sha256 = rep.Sha256, Size = new FileInfo(zip).Length, Released = DateTime.UtcNow,
                Urls = new[] { $"https://github.com/{Config.Owner}/{Config.MetaRepo}/releases/download/{id.Text}-{ver.Text}/{zname}" } } }
        };
        Json.Save(Path.Combine(fd.SelectedPath, id.Text + ".cppan.json"), entry);
        var body = $"Mod: {nm.Text} {ver.Text}\nSHA256: {rep.Sha256}\nScan verdict: {rep.Verdict}\n\nPlease drag-and-drop BOTH files from your submission folder into this issue:\n- {zname}\n- {id.Text}.cppan.json";
        Process.Start(new ProcessStartInfo($"https://github.com/{Config.Owner}/{Config.MetaRepo}/issues/new?labels=submission&title={Uri.EscapeDataString("Mod submission: " + id.Text)}&body={Uri.EscapeDataString(body)}") { UseShellExecute = true });
        MessageBox.Show($"Saved to {fd.SelectedPath}.\nA GitHub issue page was opened - attach the two files there.");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    { if (tempZip && File.Exists(zip)) try { File.Delete(zip); } catch { } base.OnFormClosed(e); }
}
