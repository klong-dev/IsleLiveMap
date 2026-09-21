using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.Tests;

public sealed class TeamRelayClientPolicyTests
{
    [Fact]
    public void RoomLimits_FreeIsSevenAndProIsTwentyOne()
    {
        Assert.Equal(7, TeamRoomLimits.For(TeamAccessTier.Free));
        Assert.Equal(21, TeamRoomLimits.For(TeamAccessTier.Pro));
        Assert.Equal(TeamRoomLimits.FreeMaxMembers, new CreateTeamRequest("x").RequestedMaxMembers);
        Assert.Equal(TeamRoomLimits.ProMaxMembers, new CreateTeamRequest("x", TeamAccessTier.Pro, TeamRoomLimits.ProMaxMembers).RequestedMaxMembers);
    }

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
