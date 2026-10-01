namespace Inkspire.Panel.App;

/// <summary>Last-resort handlers: log the exception and tell the user instead of vanishing.</summary>
public static class GlobalExceptionHandler
{
    private static int _shown;

    public static void Install()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    private static void Report(Exception? ex)
    {
        Log.Error("Unhandled exception", ex);
        // One dialog per run: a failure loop must not stack up message boxes.
        if (Interlocked.Exchange(ref _shown, 1) == 1) return;
        try
        {
            MessageBox.Show(
                $"Something unexpected went wrong:\n\n{ex?.Message}\n\nDetails were written to:\n{Log.CurrentFile}",
                "Inkspire Control Panel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _shown, 0);
        }
    }
}
