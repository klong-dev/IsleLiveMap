namespace TheIsleOverlay.LocalTelemetry;

/// <summary>
/// Inbound stats run in the background for the standard map. IslePilot remains
/// the primary provider when it has valid data; the replacement switch is only
/// a diagnostic override.
/// </summary>
public static class LocalVitalsFeature
{
    public const string EnvironmentVariable = "ISLELIVEMAP_LOCAL_VITALS_CANARY";
    public const string SourceName = "LocalIris";
    public const string ReplaceIslePilotEnvironmentVariable = "ISLELIVEMAP_INBOUND_STATS_REPLACE_ISLEPILOT";

    public static bool ReplacesIslePilot() => IsEnabled(
        Environment.GetEnvironmentVariable(ReplaceIslePilotEnvironmentVariable));
    internal static bool IsEnabledForReplacement(string? value) => IsEnabled(value);

    public static bool IsEnabled() => !IsDisabled(
        Environment.GetEnvironmentVariable(ReplaceIslePilotEnvironmentVariable))
        && !IsDisabled(Environment.GetEnvironmentVariable(EnvironmentVariable));

    private static bool IsDisabled(string? value) =>
        string.Equals(value?.Trim(), "0", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "off", StringComparison.OrdinalIgnoreCase);

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
