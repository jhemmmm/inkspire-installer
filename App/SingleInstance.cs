namespace Inkspire.Panel.App;

/// <summary>One panel per Windows session. A second launch asks the first to show its window and exits.</summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\InkspirePanel.SingleInstance";
    private const string ShowEventName = @"Local\InkspirePanel.Show";

    private readonly Mutex _mutex;
    private EventWaitHandle? _showEvent;
    private Thread? _listener;
    private volatile bool _stop;
    private bool _released;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>Null when another instance already owns the mutex.</summary>
    public static SingleInstance? TryAcquire()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
            if (created) return new SingleInstance(mutex);
            mutex.Dispose();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void SignalShow()
    {
        try
        {
            using var e = EventWaitHandle.OpenExisting(ShowEventName);
            e.Set();
        }
        catch
        {
            // first instance may still be starting; nothing else to do
        }
    }

    /// <summary>Runs <paramref name="onShow"/> (on a background thread) whenever a second launch signals.</summary>
    public void ListenForShow(Action onShow)
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _listener = new Thread(() =>
        {
            while (!_stop)
            {
                try
                {
                    if (_showEvent.WaitOne(500) && !_stop) onShow();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        })
        { IsBackground = true, Name = "single-instance-listener" };
        _listener.Start();
    }

    /// <summary>Must run on the thread that acquired the mutex (the UI thread).</summary>
    public void Release()
    {
        if (_released) return;
        _released = true;
        _stop = true;
        try { _mutex.ReleaseMutex(); } catch { }
    }

    public void Dispose()
    {
        Release();
        _showEvent?.Dispose();
        _mutex.Dispose();
    }
}
