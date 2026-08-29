using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

public readonly record struct LocalDinosaurVitalsObservation(
    DateTimeOffset ObservedAt,
    ExactVitals Vitals,
    ulong NetRefHandle);
