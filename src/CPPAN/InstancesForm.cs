namespace CPPAN;

public class InstancesForm : Form
{
    readonly Settings st;
    readonly ListBox list = new() { Left = 10, Top = 10, Width = 230, Height = 300 };
    readonly TextBox name = new(), exe = new(), mods = new(), ver = new(), contr = new();
    readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    GameInstance? cur;
    public GameInstance? Selected { get; private set; }

    public InstancesForm(Settings st)
    {
        this.st = st;
        Text = "Game instances"; Size = new Size(720, 400); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        mode.Items.AddRange(new object[] { "Exe", "Steam" });
        int y = 14;
        void Row(string t, Control c) { Controls.Add(new Label { Text = t, Left = 255, Top = y + 3, Width = 110 }); c.Left = 370; c.Top = y; c.Width = 320; Controls.Add(c); y += 32; }
        Row("Name", name); Row("People Playground.exe", exe); Row("Mods folder (blank=auto)", mods); Row("Contraptions (blank=auto)", contr); Row("Game version", ver);
        Controls.Add(new Label { Text = "Set the exact game version (e.g. 1.27.5) so CPPAN picks compatible mods and ports old ones.", Left = 255, Top = y + 22, Width = 440, Height = 34 }); Row("Launch with", mode);
        var add = new Button { Text = "Add (locate .exe)...", Left = 10, Top = 318, Width = 140 };
        var steam = new Button { Text = "Detect Steam", Left = 155, Top = 318, Width = 90 };
        var rem = new Button { Text = "Remove", Left = 255, Top = 318, Width = 80 };
        var use = new Button { Text = "Select", Left = 520, Top = 318, Width = 80, DialogResult = DialogResult.OK };
        var close = new Button { Text = "Close", Left = 610, Top = 318, Width = 80, DialogResult = DialogResult.Cancel };
        Controls.AddRange(new Control[] { list, add, steam, rem, use, close });
        add.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "People Playground|*.exe", Title = "Locate People Playground.exe" };
            if (d.ShowDialog() == DialogResult.OK) { st.Instances.Add(InstanceTools.FromExe(d.FileName, "Exe")); Fill(st.Instances.Count - 1); }
        };
        steam.Click += (_, _) =>
        {
            var p = InstanceTools.FindSteamInstall();
            if (p == null) { MessageBox.Show("Could not find a Steam install of People Playground."); return; }
            st.Instances.Add(InstanceTools.FromExe(p, "Steam")); Fill(st.Instances.Count - 1);
        };
        rem.Click += (_, _) => { if (cur != null) { st.Instances.Remove(cur); cur = null; Fill(0); } };
        list.SelectedIndexChanged += (_, _) => { Save(); cur = list.SelectedIndex >= 0 ? st.Instances[list.SelectedIndex] : null; LoadFields(); };
        use.Click += (_, _) => { Save(); Selected = cur; if (cur != null) st.CurrentId = cur.Id; st.Save(); };
        close.Click += (_, _) => { Save(); st.Save(); };
        Fill(Math.Max(0, st.Instances.FindIndex(i => i.Id == st.CurrentId)));
    }

    void Fill(int sel)
    {
        list.Items.Clear();
        foreach (var i in st.Instances) list.Items.Add(i.Name);
        if (list.Items.Count > 0) list.SelectedIndex = Math.Min(sel, list.Items.Count - 1); else { cur = null; LoadFields(); }
    }
    void LoadFields()
    {
        name.Text = cur?.Name ?? ""; exe.Text = cur?.ExePath ?? ""; mods.Text = cur?.ModsDirOverride ?? "";
        ver.Text = cur?.GameVersion ?? ""; contr.Text = cur?.ContraptionsDirOverride ?? ""; mode.SelectedItem = cur?.LaunchMode ?? "Exe";
    }
    void Save()
    {
        if (cur == null) return;
        cur.Name = name.Text; cur.ExePath = exe.Text; cur.ModsDirOverride = mods.Text; cur.GameVersion = ver.Text; cur.ContraptionsDirOverride = contr.Text;
        if (cur.GameVersion == "") cur.GameVersion = InstanceTools.DetectVersion(cur.ExePath);
        cur.LaunchMode = mode.SelectedItem as string ?? "Exe";
    }
}
