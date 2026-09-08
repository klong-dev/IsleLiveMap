using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

internal static class OverlayRenderWorkPolicy
{
    public static bool HeadingChanged(double? previousTarget, double nextTarget) =>
        double.IsFinite(nextTarget)
        && (previousTarget is null
            || Math.Abs((MapHeading.Normalize(nextTarget)
                         - MapHeading.Normalize(previousTarget.Value) + 540d) % 360d - 180d) > 0.01d);
}

/// <summary>Keeps WPF ItemsSource stable when only the GPS changes.</summary>
internal sealed class MissionListRenderCache
{
    private PrimeQuestTelemetry[] _rendered = [];

    public bool Update(IReadOnlyList<PrimeQuestTelemetry> quests)
    {
        if (_rendered.SequenceEqual(quests))
            return false;

        _rendered = quests.ToArray();
        return true;
    }

    public void Reset() => _rendered = [];
}
