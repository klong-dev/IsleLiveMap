using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotOverlayWebSocket : IIslePilotOverlayWebSocket
{
    private const int ReceiveBufferSize = 4096;
    private const int MaximumMessageBytes = 1024 * 1024;

    private readonly ClientWebSocket _socket = new();
    private bool _disposed;

    public async Task ConnectAsync(
        string overlayToken,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(overlayToken) ||
            overlayToken.Contains('\r') || overlayToken.Contains('\n'))
        {
            throw new ArgumentException("The IslePilot overlay token is invalid.", nameof(overlayToken));
        }

        _socket.Options.SetRequestHeader("Authorization", $"Bearer {overlayToken}");
        await _socket.ConnectAsync(IslePilotOverlayOptions.WebSocketUri, cancellationToken);
    }

    public async Task SendHelloAsync(
        string? personaName,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("The IslePilot overlay WebSocket is not connected.");
        }

        var payload = CreateHelloPayload(personaName);
        await _socket.SendAsync(
            new ArraySegment<byte>(payload),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    public async IAsyncEnumerable<IslePilotOverlayLiveDataDto> ReadLiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
        {
            var message = await ReceiveCompleteMessageAsync(_socket, cancellationToken);
            if (message is null)
            {
                yield break;
            }

            if (message.Value.Type != WebSocketMessageType.Text)
            {
                continue;
            }

            IslePilotOverlayFrame? frame;
            try
            {
                frame = JsonSerializer.Deserialize<IslePilotOverlayFrame>(
                    message.Value.Text,
                    IslePilotOverlayJson.Options);
            }
            catch (JsonException)
            {
                continue;
            }

            var liveData = frame?.Data;
            if (string.Equals(frame?.Type, "live", StringComparison.Ordinal) && liveData is not null)
            {
                yield return liveData;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await _socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Overlay closing",
                    timeout.Token);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            _socket.Abort();
        }
        finally
        {
            _socket.Dispose();
        }
    }

    internal static byte[] CreateHelloPayload(string? personaName) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new IslePilotOverlayHelloFrame { Name = personaName },
            IslePilotOverlayJson.Options);

    internal static async Task<IslePilotOverlaySocketMessage?> ReceiveCompleteMessageAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            using var message = new MemoryStream();
            WebSocketMessageType? messageType = null;

            while (true)
            {
                var result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer, 0, ReceiveBufferSize),
                    cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (messageType is not null && result.MessageType != messageType)
                {
                    throw new InvalidDataException("A fragmented WebSocket message changed type.");
                }

                messageType ??= result.MessageType;
                if (message.Length + result.Count > MaximumMessageBytes)
                {
                    throw new InvalidDataException("An IslePilot WebSocket message exceeded the size limit.");
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                return new IslePilotOverlaySocketMessage(
                    messageType.Value,
                    Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length)));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed record IslePilotOverlayHelloFrame
    {
        [JsonPropertyName("t")]
        public string Type { get; init; } = "hello";
        public string? Name { get; init; }
    }
}

internal readonly record struct IslePilotOverlaySocketMessage(
    WebSocketMessageType Type,
    string Text);
