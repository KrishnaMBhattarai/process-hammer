namespace ProcessBooster.Core.Models;

/// <summary>
/// Root persisted configuration: the rule set plus engine settings.
/// <see cref="SchemaVersion"/> lets us migrate old configs safely on import.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Config file schema version (independent of the app version).</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public const int CurrentSchemaVersion = 1;

    /// <summary>How often (seconds) the engine re-scans processes and re-applies rules.</summary>
    public int PollSeconds { get; set; } = 4;

    /// <summary>Restore the previous power plan when no rule process is running.</summary>
    public bool RestorePowerPlan { get; set; } = true;

    public List<ProcessRule> Rules { get; set; } = new();

    public AppConfig Clone()
    {
        // Deep-clone via serialization keeps this simple and correct as the model grows.
        var json = Config.ConfigStore.Serialize(this);
        return Config.ConfigStore.Deserialize(json);
    }
}
