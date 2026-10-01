using System.Text;
using Inkspire.Panel.Processes;
using Inkspire.Panel.Services;

namespace Inkspire.Panel.UI.Panel;

/// <summary>Bottom half of the main window: pick a service log (live console or a file) and watch it.
/// Lines are queued and flushed on a UI timer so a chatty process cannot freeze the window.</summary>
public sealed class LogViewerControl : UserControl
{
    private const int MaxLines = 6000;

    private sealed record Choice(ServiceBase Service, LogSource Source)
    {
        public override string ToString() => $"{Service.Title}  ›  {Source.Name}";
    }

    private readonly ComboBox _sources = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    private readonly CheckBox _autoScroll = new() { Text = "Auto-scroll", Checked = true, AutoSize = true };
    private readonly Button _clear = new() { Text = "Clear", AutoSize = true };
    private readonly Button _openFile = new() { Text = "Open file", AutoSize = true };
    private readonly TextBox _text = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
        BackColor = Color.FromArgb(24, 24, 28),
        ForeColor = Color.Gainsboro,
        Dock = DockStyle.Fill,
        HideSelection = false,
    };
    private readonly System.Windows.Forms.Timer _flush = new() { Interval = 200 };
    private readonly Queue<string> _pending = new();
    private readonly object _gate = new();
    private int _lineCount;
    private Choice? _current;
    private OutputBuffer? _boundBuffer;
    private FileTailer? _tailer;

    public LogViewerControl()
    {
        Dock = DockStyle.Fill;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 5, 8, 0), WrapContents = false };
        bar.Controls.Add(new Label { Text = "Log:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        bar.Controls.Add(_sources);
        bar.Controls.Add(_autoScroll);
        bar.Controls.Add(_clear);
        bar.Controls.Add(_openFile);

        Controls.Add(_text);
        Controls.Add(bar);

        _sources.SelectedIndexChanged += (_, _) => { if (_sources.SelectedItem is Choice c) Bind(c); };
        _clear.Click += (_, _) => { _current?.Source.Buffer?.Clear(); Reset(); };
        _openFile.Click += (_, _) =>
        {
            var file = _tailer?.CurrentFile;
            if (file is not null) Ui.OpenExternal(file);
        };
        _flush.Tick += (_, _) => Flush();
        _flush.Start();
    }

    public void SetServices(IEnumerable<ServiceBase> services)
    {
        _sources.Items.Clear();
        foreach (var s in services)
        foreach (var src in s.LogSources)
            _sources.Items.Add(new Choice(s, src));
        if (_sources.Items.Count > 0) _sources.SelectedIndex = 0;
    }

    public void ShowService(ServiceBase service)
    {
        foreach (var item in _sources.Items)
        {
            if (item is Choice c && c.Service == service)
            {
                _sources.SelectedItem = c;
                return;
            }
        }
    }

    private void Bind(Choice choice)
    {
        Unbind();
        _current = choice;
        Reset();
        _openFile.Enabled = choice.Source.FileDir is not null;

        if (choice.Source.Buffer is { } buffer)
        {
            _boundBuffer = buffer;
            foreach (var line in buffer.Snapshot()) Enqueue(line);
            buffer.LineAppended += Enqueue;
            buffer.Cleared += OnBufferCleared;
        }
        else if (choice.Source.FileDir is { } dir && choice.Source.FilePattern is { } pattern)
        {
            _tailer = new FileTailer(dir, pattern, Enqueue);
        }
    }

    private void Unbind()
    {
        if (_boundBuffer is not null)
        {
            _boundBuffer.LineAppended -= Enqueue;
            _boundBuffer.Cleared -= OnBufferCleared;
            _boundBuffer = null;
        }
        _tailer?.Dispose();
        _tailer = null;
    }

    private void OnBufferCleared() => BeginInvoke(new Action(Reset));

    private void Reset()
    {
        lock (_gate) _pending.Clear();
        _text.Clear();
        _lineCount = 0;
    }

    private void Enqueue(string line)
    {
        lock (_gate)
        {
            _pending.Enqueue(line);
            while (_pending.Count > MaxLines) _pending.Dequeue();
        }
    }

    private void Flush()
    {
        string[] lines;
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            lines = _pending.ToArray();
            _pending.Clear();
        }

        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l).Append("\r\n");
        _text.AppendText(sb.ToString());
        _lineCount += lines.Length;

        if (_lineCount > MaxLines)
        {
            // Drop the oldest third in one go instead of one line per tick.
            var all = _text.Lines;
            var keep = all.Skip(all.Length - MaxLines * 2 / 3).ToArray();
            _text.Lines = keep;
            _lineCount = keep.Length;
        }
        if (_autoScroll.Checked)
        {
            _text.SelectionStart = _text.TextLength;
            _text.ScrollToCaret();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _flush.Dispose();
            Unbind();
        }
        base.Dispose(disposing);
    }
}
