namespace TheIsleOverlay.LocalTelemetry;

/// <summary>
/// Production gate for passive inbound Iris vitals. Capture/probe code can opt
/// in explicitly while the default remains disabled until live fixtures have
/// validated each supported dinosaur and lifecycle transition.
/// </summary>
public static class LocalVitalsFeature
{
    public const string EnvironmentVariable = "ISLELIVEMAP_LOCAL_VITALS_CANARY";
    public const string SourceName = "LocalIris";
    public const string ReplaceIslePilotEnvironmentVariable = "ISLELIVEMAP_INBOUND_STATS_REPLACE_ISLEPILOT";

    public static bool ReplacesIslePilot() => IsEnabled(
        Environment.GetEnvironmentVariable(ReplaceIslePilotEnvironmentVariable));

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
