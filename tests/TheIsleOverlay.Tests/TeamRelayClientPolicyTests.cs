using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.Tests;

public sealed class TeamRelayClientPolicyTests
{
    [Fact]
    public void TeamBootstrap_HasABoundedTwelveSecondDeadline()
    {
        Assert.Equal(TimeSpan.FromSeconds(12), TeamRelayClient.SessionStartTimeout);
    }

    [Fact]
    public async Task JoinTimeout_StopsWaitingAndReturnsAnActionableErrorState()
    {
        using var httpClient = new HttpClient(new HangingHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        await using var client = new TeamRelayClient(
            new Uri("https://relay.invalid/"),
            httpClient,
            TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            client.JoinAsync("ABC123", "Survivor"));

        Assert.Contains("không phản hồi", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TeamRelayConnectionState.Error, client.CurrentState.ConnectionState);
        Assert.Null(client.CurrentState.Session);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }
}
