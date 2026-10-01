namespace Inkspire.Panel.App;

/// <summary>Marshals work onto the WinForms UI thread from service/supervisor threads.</summary>
public static class UiThread
{
    private static SynchronizationContext? _context;

    /// <summary>Call once from the UI thread after the message loop's context exists.</summary>
    public static void Capture() => _context = SynchronizationContext.Current;

    public static void Post(Action action)
    {
        var ctx = _context;
        if (ctx is null) action();
        else ctx.Post(_ =>
        {
            try { action(); }
            catch (Exception ex) { Log.Error("UI callback failed", ex); }
        }, null);
    }
}
