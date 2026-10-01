using System.Reflection;
using System.Text.RegularExpressions;

namespace Inkspire.Panel.Runtime;

/// <summary>Replaces <c>{{KEY}}</c> placeholders in an embedded resource. A placeholder without a value is an error,
/// so a typo can never produce a silently broken nginx.conf.</summary>
public static class TemplateEngine
{
    private const string ResourcePrefix = "Inkspire.Panel.Runtime.Resources.";
    private static readonly Regex Placeholder = new(@"\{\{([A-Z0-9_]+)\}\}", RegexOptions.Compiled);

    public static string ReadResource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(ResourcePrefix + name)
                           ?? throw new InvalidOperationException($"Embedded resource '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string RenderResource(string name, IReadOnlyDictionary<string, string> values) =>
        Render(ReadResource(name), values);

    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        return Placeholder.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            return values.TryGetValue(key, out var v)
                ? v
                : throw new InvalidOperationException($"Template placeholder {{{{{key}}}}} has no value.");
        });
    }
}
