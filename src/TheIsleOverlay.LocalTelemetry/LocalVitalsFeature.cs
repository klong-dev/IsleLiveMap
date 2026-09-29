namespace TheIsleOverlay.LocalTelemetry;

/// <summary>
/// Inbound stats ship enabled for the standard map. Set the replacement
/// environment variable to 0 for a local diagnostic rollback.
/// </summary>
public static class LocalVitalsFeature
{
    public const string EnvironmentVariable = "ISLELIVEMAP_LOCAL_VITALS_CANARY";
    public const string SourceName = "LocalIris";
    public const string ReplaceIslePilotEnvironmentVariable = "ISLELIVEMAP_INBOUND_STATS_REPLACE_ISLEPILOT";

    public static bool ReplacesIslePilot() => ReplacementEnabled(
        Environment.GetEnvironmentVariable(ReplaceIslePilotEnvironmentVariable));

    internal static bool ReplacementEnabled(string? value) => value is null || IsEnabled(value);

    public static bool IsEnabled() => ReplacesIslePilot() || IsEnabled(
        Environment.GetEnvironmentVariable(EnvironmentVariable));

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
