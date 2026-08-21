using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotOverlayStateReducer
{
    public static readonly TimeSpan LiveDataLifetime = TimeSpan.FromSeconds(4);

    private readonly TimeSpan _liveDataLifetime;
    private IslePilotOverlayMeDto? _me;
    private IslePilotOverlayMapDto? _map;
    private IslePilotOverlayLiveDataDto? _live;
    private IslePilotMapCalibration? _calibration;
    private DateTimeOffset? _lastMeAt;
    private DateTimeOffset? _lastMapAt;
    private DateTimeOffset? _lastLiveAt;
    private TelemetrySessionState _sessionState = TelemetrySessionState.Connecting;

    public IslePilotOverlayStateReducer(TimeSpan? liveDataLifetime = null)
    {
        _liveDataLifetime = liveDataLifetime ?? LiveDataLifetime;
        if (_liveDataLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(liveDataLifetime));
        }
    }

    public string? PersonaName => _me?.PersonaName ?? _me?.Name;

    public void ApplyMe(IslePilotOverlayMeDto me, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(me);

        _me = Merge(_me, me);
        _lastMeAt = receivedAt;
    }

    public void ApplyMap(IslePilotOverlayMapDto map, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(map);

        _map = map;
        _lastMapAt = receivedAt;

        if (map.Calibration is not null)
        {
            _calibration = new IslePilotMapCalibration(map.Calibration);
        }
    }

    public void ApplyLive(IslePilotOverlayLiveDataDto live, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(live);

        _live = Merge(_live, live);
        _lastLiveAt = receivedAt;
        _sessionState = TelemetrySessionState.Live;
    }

    public void SetSessionState(TelemetrySessionState state) => _sessionState = state;

    public TelemetrySnapshot BuildSnapshot(DateTimeOffset now)
    {
        var liveDataStale = _lastLiveAt is not null && now - _lastLiveAt > _liveDataLifetime;
        var sessionState = ResolveSessionState(liveDataStale);
        var selfMarker = FindSelfMarker();
        var playerOnline = ResolvePlayerOnline(selfMarker);
        var player = playerOnline ? BuildPlayer(liveDataStale, selfMarker) : null;

        return new TelemetrySnapshot
        {
            Source = "IslePilot",
            Success = sessionState != TelemetrySessionState.AuthenticationRequired,
            ServerOnline = sessionState is not TelemetrySessionState.AuthenticationRequired and
                not TelemetrySessionState.Stopped,
            PlayerOnline = playerOnline,
            UpdatedAt = LatestTimestamp(),
            Player = player,
            Map = BuildMap(),
            SessionState = sessionState,
            LiveDataStale = liveDataStale,
            StatusMessage = ResolveStatusMessage(sessionState, playerOnline)
        };
    }

    private TelemetrySessionState ResolveSessionState(bool liveDataStale)
    {
        if (_sessionState is TelemetrySessionState.AuthenticationRequired or TelemetrySessionState.Stopped)
        {
            return _sessionState;
        }

        if (_me?.HasData == false)
        {
            return TelemetrySessionState.UnsupportedServer;
        }

        if (_sessionState == TelemetrySessionState.Reconnecting)
        {
            return TelemetrySessionState.Reconnecting;
        }

        if (liveDataStale)
        {
            return TelemetrySessionState.Stale;
        }

        return _lastLiveAt is not null ? TelemetrySessionState.Live : _sessionState;
    }

    private bool ResolvePlayerOnline(IslePilotOverlayMapMarkerDto? selfMarker)
    {
        if (_live?.HasDino == false || _me?.HasData == false)
        {
            return false;
        }

        return _live?.HasDino == true || _me?.Online == true || selfMarker is not null;
    }

    private PlayerTelemetry BuildPlayer(bool liveDataStale, IslePilotOverlayMapMarkerDto? selfMarker)
    {
        var useLivePosition = !liveDataStale && HasLocation(_live?.Position);
        var location = useLivePosition
            ? ToLocation(_live!.Position!)
            : ToLocation(selfMarker) ?? ToLocation(_live?.Position);
        var yaw = useLivePosition ? _live?.Position?.Yaw : selfMarker?.Yaw ?? _live?.Position?.Yaw;
        MapPoint? mapLocation = location is null || _calibration is null
            ? null
            : _calibration.Project(location.X, location.Y);
        double? mapHeading = location is null || yaw is null || _calibration is null
            ? null
            : _calibration.ProjectHeading(location.X, location.Y, yaw.Value);

        var growth = _live?.Growth ?? _me?.Growth;
        var health = _live?.Health ?? _me?.Health;
        var maxHealth = _live?.MaxHealth ?? _me?.MaxHealth;
        var hunger = _live?.Hunger ?? _me?.Hunger;
        var maxHunger = _live?.MaxHunger ?? _me?.MaxHunger;
        var thirst = _live?.Thirst ?? _me?.Thirst;
        var maxThirst = _live?.MaxThirst ?? _me?.MaxThirst;
        var stamina = _live?.Stamina ?? _me?.Stamina;
        var maxStamina = _live?.MaxStamina ?? _me?.MaxStamina;

        return new PlayerTelemetry
        {
            SteamId = _live?.SteamId ?? _me?.SteamId,
            Name = _me?.PersonaName ?? _me?.Name,
            Class = _me?.Species,
            Server = _me?.Server,
            Female = _me?.Female,
            GrowthPercent = FractionToPercent(growth),
            HealthPercent = Percent(health, maxHealth),
            StaminaPercent = Percent(stamina, maxStamina),
            HungerPercent = Percent(hunger, maxHunger),
            ThirstPercent = Percent(thirst, maxThirst),
            ExactVitalsSource = "IslePilotOverlayV2",
            ExactVitals = new ExactVitals
            {
                Growth = growth,
                Health = health,
                MaxHealth = maxHealth,
                Stamina = stamina,
                MaxStamina = maxStamina,
                Hunger = hunger,
                MaxHunger = maxHunger,
                Thirst = thirst,
                MaxThirst = maxThirst
            },
            Nutrition = ToNutrition(Merge(_me?.Nutrition, _live?.Nutrition)),
            Location = location,
            MapLocation = mapLocation,
            ExactMapHeadingDegrees = mapHeading,
            Prime = ToPrime(_me?.Prime)
        };
    }

    private MapTelemetry? BuildMap()
    {
        if (_map is null)
        {
            return null;
        }

        return new MapTelemetry
        {
            Markers = _map.Markers.Select(ToMarker).ToArray(),
            PointsOfInterest = _map.Pois.Select(ToPointOfInterest).ToArray()
        };
    }

    private MapMarkerTelemetry ToMarker(IslePilotOverlayMapMarkerDto marker)
    {
        var location = ToLocation(marker);
        return new MapMarkerTelemetry
        {
            SteamId = marker.SteamId,
            Label = marker.Label,
            Self = marker.Self,
            Location = location,
            MapLocation = location is null || _calibration is null
                ? null
                : _calibration.Project(location.X, location.Y),
            ExactMapHeadingDegrees = location is null || marker.Yaw is null || _calibration is null
                ? null
                : _calibration.ProjectHeading(location.X, location.Y, marker.Yaw.Value),
            Path = marker.Path
                .Where(HasLocation)
                .Select(point => _calibration?.Project(point.X!.Value, point.Y!.Value))
                .Where(point => point is not null)
                .Select(point => point!.Value)
                .ToArray()
        };
    }

    private MapPointOfInterestTelemetry ToPointOfInterest(IslePilotOverlayMapPoiDto poi) => new()
    {
        Id = poi.Id,
        Name = poi.Name,
        CategoryId = poi.CategoryId,
        Points = poi.Points
            .Where(HasLocation)
            .Select(point => _calibration?.Project(point.X!.Value, point.Y!.Value))
            .Where(point => point is not null)
            .Select(point => point!.Value)
            .ToArray()
    };

    private IslePilotOverlayMapMarkerDto? FindSelfMarker()
    {
        if (_map is null)
        {
            return null;
        }

        var steamId = _live?.SteamId ?? _me?.SteamId;
        return _map.Markers.FirstOrDefault(marker => marker.Self) ??
            _map.Markers.FirstOrDefault(marker =>
                steamId is not null && string.Equals(marker.SteamId, steamId, StringComparison.Ordinal));
    }

    private DateTimeOffset? LatestTimestamp()
    {
        var values = new[] { _lastMeAt, _lastMapAt, _lastLiveAt }
            .Where(value => value is not null)
            .Select(value => value!.Value);
        return values.Any() ? values.Max() : null;
    }

    private static string ResolveStatusMessage(TelemetrySessionState state, bool playerOnline) => state switch
    {
        TelemetrySessionState.AuthenticationRequired => "PHIÊN ĐÃ HẾT HẠN",
        TelemetrySessionState.UnsupportedServer => "ISLEPILOT · CHƯA VÀO SERVER HỖ TRỢ",
        TelemetrySessionState.Reconnecting => "ISLEPILOT · RECONNECTING",
        TelemetrySessionState.Stale => "ISLEPILOT · DATA STALE",
        TelemetrySessionState.Live when !playerOnline => "NO ACTIVE DINOSAUR",
        TelemetrySessionState.Live => "ISLEPILOT · LIVE",
        TelemetrySessionState.Connecting => "ISLEPILOT · CONNECTING",
        _ => $"ISLEPILOT · {state.ToString().ToUpperInvariant()}"
    };

    private static WorldLocation? ToLocation(IslePilotOverlayPositionDto? position) =>
        HasLocation(position)
            ? new WorldLocation { X = position!.X!.Value, Y = position.Y!.Value, Z = position.Z }
            : null;

    private static WorldLocation? ToLocation(IslePilotOverlayMapMarkerDto? marker) =>
        marker is not null && marker.X is not null && marker.Y is not null
            ? new WorldLocation { X = marker.X.Value, Y = marker.Y.Value, Z = marker.Z }
            : null;

    private static bool HasLocation(IslePilotOverlayPositionDto? position) =>
        position?.X is not null && position.Y is not null;

    private static bool HasLocation(IslePilotOverlayWorldPointDto point) =>
        point.X is not null && point.Y is not null;

    private static NutritionTelemetry? ToNutrition(IslePilotNutritionDto? nutrition) => nutrition is null
        ? null
        : new NutritionTelemetry
        {
            Carb = nutrition.Carb,
            Protein = nutrition.Protein,
            Lipid = nutrition.Lipid
        };

    private static PrimeTelemetry? ToPrime(IslePilotPrimeDto? prime) => prime is null
        ? null
        : new PrimeTelemetry
        {
            IsPrime = prime.Elder,
            Progress = Percent(prime.Done, prime.Required),
            Elder = prime.Elder,
            Eligible = prime.Eligible,
            Done = prime.Done,
            Required = prime.Required,
            Quests = prime.Quests.Select(quest => new PrimeQuestTelemetry
            {
                Name = quest.Name,
                Done = quest.Done
            }).ToArray()
        };

    private static IslePilotOverlayMeDto Merge(IslePilotOverlayMeDto? previous, IslePilotOverlayMeDto current)
    {
        if (previous is null)
        {
            return current;
        }

        return new IslePilotOverlayMeDto
        {
            HasData = current.HasData ?? previous.HasData,
            Online = current.Online ?? previous.Online,
            SteamId = current.SteamId ?? previous.SteamId,
            PersonaName = current.PersonaName ?? previous.PersonaName,
            Name = current.Name ?? previous.Name,
            Species = current.Species ?? previous.Species,
            Server = current.Server ?? previous.Server,
            Female = current.Female ?? previous.Female,
            Growth = current.Growth ?? previous.Growth,
            Health = current.Health ?? previous.Health,
            MaxHealth = current.MaxHealth ?? previous.MaxHealth,
            Hunger = current.Hunger ?? previous.Hunger,
            MaxHunger = current.MaxHunger ?? previous.MaxHunger,
            Thirst = current.Thirst ?? previous.Thirst,
            MaxThirst = current.MaxThirst ?? previous.MaxThirst,
            Stamina = current.Stamina ?? previous.Stamina,
            MaxStamina = current.MaxStamina ?? previous.MaxStamina,
            Nutrition = Merge(previous.Nutrition, current.Nutrition),
            Prime = current.Prime ?? previous.Prime
        };
    }

    private static IslePilotOverlayLiveDataDto Merge(
        IslePilotOverlayLiveDataDto? previous,
        IslePilotOverlayLiveDataDto current)
    {
        if (previous is null)
        {
            return current;
        }

        return new IslePilotOverlayLiveDataDto
        {
            HasDino = current.HasDino ?? previous.HasDino,
            SteamId = current.SteamId ?? previous.SteamId,
            Growth = current.Growth ?? previous.Growth,
            Health = current.Health ?? previous.Health,
            MaxHealth = current.MaxHealth ?? previous.MaxHealth,
            Hunger = current.Hunger ?? previous.Hunger,
            MaxHunger = current.MaxHunger ?? previous.MaxHunger,
            Thirst = current.Thirst ?? previous.Thirst,
            MaxThirst = current.MaxThirst ?? previous.MaxThirst,
            Stamina = current.Stamina ?? previous.Stamina,
            MaxStamina = current.MaxStamina ?? previous.MaxStamina,
            Nutrition = Merge(previous.Nutrition, current.Nutrition),
            Position = Merge(previous.Position, current.Position)
        };
    }

    private static IslePilotNutritionDto? Merge(
        IslePilotNutritionDto? previous,
        IslePilotNutritionDto? current)
    {
        if (current is null)
        {
            return previous;
        }

        return new IslePilotNutritionDto
        {
            Carb = current.Carb ?? previous?.Carb,
            Protein = current.Protein ?? previous?.Protein,
            Lipid = current.Lipid ?? previous?.Lipid
        };
    }

    private static IslePilotOverlayPositionDto? Merge(
        IslePilotOverlayPositionDto? previous,
        IslePilotOverlayPositionDto? current)
    {
        if (current is null)
        {
            return previous;
        }

        return new IslePilotOverlayPositionDto
        {
            X = current.X ?? previous?.X,
            Y = current.Y ?? previous?.Y,
            Z = current.Z ?? previous?.Z,
            Yaw = current.Yaw ?? previous?.Yaw
        };
    }

    private static double? FractionToPercent(double? value) => value is null
        ? null
        : Math.Clamp(value.Value <= 1d ? value.Value * 100d : value.Value, 0d, 100d);

    private static double? Percent(double? current, double? maximum) =>
        current is not null && maximum is > 0d
            ? Math.Clamp(current.Value / maximum.Value * 100d, 0d, 100d)
            : null;

    private static double? Percent(int? current, int? maximum) =>
        current is not null && maximum is > 0
            ? Math.Clamp((double)current.Value / maximum.Value * 100d, 0d, 100d)
            : null;
}
