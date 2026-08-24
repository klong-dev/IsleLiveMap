namespace TheIsleOverlay.LocalTelemetry;

public sealed class LocalPacketCaptureUnavailableException : Exception
{
    public LocalPacketCaptureUnavailableException(string message)
        : base(message)
    {
    }

    public LocalPacketCaptureUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
