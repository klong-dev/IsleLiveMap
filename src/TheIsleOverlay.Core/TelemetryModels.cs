namespace TheIsleOverlay.Core;

public sealed record TelemetrySnapshot
{
    public string Source { get; init; } = "Unknown";
    public bool Success { get; init; }
    public bool ServerOnline { get; init; }
    public bool PlayerOnline { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public PlayerTelemetry? Player { get; init; }
    public MapTelemetry? Map { get; init; }
    public TelemetrySessionState SessionState { get; init; } = TelemetrySessionState.Polling;
    public bool LiveDataStale { get; init; }
    public string? StatusMessage { get; init; }
    public bool ProPlayerTrackingActive { get; init; }
}

public sealed record PlayerTelemetry
{
    public string? SteamId { get; init; }
    public string? Name { get; init; }
    public string? Class { get; init; }
    public string? Server { get; init; }
    public bool? Female { get; init; }
    public double? GrowthPercent { get; init; }
    public double? HealthPercent { get; init; }
    public double? StaminaPercent { get; init; }
    public double? HungerPercent { get; init; }
    public double? ThirstPercent { get; init; }
    public ExactVitals? ExactVitals { get; init; }
    public string? ExactVitalsSource { get; init; }
    public NutritionTelemetry? Nutrition { get; init; }
    public WorldLocation? Location { get; init; }
    public MapPoint? MapLocation { get; init; }
    public double? ExactMapHeadingDegrees { get; init; }
    public PrimeTelemetry? Prime { get; init; }
}

public sealed record ExactVitals
{
    public double? Growth { get; init; }
    public double? Health { get; init; }
    public double? MaxHealth { get; init; }
    public double? Stamina { get; init; }
    public double? MaxStamina { get; init; }
    public double? Hunger { get; init; }
    public double? MaxHunger { get; init; }
    public double? FoodValue { get; init; }
    public double? MaxFoodValue { get; init; }
    public double? Thirst { get; init; }
    public double? MaxThirst { get; init; }
}

public sealed record WorldLocation
{
    public double X { get; init; }
    public double Y { get; init; }
    public double? Z { get; init; }
}

public sealed record PrimeTelemetry
{
    public bool? IsPrime { get; init; }
    public double? Progress { get; init; }
    public bool? Elder { get; init; }
    public bool? Eligible { get; init; }
    public int? Done { get; init; }
    public int? Required { get; init; }
    public IReadOnlyList<PrimeQuestTelemetry> Quests { get; init; } = [];
}
