using System.Text;
using System.Text.RegularExpressions;

namespace Inkspire.Panel.Runtime;

/// <summary>A Laravel <c>.env</c> file that keeps comments, blank lines and the order of lines the panel does not touch.
/// Lines are only rewritten when <see cref="Set"/> changes their value.</summary>
public sealed class EnvFile
{
    private sealed class Line
    {
        public string Raw = "";
        public string? Key;
        public string? Value;
    }

    private static readonly Regex Assignment = new(@"^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_.]*)\s*=(.*)$", RegexOptions.Compiled);
    private static readonly Regex SafeUnquoted = new(@"^[A-Za-z0-9_./:@+,\-]+$", RegexOptions.Compiled);

    private readonly List<Line> _lines = new();

    public static EnvFile Empty() => new();

    public static EnvFile Load(string path) => Parse(File.ReadAllText(path));

    public static EnvFile Parse(string text)
    {
        var env = new EnvFile();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1); // trailing newline
        foreach (var raw in lines)
        {
            var m = Assignment.Match(raw);
            env._lines.Add(m.Success
                ? new Line { Raw = raw, Key = m.Groups[1].Value, Value = Decode(m.Groups[2].Value) }
                : new Line { Raw = raw });
        }
        return env;
    }

    public IEnumerable<string> Keys => _lines.Where(l => l.Key is not null).Select(l => l.Key!).Distinct();

    public IEnumerable<(string Key, string Value)> Entries() =>
        _lines.Where(l => l.Key is not null).Select(l => (l.Key!, l.Value ?? ""));

    public bool Has(string key) => _lines.Any(l => l.Key == key);

    /// <summary>The effective value (first definition wins, like phpdotenv), or null when absent.</summary>
    public string? Get(string key) => _lines.FirstOrDefault(l => l.Key == key)?.Value;

    /// <summary>Sets <paramref name="key"/>; <c>null</c> removes it. Existing lines keep their position.</summary>
    public EnvFile Set(string key, string? value)
    {
        if (value is null)
        {
            _lines.RemoveAll(l => l.Key == key);
            return this;
        }
        var existing = _lines.FirstOrDefault(l => l.Key == key);
        if (existing is not null)
        {
            if (existing.Value == value) return this; // untouched: keeps the user's own quoting
            existing.Value = value;
            existing.Raw = $"{key}={Format(value)}";
        }
        else
        {
            _lines.Add(new Line { Raw = $"{key}={Format(value)}", Key = key, Value = value });
        }
        return this;
    }

    public EnvFile SetAll(IEnumerable<KeyValuePair<string, string?>> values)
    {
        foreach (var (k, v) in values) Set(k, v);
        return this;
    }

    public string Build() => string.Join("\n", _lines.Select(l => l.Raw)) + "\n";

    public void Save(string path) => File.WriteAllText(path, Build(), new UTF8Encoding(false));

    /// <summary>How a value is written: bare when plainly safe, single-quoted (literal) when it only needs protecting from
    /// spaces/#/$, double-quoted with escapes when it holds backslashes, quotes or newlines.</summary>
    public static string Format(string value)
    {
        if (value.Length == 0) return "";
        if (SafeUnquoted.IsMatch(value)) return value;
        if (value.IndexOfAny(new[] { '\\', '\'', '\n', '\r' }) < 0) return "'" + value + "'";
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '$' => "\\$",
                '\n' => "\\n",
                '\r' => "\\r",
                _ => c.ToString(),
            });
        }
        return sb.Append('"').ToString();
    }

    private static string Decode(string raw)
    {
        var v = raw.Trim();
        if (v.Length >= 2 && v[0] == '"')
        {
            var sb = new StringBuilder();
            for (var i = 1; i < v.Length; i++)
            {
                var c = v[i];
                if (c == '\\' && i + 1 < v.Length)
                {
                    var n = v[++i];
                    sb.Append(n switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => n });
                }
                else if (c == '"')
                {
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
        if (v.Length >= 2 && v[0] == '\'')
        {
            var end = v.IndexOf('\'', 1);
            return end < 0 ? v[1..] : v[1..end];
        }
        var comment = Regex.Match(v, @"\s#");
        if (comment.Success) v = v[..comment.Index];
        return v.Trim();
    }
}
