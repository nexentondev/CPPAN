using System.Diagnostics;
using System.Text.RegularExpressions;
namespace CPPAN;

public class MainForm : Form
{
    readonly Settings st = Settings.Load();
    GameInstance? inst;
    RepoIndex idx = new();
    Dictionary<string, InstalledMod> installed = new();
    readonly Dictionary<string, bool> pending = new();
    readonly HashSet<string> updating = new();

    readonly DataGridView grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, EnableHeadersVisualStyles = true };
    readonly TextBox search = new() { Text = "is:compatible", Width = 600 };
    readonly Label dTitle = new() { Dock = DockStyle.Top, Font = new Font("Segoe UI", 13, FontStyle.Bold), Height = 56, AutoEllipsis = false };
    readonly Label dAbs = new() { Dock = DockStyle.Top, Height = 60 };
    readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    readonly Dictionary<string, TextBox> tab = new();
    readonly ToolStripStatusLabel statusLbl = new();
    ToolStripButton bPlay = new(), bRefresh = new(), bUpdAll = new(), bApply = new();

    public MainForm()
    {
        Text = $"CPPAN v{typeof(MainForm).Assembly.GetName().Version?.ToString(3)} - Comprehensive People Playground Archive Network";
        Size = new Size(1280, 760); StartPosition = FormStartPosition.CenterScreen;

        // ---- grid ----
        grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Installed", Width = 60 });
        foreach (var (h, w) in new[] { ("Name", 260), ("Author", 120), ("Installed version", 70), ("Latest version", 70), ("Min game ver.", 70),
                                       ("Size", 70), ("Release date", 100), ("Install date", 100), ("Description", 400) })
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h, Width = w, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic });
        grid.CellContentClick += OnCellClick;
        grid.SelectionChanged += (_, _) => ShowDetails();

        // ---- details panel ----
        foreach (var n in new[] { "Metadata", "Relationships", "Contents", "Versions" })
        {
            var tb = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = SystemColors.Window, ScrollBars = ScrollBars.Vertical };
            tab[n] = tb; var tp = new TabPage(n); tp.Controls.Add(tb); tabs.TabPages.Add(tp);
        }
        var detail = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        detail.Controls.Add(tabs); detail.Controls.Add(dAbs); detail.Controls.Add(dTitle);

        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
        split.Panel1.Controls.Add(grid); split.Panel2.Controls.Add(detail);
        Shown += (_, _) => { split.SplitterDistance = Math.Max(300, Width - 420); };

        // ---- search row ----
        var sp = new Panel { Dock = DockStyle.Top, Height = 32 };
        sp.Controls.Add(new Label { Text = "Search:", Left = 8, Top = 8, Width = 50 }); search.Left = 62; search.Top = 5; sp.Controls.Add(search);
        search.TextChanged += (_, _) => RefreshGrid();

        // ---- toolbar ----
        var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(24, 24), RenderMode = ToolStripRenderMode.System };
        bPlay = Btn("Play", (_, _) => Play()); bRefresh = Btn("Refresh", async (_, _) => await RefreshRepoAsync());
        bUpdAll = Btn("Update all", (_, _) => UpdateAll()); bApply = Btn("Apply", (_, _) => Apply());
        bApply.Enabled = false; bUpdAll.Enabled = false;
        ts.Items.AddRange(new ToolStripItem[] { bPlay, new ToolStripSeparator(), bRefresh, bUpdAll, bApply, new ToolStripSeparator(),
            Btn("Instances", (_, _) => ManageInstances()), Btn("Upload mod", (_, _) => new UploadForm(idx, st.UseDefender).ShowDialog(this)) });
        ToolStripButton Btn(string t, EventHandler h) { var b = new ToolStripButton(t) { DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(6) }; b.Click += h; return b; }

        // ---- menu ----
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("File"); var set = new ToolStripMenuItem("Settings"); var help = new ToolStripMenuItem("Help");
        file.DropDownItems.Add("Game instances...", null, (_, _) => ManageInstances());
        file.DropDownItems.Add("Install mod from zip...", null, (_, _) => InstallLocal());
        file.DropDownItems.Add("Upload / submit a mod...", null, (_, _) => new UploadForm(idx, st.UseDefender).ShowDialog(this));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Export modpack...", null, (_, _) => ExportPack());
        file.DropDownItems.Add("Import modpack...", null, (_, _) => ImportPack());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, (_, _) => Close());
        var viaSteam = new ToolStripMenuItem("Launch through Steam") { CheckOnClick = true };
        var viaExe = new ToolStripMenuItem("Launch through game .exe") { CheckOnClick = true };
        var defender = new ToolStripMenuItem("Scan installs with Windows Defender") { CheckOnClick = true, Checked = st.UseDefender };
        viaSteam.Click += (_, _) => { SetMode("Steam"); viaSteam.Checked = true; viaExe.Checked = false; };
        viaExe.Click += (_, _) => { SetMode("Exe"); viaSteam.Checked = false; viaExe.Checked = true; };
        defender.Click += (_, _) => { st.UseDefender = defender.Checked; st.Save(); };
        set.DropDownOpening += (_, _) => { viaSteam.Checked = inst?.LaunchMode == "Steam"; viaExe.Checked = inst?.LaunchMode != "Steam"; };
        set.DropDownItems.AddRange(new ToolStripItem[] { viaSteam, viaExe, new ToolStripSeparator(), defender });
        help.DropDownItems.Add("Check for CPPAN updates", null, async (_, _) => await CheckAppUpdateAsync(false));
        help.DropDownItems.Add("Open registry on GitHub", null, (_, _) => Open($"https://github.com/{Config.Owner}/{Config.MetaRepo}"));
        help.DropDownItems.Add("About", null, (_, _) => MessageBox.Show("CPPAN - Comprehensive People Playground Archive Network\r\nInspired by CKAN for Kerbal Space Program.", "About"));
        menu.Items.AddRange(new ToolStripItem[] { file, set, help });

        var status = new StatusStrip(); status.Items.Add(statusLbl);

        // dock order: Fill first, then edges (menu last = very top)
        Controls.Add(split); Controls.Add(sp); Controls.Add(ts); Controls.Add(menu); Controls.Add(status);
        MainMenuStrip = menu;
        Shown += async (_, _) => await StartupAsync();
    }

    // ================= startup / repo =================
    async Task StartupAsync()
    {
        if (st.Current == null) ManageInstances();
        if (st.Current == null) { MessageBox.Show("CPPAN needs a People Playground instance to work. Use File > Game instances."); }
        SelectInstance(st.Current);
        await RefreshRepoAsync();
        if (st.CheckAppUpdates) await CheckAppUpdateAsync(true);
    }

    async Task RefreshRepoAsync()
    {
        idx = Net.LoadCachedIndex(); RefreshGrid();
        var pf = new ProgressForm("Updating repositories", async (p, ct) => { idx = await Net.UpdateIndexAsync(p, ct); });
        pf.ShowDialog(this);
        RefreshGrid();
        await EnsureCoreAsync();
    }

    void SelectInstance(GameInstance? g)
    {
        inst = g; pending.Clear(); updating.Clear();
        installed = g?.LoadInstalled() ?? new();
        statusLbl.Text = g == null ? "No game instance" :
            $"Game instance: {g.Name} (PP {(g.GameVersion == "" ? "unknown" : g.GameVersion)}, launch via {g.LaunchMode})";
        RefreshGrid();
    }

    void ManageInstances()
    {
        using var f = new InstancesForm(st);
        if (f.ShowDialog(this) == DialogResult.OK && f.Selected != null) { SelectInstance(f.Selected); _ = EnsureCoreAsync(); }
    }

    void SetMode(string m) { if (inst == null) return; inst.LaunchMode = m; st.Save(); SelectInstance(inst); }

    /// People API is mandatory: installed automatically for every instance.
    async Task EnsureCoreAsync()
    {
        if (inst == null || installed.ContainsKey(Config.CoreId)) return;
        if (idx.Find(Config.CoreId) == null)
        { MessageBox.Show("People API was not found in the registry yet. Check your internet connection and press Refresh.", "CPPAN"); return; }
        await Task.Yield();
        RunApply(Installer.MakePlan(idx, installed, Array.Empty<string>(), Array.Empty<string>(), updating), "Installing People API (required)");
    }

    async Task CheckAppUpdateAsync(bool silent)
    {
        var u = await Net.CheckAppUpdateAsync();
        if (u == null) { if (!silent) MessageBox.Show("CPPAN is up to date."); return; }
        if (MessageBox.Show($"CPPAN {u.Value.tag} is available.\r\n\r\n{u.Value.notes}\r\n\r\nUpdate now?", "Update available", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        var pf = new ProgressForm("Updating CPPAN", (p, ct) => Net.ApplyAppUpdateAsync(u.Value.url, p)) { AutoClose = false };
        pf.ShowDialog(this);
    }

    // ================= grid =================
    bool Compatible(ModVersion v) => inst == null || v.MinGameVersion == "" || inst.GameVersion == "" || Ver.Cmp(inst.GameVersion, v.MinGameVersion) >= 0;
    bool Updatable(ModEntry m) => installed.TryGetValue(m.Id, out var im) && m.Latest != null && Ver.Cmp(m.Latest.Version, im.Version) > 0;

    IEnumerable<ModEntry> Filtered()
    {
        var q = idx.Mods.Where(m => m.Latest != null);
        foreach (var tok in search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = tok.ToLowerInvariant();
            if (t == "is:compatible") q = q.Where(m => Compatible(m.Latest!));
            else if (t == "is:installed") q = q.Where(m => installed.ContainsKey(m.Id));
            else if (t == "is:updatable") q = q.Where(Updatable);
            else if (t == "is:removed") q = q.Where(m => m.Removed);
            else if (t.StartsWith("tag:")) q = q.Where(m => m.Tags.Any(x => x.ToLowerInvariant() == t[4..]));
            else if (t.StartsWith("author:")) q = q.Where(m => m.Author.ToLowerInvariant().Contains(t[7..]));
            else q = q.Where(m => (m.Name + " " + m.Id + " " + m.Abstract).ToLowerInvariant().Contains(t));
        }
        return q.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
    }

    void RefreshGrid()
    {
        grid.Rows.Clear();
        foreach (var m in Filtered())
        {
            var l = m.Latest!; installed.TryGetValue(m.Id, out var im);
            bool chk = pending.TryGetValue(m.Id, out var pv) ? pv : im != null;
            int i = grid.Rows.Add(chk, (m.Removed ? "[archived] " : "") + m.Name, m.Author, im?.Version ?? "", l.Version, l.MinGameVersion,
                Size(l.Size), l.Released.ToString("dd/MM/yyyy"), im?.InstalledOn.ToString("dd/MM/yyyy") ?? "", m.Abstract);
            var row = grid.Rows[i]; row.Tag = m;
            if (m.Id == Config.CoreId) { row.Cells[0].ReadOnly = true; row.DefaultCellStyle.Font = new Font(grid.Font, FontStyle.Bold); }
            if (Updatable(m)) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 205);
        }
        bUpdAll.Enabled = idx.Mods.Any(Updatable);
        bApply.Enabled = pending.Count > 0 || updating.Count > 0;
        ShowDetails();
    }

    static string Size(long b) => b >= 1 << 20 ? $"{b / 1048576.0:0.0} MiB" : b >= 1024 ? $"{b / 1024.0:0.0} KiB" : $"{b} B";

    void OnCellClick(object? s, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
        var m = (ModEntry)grid.Rows[e.RowIndex].Tag!;
        if (m.Id == Config.CoreId) { MessageBox.Show("People API is required and cannot be removed.", "CPPAN"); return; }
        grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        bool v = (bool)grid.Rows[e.RowIndex].Cells[0].Value;
        if (v == installed.ContainsKey(m.Id)) pending.Remove(m.Id); else pending[m.Id] = v;
        bApply.Enabled = pending.Count > 0 || updating.Count > 0;
    }

    void ShowDetails()
    {
        if (grid.CurrentRow?.Tag is not ModEntry m) { dTitle.Text = ""; dAbs.Text = ""; foreach (var t in tab.Values) t.Text = ""; return; }
        var l = m.Latest!; installed.TryGetValue(m.Id, out var im);
        dTitle.Text = m.Name; dAbs.Text = m.Abstract;
        tab["Metadata"].Text = string.Join("\r\n", new[] {
            $"Version:        {l.Version}", $"License:        {m.License}", $"Author:         {m.Author}", $"Min game ver.:  {l.MinGameVersion}",
            $"Identifier:     {m.Id}", $"Tags:           {string.Join(", ", m.Tags)}", $"Home page:      {m.Homepage}",
            $"Status:         {(m.Removed ? "Removed from original host - archived by CPPAN" : "Available")}",
            $"SHA256:         {l.Sha256}" });
        tab["Relationships"].Text = $"Depends:\r\n  {string.Join("\r\n  ", m.Depends)}\r\n\r\nRecommends:\r\n  {string.Join("\r\n  ", m.Recommends)}\r\n\r\nConflicts:\r\n  {string.Join("\r\n  ", m.Conflicts)}";
        tab["Contents"].Text = im == null ? "Install this mod to see its files." : string.Join("\r\n", im.Files);
        tab["Versions"].Text = string.Join("\r\n\r\n", m.Versions.OrderByDescending(v => v.Version, Comparer<string>.Create(Ver.Cmp))
            .Select(v => $"{v.Version}   ({v.Released:dd/MM/yyyy}, {Size(v.Size)}, needs game >= {(v.MinGameVersion == "" ? "any" : v.MinGameVersion)})\r\n{v.Changelog}"));
    }

    // ================= actions =================
    void UpdateAll()
    {
        foreach (var m in idx.Mods.Where(Updatable)) updating.Add(m.Id);
        RefreshGrid(); bApply.Enabled = true;
    }

    void Apply()
    {
        if (inst == null) { MessageBox.Show("Select a game instance first."); return; }
        try
        {
            var plan = Installer.MakePlan(idx, installed, pending.Where(p => p.Value).Select(p => p.Key), pending.Where(p => !p.Value).Select(p => p.Key), updating);
            var msg = "Install/update:\r\n" + string.Join("\r\n", plan.Install.Select(x => $"  {x.m.Name} {x.v.Version}")) +
                      "\r\n\r\nRemove:\r\n" + string.Join("\r\n", plan.Remove.Select(x => "  " + x));
            if (MessageBox.Show(msg + "\r\n\r\nContinue?", "Apply changes", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            RunApply(plan, "Applying changes");
            pending.Clear(); updating.Clear(); RefreshGrid();
        }
        catch (Exception e) { MessageBox.Show(e.Message, "Cannot apply changes", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    void RunApply(Plan plan, string title)
    {
        if (inst == null || (plan.Install.Count == 0 && plan.Remove.Count == 0)) return;
        var g = inst;
        var pf = new ProgressForm(title, (p, ct) => Installer.ApplyAsync(g, idx, plan, installed, st.UseDefender, ConfirmWarning, p, ct)) { AutoClose = false };
        pf.ShowDialog(this);
        RefreshGrid();
    }

    bool ConfirmWarning(string name, ScanReport r) => (bool)Invoke(new Func<bool>(() =>
        MessageBox.Show($"The safety scan raised warnings for {name}:\r\n\r\n{r.Text}\r\n\r\nInstall anyway?", "Safety warning",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes));

    void Play()
    {
        if (inst == null) { MessageBox.Show("Select a game instance first."); return; }
        if (!installed.ContainsKey(Config.CoreId)) { _ = EnsureCoreAsync(); if (!installed.ContainsKey(Config.CoreId)) return; }
        try { InstanceTools.Launch(inst); } catch (Exception e) { MessageBox.Show(e.Message, "Could not launch"); }
    }

    void InstallLocal()
    {
        if (inst == null) return;
        using var d = new OpenFileDialog { Filter = "Mod zip|*.zip" };
        if (d.ShowDialog() != DialogResult.OK) return;
        Cursor = Cursors.WaitCursor; ScanReport r;
        try { r = Scanner.ScanZip(d.FileName, idx.BlockedHashes, st.UseDefender); } finally { Cursor = Cursors.Default; }
        if (r.Verdict == Verdict.Blocked) { MessageBox.Show("Blocked by safety scan:\r\n\r\n" + r.Text, "CPPAN", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        if (MessageBox.Show($"Scan result: {r.Verdict}\r\n\r\n{r.Text}\r\n\r\nInstall?", "Install local mod", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        var nm = Regex.Match(r.ModJson, "\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
        var id = "local-" + Regex.Replace(nm.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        var files = Installer.Extract(d.FileName, Path.Combine(inst.ModsDir, id));
        installed[id] = new InstalledMod { Id = id, Version = "local", Files = files }; inst.SaveInstalled(installed);
        MessageBox.Show($"Installed {nm} (local). Remove it by deleting its folder in {inst.ModsDir}.");
    }

    void ExportPack()
    {
        using var d = new SaveFileDialog { Filter = "CPPAN modpack|*.cppanpack", FileName = "my-modpack.cppanpack" };
        if (d.ShowDialog() == DialogResult.OK) Json.Save(d.FileName, installed.Values.Where(i => !i.Id.StartsWith("local-")).ToDictionary(i => i.Id, i => i.Version));
    }

    void ImportPack()
    {
        using var d = new OpenFileDialog { Filter = "CPPAN modpack|*.cppanpack" };
        if (d.ShowDialog() != DialogResult.OK) return;
        foreach (var id in Json.Load<Dictionary<string, string>>(d.FileName).Keys)
            if (idx.Find(id) != null && !installed.ContainsKey(id)) pending[id] = true;
        RefreshGrid(); bApply.Enabled = pending.Count > 0;
    }

    static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
