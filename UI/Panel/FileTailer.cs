using System.Text;

namespace Inkspire.Panel.UI.Panel;

/// <summary>Follows the newest file matching a pattern in a folder, like <c>tail -f</c>: starts with the last chunk,
/// then reports appended lines. Survives rotation (new newest file) and truncation.</summary>
public sealed class FileTailer : IDisposable
{
    private readonly string _dir;
    private readonly string _pattern;
    private readonly Action<string> _onLine;
    private readonly long _initialBytes;
    private readonly System.Threading.Timer _timer;
    private string? _file;
    private long _position;
    private string _partial = "";
    private int _running;

    public FileTailer(string dir, string pattern, Action<string> onLine, long initialBytes = 64 * 1024)
    {
        _dir = dir;
        _pattern = pattern;
        _onLine = onLine;
        _initialBytes = initialBytes;
        _timer = new System.Threading.Timer(_ => Tick(), null, 0, 500);
    }

    public string? CurrentFile => _file;

    private void Tick()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            if (!Directory.Exists(_dir)) return;
            var newest = new DirectoryInfo(_dir).GetFiles(_pattern).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (newest is null) return;

            var first = false;
            if (_file != newest.FullName)
            {
                _file = newest.FullName;
                _position = Math.Max(0, newest.Length - _initialBytes);
                _partial = "";
                first = _position > 0;
            }
            if (newest.Length < _position) _position = 0; // truncated
            if (newest.Length == _position) return;

            using var fs = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fs.Seek(_position, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8, false, 4096, leaveOpen: true);
            var text = _partial + reader.ReadToEnd();
            _position = fs.Position;

            var lines = text.Split('\n');
            _partial = lines[^1];
            for (var i = 0; i < lines.Length - 1; i++)
            {
                if (first && i == 0) continue; // we started mid-line
                _onLine(lines[i].TrimEnd('\r'));
            }
        }
        catch (IOException)
        {
            // file locked or rotating: try again next tick
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    public void Dispose() => _timer.Dispose();
}
