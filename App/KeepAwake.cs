using System.Runtime.InteropServices;

namespace Inkspire.Panel.App;

/// <summary>Stops Windows from going to sleep while the server runs (the display may still turn off). Does not touch the
/// power plan. <see cref="Set"/> must always be called from the same thread (the UI thread): the flag lives and dies
/// with the calling thread.</summary>
public static class KeepAwake
{
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private static bool _on;

    public static void Set(bool on)
    {
        if (on == _on) return;
        _on = on;
        SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS);
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
