using System.Diagnostics.CodeAnalysis;

namespace TheIsleOverlay.App;

internal sealed class LatestValueBuffer<T>
    where T : class
{
    private readonly object _gate = new();
    private T? _latest;

    public void Publish(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            _latest = value;
        }
    }

    public bool TryTake([NotNullWhen(true)] out T? value)
    {
        lock (_gate)
        {
            value = _latest;
            _latest = null;
            return value is not null;
        }
    }
}
