namespace TheIsleOverlay.LocalTelemetry;

/// <summary>Evidence from game-owned outbound UDP traffic, independent of packet decoding.</summary>
public interface IGameEndpointEvidenceSource
{
    bool HasRecentOutboundTraffic(string endpoint, DateTimeOffset now, TimeSpan maxAge);
}
