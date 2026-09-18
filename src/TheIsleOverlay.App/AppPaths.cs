using System.IO;

namespace TheIsleOverlay.App;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KLongDev",
        "IsleLiveMap");

    public static string WebView2Profile { get; } = Path.Combine(Root, "WebView2");

    public static string IslePilotCredential { get; } = Path.Combine(
        Root,
        "islepilot-overlay.credential");

    public static string IslePilotVoiceCredential { get; } = Path.Combine(
        Root,
        "islepilot-voice.credential");

    public static string GachaOverlayCredential { get; } = Path.Combine(
        Root,
        "gacha-overlay.credential");

    public static string GachaWebView2Profile { get; } = Path.Combine(
        Root,
        "GachaWebView2");

    public static string OverlayLayoutSettings { get; } = Path.Combine(
        Root,
        "overlay-layout.json");

    public static string MapNotes { get; } = Path.Combine(
        Root,
        "map-notes.json");

    public static string MapLayerSettings { get; } = Path.Combine(
        Root,
        "map-layer-settings.json");

    public static string ReleaseHighlightsPreferences { get; } = Path.Combine(
        Root,
        "release-highlights.json");

    public static string ZaloChannelInvitePreferences { get; } = Path.Combine(
        Root,
        "zalo-channel-invite.json");

}
