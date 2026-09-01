using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App;

internal readonly record struct ProFeatureAccessGrant(
    bool Enabled,
    DateTimeOffset? ExpiresAt)
{
    public static ProFeatureAccessGrant Free => new(false, null);

    public bool IsActiveAt(DateTimeOffset now) =>
        Enabled && (ExpiresAt is null || ExpiresAt > now);

    public static ProFeatureAccessGrant FromSnapshot(
        ProAccessSnapshot access,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(access);
        return new ProFeatureAccessGrant(
            access.Entitlement.IsProAt(now),
            access.Entitlement.ExpiresAt);
    }
}
