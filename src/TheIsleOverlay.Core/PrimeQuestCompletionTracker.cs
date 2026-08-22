namespace TheIsleOverlay.Core;

public sealed class PrimeQuestCompletionTracker
{
    private Dictionary<string, bool?>? _previous;

    public IReadOnlyList<PrimeQuestTelemetry> Capture(
        IReadOnlyList<PrimeQuestTelemetry>? quests)
    {
        var current = (quests ?? [])
            .Where(quest => !string.IsNullOrWhiteSpace(quest.Name))
            .GroupBy(quest => quest.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Done,
                StringComparer.OrdinalIgnoreCase);

        if (_previous is null)
        {
            _previous = current;
            return [];
        }

        var completed = current
            .Where(pair => pair.Value == true &&
                           _previous.TryGetValue(pair.Key, out var wasDone) &&
                           wasDone == false)
            .Select(pair => new PrimeQuestTelemetry { Name = pair.Key, Done = true })
            .ToArray();
        _previous = current;
        return completed;
    }

    public void Reset() => _previous = null;
}
