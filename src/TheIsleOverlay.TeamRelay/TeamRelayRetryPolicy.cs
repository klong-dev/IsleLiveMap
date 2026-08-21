using Microsoft.AspNetCore.SignalR.Client;

namespace TheIsleOverlay.TeamRelay;

public sealed class TeamRelayRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(32);

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        if (retryContext.ElapsedTime >= RetryWindow)
        {
            return null;
        }

        return retryContext.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(2),
            _ => TimeSpan.FromSeconds(5)
        };
    }
}
