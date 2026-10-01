using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inkspire.Panel.Config;

/// <summary>Reads and atomically writes <c>config\panel.json</c>.</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();

    /// <summary>A deep copy (the Settings dialog edits a copy and compares it with the live config).</summary>
    public static PanelConfig Clone(PanelConfig config) =>
        JsonSerializer.Deserialize<PanelConfig>(JsonSerializer.Serialize(config, Options), Options)!;

    public ConfigStore(string path)
    {
        Path = path;
    }

    public string Path { get; }
    public bool Exists => File.Exists(Path);

    public PanelConfig Load()
    {
        lock (_gate)
        {
            var json = File.ReadAllText(Path, Encoding.UTF8);
            var config = JsonSerializer.Deserialize<PanelConfig>(json, Options)
                         ?? throw new InvalidDataException("panel.json is empty.");
            if (config.SchemaVersion > PanelConfig.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"panel.json was written by a newer panel (schema {config.SchemaVersion}); update the panel first.");
            }
            config.Normalize();
            return config;
        }
    }

    public void Save(PanelConfig config)
    {
        lock (_gate)
        {
            var dir = System.IO.Path.GetDirectoryName(Path)!;
            Directory.CreateDirectory(dir);
            config.SchemaVersion = PanelConfig.CurrentSchemaVersion;
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options), new UTF8Encoding(false));
            if (File.Exists(Path)) File.Replace(tmp, Path, null);
            else File.Move(tmp, Path);
        }
    }
}
