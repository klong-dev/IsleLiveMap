using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotOverlayWebSocketTests
{
    [Fact]
    public async Task ReceiveCompleteMessageAsync_ReassemblesFragmentedUtf8Text()
    {
        const string json = """
            {"t":"live","d":{"hasDino":true,"position":{"x":12,"y":34}}}
            """;
        var bytes = Encoding.UTF8.GetBytes(json);
        using var socket = new FragmentedWebSocket(
            bytes[..13],
            bytes[13..31],
            bytes[31..]);

        var message = await IslePilotOverlayWebSocket.ReceiveCompleteMessageAsync(
            socket,
            CancellationToken.None);

        Assert.NotNull(message);
        Assert.Equal(WebSocketMessageType.Text, message.Value.Type);
        Assert.Equal(json, message.Value.Text);
    }

    [Fact]
    public void CreateHelloPayload_UsesExpectedWireContract()
    {
        var payload = IslePilotOverlayWebSocket.CreateHelloPayload("Player Name");
        using var json = JsonDocument.Parse(payload);

        Assert.Equal("hello", json.RootElement.GetProperty("t").GetString());
        Assert.Equal("Player Name", json.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ReceiveCompleteMessageAsync_ReturnsNullForCloseFrame()
    {
        using var socket = new FragmentedWebSocket();

        var message = await IslePilotOverlayWebSocket.ReceiveCompleteMessageAsync(
            socket,
            CancellationToken.None);

        Assert.Null(message);
    }

    private sealed class FragmentedWebSocket(params byte[][] fragments) : WebSocket
    {
        private readonly Queue<byte[]> _fragments = new(fragments);
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus { get; } = WebSocketCloseStatus.NormalClosure;
        public override string? CloseStatusDescription { get; } = "closed";
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (_fragments.Count == 0)
            {
                _state = WebSocketState.CloseReceived;
                return Task.FromResult(new WebSocketReceiveResult(
                    0,
                    WebSocketMessageType.Close,
                    endOfMessage: true,
                    WebSocketCloseStatus.NormalClosure,
                    "closed"));
            }

            var fragment = _fragments.Dequeue();
            Array.Copy(fragment, 0, buffer.Array!, buffer.Offset, fragment.Length);
            return Task.FromResult(new WebSocketReceiveResult(
                fragment.Length,
                WebSocketMessageType.Text,
                endOfMessage: _fragments.Count == 0));
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
