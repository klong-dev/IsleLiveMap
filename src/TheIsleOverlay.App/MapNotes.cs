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
    Sighting,
    LastKnown,
    Death
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
    public string? ServerKey { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool IsPersonalHistory => Kind is MapNoteKind.LastKnown or MapNoteKind.Death;
}

public sealed record MapNoteMutationResult(
    bool Success,
    MapNote? Note = null,
    string? Error = null)
{
    public static MapNoteMutationResult Succeeded(MapNote? note = null) => new(true, note);
    public static MapNoteMutationResult Failed(string error) => new(false, null, error);
}

public sealed partial class MapNoteStore
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
        var result = TryAddDefault(u, v);
        if (!result.Success)
        {
            throw new IOException(result.Error ?? "Không thể lưu mốc.");
        }
        return result.Note!;
    }

    public MapNoteMutationResult TryAddDefault(double u, double v)
    {
        var previousNotes = _notes.ToArray();
        var point = NormalizePoint(u, v);
        var world = GatewayMapProjection.Unproject(point);
        var note = new MapNote
        {
            U = point.Left,
            V = point.Top,
            WorldX = world.X,
            WorldY = world.Y
        };
        if (_notes.Count(n => !n.IsPersonalHistory) >= MaximumNotes)
        {
            _notes.RemoveAt(_notes.FindIndex(n => !n.IsPersonalHistory));
        }
        _notes.Add(note);
        if (!TrySaveAndNotify(out var error))
        {
            _notes.Clear();
            _notes.AddRange(previousNotes);
            return MapNoteMutationResult.Failed(error!);
        }
        return MapNoteMutationResult.Succeeded(note);
    }

    public bool ChangeKind(Guid id, MapNoteKind kind)
    {
        return TryChangeKind(id, kind).Success;
    }

    public MapNoteMutationResult TryChangeKind(Guid id, MapNoteKind kind)
    {
        var index = _notes.FindIndex(note => note.Id == id);
        if (index < 0 || !Enum.IsDefined(kind))
        {
            return MapNoteMutationResult.Failed("Không tìm thấy mốc cần sửa.");
        }

        var previous = _notes[index];
        if (previous.IsPersonalHistory
            ? previous.Kind != MapNoteKind.LastKnown || kind != MapNoteKind.Death
            : kind is MapNoteKind.LastKnown or MapNoteKind.Death)
            return MapNoteMutationResult.Failed("Mốc lịch sử chỉ cho phép xác nhận chết hoặc xóa.");
        _notes[index] = _notes[index] with { Kind = kind };
        if (!TrySaveAndNotify(out var error))
        {
            _notes[index] = previous;
            return MapNoteMutationResult.Failed(error!);
        }
        return MapNoteMutationResult.Succeeded(_notes[index]);
    }

    public bool Delete(Guid id)
    {
        return TryDelete(id).Success;
    }

    public MapNoteMutationResult TryDelete(Guid id)
    {
        var index = _notes.FindIndex(note => note.Id == id);
        if (index < 0)
        {
            return MapNoteMutationResult.Failed("Không tìm thấy mốc cần xóa.");
        }

        var removed = _notes[index];
        _notes.RemoveAt(index);
        if (!TrySaveAndNotify(out var error))
        {
            _notes.Insert(index, removed);
            return MapNoteMutationResult.Failed(error!);
        }
        return MapNoteMutationResult.Succeeded(removed);
    }

    private bool TrySaveAndNotify(out string? error)
    {
        if (!TrySaveToDisk(out error))
        {
            return false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool TrySaveToDisk(out string? error)
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
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidOperationException)
        {
            error = $"Không thể lưu mốc xuống máy: {exception.Message}";
            return false;
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
                .TakeLast(MaximumNotes + 20)
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
        && Enum.IsDefined(note.Kind)
        && (!note.IsPersonalHistory || (!string.IsNullOrWhiteSpace(note.ServerKey)
            && note.ServerKey.Length <= 160 && double.IsFinite(note.WorldX) && double.IsFinite(note.WorldY)
            && note.ExpiresAt == note.CreatedAt.AddHours(24)));

    private static MapNote Normalize(MapNote note)
    {
        var point = NormalizePoint(note.U, note.V);
        var world = GatewayMapProjection.Unproject(point);
        return note with
        {
            MapId = GatewayMapId,
            U = point.Left,
            V = point.Top,
            WorldX = note.IsPersonalHistory ? note.WorldX : world.X,
            WorldY = note.IsPersonalHistory ? note.WorldY : world.Y
        };
    }

    private static MapPoint NormalizePoint(double u, double v) => new(
        Math.Clamp(double.IsFinite(u) ? u : 0.5d, 0d, 1d),
        Math.Clamp(double.IsFinite(v) ? v : 0.5d, 0d, 1d));
}
