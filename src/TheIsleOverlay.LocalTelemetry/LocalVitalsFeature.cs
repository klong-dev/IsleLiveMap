namespace TheIsleOverlay.LocalTelemetry;

/// <summary>
/// Inbound Iris vitals are the default source for the direct map. IslePilot
/// login is not part of the map startup path anymore.
/// </summary>
public static class LocalVitalsFeature
{
    public const string EnvironmentVariable = "ISLELIVEMAP_LOCAL_VITALS_CANARY";
    public const string SourceName = "LocalIris";
    public const string ReplaceIslePilotEnvironmentVariable = "ISLELIVEMAP_INBOUND_STATS_REPLACE_ISLEPILOT";

    public static bool ReplacesIslePilot() => true;

    public static bool IsEnabled() => true;

    internal static bool IsEnabled(string? value) =>
        value is not null
        && (string.Equals(value.Trim(), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "on", StringComparison.OrdinalIgnoreCase));
}

public readonly record struct LocalVitalsCaptureDiagnostics(
    bool Enabled,
    string Source,
    DateTimeOffset? LastObservationAt,
    long PublishedObservations,
    long SessionResets);
