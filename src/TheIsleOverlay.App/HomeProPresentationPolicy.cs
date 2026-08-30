using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App;

internal readonly record struct HomeProPresentationState(
    bool HasCurrentProAccess,
    bool IsVerified,
    string MapTitle,
    string MapAction);

internal static class HomeProPresentationPolicy
{
    public static HomeProPresentationState Evaluate(
        ProAccessSnapshot access,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(access);
        var hasCurrentProAccess = access.Entitlement.IsProAt(now);
        var isVerified = hasCurrentProAccess && access.AgentReady;
        return new HomeProPresentationState(
            hasCurrentProAccess,
            isVerified,
            hasCurrentProAccess ? "MỞ MAP PRO" : "MỞ LIVE MAP",
            hasCurrentProAccess ? "MỞ MAP PRO  →" : "MỞ MAP  →");
    }
}
