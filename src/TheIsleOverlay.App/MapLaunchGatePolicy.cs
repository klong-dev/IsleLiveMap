namespace TheIsleOverlay.App;

public enum MapLaunchGateState
{
    Checking,
    Available,
    UpdateRequired
}

public static class MapLaunchGatePolicy
{
    public static MapLaunchGateState FromUpdate(UpdatePreparationState state) => state switch
    {
        UpdatePreparationState.Ready => MapLaunchGateState.UpdateRequired,
        UpdatePreparationState.Current
            or UpdatePreparationState.DevelopmentBuild
            or UpdatePreparationState.Unavailable => MapLaunchGateState.Available,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    public static bool AllowsMap(MapLaunchGateState state) =>
        state == MapLaunchGateState.Available;
}
