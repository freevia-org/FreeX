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
    public void Fill_EmailDisplayName_ConvertsCamelCaseUserNameToProperName()
    {
        var result = FlashFillService.Fill(
            [
                ("adaLovelace@example.com", "Ada Lovelace"),
                ("graceHopper+sales@contoso.com", "Grace Hopper")
            ],
            ["alanTuring@research.example", "katherineJohnson+math@contoso.com"]);

        result.Should().BeEquivalentTo(["Alan Turing", "Katherine Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDisplayName_ReturnsNullForAllLowerUserNameWithoutSeparators()
    {
        var result = FlashFillService.Fill(
            [
                ("adalovelace@example.com", "Ada Lovelace"),
                ("gracehopper@contoso.com", "Grace Hopper")
            ],
            ["alanturing@research.example"]);

        result.Should().BeNull();
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
    public void Fill_EmailDomainStem_ExtractsRootStemFromVariableSubdomainDepth()
    {
        var result = FlashFillService.Fill(
            [
                ("ada@eu.sales.contoso.com", "contoso"),
                ("grace@research.fabrikam.org", "fabrikam")
            ],
            ["alan@labs.northwind.net", "katherine@north.america.adatum.co"]);

        result.Should().BeEquivalentTo(["northwind", "adatum"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_EmailDomainStem_ExtractsRootStemFromCuratedMultiLabelPublicSuffix()
    {
        var result = FlashFillService.Fill(
            [
                ("ada@eu.sales.contoso.co.uk", "contoso"),
                ("grace@research.fabrikam.org.uk", "fabrikam")
            ],
            ["alan@labs.northwind.com.au", "katherine@north.america.adatum.co.nz"]);

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
    public void Fill_EmailDomainStem_ReturnsNullWhenRootStemRemainingDomainHasNoSuffix()
    {
        var result = FlashFillService.Fill(
            [
                ("ada@eu.sales.contoso.com", "contoso"),
                ("grace@research.fabrikam.org", "fabrikam")
            ],
            ["alan@localhost"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_EmailDomainSuffix_ExtractsSingleAndCuratedMultiLabelPublicSuffixes()
    {
        var result = FlashFillService.Fill(
            [
                ("alice@contoso.com", "com"),
                ("bob@northwind.co.uk", "co.uk")
            ],
            ["ada@fabrikam.com.au"]);

        result.Should().BeEquivalentTo(["com.au"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("ada@localhost")]
    [InlineData("ada@intranet.local")]
    [InlineData("ada@contoso.invalid")]
    [InlineData("ada@contoso")]
    [InlineData("ada@contoso.")]
    public void Fill_EmailDomainSuffix_ReturnsNullForInvalidNoSuffixOrIntranetStyleRemainingDomain(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("alice@contoso.com", "com"),
                ("bob@northwind.co.uk", "co.uk")
            ],
            [remaining]);

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
    public void Fill_WebAddressDomainStem_ExtractsRootStemFromVariableSubdomainDepth()
    {
        var result = FlashFillService.Fill(
            [
                ("https://eu.sales.contoso.com/path", "contoso"),
                ("http://research.fabrikam.org/x", "fabrikam")
            ],
            ["https://labs.northwind.net/catalog/list?page=2#details", "north.america.adatum.co/products"]);

        result.Should().BeEquivalentTo(["northwind", "adatum"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_WebAddressDomainStem_ExtractsRootStemFromCuratedMultiLabelPublicSuffix()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.contoso.com.au/path", "contoso"),
                ("http://eu.research.fabrikam.co.uk/x", "fabrikam")
            ],
            ["https://checkout.northwind.co.nz/cart?x=1", "https://portal.adatum.com.sg/path"]);

        result.Should().BeEquivalentTo(["northwind", "adatum"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_WebAddressDomainStem_PreservesSubdomainStemBeforeTopLevelDomain()
    {
        var result = FlashFillService.Fill(
            [
                ("https://sales.contoso.com/products?id=1", "sales.contoso"),
                ("http://research.fabrikam.org/support#top", "research.fabrikam")
            ],
            ["https://labs.northwind.net/catalog/list?page=2#details"]);

        result.Should().BeEquivalentTo(["labs.northwind"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_WebAddressDomainStem_PreservesStemBeforeFinalTldForMultiLabelPublicSuffixes()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.contoso.com.au/products?id=1", "shop.contoso.com"),
                ("http://research.fabrikam.co.uk/support#top", "research.fabrikam.co")
            ],
            ["https://labs.northwind.net.au/catalog/list?page=2#details"]);

        result.Should().BeEquivalentTo(["labs.northwind.net"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_WebAddressDomainStem_LeavesNonCuratedCcTldSuffixBehaviorUnchanged()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.contoso.store.uk/path", "store"),
                ("http://portal.fabrikam.market.au/x", "market")
            ],
            ["https://checkout.northwind.retail.uk/cart", "http://service.adatum.portal.au/path"]);

        result.Should().BeEquivalentTo(["retail", "portal"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_WebAddressDomainStem_ReturnsNullWhenRootStemRemainingHostHasNoSuffix()
    {
        var result = FlashFillService.Fill(
            [
                ("https://eu.sales.contoso.com/path", "contoso"),
                ("http://research.fabrikam.org/x", "fabrikam")
            ],
            ["http://localhost/x"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_WebAddressDomainSuffix_ExtractsSingleAndCuratedMultiLabelPublicSuffixes()
    {
        var result = FlashFillService.Fill(
            [
                ("https://www.contoso.com/reports", "com"),
                ("https://shop.northwind.co.uk/path", "co.uk")
            ],
            ["https://portal.fabrikam.com.au/a"]);

        result.Should().BeEquivalentTo(["com.au"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("https://localhost/a")]
    [InlineData("https://intranet.local/a")]
    [InlineData("https://contoso.invalid/a")]
    [InlineData("ftp://contoso.com/a")]
    [InlineData("https://user@contoso.com/a")]
    public void Fill_WebAddressDomainSuffix_ReturnsNullForInvalidNoSuffixIntranetOrUnsupportedRemainingUrl(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://www.contoso.com/reports", "com"),
                ("https://shop.northwind.co.uk/path", "co.uk")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentStem_StripsQueryFragmentAndExtension()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road-bike.html", "road-bike"),
                ("https://docs.example.com/help/safety-guide.pdf?download=v1.2#top", "safety-guide")
            ],
            ["https://example.com/releases/FreeX-Setup.exe?channel=stable#download"]);

        result.Should().BeEquivalentTo(["FreeX-Setup"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentStem_ReturnsNullForHostOnlyRemainingUrl()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road-bike.html", "road-bike"),
                ("https://docs.example.com/help/safety-guide.pdf?download=1#top", "safety-guide")
            ],
            ["https://example.com"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentStem_ReturnsNullForEmptyRemainingUrlPathSegment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road-bike.html", "road-bike"),
                ("https://docs.example.com/help/safety-guide.pdf?download=1#top", "safety-guide")
            ],
            ["https://example.com/releases/?download=1#top"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentStem_ReturnsNullForHostOnlyExample()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road-bike.html", "road-bike"),
                ("https://example.com", "example")
            ],
            ["https://example.com/releases/FreeX-Setup.exe"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlTrackingQueryCleanup_RemovesMarketingQueryAndFragment()
    {
        var result = FlashFillService.Fill(
            [
                (
                    "https://shop.contoso.example/products/road-bike?utm_source=newsletter&utm_medium=email#hero",
                    "https://shop.contoso.example/products/road-bike"
                ),
                (
                    "https://fabrikam.example/docs/safety-guide.pdf?gclid=abc123;utm_campaign=spring",
                    "https://fabrikam.example/docs/safety-guide.pdf"
                )
            ],
            [
                "https://northwind.example/catalog/electric-cargo-bike?utm_content=cta&msclkid=xyz#details",
                "www.adatum.example/reports/q1?mc_cid=mail-42&mc_eid=user-17"
            ]);

        result.Should().BeEquivalentTo(
            [
                "https://northwind.example/catalog/electric-cargo-bike",
                "www.adatum.example/reports/q1"
            ],
            o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlTrackingQueryCleanup_ReturnsNullWhenRemainingHasBusinessQueryParameters()
    {
        var result = FlashFillService.Fill(
            [
                (
                    "https://shop.contoso.example/products/road-bike?utm_source=newsletter&utm_medium=email#hero",
                    "https://shop.contoso.example/products/road-bike"
                )
            ],
            ["https://northwind.example/order?id=123&utm_source=email"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentRawSlugStem_ExtractsExtensionlessDecodedFinalSegment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road-bike?ref=nav#top", "road-bike"),
                ("https://docs.example/help/safety%20guide?download=1#read", "safety guide")
            ],
            ["https://example.com/releases/electric_cargo-bike?x=1#details"]);

        result.Should().BeEquivalentTo(["electric_cargo-bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentRawSlugStem_PreservesPlusInPathSegments()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road+bike?ref=nav#top", "road+bike"),
                ("https://docs.example/help/safety+guide?download=1#read", "safety+guide")
            ],
            ["https://example.com/releases/electric+cargo-bike?x=1#details"]);

        result.Should().BeEquivalentTo(["electric+cargo-bike"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("ftp://example.com/releases/electric-cargo-bike")]
    [InlineData("https://user@example.com/releases/electric-cargo-bike")]
    [InlineData("https://example.com")]
    [InlineData("https://example.com?x=1#details")]
    [InlineData("https://example.com/releases/")]
    [InlineData("https://example.com/releases/%ZZ")]
    [InlineData("https://example.com/releases/electric-cargo-bike.html")]
    public void Fill_FinalUrlPathSegmentRawSlugStem_ReturnsNullForUnsupportedRemainingUrl(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road-bike?ref=nav#top", "road-bike"),
                ("https://docs.example/help/safety%20guide?download=1#read", "safety guide")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentSlugTitle_ConvertsUrlSlugStemToTitle()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road-bike.html", "Road Bike"),
                ("https://docs.example.com/help/safety-guide.pdf?download=1#top", "Safety Guide")
            ],
            ["https://example.com/releases/electric-cargo-bike?x=1#details"]);

        result.Should().BeEquivalentTo(["Electric Cargo Bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentSlugTitle_SplitsCamelCaseInsideSlugStem()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/roadBike.html", "Road Bike"),
                ("https://docs.example.com/help/safetyGuide.pdf?download=1#top", "Safety Guide")
            ],
            ["https://example.com/releases/electricCargoBike?x=1#details"]);

        result.Should().BeEquivalentTo(["Electric Cargo Bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentSlugTitle_SplitsCamelCaseInsideMixedSeparatedSlugStem()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road-bikePro.html", "Road Bike Pro"),
                ("https://docs.example.com/help/safety_gearGuide.pdf?download=1#top", "Safety Gear Guide")
            ],
            ["https://example.com/releases/electric-cargoBike?x=1#details"]);

        result.Should().BeEquivalentTo(["Electric Cargo Bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentSlugTitle_HandlesSegmentsWithoutExtensions()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road-bike", "Road Bike"),
                ("https://docs.example.com/help/safety-guide", "Safety Guide")
            ],
            ["https://example.com/releases/electric-cargo-bike"]);

        result.Should().BeEquivalentTo(["Electric Cargo Bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentSlugTitle_HandlesUnderscoresAndDecodedSpaces()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road_bike.html", "Road Bike"),
                ("https://docs.example.com/help/safety%20guide.pdf", "Safety Guide")
            ],
            ["https://example.com/releases/electric_cargo%20bike"]);

        result.Should().BeEquivalentTo(["Electric Cargo Bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FinalUrlPathSegmentSlugTitle_PreservesDigitsInsideWords()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road-bike2.html", "Road Bike2"),
                ("https://docs.example.com/help/safety2-guide.pdf", "Safety2 Guide")
            ],
            ["https://example.com/releases/electric2-cargo-bike"]);

        result.Should().BeEquivalentTo(["Electric2 Cargo Bike"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("ftp://example.com/releases/electric-cargo-bike")]
    [InlineData("https://user@example.com/releases/electric-cargo-bike")]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/releases/")]
    [InlineData("https://example.com/releases/%ZZ")]
    [InlineData("https://example.com/releases/2026")]
    [InlineData("https://example.com/releases/electric--cargo-bike")]
    public void Fill_FinalUrlPathSegmentSlugTitle_ReturnsNullForDubiousRemainingUrl(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/products/road-bike.html", "Road Bike"),
                ("https://docs.example.com/help/safety-guide.pdf?download=1#top", "Safety Guide")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_ParentUrlPathSegment_ExtractsDecodedPenultimateSegment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/catalog/bikes/road-bike.html?ref=nav", "bikes"),
                ("https://northwind.example/store/helmets/trail-helmet.html#details", "helmets")
            ],
            ["https://adatum.example/shop/skis/powder-ski.html?color=blue"]);

        result.Should().BeEquivalentTo(["skis"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ParentUrlPathSegment_DecodesPercentEscapes()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/catalog/spring%20deals/road-bike.html?ref=nav", "spring deals"),
                ("https://northwind.example/store/trail%20helmets/trail-helmet.html#details", "trail helmets")
            ],
            ["https://adatum.example/shop/powder%20skis/powder-ski.html?color=blue"]);

        result.Should().BeEquivalentTo(["powder skis"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ParentUrlPathSegment_PreservesPlusInPathSegments()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/catalog/bike+deals/road-bike.html?ref=nav", "bike+deals"),
                ("https://northwind.example/store/trail+helmets/trail-helmet.html#details", "trail+helmets")
            ],
            ["https://adatum.example/shop/powder+skis/powder-ski.html?color=blue"]);

        result.Should().BeEquivalentTo(["powder+skis"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("https://adatum.example")]
    [InlineData("https://adatum.example/powder-ski.html")]
    [InlineData("https://adatum.example/shop/skis/")]
    [InlineData("ftp://adatum.example/shop/skis/powder-ski.html")]
    [InlineData("https://user@adatum.example/shop/skis/powder-ski.html")]
    public void Fill_ParentUrlPathSegment_ReturnsNullForUnsupportedRemainingUrl(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/catalog/bikes/road-bike.html?ref=nav", "bikes"),
                ("https://northwind.example/store/helmets/trail-helmet.html#details", "helmets")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_FirstUrlPathSegment_ExtractsDecodedLeadingSegment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/catalog/bikes/road-bike.html?ref=nav", "catalog"),
                ("https://northwind.example/store/helmets/trail-helmet.html#details", "store")
            ],
            ["https://adatum.example/shop/skis/powder-ski.html?color=blue"]);

        result.Should().BeEquivalentTo(["shop"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FirstUrlPathSegment_DecodesPercentEscapes()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/spring%20catalog/bikes/road-bike.html?ref=nav", "spring catalog"),
                ("https://northwind.example/helmet%20store/helmets/trail-helmet.html#details", "helmet store")
            ],
            ["https://adatum.example/ski%20shop/skis/powder-ski.html?color=blue"]);

        result.Should().BeEquivalentTo(["ski shop"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FirstUrlPathSegment_PreservesPlusInPathSegments()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/catalog+sale/bikes/road-bike.html?ref=nav", "catalog+sale"),
                ("https://northwind.example/store+gear/helmets/trail-helmet.html#details", "store+gear")
            ],
            ["https://adatum.example/shop+snow/skis/powder-ski.html?color=blue"]);

        result.Should().BeEquivalentTo(["shop+snow"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("https://adatum.example")]
    [InlineData("https://adatum.example/")]
    [InlineData("https://adatum.example?color=blue")]
    [InlineData("ftp://adatum.example/shop/skis/powder-ski.html")]
    [InlineData("https://user@adatum.example/shop/skis/powder-ski.html")]
    public void Fill_FirstUrlPathSegment_ReturnsNullForUnsupportedRemainingUrl(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/catalog/bikes/road-bike.html?ref=nav", "catalog"),
                ("https://northwind.example/store/helmets/trail-helmet.html#details", "store")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_SecondUrlPathSegment_ExtractsDecodedSecondSegment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/regions/north/bikes/road-bike.html?ref=nav", "north"),
                ("https://northwind.example/markets/south/helmets/trail-helmet.html#details", "south")
            ],
            ["https://adatum.example/areas/west/skis/powder-ski.html?color=blue"]);

        result.Should().BeEquivalentTo(["west"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_SecondUrlPathSegment_DecodesPercentEscapes()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/regions/north%20east/bikes/road-bike.html?ref=nav", "north east"),
                ("https://northwind.example/markets/south%20west/helmets/trail-helmet.html#details", "south west")
            ],
            ["https://adatum.example/areas/mountain%20west/skis/powder-ski.html?color=blue"]);

        result.Should().BeEquivalentTo(["mountain west"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_SecondUrlPathSegment_PreservesPlusInPathSegments()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/regions/north+east/bikes/road-bike.html?ref=nav", "north+east"),
                ("https://northwind.example/markets/south+west/helmets/trail-helmet.html#details", "south+west")
            ],
            ["https://adatum.example/areas/mountain+west/skis/powder-ski.html?color=blue"]);

        result.Should().BeEquivalentTo(["mountain+west"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("https://adatum.example")]
    [InlineData("https://adatum.example/areas")]
    [InlineData("https://adatum.example/areas/")]
    [InlineData("ftp://adatum.example/areas/west/skis/powder-ski.html")]
    [InlineData("https://user@adatum.example/areas/west/skis/powder-ski.html")]
    public void Fill_SecondUrlPathSegment_ReturnsNullForUnsupportedRemainingUrl(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/regions/north/bikes/road-bike.html?ref=nav", "north"),
                ("https://northwind.example/markets/south/helmets/trail-helmet.html#details", "south")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_ParentUrlPathSegmentTitle_TitleizesDecodedPenultimateSegment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/catalog/bike-deals/road-bike.html", "Bike Deals"),
                ("https://northwind.example/store/trail_helmets/trail-helmet.html", "Trail Helmets")
            ],
            ["https://adatum.example/shop/powder-skis/powder-ski.html"]);

        result.Should().BeEquivalentTo(["Powder Skis"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FirstUrlPathSegmentTitle_TitleizesDecodedLeadingSegment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/spring-catalog/bikes/road-bike.html", "Spring Catalog"),
                ("https://northwind.example/helmet_store/helmets/trail-helmet.html", "Helmet Store")
            ],
            ["https://adatum.example/ski-shop/skis/powder-ski.html"]);

        result.Should().BeEquivalentTo(["Ski Shop"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlPathSegmentTitle_TitleizesPercentDecodedSpaces()
    {
        var parentResult = FlashFillService.Fill(
            [
                ("https://contoso.example/catalog/bike%20deals/road-bike.html", "Bike Deals"),
                ("https://northwind.example/store/trail%20helmets/trail-helmet.html", "Trail Helmets")
            ],
            ["https://adatum.example/shop/powder%20skis/powder-ski.html"]);

        parentResult.Should().BeEquivalentTo(["Powder Skis"], o => o.WithStrictOrdering());

        var firstResult = FlashFillService.Fill(
            [
                ("https://contoso.example/spring%20catalog/bikes/road-bike.html", "Spring Catalog"),
                ("https://northwind.example/helmet%20store/helmets/trail-helmet.html", "Helmet Store")
            ],
            ["https://adatum.example/ski%20shop/skis/powder-ski.html"]);

        firstResult.Should().BeEquivalentTo(["Ski Shop"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ParentUrlPathSegmentTitle_ReturnsNullForPlusPathSegment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/catalog/bike-deals/road-bike.html", "Bike Deals"),
                ("https://northwind.example/store/trail_helmets/trail-helmet.html", "Trail Helmets")
            ],
            ["https://adatum.example/shop/powder+skis/powder-ski.html"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_FirstUrlPathSegmentTitle_ReturnsNullForPlusPathSegment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/spring-catalog/bikes/road-bike.html", "Spring Catalog"),
                ("https://northwind.example/helmet_store/helmets/trail-helmet.html", "Helmet Store")
            ],
            ["https://adatum.example/ski+shop/skis/powder-ski.html"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_SecondUrlPathSegmentTitle_TitleizesDecodedSecondSegment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/regions/north-america/bikes/road-bike.html", "North America"),
                ("https://northwind.example/markets/south_america/helmets/trail-helmet.html", "South America")
            ],
            ["https://adatum.example/areas/west-europe/skis/powder-ski.html"]);

        result.Should().BeEquivalentTo(["West Europe"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_SecondUrlPathSegmentTitle_TitleizesPercentDecodedSpaces()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/regions/north%20america/bikes/road-bike.html", "North America"),
                ("https://northwind.example/markets/south%20america/helmets/trail-helmet.html", "South America")
            ],
            ["https://adatum.example/areas/west%20europe/skis/powder-ski.html"]);

        result.Should().BeEquivalentTo(["West Europe"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_SecondUrlPathSegmentTitle_ReturnsNullForPlusPathSegment()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/regions/north-america/bikes/road-bike.html", "North America"),
                ("https://northwind.example/markets/south_america/helmets/trail-helmet.html", "South America")
            ],
            ["https://adatum.example/areas/west+europe/skis/powder-ski.html"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_ExtractsDecodedParameterValue()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road%20bike&sort=asc", "road bike"),
                ("http://fabrikam.example/find?sort=desc&q=gravel+bike#results", "gravel bike")
            ],
            ["https://northwind.example/catalog?sort=popular&q=electric%20cargo+bike"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFirstQueryParameterName_ExtractsDecodedFirstParameterName()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.com/search?campaign=spring&source=email", "campaign"),
                ("https://fabrikam.example/items?region=west;sort=asc", "region")
            ],
            ["https://northwind.test/page?product=bike&ref=nav"]);

        result.Should().BeEquivalentTo(["product"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFirstQueryParameterName_DecodesEncodedParameterName()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?utm%5Fcampaign=spring&source=email", "utm_campaign"),
                ("https://fabrikam.example/items?region%5Fcode=west;sort=asc", "region_code")
            ],
            ["https://northwind.test/page?product%5Fid=bike&ref=nav"]);

        result.Should().BeEquivalentTo(["product_id"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFirstQueryParameterName_DecodesPlusInParameterName()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?utm+campaign=spring&source=email", "utm campaign"),
                ("https://fabrikam.example/items?region+code=west;sort=asc", "region code")
            ],
            ["https://northwind.test/page?product+id=bike&ref=nav"]);

        result.Should().BeEquivalentTo(["product id"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("https://northwind.test/page")]
    [InlineData("https://northwind.test/page?=bike&ref=nav")]
    [InlineData("ftp://northwind.test/page?product=bike&ref=nav")]
    [InlineData("https://user@northwind.test/page?product=bike&ref=nav")]
    [InlineData("https://localhost/page?product=bike&ref=nav")]
    [InlineData("northwind.test/page?product=bike&ref=nav")]
    public void Fill_UrlFirstQueryParameterName_ReturnsNullForUnsupportedRemainingUrl(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.com/search?campaign=spring&source=email", "campaign"),
                ("https://fabrikam.example/items?region=west;sort=asc", "region")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlFirstQueryParameterName_TakesPrecedenceWhenLastNameDiffers()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.com/search?campaign=spring&source=email", "campaign"),
                ("https://fabrikam.example/items?region=west;sort=asc", "region")
            ],
            ["https://northwind.test/page?product=bike&ref=nav"]);

        result.Should().BeEquivalentTo(["product"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterName_ExtractsLastNameAcrossAmpersandSemicolonAndEmptySegments()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.com/search?campaign=spring&&source", "source"),
                ("https://fabrikam.example/items?region=west;;sort", "sort")
            ],
            [
                "https://northwind.test/page?product=bike&&ref#details",
                "https://adatum.example/page?category=tools;;page"
            ]);

        result.Should().BeEquivalentTo(["ref", "page"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterName_DecodesEncodedAndPlusNames()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road&utm%5Fcampaign=spring", "utm_campaign"),
                ("https://fabrikam.example/items?term=gravel;region+code=west", "region code")
            ],
            ["https://northwind.test/page?product=bike&sort+order=asc"]);

        result.Should().BeEquivalentTo(["sort order"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterName_HandlesBareWebAddressesWithQueryStrings()
    {
        var result = FlashFillService.Fill(
            [
                ("www.contoso.com/search?sku=A-100&source=web", "source"),
                ("fabrikam.org/products?item=B-200;channel=mail", "channel")
            ],
            ["northwind.net/catalog?product=C-300&medium=ad"]);

        result.Should().BeEquivalentTo(["medium"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterName_TrimsFragments()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?first=one&source=mail#results", "source"),
                ("https://fabrikam.example/find?left=two;sort=asc#top", "sort")
            ],
            ["https://northwind.test/page?product=bike&ref=nav#details"]);

        result.Should().BeEquivalentTo(["ref"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterNameTitle_TitleizesDecodedQueryParameterNames()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?product-category=powder-skis&sort=popular", "Product Category"),
                ("https://shop.example.com/search?shipping_option=ground&sort=popular", "Shipping Option")
            ],
            ["https://shop.example.com/search?promoCode=spring&sort=popular"]);

        result.Should().BeEquivalentTo(["Promo Code"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterNameTitle_TitleizesPercentAndPlusDecodedNames()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?product+category=powder-skis&sort=popular", "Product Category"),
                ("https://shop.example.com/search?shipping%20option=ground&sort=popular", "Shipping Option")
            ],
            [
                "https://shop.example.com/search?promo_code=spring&sort=popular",
                "https://shop.example.com/search?discountRate=10&sort=popular"
            ]);

        result.Should().BeEquivalentTo(["Promo Code", "Discount Rate"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterNameTitle_ReturnsNullForMissingOrEmptyQueryName()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?product-category=powder-skis&sort=popular", "Product Category"),
                ("https://shop.example.com/search?shipping_option=ground&sort=popular", "Shipping Option")
            ],
            ["https://shop.example.com/search?=spring&sort=popular"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlQueryParameterNameTitle_ReturnsNullForMultipleMatchingExampleCandidates()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?product-category=powder-skis&product_category=skis", "Product Category"),
                ("https://shop.example.com/search?shipping-option=ground&shipping_option=air", "Shipping Option")
            ],
            ["https://shop.example.com/search?promoCode=spring&sort=popular"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlQueryParameterNameTitle_ReturnsNullWhenMatchingNameIsNotFirstTitleizableName()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?sort-order=popular&product-category=powder-skis&page-size=20", "Product Category"),
                ("https://shop.example.com/search?delivery-method=ground&shipping_option=air&sortOrder=desc", "Shipping Option")
            ],
            ["https://shop.example.com/search?promoCode=spring&sort=popular"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlQueryParameterNameTitle_LeavesRawQueryNameExtractionPrecedence()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?product+category=powder-skis&sort=popular", "product category"),
                ("https://shop.example.com/search?shipping%20option=ground&sort=popular", "shipping option")
            ],
            ["https://shop.example.com/search?promo+code=spring&sort=popular"]);

        result.Should().BeEquivalentTo(["promo code"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterNameTitle_TitleizesDecodedLastQueryParameterNames()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?sort=popular&product-category=powder-skis", "Product Category"),
                ("https://shop.example.com/search?sort=popular&shipping_option=ground", "Shipping Option")
            ],
            ["https://shop.example.com/search?sort=popular&promoCode=spring"]);

        result.Should().BeEquivalentTo(["Promo Code"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterNameTitle_TitleizesPercentAndPlusDecodedNames()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?sort=popular&product+category=powder-skis", "Product Category"),
                ("https://shop.example.com/search?sort=popular&shipping%20option=ground", "Shipping Option")
            ],
            [
                "https://shop.example.com/search?sort=popular&promo_code=spring",
                "https://shop.example.com/search?sort=popular&discountRate=10"
            ]);

        result.Should().BeEquivalentTo(["Promo Code", "Discount Rate"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterNameTitle_ReturnsNullForMissingOrEmptyLastQueryName()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?sort=popular&product-category=powder-skis", "Product Category"),
                ("https://shop.example.com/search?sort=popular&shipping_option=ground", "Shipping Option")
            ],
            [
                "https://shop.example.com/search?sort=popular&",
                "https://shop.example.com/search?sort=popular&=spring"
            ]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlLastQueryParameterNameTitle_ReturnsNullForDuplicateMatchingExampleCandidates()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?sort=popular&product-category=powder-skis&product_category=skis", "Product Category"),
                ("https://shop.example.com/search?sort=popular&shipping-option=ground&shipping_option=air", "Shipping Option")
            ],
            ["https://shop.example.com/search?sort=popular&promoCode=spring"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlLastQueryParameterNameTitle_LeavesRawLastQueryNameExtractionPrecedence()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?sort=popular&product+category=powder-skis", "product category"),
                ("https://shop.example.com/search?sort=popular&shipping%20option=ground", "shipping option")
            ],
            ["https://shop.example.com/search?sort=popular&promo+code=spring"]);

        result.Should().BeEquivalentTo(["promo code"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFirstQueryParameterValue_TakesPrecedenceWhenLastNameAlsoMatchesExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?alpha=tail&tail=ignored", "tail"),
                ("https://fabrikam.example/find?beta=end;end=ignored", "end")
            ],
            ["https://northwind.example/catalog?gamma=winner&final=ignored"]);

        result.Should().BeEquivalentTo(["winner"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_TakesPrecedenceWhenLastNameAlsoMatchesExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?target=tail&tail=ignored", "tail"),
                ("https://fabrikam.example/find?target=end;end=ignored", "end")
            ],
            ["https://northwind.example/catalog?target=winner&final=ignored"]);

        result.Should().BeEquivalentTo(["winner"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("https://northwind.test/page")]
    [InlineData("https://northwind.test/page?first=ok&=blank")]
    [InlineData("https://northwind.test/page?first=ok&+=blank")]
    [InlineData("https://northwind.test/page?first=ok&bad%ZZ=value")]
    [InlineData("ftp://northwind.test/page?first=ok&last=value")]
    [InlineData("https://user@northwind.test/page?first=ok&last=value")]
    [InlineData("https://localhost/page?first=ok&last=value")]
    public void Fill_UrlLastQueryParameterName_ReturnsNullForUnsupportedOrBlankRemainingUrl(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?first=one&source=mail", "source"),
                ("https://fabrikam.example/find?left=two;sort=asc", "sort")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlLastQueryParameterValue_ExtractsDecodedLastValueAcrossAmpersandQueries()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?first=skip&category=road%20bike", "road bike"),
                ("https://fabrikam.example/find?source=mail&item=gravel%20bike", "gravel bike")
            ],
            ["https://northwind.example/catalog?ref=nav&product=electric%20cargo+bike"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterValue_ExtractsDecodedLastValueAcrossSemicolonQueries()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?first=skip;category=road%20bike", "road bike"),
                ("https://fabrikam.example/find?source=mail;item=gravel%20bike", "gravel bike")
            ],
            ["https://northwind.example/catalog?ref=nav;product=electric%20cargo+bike"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterValue_PreservesEncodedSemicolonsInsideValues()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?first=skip&category=road%3Bbike", "road;bike"),
                ("https://fabrikam.example/find?source=mail&item=gravel%3Bbike", "gravel;bike")
            ],
            ["https://northwind.example/catalog?ref=nav&product=electric%3Bcargo+bike"]);

        result.Should().BeEquivalentTo(["electric;cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterValue_DecodesPlusAsSpace()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?first=skip&category=road+bike", "road bike"),
                ("https://fabrikam.example/find?source=mail&item=gravel+bike", "gravel bike")
            ],
            ["https://northwind.example/catalog?ref=nav&product=electric+cargo+bike"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterValue_HandlesBareWebAddressesWithQueryStrings()
    {
        var result = FlashFillService.Fill(
            [
                ("www.contoso.example/search?first=skip&category=A-100", "A-100"),
                ("fabrikam.example/find?source=mail;item=B-200", "B-200")
            ],
            ["northwind.example/catalog?ref=nav&product=C-300"]);

        result.Should().BeEquivalentTo(["C-300"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterValue_TrimsFragments()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?first=skip&category=road+bike#results", "road bike"),
                ("https://fabrikam.example/find?source=mail;item=gravel+bike#top", "gravel bike")
            ],
            ["https://northwind.example/catalog?ref=nav&product=electric+cargo+bike#details"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlLastQueryParameterValue_UsesPreviousNonEmptyValueWhenLaterValuesAreBlank()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?first=skip&category=road+bike&empty=", "road bike"),
                ("https://fabrikam.example/find?source=mail;item=gravel+bike;empty=%20", "gravel bike")
            ],
            [
                "https://northwind.example/catalog?ref=nav&product=electric+cargo+bike&empty=",
                "https://adatum.example/catalog?ref=nav;sku=Touring+Bike;empty=+"
            ]);

        result.Should().BeEquivalentTo(["electric cargo bike", "Touring Bike"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://northwind.example/catalog")]
    [InlineData("https://northwind.example/catalog?product=&empty=+")]
    [InlineData("https://northwind.example/catalog?ref=nav&product=electric%ZZbike")]
    [InlineData("ftp://northwind.example/catalog?ref=nav&product=electric+bike")]
    [InlineData("https://user@northwind.example/catalog?ref=nav&product=electric+bike")]
    [InlineData("https://localhost/catalog?ref=nav&product=electric+bike")]
    public void Fill_UrlLastQueryParameterValue_ReturnsNullForUnsupportedInvalidOrBlankRemainingUrl(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?first=skip&category=road+bike", "road bike"),
                ("https://fabrikam.example/find?source=mail&item=gravel+bike", "gravel bike")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlLastQueryParameterName_TakesPrecedenceWhenLastValueAlsoMatchesExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?first=skip&source=source", "source"),
                ("https://fabrikam.example/find?source=skip&channel=channel", "channel")
            ],
            ["https://northwind.example/catalog?first=skip&ref=value"]);

        result.Should().BeEquivalentTo(["ref"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFirstQueryParameterValue_ExtractsDecodedFirstValuesAcrossDifferentParameterNames()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road%20bike&sort=asc", "road bike"),
                ("https://fabrikam.example/find?term=gravel%20bike#results", "gravel bike")
            ],
            ["https://northwind.example/catalog?product=electric%20cargo+bike&sort=popular"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFirstQueryParameterValue_UsesFirstLaterNonEmptyValue()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=&term=road+bike&sort=asc", "road bike"),
                ("https://fabrikam.example/find?source=;item=gravel%20bike#results", "gravel bike")
            ],
            ["https://northwind.example/catalog?ref=&product=electric%20cargo+bike&sort=popular"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFirstQueryParameterValue_ExtractsSemicolonSeparatedDecodedFirstValue()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road+bike;sort=asc", "road bike"),
                ("https://fabrikam.example/find?term=gravel%20bike;page=2", "gravel bike")
            ],
            ["https://northwind.example/catalog?product=electric%20cargo+bike;sort=popular"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFirstQueryParameterValue_PreservesEncodedSemicolonsInsideValues()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road%3Bbike;sort=asc", "road;bike"),
                ("https://fabrikam.example/find?term=gravel%3Bbike;page=2", "gravel;bike")
            ],
            ["https://northwind.example/catalog?product=electric%3Bcargo+bike;sort=popular"]);

        result.Should().BeEquivalentTo(["electric;cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFirstQueryParameterValue_DecodesPlusAsSpace()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road+bike&sort=asc", "road bike"),
                ("https://fabrikam.example/find?term=gravel+bike#results", "gravel bike")
            ],
            ["https://northwind.example/catalog?product=electric+cargo+bike&sort=popular"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFirstQueryParameterValue_HandlesBareWebAddressesWithQueryStrings()
    {
        var result = FlashFillService.Fill(
            [
                ("www.contoso.com/search?sku=A-100&source=web", "A-100"),
                ("fabrikam.org/products?item=B-200&source=mail", "B-200")
            ],
            ["northwind.net/catalog?product=C-300&source=ad"]);

        result.Should().BeEquivalentTo(["C-300"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_PrefersStableParameterNameOverFirstValue()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road+bike&sort=asc", "road bike"),
                ("https://fabrikam.example/find?q=gravel+bike&sort=desc", "gravel bike")
            ],
            ["https://northwind.example/catalog?sort=popular&q=electric+cargo+bike"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("https://northwind.example/catalog")]
    [InlineData("https://northwind.example/catalog?product=&sort=")]
    [InlineData("https://northwind.example/catalog?product=+")]
    [InlineData("https://northwind.example/catalog?product=electric%ZZbike")]
    [InlineData("ftp://northwind.example/catalog?product=electric+bike")]
    [InlineData("https://user@northwind.example/catalog?product=electric+bike")]
    public void Fill_UrlFirstQueryParameterValue_ReturnsNullForUnsupportedOrBlankRemainingUrl(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road+bike&sort=asc", "road bike"),
                ("https://fabrikam.example/find?term=gravel+bike#results", "gravel bike")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_ExtractsSemicolonSeparatedDecodedParameterValue()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road+bike;sort=asc", "road bike"),
                ("https://fabrikam.example/find?sort=desc;q=gravel%20bike#results", "gravel bike")
            ],
            ["https://northwind.example/catalog?sort=popular;q=electric%20cargo+bike"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_PreservesEncodedSemicolonsInsideValues()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road%3Bbike;sort=asc", "road;bike"),
                ("https://fabrikam.example/find?sort=desc;q=gravel%3Bbike#results", "gravel;bike")
            ],
            ["https://northwind.example/catalog?sort=popular;q=electric%3Bcargo+bike"]);

        result.Should().BeEquivalentTo(["electric;cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_HandlesBareWebAddressesWithQueryStrings()
    {
        var result = FlashFillService.Fill(
            [
                ("www.contoso.com/search?sku=A-100&source=web", "A-100"),
                ("fabrikam.org/products?source=mail&sku=B-200", "B-200")
            ],
            ["northwind.net/catalog?source=ad&sku=C-300"]);

        result.Should().BeEquivalentTo(["C-300"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_UsesFirstNonEmptyRepeatedParameterValue()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/items?tag=&tag=alpha&tag=omega", "alpha"),
                ("https://fabrikam.example/items?tag=beta&tag=gamma", "beta")
            ],
            ["https://northwind.example/items?tag=&tag=delta&tag=epsilon"]);

        result.Should().BeEquivalentTo(["delta"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_UsesLastNonEmptyRepeatedParameterValueWhenExamplesAreDistinct()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/items?tag=alpha&tag=omega&tag=&sort=asc", "omega"),
                ("https://fabrikam.example/items?tag=beta&sort=desc&tag=gamma&tag=+&view=1", "gamma")
            ],
            ["https://northwind.example/items?tag=&tag=delta&sort=x&tag=epsilon&tag=%20&view=1"]);

        result.Should().BeEquivalentTo(["epsilon"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_UsesLastRepeatedParameterValueAcrossSemicolonsAndDecoding()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/items?tag=alpha;tag=road+bike;sort=asc", "road bike"),
                ("https://fabrikam.example/items?tag=beta;sort=desc;tag=gravel%20bike;view=1", "gravel bike")
            ],
            ["https://northwind.example/items?tag=delta;sort=x;tag=electric%3Bcargo+bike;view=1"]);

        result.Should().BeEquivalentTo(["electric;cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterValueTitle_TitleizesStableQueryParameterValues()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?category=powder-skis&sort=popular", "Powder Skis"),
                ("https://shop.example.com/search?category=trail-running&sort=popular", "Trail Running")
            ],
            ["https://shop.example.com/search?category=road-bike&sort=popular"]);

        result.Should().BeEquivalentTo(["Road Bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterValueTitle_TitleizesDecodedPlusPercentUnderscoreAndCamelValues()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?category=powder+skis&sort=popular", "Powder Skis"),
                ("https://shop.example.com/search?category=trail%20running&sort=popular", "Trail Running")
            ],
            [
                "https://shop.example.com/search?category=road_bike&sort=popular",
                "https://shop.example.com/search?category=electricCargoBike&sort=popular"
            ]);

        result.Should().BeEquivalentTo(["Road Bike", "Electric Cargo Bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterValueTitle_ReturnsNullWhenExamplesUseDifferentParameterNames()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?category=powder-skis&sort=popular", "Powder Skis"),
                ("https://shop.example.com/search?item=trail-running&sort=popular", "Trail Running")
            ],
            ["https://shop.example.com/search?category=road-bike&sort=popular"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlQueryParameterValueTitle_ReturnsNullForMissingRemainingParameterValue()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?category=powder-skis&sort=popular", "Powder Skis"),
                ("https://shop.example.com/search?category=trail-running&sort=popular", "Trail Running")
            ],
            ["https://shop.example.com/search?category=&sort=popular"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlQueryParameterValueTitle_ReturnsNullForMultipleMatchingExampleCandidates()
    {
        var result = FlashFillService.Fill(
            [
                ("https://shop.example.com/search?category=powder-skis&item=powder-skis", "Powder Skis"),
                ("https://shop.example.com/search?category=trail-running&item=trail-running", "Trail Running")
            ],
            ["https://shop.example.com/search?category=road-bike&item=road-bike"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlLastQueryParameterValue_TakesPrecedenceWhenLastRepeatedParameterValueAlsoMatchesExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/items?tag=alpha&tag=omega", "omega"),
                ("https://fabrikam.example/items?tag=beta&tag=gamma", "gamma")
            ],
            ["https://northwind.example/items?tag=delta&tag=epsilon&sort=x"]);

        result.Should().BeEquivalentTo(["x"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_ReturnsNullWhenRemainingParameterIsMissing()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road+bike", "road bike"),
                ("https://fabrikam.example/search?q=gravel+bike", "gravel bike")
            ],
            ["https://northwind.example/search?term=electric+bike"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_ReturnsNullForBlankRemainingValue()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road+bike", "road bike"),
                ("https://fabrikam.example/search?q=gravel+bike", "gravel bike")
            ],
            ["https://northwind.example/search?q="]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_ReturnsNullForUnsupportedRemainingScheme()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road+bike", "road bike"),
                ("https://fabrikam.example/search?q=gravel+bike", "gravel bike")
            ],
            ["ftp://northwind.example/search?q=electric+bike"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_ReturnsNullForUserInfoRemainingUrl()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?q=road+bike", "road bike"),
                ("https://fabrikam.example/search?q=gravel+bike", "gravel bike")
            ],
            ["https://user@northwind.example/search?q=electric+bike"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlQueryParameterValue_ReturnsNullWhenDifferentParameterNamesAreNotConsistentlyFirstValues()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/search?sort=asc&q=road+bike", "road bike"),
                ("https://fabrikam.example/search?term=gravel+bike&sort=desc", "gravel bike")
            ],
            ["https://northwind.example/search?q=electric+bike"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlFragmentValue_ExtractsFragmentIdentifier()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.com/docs/report#section-2", "section-2"),
                ("https://fabrikam.example/path#summary", "summary")
            ],
            ["https://northwind.test/page#appendix"]);

        result.Should().BeEquivalentTo(["appendix"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFragmentValue_ExtractsDecodedFragmentIdentifier()
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.example/docs/report#sales%20summary", "sales summary"),
                ("https://fabrikam.example/path#road+bike", "road bike")
            ],
            ["https://northwind.test/page#electric%20cargo+bike"]);

        result.Should().BeEquivalentTo(["electric cargo bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFragmentValueTitle_TitleizesDecodedFragmentIdentifier()
    {
        var result = FlashFillService.Fill(
            [
                ("https://docs.example.com/help#powder-skis", "Powder Skis"),
                ("https://docs.example.com/help#trail_running", "Trail Running")
            ],
            ["https://docs.example.com/help#road-bike"]);

        result.Should().BeEquivalentTo(["Road Bike"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_UrlFragmentValueTitle_TitleizesDecodedPlusPercentAndCamelFragments()
    {
        var result = FlashFillService.Fill(
            [
                ("https://docs.example.com/help#powder+skis", "Powder Skis"),
                ("https://docs.example.com/help#trail%20running", "Trail Running")
            ],
            [
                "https://docs.example.com/help#roadBike",
                "https://docs.example.com/help#electric%20cargo-bike"
            ]);

        result.Should().BeEquivalentTo(["Road Bike", "Electric Cargo Bike"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("https://docs.example.com/help")]
    [InlineData("https://docs.example.com/help#")]
    public void Fill_UrlFragmentValueTitle_ReturnsNullForMissingOrEmptyRemainingFragment(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://docs.example.com/help#powder-skis", "Powder Skis"),
                ("https://docs.example.com/help#trail_running", "Trail Running")
            ],
            [remaining]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_UrlFragmentValueTitle_LeavesRawFragmentExtractionPrecedence()
    {
        var result = FlashFillService.Fill(
            [
                ("https://docs.example.com/help#powder+skis", "powder skis"),
                ("https://docs.example.com/help#trail%20running", "trail running")
            ],
            ["https://docs.example.com/help#road-bike"]);

        result.Should().BeEquivalentTo(["road-bike"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("https://northwind.test/page")]
    [InlineData("https://northwind.test/page#")]
    [InlineData("northwind.test/page#appendix")]
    [InlineData("ftp://northwind.test/page#appendix")]
    [InlineData("https://user@northwind.test/page#appendix")]
    [InlineData("https://localhost/page#appendix")]
    public void Fill_UrlFragmentValue_ReturnsNullForUnsupportedRemainingUrl(string remaining)
    {
        var result = FlashFillService.Fill(
            [
                ("https://contoso.com/docs/report#section-2", "section-2"),
                ("https://fabrikam.example/path#summary", "summary")
            ],
            [remaining]);

        result.Should().BeNull();
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

    [Theory]
    [InlineData("", "alant@contoso.com")]
    [InlineData(".", "alan.t@contoso.com")]
    [InlineData("_", "alan_t@contoso.com")]
    [InlineData("-", "alan-t@contoso.com")]
    public void Fill_FullNamesToFirstLastInitialEmail_LearnsSeparatorAndSharedDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", $"ada{separator}l@contoso.com"),
                ("Grace Hopper", $"grace{separator}h@contoso.com")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("", "turinga@contoso.com")]
    [InlineData(".", "turing.a@contoso.com")]
    [InlineData("_", "turing_a@contoso.com")]
    [InlineData("-", "turing-a@contoso.com")]
    public void Fill_FullNamesToLastFirstInitialEmail_LearnsSeparatorAndSharedDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", $"lovelace{separator}a@contoso.com"),
                ("Grace Hopper", $"hopper{separator}g@contoso.com")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNamesToFirstLastInitialEmail_UsesFirstAndLastAcrossMiddleNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "ada.l@contoso.com"),
                ("Grace Brewster Hopper", "grace.h@contoso.com")
            ],
            ["Katherine Coleman Johnson"]);

        result.Should().BeEquivalentTo(["katherine.j@contoso.com"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(".", "alan.m.turing@contoso.com", "katherine.c.johnson@contoso.com")]
    [InlineData("_", "alan_m_turing@contoso.com", "katherine_c_johnson@contoso.com")]
    [InlineData("-", "alan-m-turing@contoso.com", "katherine-c-johnson@contoso.com")]
    public void Fill_FullNamesToMiddleInitialEmail_LearnsSeparatedAliasAndSharedDomain(
        string separator,
        string expectedFirst,
        string expectedSecond)
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", $"ada{separator}b{separator}lovelace@contoso.com"),
                ("Grace Brewster Hopper", $"grace{separator}b{separator}hopper@contoso.com")
            ],
            [
                "Alan Mathison Turing",
                "Katherine Coleman Johnson"
            ]);

        result.Should().BeEquivalentTo([expectedFirst, expectedSecond], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNamesToMiddleInitialEmail_LearnsCompactFirstMiddleInitialLastAndSharedDomain()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "adablovelace@contoso.com"),
                ("Grace Brewster Hopper", "gracebhopper@contoso.com")
            ],
            [
                "Alan Mathison Turing",
                "Katherine Coleman Johnson"
            ]);

        result.Should().BeEquivalentTo(["alanmturing@contoso.com", "katherinecjohnson@contoso.com"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNamesToMiddleInitialEmail_LearnsCompactInitialsLastAndSharedDomain()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "ablovelace@contoso.com"),
                ("Grace Brewster Hopper", "gbhopper@contoso.com")
            ],
            [
                "Alan Mathison Turing",
                "Katherine Coleman Johnson"
            ]);

        result.Should().BeEquivalentTo(["amturing@contoso.com", "kcjohnson@contoso.com"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(".", "turing.alan.m@contoso.com", "johnson.katherine.c@contoso.com")]
    [InlineData("_", "turing_alan_m@contoso.com", "johnson_katherine_c@contoso.com")]
    [InlineData("-", "turing-alan-m@contoso.com", "johnson-katherine-c@contoso.com")]
    public void Fill_FullNamesToReversedMiddleInitialEmail_LearnsSeparatedAliasAndSharedDomain(
        string separator,
        string expectedFirst,
        string expectedSecond)
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", $"lovelace{separator}ada{separator}b@contoso.com"),
                ("Grace Brewster Hopper", $"hopper{separator}grace{separator}b@contoso.com")
            ],
            [
                "Alan Mathison Turing",
                "Katherine Coleman Johnson"
            ]);

        result.Should().BeEquivalentTo([expectedFirst, expectedSecond], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNamesToReversedMiddleInitialEmail_LearnsCompactLastFirstInitialMiddleInitialAndSharedDomain()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "lovelaceab@contoso.com"),
                ("Grace Brewster Hopper", "hoppergb@contoso.com")
            ],
            [
                "Alan Mathison Turing",
                "Katherine Coleman Johnson"
            ]);

        result.Should().BeEquivalentTo(["turingam@contoso.com", "johnsonkc@contoso.com"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNamesToReversedMiddleInitialEmail_LearnsCompactLastMiddleInitialFirstInitialAndSharedDomain()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "lovelaceba@contoso.com"),
                ("Grace Brewster Hopper", "hopperbg@contoso.com")
            ],
            [
                "Alan Mathison Turing",
                "Katherine Coleman Johnson"
            ]);

        result.Should().BeEquivalentTo(["turingma@contoso.com", "johnsonck@contoso.com"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNamesToReversedMiddleInitialEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "lovelace.ada.b@contoso.com"),
                ("Grace Brewster Hopper", "hopper.grace.b@example.org")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_FullNamesToReversedMiddleInitialEmail_ReturnsNullWhenRemainingNameDoesNotHaveExactlyThreeTokens()
    {
        var tooFewResult = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "lovelace.ada.b@contoso.com"),
                ("Grace Brewster Hopper", "hopper.grace.b@contoso.com")
            ],
            ["Alan Turing"]);

        var tooManyResult = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "lovelace.ada.b@contoso.com"),
                ("Grace Brewster Hopper", "hopper.grace.b@contoso.com")
            ],
            ["Alan Mathison M. Turing"]);

        tooFewResult.Should().BeNull();
        tooManyResult.Should().BeNull();
    }

    [Fact]
    public void Fill_FullNamesToReversedMiddleInitialEmail_ReturnsNullWhenExampleNameDoesNotHaveExactlyThreeTokens()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Augusta Byron Lovelace", "lovelace.ada.b@contoso.com"),
                ("Grace Brewster Hopper", "hopper.grace.b@contoso.com")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_FullNamesToMiddleInitialEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "ada.b.lovelace@contoso.com"),
                ("Grace Brewster Hopper", "grace.b.hopper@example.org")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_FullNamesToMiddleInitialEmail_ReturnsNullWhenRemainingNameHasTooFewTokens()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "ada.b.lovelace@contoso.com"),
                ("Grace Brewster Hopper", "grace.b.hopper@contoso.com")
            ],
            ["Alan Turing"]);

        result.Should().BeNull();
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
