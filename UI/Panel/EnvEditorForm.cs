using Inkspire.Panel.App;
using Inkspire.Panel.Runtime;

namespace Inkspire.Panel.UI.Panel;

/// <summary>Key/value grid over the app's .env. Apply saves, rebuilds Laravel's caches and restarts the PHP workers one by one.</summary>
public sealed class EnvEditorForm : Form
{
    private static readonly HashSet<string> Secret = new(StringComparer.OrdinalIgnoreCase)
    {
        "APP_KEY", "DB_PASSWORD", "MAIL_PASSWORD", "RESEND_API_KEY", "PAYMONGO_SECRET_KEY", "PAYMONGO_WEBHOOK_SIG",
        "PAYMONGO_WEBHOOK_SIG_PAYMENT_PAID", "AWS_SECRET_ACCESS_KEY", "REDIS_PASSWORD",
    };

    /// <summary>Written by the panel from its own settings: an edit here is overwritten the next time those settings are applied.</summary>
    private static readonly HashSet<string> PanelManaged = new(StringComparer.OrdinalIgnoreCase)
    {
        "APP_NAME", "APP_URL", "APP_ENV", "APP_DEBUG", "DB_CONNECTION", "DB_DATABASE", "SESSION_DRIVER", "CACHE_STORE", "QUEUE_CONNECTION",
        "LOG_CHANNEL", "LOG_LEVEL", "BROADCAST_CONNECTION", "FILESYSTEM_DISK",
    };

    private readonly AppServices _ctx;
    private readonly DataGridView _grid;
    private readonly CheckBox _reveal;

    public EnvEditorForm(AppServices ctx)
    {
        _ctx = ctx;
        Text = "Edit .env";
        Icon = AppIcon.Main;
        Width = 840;
        Height = 660;
        StartPosition = FormStartPosition.CenterParent;

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Key", Name = "key", FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Name = "value", FillWeight = 65 });
        _grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex != 1 || _reveal!.Checked || e.RowIndex < 0) return;
            var key = _grid.Rows[e.RowIndex].Cells[0].Value as string ?? "";
            if (Secret.Contains(key) && e.Value is string s && s.Length > 0)
            {
                e.Value = new string('•', Math.Min(s.Length, 24));
                e.FormattingApplied = true;
            }
        };
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var key = _grid.Rows[e.RowIndex].Cells[0].Value as string ?? "";
            if (PanelManaged.Contains(key)) e.ToolTipText = "Managed by the panel (Settings). A change here is overwritten when those settings are applied.";
        };
        _grid.CellEnter += (_, e) =>
        {
            // Show the real value while editing a masked secret.
            if (e.ColumnIndex == 1 && e.RowIndex >= 0 && !_reveal!.Checked) _reveal.Checked = false;
        };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 6, 8, 0) };
        top.Controls.Add(new Label
        {
            Text = "Apply saves the file, rebuilds Laravel's caches and restarts the PHP workers one by one (no downtime).",
            AutoSize = true, Margin = new Padding(0, 4, 20, 0), ForeColor = Color.DimGray,
        });
        _reveal = new CheckBox { Text = "Show secrets", AutoSize = true };
        _reveal.CheckedChanged += (_, _) => _grid.Invalidate();
        top.Controls.Add(_reveal);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(10) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var apply = new Button { Text = "Apply", Width = 90 };
        apply.Click += (_, _) => Apply();
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(apply);

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(bottom);
        CancelButton = cancel;

        if (File.Exists(ctx.Paths.AppEnvFile))
            foreach (var (k, v) in EnvFile.Load(ctx.Paths.AppEnvFile).Entries()) _grid.Rows.Add(k, v);
    }

    private void Apply()
    {
        _grid.EndEdit();
        var env = EnvFile.Load(_ctx.Paths.AppEnvFile);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var key = (row.Cells[0].Value as string ?? "").Trim();
            var value = row.Cells[1].Value as string ?? "";
            if (key.Length == 0) continue;
            if (!System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                MessageBox.Show(this, $"'{key}' is not a valid .env key.", "Edit .env", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            seen.Add(key);
            env.Set(key, value);
        }
        foreach (var k in env.Keys.ToList())
            if (!seen.Contains(k)) env.Set(k, null);

        // A mistyped APP_KEY would make every saved session and encrypted value unreadable. Make that a deliberate choice.
        var oldKey = EnvFile.Load(_ctx.Paths.AppEnvFile).Get("APP_KEY");
        if (!string.IsNullOrEmpty(oldKey) && env.Get("APP_KEY") != oldKey)
        {
            var answer = MessageBox.Show(this, "You changed APP_KEY. Everyone will be signed out and anything encrypted with the old key (such as two-factor secrets) becomes unreadable.\n\nChange it anyway?",
                "Edit .env", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }

        env.Save(_ctx.Paths.AppEnvFile);
        var ok = TaskDialog.Run(this, "Applying .env", (log, ct) => AppRuntime.ReloadAsync(_ctx, log, ct));
        if (ok)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
