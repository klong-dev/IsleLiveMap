namespace TheIsleOverlay.Core;

public sealed record NutritionTelemetry
{
    public double? Carb { get; init; }
    public double? Protein { get; init; }
    public double? Lipid { get; init; }
}
