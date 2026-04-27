using OvcinaHra.Api.Configuration;

namespace OvcinaHra.Api.Tests.Configuration;

public class OvcinaCorsPolicyTests
{
    [Fact]
    public void Glejt_IsAllowed_WhenConfigOnlyListsHra()
    {
        // Repro of Issue #244 (2026-04-27): prod Container App env vars only
        // listed https://hra.ovcina.cz, but the API must still allow Glejt.
        var configured = new[] { "https://hra.ovcina.cz" };

        var effective = OvcinaCorsPolicy.BuildEffectiveOrigins(configured);

        Assert.Contains("https://glejt.ovcina.cz", effective);
        Assert.Contains("https://hra.ovcina.cz", effective);
    }

    [Fact]
    public void EcosystemOrigins_AreAllowed_WhenConfigIsNull()
    {
        var effective = OvcinaCorsPolicy.BuildEffectiveOrigins(null);

        Assert.Contains("https://hra.ovcina.cz", effective);
        Assert.Contains("https://glejt.ovcina.cz", effective);
    }

    [Fact]
    public void EcosystemOrigins_AreAllowed_WhenConfigIsEmpty()
    {
        var effective = OvcinaCorsPolicy.BuildEffectiveOrigins([]);

        Assert.Contains("https://hra.ovcina.cz", effective);
        Assert.Contains("https://glejt.ovcina.cz", effective);
    }

    [Fact]
    public void ConfiguredOrigins_ExtendTheAllowlist()
    {
        var configured = new[] { "https://preview.ovcina.cz" };

        var effective = OvcinaCorsPolicy.BuildEffectiveOrigins(configured);

        Assert.Contains("https://preview.ovcina.cz", effective);
        Assert.Contains("https://hra.ovcina.cz", effective);
        Assert.Contains("https://glejt.ovcina.cz", effective);
    }

    [Fact]
    public void DuplicatesAreDeduped_CaseInsensitive()
    {
        var configured = new[] { "HTTPS://Hra.Ovcina.cz", "https://hra.ovcina.cz" };

        var effective = OvcinaCorsPolicy.BuildEffectiveOrigins(configured);

        // hra.ovcina.cz appears once (the canonical ecosystem entry survives
        // because HashSet keeps the first inserted casing).
        var hraCount = effective.Count(o =>
            string.Equals(o, "https://hra.ovcina.cz", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, hraCount);
    }

    [Fact]
    public void NullAndWhitespaceEntries_AreIgnored()
    {
        var configured = new[] { "https://preview.ovcina.cz", "", "   ", null! };

        var effective = OvcinaCorsPolicy.BuildEffectiveOrigins(configured);

        Assert.DoesNotContain("", effective);
        Assert.DoesNotContain("   ", effective);
        Assert.Contains("https://preview.ovcina.cz", effective);
    }

    [Fact]
    public void EcosystemOrigins_CannotBeRemoved_EvenIfConfigOmitsThem()
    {
        // Even if config lists nothing, ecosystem origins must remain.
        var effective = OvcinaCorsPolicy.BuildEffectiveOrigins(new string[0]);

        foreach (var ecosystem in OvcinaCorsPolicy.EcosystemOrigins)
        {
            Assert.Contains(ecosystem, effective);
        }
    }
}
