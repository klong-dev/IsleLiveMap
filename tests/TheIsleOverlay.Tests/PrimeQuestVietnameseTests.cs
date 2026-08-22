using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class PrimeQuestVietnameseTests
{
    [Theory]
    [InlineData("Visit a Sanctuary as a juvenile", "Ghé Sanctuary khi còn non")]
    [InlineData("Get nested in", "Được sinh ra từ tổ")]
    [InlineData("Get perfect diet (1% of each)", "Đủ 3 chất dinh dưỡng (mỗi chất ≥ 1%)")]
    [InlineData("Visit Mass Migration zone", "Ghé vùng Đại di cư")]
    [InlineData("Visit 2 Migration zones", "Ghé 2 vùng Di cư")]
    [InlineData("Visit 4 Patrol zones", "Ghé 4 vùng Tuần tra")]
    [InlineData("Never be Infertile", "Không bị Vô sinh")]
    [InlineData("Never get Muscle spasms", "Không bị Co thắt cơ")]
    [InlineData("Raise children to Subadult", "Nuôi con tới Subadult")]
    [InlineData("Be a Hypsi, Troodon, Beipi, Dryo or Deino", "Chơi Hypsi / Troodon / Beipi / Dryo / Deino")]
    public void Translate_MapsCurrentIslePilotPrimeMissions(string source, string expected)
    {
        Assert.Equal(expected, PrimeQuestVietnamese.Translate(source));
    }

    [Fact]
    public void Translate_PreservesAnUnknownFutureMission()
    {
        Assert.Equal("A future IslePilot mission", PrimeQuestVietnamese.Translate(" A future IslePilot mission "));
    }
}
