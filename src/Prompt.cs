namespace Deskglow;

/// <summary>A tiny "type a value" dialog.</summary>
internal static class Prompt
{
    /// <summary>Returns the text, or null if the user cancelled.</summary>
    public static string? Ask(string title, string label, string value)
    {
        using var form = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(420, 130), TopMost = true,
            AutoScaleMode = AutoScaleMode.Dpi,
        };
        var lbl = new Label { Text = label, Left = 14, Top = 14, Width = 392 };
        var box = new TextBox { Text = value, Left = 14, Top = 40, Width = 392 };
        var ok = new Button { Text = "OK", Left = 230, Top = 84, Width = 84, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 322, Top = 84, Width = 84, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog() == DialogResult.OK ? box.Text : null;
    }
}
