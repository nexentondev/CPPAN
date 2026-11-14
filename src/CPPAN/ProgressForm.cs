namespace CPPAN;

/// Looks like CKAN's "Updating repositories" screen: two bars + a log box + Cancel/OK.
public class ProgressForm : Form
{
    readonly ProgressBar overall = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous, ForeColor = Color.FromArgb(6, 176, 37) };
    readonly ProgressBar bar = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous, ForeColor = Color.FromArgb(6, 176, 37) };
    readonly Label lbl = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly TextBox log = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
    readonly Button ok = new() { Text = "OK", Enabled = false, Width = 90 };
    readonly Button cancel = new() { Text = "Cancel", Width = 90 };
    readonly CancellationTokenSource cts = new();
    readonly Func<Prog, CancellationToken, Task> work;
    bool done;
    public bool Failed { get; private set; }
    public bool AutoClose { get; set; } = true;

    public ProgressForm(string title, Func<Prog, CancellationToken, Task> work)
    {
        this.work = work;
        Text = title; Size = new Size(760, 460); StartPosition = FormStartPosition.CenterParent;
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(4) };
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        btns.Controls.Add(ok); btns.Controls.Add(cancel);
        t.Controls.Add(lbl, 0, 0); t.Controls.Add(overall, 0, 1); t.Controls.Add(bar, 0, 2);
        t.Controls.Add(log, 0, 3); t.Controls.Add(btns, 0, 4);
        Controls.Add(t);
        ok.Click += (_, _) => Close();
        cancel.Click += (_, _) => { if (done) Close(); else cts.Cancel(); };
        Shown += async (_, _) => await Run();
    }

    void Ui(Action a) { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(a); else a(); }

    async Task Run()
    {
        var p = new Prog
        {
            Log = s => Ui(() => log.AppendText(s + "\r\n")),
            Percent = v => Ui(() => bar.Value = Math.Clamp(v, 0, 100)),
            Status = s => Ui(() => lbl.Text = s)
        };
        try { await work(p, cts.Token); }
        catch (OperationCanceledException) { p.Log("Cancelled."); Failed = true; }
        catch (Exception e) { p.Log("ERROR: " + e.Message); Failed = true; }
        done = true;
        Ui(() => { overall.Value = 100; bar.Value = 100; ok.Enabled = true; cancel.Text = "Close"; if (lbl.Text == "") lbl.Text = Failed ? "Failed" : "Done"; });
        if (!Failed && AutoClose) { await Task.Delay(500); Ui(Close); }
    }
}
