using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.ProClient.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.4.0", 1, 4, 0)]
    [InlineData("0.1.12", 0, 1, 12)]
    public void TryParse_AcceptsThreePartVersions(
        string value,
        int major,
        int minor,
        int patch)
    {
        Assert.True(SemanticVersion.TryParse(value, out var parsed));
        Assert.Equal(new SemanticVersion(major, minor, patch), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.4")]
    [InlineData("1.4.0-beta")]
    [InlineData("-1.4.0")]
    public void TryParse_RejectsNonCanonicalVersions(string value) =>
        Assert.False(SemanticVersion.TryParse(value, out _));

    [Fact]
    public void CompareTo_OrdersMajorMinorAndPatch()
    {
        Assert.True(new SemanticVersion(1, 4, 0).CompareTo(new SemanticVersion(1, 3, 9)) > 0);
        Assert.True(new SemanticVersion(1, 4, 1).CompareTo(new SemanticVersion(1, 4, 2)) < 0);
    }
}
