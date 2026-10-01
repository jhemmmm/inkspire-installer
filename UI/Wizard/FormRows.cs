namespace Inkspire.Panel.UI.Wizard;

/// <summary>Tiny layout helper: a two-column "label | control + hint" table, so each wizard page stays short.</summary>
public static class FormRows
{
    public static TableLayoutPanel Table(int labelWidth = 170)
    {
        var t = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
        };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return t;
    }

    public static void Add(TableLayoutPanel table, string label, Control control, string? hint = null)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 8, 8, 0) }, 0, row);

        var cell = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 6),
        };
        cell.Controls.Add(control);
        if (hint is not null) cell.Controls.Add(Hint(hint));
        table.Controls.Add(cell, 1, row);
    }

    public static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(520, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 2, 0, 0),
    };

    public static Label Body(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(640, 0),
        Margin = new Padding(0, 0, 0, 8),
    };

    public static TextBox Text(int width = 360, bool password = false) => new() { Width = width, UseSystemPasswordChar = password };
}
