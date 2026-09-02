namespace TheIsleOverlay.App.Tests;

public sealed class MouseShortcutActivationPolicyTests
{
    [Fact]
    public void NormalCameraMovement_DoesNotRequireTheGlobalMouseHook()
    {
        Assert.False(MouseShortcutActivationPolicy.ShouldInstall(
            activationKeyPressed: false,
            gestureActive: false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AltOrAnActivePanGesture_KeepsTheHookInstalled(
        bool activationKeyPressed,
        bool gestureActive)
    {
        Assert.True(MouseShortcutActivationPolicy.ShouldInstall(
            activationKeyPressed,
            gestureActive));
    }
}
