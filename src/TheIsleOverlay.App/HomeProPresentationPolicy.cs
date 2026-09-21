using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App;

internal readonly record struct HomeProPresentationState(
    bool HasCurrentProAccess,
    bool IsPremiumMode,
    bool IsVerified,
    bool IsOffline,
    bool AgentReady,
    bool ShowPromotion,
    string TierLabel,
    string StatusLabel,
    string HeroEyebrow,
    string HeroTitle,
    string HeroDescription,
    string MapTitle,
    string MapAction,
    string AccentKey);

internal static class HomeProPresentationPolicy
{
    public static HomeProPresentationState Evaluate(
        ProAccessSnapshot access,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(access);
        var hasCurrentProAccess = access.Entitlement.IsProAt(now);
        var isVerified = hasCurrentProAccess && access.AgentReady;
        var expired = access.Entitlement.ExpiresAt is { } expiry && expiry <= now;
        var tier = hasCurrentProAccess ? "PRO ĐANG HOẠT ĐỘNG" : access.IsAuthenticated ? "STEAM ĐÃ XÁC MINH" : "MIỄN PHÍ";
        var status = hasCurrentProAccess
            ? access.AgentReady ? "PRO ĐANG HOẠT ĐỘNG · TRỢ LÝ SẴN SÀNG" : "PRO ĐANG HOẠT ĐỘNG · TRỢ LÝ ĐANG CHỜ"
            : expired ? "GIẤY PHÉP HẾT HẠN" : access.StatusCode == "license_service_unavailable" ? "CHƯA TẢI LẠI GIẤY PHÉP" : "MIỄN PHÍ";
        return new HomeProPresentationState(
            hasCurrentProAccess,
            hasCurrentProAccess,
            isVerified,
            access.IsOffline,
            access.AgentReady,
            !hasCurrentProAccess,
            tier,
            status,
            hasCurrentProAccess ? "CHẾ ĐỘ PRO" : "CHẾ ĐỘ MIỄN PHÍ",
            hasCurrentProAccess ? "KHÔNG GIAN THEO DÕI PRO" : "MỞ TRÌNH THEO DÕI",
            hasCurrentProAccess ? "Theo dõi người chơi và AI trong cùng một không gian cao cấp." : "Mở một phiên theo dõi khi bạn yêu cầu.",
            hasCurrentProAccess ? "KHÔNG GIAN THEO DÕI PRO" : "MỞ TRÌNH THEO DÕI",
            hasCurrentProAccess ? "MỞ MAP PRO  →" : "MỞ MAP  →",
            hasCurrentProAccess ? "Cao cấp" : "Miễn phí");
    }
}
