using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FlashFillServiceTests
{
    [Fact]
    public void Fill_EmailDisplayName_ConvertsDottedUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada.lovelace@contoso.com", "Ada Lovelace"),
                ("grace.hopper@contoso.com", "Grace Hopper")
            ],
            ["alan.turing@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_ConvertsUnderscoreUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada_lovelace@contoso.com", "Ada Lovelace"),
                ("grace_hopper@contoso.com", "Grace Hopper")
            ],
            ["alan_turing@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_ConvertsHyphenUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada-lovelace@contoso.com", "Ada Lovelace"),
                ("grace-hopper@contoso.com", "Grace Hopper")
            ],
            ["alan-turing@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_ConvertsMixedSeparatorUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada.lovelace_smith@contoso.com", "Ada Lovelace Smith"),
                ("grace-hopper_murray@example.org", "Grace Hopper Murray")
            ],
            ["alan.turing-mathison@test.net"]);

        result.Should().BeEquivalentTo(["Alan Turing Mathison"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_IgnoresPlusAddressTags()
    {
        var result = FlashFillService.Fill(
            [
                ("ada.lovelace+analytics@contoso.com", "Ada Lovelace"),
                ("grace.hopper+navy@contoso.com", "Grace Hopper")
            ],
            ["alan.turing+math@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_ConvertsMultiTokenUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada.byron.lovelace@contoso.com", "Ada Byron Lovelace"),
                ("grace.brewster.hopper@contoso.com", "Grace Brewster Hopper")
            ],
            ["alan.mathison.turing@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Mathison Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_StripsTrailingNumericSuffixesFromUserNameTokens()
    {
        var result = FlashFillService.Fill(
            [
                ("ada.lovelace2@contoso.com", "Ada Lovelace"),
                ("grace.hopper17@contoso.com", "Grace Hopper")
            ],
            ["alan.turing3@contoso.com", "katherine.johnson42@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing", "Katherine Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_ReturnsNullWhenNumericSuffixWouldEmptyAToken()
    {
        var result = FlashFillService.Fill(
            [
                ("ada.lovelace2@contoso.com", "Ada Lovelace"),
                ("grace.hopper17@contoso.com", "Grace Hopper")
            ],
            ["alan.123@contoso.com"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_SplitPascalCaseWords_InsertsWordSpaces()
    {
        var result = FlashFillService.Fill(
            [
                ("AdaLovelace", "Ada Lovelace"),
                ("GraceHopper", "Grace Hopper")
            ],
            ["AlanTuring", "KatherineJohnson"]);

        result.Should().BeEquivalentTo(["Alan Turing", "Katherine Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_SplitPascalCaseWords_ReturnsNullWhenRemainingHasNoCaseBoundary()
    {
        var result = FlashFillService.Fill(
            [
                ("AdaLovelace", "Ada Lovelace"),
                ("GraceHopper", "Grace Hopper")
            ],
            ["alan"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmailLocalPartWithoutPlusTag_ExtractsUntaggedUserName()
    {
        var result = FlashFillService.Fill(
            [
                ("ada+analytics@contoso.com", "ada"),
                ("grace+navy@contoso.com", "grace")
            ],
            ["alan+math@contoso.com"]);

        result.Should().BeEquivalentTo(["alan"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailLocalPartWithoutPlusTag_ReturnsNullWhenRemainingTagIsMissing()
    {
        var result = FlashFillService.Fill(
            [
                ("ada+analytics@contoso.com", "ada"),
                ("grace+navy@contoso.com", "grace")
            ],
            ["alan@contoso.com"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmailDomainStem_ExtractsOrganizationFromEmailDomain()
    {
        var result = FlashFillService.Fill(
            [
                ("ada@contoso.com", "contoso"),
                ("grace@fabrikam.org", "fabrikam")
            ],
            ["alan@northwind.net", "katherine@adatum.co"]);

        result.Should().BeEquivalentTo(["northwind", "adatum"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDomainStem_PreservesSubdomainStemBeforeTopLevelDomain()
    {
        var result = FlashFillService.Fill(
            [
                ("ada@sales.contoso.com", "sales.contoso"),
                ("grace@research.fabrikam.org", "research.fabrikam")
            ],
            ["alan@labs.northwind.net"]);

        result.Should().BeEquivalentTo(["labs.northwind"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDomainStem_ReturnsNullWhenRemainingDomainHasNoSuffix()
    {
        var result = FlashFillService.Fill(
            [
                ("ada@contoso.com", "contoso"),
                ("grace@fabrikam.org", "fabrikam")
            ],
            ["alan@localhost"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_WebAddressHostWithoutWww_StripsSchemeWwwPathQueryAndFragment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://www.contoso.com/products?id=1", "contoso.com"),
                ("http://www.fabrikam.org/support#top", "fabrikam.org")
            ],
            ["https://www.northwind.net/catalog/list?page=2#details", "http://adatum.co/about"]);

        result.Should().BeEquivalentTo(["northwind.net", "adatum.co"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_WebAddressHost_PreservesWwwWhenExamplesDo()
    {
        var result = FlashFillService.Fill(
            [
                ("https://www.contoso.com/products?id=1", "www.contoso.com"),
                ("http://fabrikam.org/support#top", "fabrikam.org")
            ],
            ["https://www.northwind.net/catalog?x=1#details", "http://adatum.co/about"]);

        result.Should().BeEquivalentTo(["www.northwind.net", "adatum.co"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_WebAddressDomainStem_StripsSchemeWwwPathQueryFragmentAndTopLevelDomain()
    {
        var result = FlashFillService.Fill(
            [
                ("https://www.contoso.com/products?id=1", "contoso"),
                ("http://www.fabrikam.org/support#top", "fabrikam")
            ],
            ["https://www.northwind.net/catalog/list?page=2#details", "http://adatum.co/about"]);

        result.Should().BeEquivalentTo(["northwind", "adatum"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_WebAddressCleanup_HandlesBareWebAddresses()
    {
        var result = FlashFillService.Fill(
            [
                ("www.contoso.com/products?id=1", "contoso.com"),
                ("www.fabrikam.org/support#top", "fabrikam.org")
            ],
            ["www.northwind.net/catalog/list?page=2#details"]);

        result.Should().BeEquivalentTo(["northwind.net"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_WebAddressCleanup_HandlesPathFreeBareHosts()
    {
        var result = FlashFillService.Fill(
            [
                ("contoso.com", "contoso"),
                ("fabrikam.org", "fabrikam")
            ],
            ["northwind.net", "adatum.co"]);

        result.Should().BeEquivalentTo(["northwind", "adatum"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_WebAddressCleanup_ReturnsNullForMixedHostAndStemExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("https://www.contoso.com/products?id=1", "contoso.com"),
                ("http://www.fabrikam.org/support#top", "fabrikam")
            ],
            ["https://www.northwind.net/catalog/list?page=2#details"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(".", "alan.turing@contoso.com")]
    [InlineData("_", "alan_turing@contoso.com")]
    [InlineData("-", "alan-turing@contoso.com")]
    public void Fill_FullNamesToFirstLastEmail_LearnsSeparatorAndSharedDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", $"ada{separator}lovelace@contoso.com"),
                ("Grace Hopper", $"grace{separator}hopper@contoso.com")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNamesToFirstLastEmail_UsesFirstAndLastAcrossMiddleNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "ada.lovelace@contoso.com"),
                ("Grace Brewster Hopper", "grace.hopper@contoso.com")
            ],
            ["Katherine Coleman Johnson"]);

        result.Should().BeEquivalentTo(["katherine.johnson@contoso.com"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(".", "turing.alan@contoso.com")]
    [InlineData("_", "turing_alan@contoso.com")]
    [InlineData("-", "turing-alan@contoso.com")]
    public void Fill_FullNamesToLastFirstEmail_LearnsSeparatorAndSharedDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", $"lovelace{separator}ada@contoso.com"),
                ("Grace Hopper", $"hopper{separator}grace@contoso.com")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("", "aturing@contoso.com")]
    [InlineData(".", "a.turing@contoso.com")]
    [InlineData("_", "a_turing@contoso.com")]
    [InlineData("-", "a-turing@contoso.com")]
    public void Fill_FullNamesToFirstInitialLastEmail_LearnsSeparatorAndSharedDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", $"a{separator}lovelace@contoso.com"),
                ("Grace Hopper", $"g{separator}hopper@contoso.com")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNamesToEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "ada.lovelace@contoso.com"),
                ("Grace Hopper", "grace.hopper@example.org")
            ],
            ["Alan Turing"]);

        result.Should().BeNull();
    }

}
