using System.Text.Json;
using System.Text.Json.Serialization;
using ProcessHammer.Core.Models;

namespace ProcessHammer.Core.Config;

/// <summary>
/// Loads/saves <see cref="AppConfig"/> as JSON, and imports/exports config files. Enums are written
/// as names so files stay human-readable and stable across enum reordering.
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; }

    public ConfigStore(string filePath) => FilePath = filePath;

    /// <summary>Default location: %LOCALAPPDATA%\ProcessHammer\config.json.</summary>
    public static string DefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProcessHammer");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    public AppConfig Load()
    {
        if (!File.Exists(FilePath)) return new AppConfig();
        return Deserialize(File.ReadAllText(FilePath));
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, Serialize(config));
    }

    /// <summary>Read a config from an arbitrary path (Import). Throws on malformed/incompatible files.</summary>
    public static AppConfig Import(string path) => Deserialize(File.ReadAllText(path));

    /// <summary>Write a config to an arbitrary path (Export).</summary>
    public static void Export(AppConfig config, string path) => File.WriteAllText(path, Serialize(config));

    public static string Serialize(AppConfig config) => JsonSerializer.Serialize(config, Options);

    public static AppConfig Deserialize(string json)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(json, Options)
            ?? throw new InvalidDataException("Config JSON deserialized to null.");
        if (config.SchemaVersion > AppConfig.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Config schema v{config.SchemaVersion} is newer than supported v{AppConfig.CurrentSchemaVersion}. Update Process Hammer.");
        return Migrate(config);
    }

    /// <summary>Hook for future schema migrations. Today all supported versions load as-is.</summary>
    private static AppConfig Migrate(AppConfig config)
    {
        config.SchemaVersion = AppConfig.CurrentSchemaVersion;
        return config;
    }
}
