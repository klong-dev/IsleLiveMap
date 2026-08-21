namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotReconnectBackoff
{
    private static readonly double[] DelaySeconds = [1d, 2d, 4d, 8d, 15d];

    private readonly Func<double> _nextUnitInterval;
    private int _index;

    public IslePilotReconnectBackoff()
        : this(Random.Shared.NextDouble)
    {
    }

    public IslePilotReconnectBackoff(Func<double> nextUnitInterval)
    {
        _nextUnitInterval = nextUnitInterval ?? throw new ArgumentNullException(nameof(nextUnitInterval));
    }

    public TimeSpan NextDelay()
    {
        var baseSeconds = DelaySeconds[Math.Min(_index, DelaySeconds.Length - 1)];
        _index++;

        var sample = Math.Clamp(_nextUnitInterval(), 0d, 1d);
        var jitterMultiplier = 0.8d + sample * 0.4d;
        return TimeSpan.FromSeconds(baseSeconds * jitterMultiplier);
    }

    public void Reset() => _index = 0;
}
