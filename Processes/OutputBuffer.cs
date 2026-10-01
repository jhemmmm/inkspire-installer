namespace Inkspire.Panel.Processes;

/// <summary>Bounded, thread-safe ring of console lines for the log viewer. Producers never block on the UI.</summary>
public sealed class OutputBuffer
{
    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private readonly int _capacity;

    public OutputBuffer(int capacity = 5000)
    {
        _capacity = capacity;
    }

    public event Action<string>? LineAppended;
    public event Action? Cleared;

    public void Append(string line)
    {
        lock (_gate)
        {
            _lines.Enqueue(line);
            while (_lines.Count > _capacity) _lines.Dequeue();
        }
        LineAppended?.Invoke(line);
    }

    public string[] Snapshot()
    {
        lock (_gate) return _lines.ToArray();
    }

    public void Clear()
    {
        lock (_gate) _lines.Clear();
        Cleared?.Invoke();
    }
}
