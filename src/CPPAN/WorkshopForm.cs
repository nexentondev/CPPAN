namespace CPPAN;

/// Add a mod from the Steam Workshop through SteamCMD.
public class WorkshopForm : Form
{
    readonly TextBox url = new(), cmd = new() { ReadOnly = true, Font = new Font("Consolas", 10) };
    readonly Label parsed = new() { AutoSize = true }, status = new() { AutoSize = false, Height = 40 };
    readonly Button open = new() { Text = "1. Open SteamCMD" }, loggedIn = new() { Text = "2. I typed it - show the download command", Enabled = false },
        auto = new() { Text = "Auto-download instead (no typing)" }, cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    string? id, lastSig;
    public string? ItemId { get; private set; }

    public WorkshopForm()
    {
        Text = "Add from Steam Workshop"; Size = new Size(640, 470); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        Controls.Add(new Label { Left = 12, Top = 10, Width = 600, Height = 34, ForeColor = Color.DarkRed,
            Text = "Workshop mods can be malicious (this is what happened in the 2026 outbreaks). CPPAN scans everything before it installs." });
        Controls.Add(new Label { Left = 12, Top = 52, Width = 600, Text = "Paste the Steam Workshop link (or just the item id):" });
        url.SetBounds(12, 74, 600, 24); Controls.Add(url);
        parsed.Left = 12; parsed.Top = 102; Controls.Add(parsed);
        open.SetBounds(12, 130, 200, 30); Controls.Add(open);
        Controls.Add(new Label { Left = 12, Top = 168, Width = 600, Height = 38,
            Text = "In the SteamCMD window that opens, type:   login anonymous   and press Enter. (Nothing is downloaded until you run the command below.)" });
        loggedIn.SetBounds(12, 212, 330, 30); Controls.Add(loggedIn);
        cmd.SetBounds(12, 250, 600, 26); Controls.Add(cmd);
        status.Left = 12; status.Top = 284; status.Width = 600; Controls.Add(status);
        auto.SetBounds(12, 340, 240, 30); Controls.Add(auto);
        Controls.Add(new Label { Left = 12, Top = 376, Width = 600, Height = 36, ForeColor = Color.DimGray,
            Text = "Heads up: People Playground is a paid game, so anonymous downloads may be refused. If so, log in with your own Steam account in the SteamCMD window instead." });
        cancel.SetBounds(530, 395, 82, 28); Controls.Add(cancel);

        url.TextChanged += (_, _) => { id = SteamCmd.ParseItemId(url.Text); parsed.Text = id == null ? "No item id found yet." : "Item id: " + id; };
        open.Click += async (_, _) => await OpenSteamCmd();
        loggedIn.Click += (_, _) =>
        {
            cmd.Text = SteamCmd.Command(id!);
            try { Clipboard.SetText(cmd.Text); } catch { }
            status.Text = "Copied to your clipboard. Paste it into SteamCMD and press Enter. CPPAN is watching for the download...";
            lastSig = null; timer.Start();
        };
        auto.Click += (_, _) => AutoDownload();
        timer.Tick += (_, _) => Watch();
        FormClosed += (_, _) => timer.Stop();
    }

    bool NeedId() { if (id != null) return true; MessageBox.Show("Paste a Workshop link or item id first."); return false; }

    async Task OpenSteamCmd()
    {
        if (!NeedId()) return;
        var pf = new ProgressForm("Installing SteamCMD", (p, ct) => SteamCmd.EnsureInstalledAsync(p, ct));
        pf.ShowDialog(this);
        if (pf.Failed || !SteamCmd.Installed) return;
        try { SteamCmd.OpenConsole(); loggedIn.Enabled = true; status.Text = "SteamCMD is open. Type: login anonymous"; }
        catch (Exception e) { MessageBox.Show(e.Message); }
        await Task.CompletedTask;
    }

    void Watch()
    {
        if (id == null) return;
        var sig = SteamCmd.Signature(id);
        if (sig == "") return;
        status.Text = "Downloading... (" + sig + ")";
        if (sig == lastSig) { timer.Stop(); ItemId = id; DialogResult = DialogResult.OK; Close(); }
        lastSig = sig;
    }

    void AutoDownload()
    {
        if (!NeedId()) return;
        var pf = new ProgressForm("Downloading from Workshop", async (p, ct) =>
        {
            await SteamCmd.EnsureInstalledAsync(p, ct);
            if (!await SteamCmd.AutoDownloadAsync(id!, p, ct)) throw new Exception("Auto-download failed (see log).");
        }) { AutoClose = true };
        pf.ShowDialog(this);
        if (!pf.Failed) { ItemId = id; DialogResult = DialogResult.OK; Close(); }
    }
}
