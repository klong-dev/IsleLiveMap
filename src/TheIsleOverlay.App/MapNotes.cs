using System.IO;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public enum MapNoteKind
{
    Pin,
    Rally,
    Water,
    Meat,
    Plant,
    Nest,
    Danger,
    Sighting
}

public sealed record MapNote
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string MapId { get; init; } = MapNoteStore.GatewayMapId;
    public double U { get; init; }
    public double V { get; init; }
    public double WorldX { get; init; }
    public double WorldY { get; init; }
    public MapNoteKind Kind { get; init; } = MapNoteKind.Pin;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class MapNoteStore
{
    public const string GatewayMapId = "gateway";
    public const int MaximumNotes = 50;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly List<MapNote> _notes;

    public MapNoteStore(string? path = null)
    {
        var overridePath = Environment.GetEnvironmentVariable("ISLELIVEMAP_MAP_NOTES_PATH");
        _path = Path.GetFullPath(path
            ?? (string.IsNullOrWhiteSpace(overridePath) ? AppPaths.MapNotes : overridePath));
        _notes = LoadFromDisk(_path).ToList();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<MapNote> Notes => _notes;

    public MapNote AddDefault(double u, double v)
    {
        var point = NormalizePoint(u, v);
        var world = GatewayMapProjection.Unproject(point);
        var note = new MapNote
        {
            U = point.Left,
            V = point.Top,
            WorldX = world.X,
            WorldY = world.Y
        };
        if (_notes.Count >= MaximumNotes)
        {
            _notes.RemoveAt(0);
        }
        _notes.Add(note);
        SaveAndNotify();
        return note;
    }

    public bool ChangeKind(Guid id, MapNoteKind kind)
    {
        var index = _notes.FindIndex(note => note.Id == id);
        if (index < 0 || !Enum.IsDefined(kind))
        {
            return false;
        }

        _notes[index] = _notes[index] with { Kind = kind };
        SaveAndNotify();
        return true;
    }

    public bool Delete(Guid id)
    {
        var removed = _notes.RemoveAll(note => note.Id == id) > 0;
        if (removed)
        {
            SaveAndNotify();
        }
        return removed;
    }

    private void SaveAndNotify()
    {
        SaveToDisk();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SaveToDisk()
    {
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Map-note path has no parent directory.");
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_notes, JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
        }
        catch
        {
            // Notes remain available in memory if Windows temporarily blocks persistence.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); } catch { }
            }
        }
    }

    private static IEnumerable<MapNote> LoadFromDisk(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            return (JsonSerializer.Deserialize<List<MapNote>>(File.ReadAllText(path), JsonOptions) ?? [])
                .Where(IsValid)
                .TakeLast(MaximumNotes)
                .Select(Normalize)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsValid(MapNote note) =>
        note.Id != Guid.Empty
        && string.Equals(note.MapId, GatewayMapId, StringComparison.OrdinalIgnoreCase)
        && double.IsFinite(note.U)
        && double.IsFinite(note.V)
        && Enum.IsDefined(note.Kind);

    private static MapNote Normalize(MapNote note)
    {
        var point = NormalizePoint(note.U, note.V);
        var world = GatewayMapProjection.Unproject(point);
        return note with
        {
            MapId = GatewayMapId,
            U = point.Left,
            V = point.Top,
            WorldX = world.X,
            WorldY = world.Y
        };
    }

    private static MapPoint NormalizePoint(double u, double v) => new(
        Math.Clamp(double.IsFinite(u) ? u : 0.5d, 0d, 1d),
        Math.Clamp(double.IsFinite(v) ? v : 0.5d, 0d, 1d));
}
