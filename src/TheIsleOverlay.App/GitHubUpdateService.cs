using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace TheIsleOverlay.App;

public sealed class GitHubUpdateService
{
    public const string RepositoryUrl = "https://github.com/klong-dev/IsleLiveMap";
    private static readonly TimeSpan UpdateCheckTimeout = TimeSpan.FromSeconds(15);

    private UpdateManager? _manager;
    private UpdateInfo? _pendingUpdate;

    public async Task<UpdatePreparationResult> PrepareUpdateAsync(
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
            _pendingUpdate = await _manager
                .CheckForUpdatesAsync()
                .WaitAsync(UpdateCheckTimeout, cancellationToken);
            if (_pendingUpdate is null)
            {
                return UpdatePreparationResult.Current;
            }

            await _manager.DownloadUpdatesAsync(_pendingUpdate, progress, cancellationToken);
            return new UpdatePreparationResult(
                UpdatePreparationState.Ready,
                _pendingUpdate.TargetFullRelease.Version.ToString());
        }
        catch (NotInstalledException)
        {
            return UpdatePreparationResult.DevelopmentBuild;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return UpdatePreparationResult.Unavailable;
        }
    }

    public void ApplyAndRestart()
    {
        if (_manager is null || _pendingUpdate is null)
        {
            return;
        }

        _manager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
    }
}

public enum UpdatePreparationState
{
    Current,
    Ready,
    DevelopmentBuild,
    Unavailable
}

public sealed record UpdatePreparationResult(UpdatePreparationState State, string? Version = null)
{
    public static UpdatePreparationResult Current { get; } = new(UpdatePreparationState.Current);
    public static UpdatePreparationResult DevelopmentBuild { get; } = new(UpdatePreparationState.DevelopmentBuild);
    public static UpdatePreparationResult Unavailable { get; } = new(UpdatePreparationState.Unavailable);
}
