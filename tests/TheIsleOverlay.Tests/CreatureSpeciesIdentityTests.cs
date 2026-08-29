using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class CreatureSpeciesIdentityTests
{
    [Theory]
    [InlineData("BP_TyrannosaurusRex_C", "tyrannosaurus")]
    [InlineData("Default__BP_Tyrannosaurus_C", "tyrannosaurus")]
    [InlineData("TITyrannosaurus", "tyrannosaurus")]
    [InlineData("T-Rex", "tyrannosaurus")]
    [InlineData("Utahraptor", "omniraptor")]
    [InlineData("Trice", "triceratops")]
    public void Normalize_ProducesCanonicalSpecies(string value, string expected)
    {
        Assert.Equal(expected, CreatureSpeciesIdentity.Normalize(value));
    }

    [Theory]
    [InlineData("BP_Omniraptor_C", "Utahraptor")]
    [InlineData("Triceratops", "Trice")]
    [InlineData("TyrannosaurusRex", "T-Rex")]
    public void AreSame_AcceptsKnownAliases(string left, string right)
    {
        Assert.True(CreatureSpeciesIdentity.AreSame(left, right));
    }

    [Theory]
    [InlineData(null, "T-Rex")]
    [InlineData("", "T-Rex")]
    [InlineData("Carnotaurus", "T-Rex")]
    public void AreSame_RejectsMissingOrDifferentSpecies(string? left, string right)
    {
        Assert.False(CreatureSpeciesIdentity.AreSame(left, right));
    }
}
