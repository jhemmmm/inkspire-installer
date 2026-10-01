using System.Text;
using System.Text.RegularExpressions;

namespace Inkspire.Panel.Runtime;

/// <summary>Turns <c>php.ini-production</c> into our <c>php.ini</c>: sets directives and enables extensions in place (so the
/// file stays recognisable) and appends what the template lacks under a marker.</summary>
public sealed class PhpIniPatcher
{
    private readonly List<string> _lines;
    private readonly List<string> _appended = new();

    public PhpIniPatcher(string iniText)
    {
        _lines = iniText.Replace("\r\n", "\n").Split('\n').ToList();
        // A trailing newline must not turn into an extra blank line every time the file is rebuilt.
        if (_lines.Count > 0 && _lines[^1].Length == 0) _lines.RemoveAt(_lines.Count - 1);
    }

    public static PhpIniPatcher FromFile(string path) => new(File.ReadAllText(path));

    /// <summary>Sets <c>key = value</c>, replacing the first (possibly commented-out) occurrence.</summary>
    public PhpIniPatcher Set(string key, string value)
    {
        var rx = new Regex(@"^\s*;?\s*" + Regex.Escape(key) + @"\s*=", RegexOptions.IgnoreCase);
        var line = $"{key} = {value}";
        for (var i = 0; i < _lines.Count; i++)
        {
            if (!rx.IsMatch(_lines[i]) || IsExtensionLine(_lines[i])) continue;
            _lines[i] = line;
            return this;
        }
        _appended.Add(line);
        return this;
    }

    public PhpIniPatcher EnableExtension(string name) => EnableLine("extension", name);

    public PhpIniPatcher EnableZendExtension(string name) => EnableLine("zend_extension", name);

    private PhpIniPatcher EnableLine(string directive, string name)
    {
        var rx = new Regex(@"^\s*;?\s*" + directive + @"\s*=\s*""?(php_)?" + Regex.Escape(name) + @"(\.dll)?""?\s*$", RegexOptions.IgnoreCase);
        var line = $"{directive}={name}";
        for (var i = 0; i < _lines.Count; i++)
        {
            if (!rx.IsMatch(_lines[i])) continue;
            _lines[i] = line;
            return this;
        }
        if (!_appended.Contains(line)) _appended.Add(line);
        return this;
    }

    private static bool IsExtensionLine(string line) =>
        Regex.IsMatch(line, @"^\s*;?\s*(zend_)?extension\s*=", RegexOptions.IgnoreCase);

    public string Build()
    {
        var sb = new StringBuilder();
        foreach (var l in _lines) sb.Append(l).Append('\n');
        if (_appended.Count > 0)
        {
            sb.Append("\n; ---- Added by Inkspire Control Panel ----\n");
            foreach (var l in _appended) sb.Append(l).Append('\n');
        }
        return sb.ToString();
    }

    public void Save(string path) => File.WriteAllText(path, Build(), new UTF8Encoding(false));
}
