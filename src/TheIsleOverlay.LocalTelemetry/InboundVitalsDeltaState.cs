namespace TheIsleOverlay.LocalTelemetry;

/// <summary>Last received fields, scoped to one flow and one actor. Absence is not deletion.</summary>
public sealed class InboundVitalsDeltaState
{
    public const double ProtocolMaxWater = 1000d;
    private readonly Dictionary<string, VitalsFieldEvidence> _fields = [];
    public string? Flow { get; private set; }
    public ulong? Owner { get; private set; }

    public IReadOnlyList<VitalsFieldEvidence> Apply(string flow, ulong owner, IEnumerable<VitalsFieldEvidence> updates)
    {
        if (Owner != owner || Flow != flow) { Clear(); Owner = owner; Flow = flow; }
        foreach (var field in updates)
        {
            if (field.Flow != flow || field.Owner != owner || !double.IsFinite(field.Value) || field.Value < 0) continue;
            if (_fields.TryGetValue(field.Field, out var existing) && field.ObservedAt <= existing.ObservedAt) continue;
            _fields[field.Field] = field;
        }
        return _fields.Values.ToArray();
    }
    public void Clear() { _fields.Clear(); Flow = null; Owner = null; }
}
