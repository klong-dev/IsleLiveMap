using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

public readonly record struct LocalDinosaurVitalsObservation(
    DateTimeOffset ObservedAt,
    ExactVitals Vitals,
    ulong NetRefHandle)
{
    // Only populated by the opt-in in-map decoder. Each field keeps its own
    // wire timestamp; a heartbeat must not freshen another field.
    public IReadOnlyList<VitalsFieldEvidence>? ExperimentalEvidence { get; init; }
}
