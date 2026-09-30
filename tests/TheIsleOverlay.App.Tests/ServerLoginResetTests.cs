using System.IO;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App.Tests;

public sealed class ServerLoginResetTests
{
    [Fact]
    public void TargetsIncludeOriginSharedBrowserGachaAndEverySdvnTenantButNotProLicense()
    {
        var root = Path.Combine(Path.GetTempPath(), "login-reset-targets");
        var targets = ServerLoginResetService.Targets(root);
        Assert.Contains(targets, t => t.Profile == Path.Combine(root, "WebView2"));
        Assert.Contains(targets, t => t.Profile == Path.Combine(root, "GachaWebView2"));
        foreach (var tenant in SdvnTenant.All)
            Assert.Contains(targets, t => t.Credential == Path.Combine(root, "SDVN", tenant.Id + ".credential")
                && t.Profile == Path.Combine(root, "SDVN", tenant.Id, "WebView2"));
        Assert.DoesNotContain(targets, t => (t.Credential ?? "").Contains("Pro", StringComparison.Ordinal));
        Assert.All(targets, t => Assert.StartsWith(root, (t.Profile ?? t.Credential)!, StringComparison.Ordinal));
    }

    [Fact]
    public void MissingParentDirectoryIsAlreadyLoggedOut()
    {
        var path = Path.Combine(Path.GetTempPath(), "absent-login-" + Guid.NewGuid().ToString("N"), "SDVN", "sdvn-1.credential");
        ServerLoginResetService.DeleteCredential(path);
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task SuccessWaitsForBrowserClearAndReportsResult()
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleted = new List<string>();
        var operation = ServerLoginResetService.ClearAsync([new("ORIGIN", "fixture.credential", "profile")],
            deleted.Add, (_, _) => barrier.Task, CancellationToken.None);
        Assert.False(operation.IsCompleted);
        Assert.Single(deleted);
        barrier.SetResult();
        var result = await operation;
        Assert.True(result.Success); Assert.Contains("đăng nhập lại", result.Message);
    }

    [Fact]
    public async Task PartialFailureDoesNotClaimSuccessAndStillClearsOtherTargets()
    {
        var profiles = new List<string>();
        var result = await ServerLoginResetService.ClearAsync(
            [new("First", "locked", "profile1"), new("Second", null, "profile2")],
            _ => throw new IOException("secret credential must not appear in UI"),
            (path, _) => { profiles.Add(path); return Task.CompletedTask; }, CancellationToken.None);
        Assert.False(result.Success); Assert.Equal(2, profiles.Count);
        Assert.Contains("First", result.Message); Assert.DoesNotContain("secret credential", result.Message);
        Assert.Contains("Second", result.Completed);
    }

    [Fact]
    public async Task BrowserFailureIsNotSwallowed()
    {
        var result = await ServerLoginResetService.ClearAsync([new("ORIGIN", null, "profile")],
            _ => { }, (_, _) => Task.FromException(new IOException("locked browser")), CancellationToken.None);
        Assert.False(result.Success); Assert.Contains("ORIGIN", result.Message); Assert.Empty(result.Completed);
    }

    [Fact]
    public async Task CancelledResetDoesNotReturnSuccess()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ServerLoginResetService.ClearAsync(
            [new("ORIGIN", null, "profile")], _ => { }, (_, _) => Task.CompletedTask, new CancellationToken(true)));
    }
}
