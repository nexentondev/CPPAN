namespace CPPAN;

public class ThumbDialog : Form
{
    public ThumbDialog(string file, byte[] data)
    {
        Text = "Is this the thumbnail?"; Size = new Size(420, 470); StartPosition = FormStartPosition.CenterParent;
        var pb = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = Image.FromStream(new MemoryStream(data)) };
        var lbl = new Label { Dock = DockStyle.Top, Height = 54, Padding = new Padding(6), Text = $"This mod has a picture in its main folder:\r\n{file}\r\nIs it the mod's thumbnail?" };
        var p = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
        var no = new Button { Text = "No, it's content", Width = 120, DialogResult = DialogResult.No };
        var yes = new Button { Text = "Yes, thumbnail", Width = 120, DialogResult = DialogResult.Yes };
        p.Controls.Add(no); p.Controls.Add(yes);
        Controls.Add(pb); Controls.Add(lbl); Controls.Add(p);
        AcceptButton = yes; CancelButton = no;
    }
}
