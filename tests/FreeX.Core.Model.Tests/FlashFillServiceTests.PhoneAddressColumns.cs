using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FlashFillServiceTests
{
    [Fact]
    public void Fill_DigitMask_FormatsPhoneNumberByExample()
    {
        var result = FlashFillService.Fill(
            [("4255550101", "(425) 555-0101"), ("2065550199", "(206) 555-0199")],
            ["3605550142"]);

        result.Should().BeEquivalentTo(["(360) 555-0142"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_PhoneNumberNormalization_FormatsMixedPunctuationByExample()
    {
        var result = FlashFillService.Fill(
            [("425.555.0101", "(425) 555-0101"), ("206-555-0199", "(206) 555-0199")],
            ["360 555 0142", "2125550198"]);

        result.Should().BeEquivalentTo(["(360) 555-0142", "(212) 555-0198"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_PhoneNumberNormalization_DropsLeadingCountryCodeWhenExamplesDo()
    {
        var result = FlashFillService.Fill(
            [("+1 (425) 555-0101", "425-555-0101"), ("1-206-555-0199", "206-555-0199")],
            ["+1 360 555 0142", "1.212.555.0198"]);

        result.Should().BeEquivalentTo(["360-555-0142", "212-555-0198"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_PhoneNumberNormalization_ReturnsNullWhenRemainingHasTooFewDigits()
    {
        var result = FlashFillService.Fill(
            [("425.555.0101", "(425) 555-0101"), ("206-555-0199", "(206) 555-0199")],
            ["555-0142"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("123 Pine St", "55 Burnside Ave", "1600 Amphitheatre Pkwy")]
    [InlineData("Seattle", "Portland", "Mountain View")]
    [InlineData("WA", "OR", "CA")]
    [InlineData("98101", "97209", "94043")]
    [InlineData("WA 98101", "OR 97209", "CA 94043")]
    public void Fill_AddressComponents_ExtractsConsistentStreetCityStateAndZipParts(
        string firstExpected,
        string secondExpected,
        string remainingExpected)
    {
        var result = FlashFillService.Fill(
            [
                ("123 Pine St, Seattle, WA 98101", firstExpected),
                ("55 Burnside Ave, Portland, OR 97209", secondExpected)
            ],
            ["1600 Amphitheatre Pkwy, Mountain View, CA 94043"]);

        result.Should().BeEquivalentTo([remainingExpected], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("123", "55", "1600")]
    [InlineData("Pine St", "Burnside Ave", "Amphitheatre Pkwy")]
    public void Fill_AddressStreetParts_ExtractsStreetNumberAndNameFromLeadingNumber(
        string firstExpected,
        string secondExpected,
        string remainingExpected)
    {
        var result = FlashFillService.Fill(
            [
                ("123 Pine St, Seattle, WA 98101", firstExpected),
                ("55 Burnside Ave, Portland, OR 97209", secondExpected)
            ],
            ["1600 Amphitheatre Pkwy, Mountain View, CA 94043"]);

        result.Should().BeEquivalentTo([remainingExpected], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("123", "55")]
    [InlineData("Pine St", "Burnside Ave")]
    public void Fill_AddressStreetParts_ReturnsNullWhenRemainingStreetHasNoLeadingNumber(
        string firstExpected,
        string secondExpected)
    {
        var result = FlashFillService.Fill(
            [
                ("123 Pine St, Seattle, WA 98101", firstExpected),
                ("55 Burnside Ave, Portland, OR 97209", secondExpected)
            ],
            ["Amphitheatre Pkwy, Mountain View, CA 94043"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("123", "55")]
    [InlineData("Pine St", "Burnside Ave")]
    public void Fill_AddressStreetParts_ReturnsNullWhenRemainingAddressIsMalformed(
        string firstExpected,
        string secondExpected)
    {
        var result = FlashFillService.Fill(
            [
                ("123 Pine St, Seattle, WA 98101", firstExpected),
                ("55 Burnside Ave, Portland, OR 97209", secondExpected)
            ],
            ["1600 Amphitheatre Pkwy Mountain View CA 94043"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Apt 4B", "Suite 200", "#12")]
    [InlineData("4B", "200", "12")]
    [InlineData("123 Pine St", "55 Burnside Ave", "1600 Amphitheatre Pkwy")]
    public void Fill_AddressUnits_ExtractsConsistentUnitAndStreetWithoutUnitParts(
        string firstExpected,
        string secondExpected,
        string remainingExpected)
    {
        var result = FlashFillService.Fill(
            [
                ("123 Pine St Apt 4B, Seattle, WA 98101", firstExpected),
                ("55 Burnside Ave Suite 200, Portland, OR 97209", secondExpected)
            ],
            ["1600 Amphitheatre Pkwy #12, Mountain View, CA 94043"]);

        result.Should().BeEquivalentTo([remainingExpected], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Apt #4B", "Ste #200", "No. 12")]
    [InlineData("4B", "200", "12")]
    [InlineData("123 Pine St", "55 Burnside Ave", "1600 Amphitheatre Pkwy")]
    public void Fill_AddressUnits_ExtractsDesignatorWithHashIdentifierAndNoDesignatorParts(
        string firstExpected,
        string secondExpected,
        string remainingExpected)
    {
        var result = FlashFillService.Fill(
            [
                ("123 Pine St Apt #4B, Seattle, WA 98101", firstExpected),
                ("55 Burnside Ave Ste #200, Portland, OR 97209", secondExpected)
            ],
            ["1600 Amphitheatre Pkwy No. 12, Mountain View, CA 94043"]);

        result.Should().BeEquivalentTo([remainingExpected], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Suite #200", "No. 12", "Apt #4B")]
    [InlineData("200", "12", "4B")]
    [InlineData("55 Burnside Ave", "1600 Amphitheatre Pkwy", "123 Pine St")]
    public void Fill_AddressUnits_AppliesDesignatorWithHashIdentifierToRemainingRows(
        string firstExpected,
        string secondExpected,
        string remainingExpected)
    {
        var result = FlashFillService.Fill(
            [
                ("55 Burnside Ave Suite #200, Portland, OR 97209", firstExpected),
                ("1600 Amphitheatre Pkwy No. 12, Mountain View, CA 94043", secondExpected)
            ],
            ["123 Pine St Apt #4B, Seattle, WA 98101"]);

        result.Should().BeEquivalentTo([remainingExpected], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Apt 4B", "Suite 200")]
    [InlineData("4B", "200")]
    [InlineData("123 Pine St", "55 Burnside Ave")]
    public void Fill_AddressUnits_ReturnsNullWhenRemainingStreetHasNoRecognizedUnit(
        string firstExpected,
        string secondExpected)
    {
        var result = FlashFillService.Fill(
            [
                ("123 Pine St Apt 4B, Seattle, WA 98101", firstExpected),
                ("55 Burnside Ave Suite 200, Portland, OR 97209", secondExpected)
            ],
            ["1600 Amphitheatre Pkwy, Mountain View, CA 94043"]);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData("Apt 4B", "Suite 200")]
    [InlineData("4B", "200")]
    [InlineData("123 Pine St", "55 Burnside Ave")]
    public void Fill_AddressUnits_ReturnsNullWhenRemainingAddressIsMalformed(
        string firstExpected,
        string secondExpected)
    {
        var result = FlashFillService.Fill(
            [
                ("123 Pine St Apt 4B, Seattle, WA 98101", firstExpected),
                ("55 Burnside Ave Suite 200, Portland, OR 97209", secondExpected)
            ],
            ["1600 Amphitheatre Pkwy #12 Mountain View CA 94043"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_AddressState_UsesStateBeforeZipWhenStreetAndCityTokenCountsVary()
    {
        var result = FlashFillService.Fill(
            [
                ("123 Pine St, Seattle, WA 98101", "WA"),
                ("55 Burnside Ave, Portland, OR 97209", "OR")
            ],
            [
                "1600 Amphitheatre Pkwy, Mountain View, CA 94043",
                "1 Congress Ave, Austin, TX 78701"
            ]);

        result.Should().BeEquivalentTo(["CA", "TX"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_AddressZip5_ExtractsBaseZipFromZipPlusFour()
    {
        var result = FlashFillService.Fill(
            [
                ("500 Market St, San Francisco, CA 94105-1205", "94105"),
                ("1 Kendall Sq, Cambridge, MA 02139-4307", "02139")
            ],
            ["1600 Amphitheatre Pkwy, Mountain View, CA 94043-1351"]);

        result.Should().BeEquivalentTo(["94043"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_AddressZip4_ExtractsExtensionFromZipPlusFour()
    {
        var result = FlashFillService.Fill(
            [
                ("500 Market St, San Francisco, CA 94105-1205", "1205"),
                ("1 Kendall Sq, Cambridge, MA 02139-4307", "4307")
            ],
            ["88 Townsend St, San Francisco, CA 94107-1234"]);

        result.Should().BeEquivalentTo(["1234"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_AddressZip4_DoesNotInferFromPlainFiveDigitZipExamples()
    {
        var result = FlashFillService.Fill(
            [
                ("123 Pine St, Seattle, WA 98101", "8101"),
                ("55 Burnside Ave, Portland, OR 97209", "7209")
            ],
            ["1600 Amphitheatre Pkwy, Mountain View, CA 94043"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_AddressZip4_ReturnsNullWhenRemainingAddressIsMalformed()
    {
        var result = FlashFillService.Fill(
            [
                ("500 Market St, San Francisco, CA 94105-1205", "1205"),
                ("1 Kendall Sq, Cambridge, MA 02139-4307", "4307")
            ],
            ["88 Townsend St San Francisco CA 94107-1234"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_AddressZip4_ReturnsNullWhenRemainingAddressHasPlainFiveDigitZip()
    {
        var result = FlashFillService.Fill(
            [
                ("500 Market St, San Francisco, CA 94105-1205", "1205"),
                ("1 Kendall Sq, Cambridge, MA 02139-4307", "4307")
            ],
            ["1600 Amphitheatre Pkwy, Mountain View, CA 94043"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_AddressComponents_ReturnsNullWhenRemainingAddressIsMalformed()
    {
        var result = FlashFillService.Fill(
            [
                ("123 Pine St, Seattle, WA 98101", "WA"),
                ("55 Burnside Ave, Portland, OR 97209", "OR")
            ],
            ["1600 Amphitheatre Pkwy Mountain View CA 94043"]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_FirstInitialPeriodLast_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["A. Lovelace", "G. Hopper"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["A. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstInitialLastLowercase_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["alovelace", "ghopper"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["aturing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastEmail_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["ada.lovelace@example.com", "grace.hopper@example.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["alan.turing@example.com"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastEmail_LearnsConstantDomainFromExamples()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["ada.lovelace@contoso.com", "grace.hopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["alan.turing@contoso.com"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastEmail_TrimsSourceNameCells()
    {
        var result = FlashFillService.FillFromColumns(
            [
                [" Ada ", " Lovelace "],
                [" Grace ", " Hopper "]
            ],
            ["ada.lovelace@contoso.com", "grace.hopper@contoso.com"],
            [
                [" Alan ", " Turing "],
                [" Katherine ", " Johnson "]
            ]);

        result.Should().BeEquivalentTo(
            ["alan.turing@contoso.com", "katherine.johnson@contoso.com"],
            o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("_", "alan_turing@contoso.com")]
    [InlineData("-", "alan-turing@contoso.com")]
    public void FillFromColumns_FirstLastSeparatedEmail_LearnsSeparatorAndConstantDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            [$"ada{separator}lovelace@contoso.com", $"grace{separator}hopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(".", "turing.alan@contoso.com")]
    [InlineData("_", "turing_alan@contoso.com")]
    [InlineData("-", "turing-alan@contoso.com")]
    public void FillFromColumns_LastFirstSeparatedEmail_LearnsSeparatorAndConstantDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            [$"lovelace{separator}ada@contoso.com", $"hopper{separator}grace@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_LastFirstSeparatedEmail_TrimsSourceNameCells()
    {
        var result = FlashFillService.FillFromColumns(
            [
                [" Ada ", " Lovelace "],
                [" Grace ", " Hopper "]
            ],
            ["lovelace.ada@contoso.com", "hopper.grace@contoso.com"],
            [
                [" Alan ", " Turing "],
                [" Katherine ", " Johnson "]
            ]);

        result.Should().BeEquivalentTo(
            ["turing.alan@contoso.com", "johnson.katherine@contoso.com"],
            o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstInitialLastEmail_LearnsConstantDomainFromExamples()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["alovelace@contoso.com", "ghopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["aturing@contoso.com"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstInitialLastEmail_TrimsSourceNameCellsBeforeTakingInitial()
    {
        var result = FlashFillService.FillFromColumns(
            [
                [" Ada ", " Lovelace "],
                [" Grace ", " Hopper "]
            ],
            ["alovelace@contoso.com", "ghopper@contoso.com"],
            [
                [" Alan ", " Turing "]
            ]);

        result.Should().BeEquivalentTo(["aturing@contoso.com"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(".", "a.turing@contoso.com")]
    [InlineData("_", "a_turing@contoso.com")]
    [InlineData("-", "a-turing@contoso.com")]
    public void FillFromColumns_FirstInitialLastSeparatedEmail_LearnsSeparatorAndConstantDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            [$"a{separator}lovelace@contoso.com", $"g{separator}hopper@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastInitialEmail_LearnsConstantDomainFromExamples()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["adal@contoso.com", "graceh@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["alant@contoso.com"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(".", "alan.t@contoso.com")]
    [InlineData("_", "alan_t@contoso.com")]
    [InlineData("-", "alan-t@contoso.com")]
    public void FillFromColumns_FirstLastInitialSeparatedEmail_LearnsSeparatorAndConstantDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            [$"ada{separator}l@contoso.com", $"grace{separator}h@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_LastFirstInitialEmail_LearnsConstantDomainFromExamples()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["lovelacea@contoso.com", "hopperg@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["turinga@contoso.com"], o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(".", "turing.a@contoso.com")]
    [InlineData("_", "turing_a@contoso.com")]
    [InlineData("-", "turing-a@contoso.com")]
    public void FillFromColumns_LastFirstInitialSeparatedEmail_LearnsSeparatorAndConstantDomain(
        string separator,
        string expected)
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            [$"lovelace{separator}a@contoso.com", $"hopper{separator}g@contoso.com"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo([expected], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["ada.lovelace@contoso.com", "grace.hopper@example.org"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_FirstInitialLastEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["alovelace@contoso.com", "ghopper@example.org"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_LastFirstInitialEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["lovelacea@contoso.com", "hopperg@example.org"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_FirstLastInitialEmail_ReturnsNullWhenExampleDomainsDiffer()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["adal@contoso.com", "graceh@example.org"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_LastFirstInitialPeriod_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["Lovelace A.", "Hopper G."],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["Turing A."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DelimitedWordsInitials_WithMixedExampleDelimiters_ReturnsNull()
    {
        var result = FlashFillService.Fill(
            [("Ada Lovelace", "AL"), ("Grace-Hopper", "GH")],
            ["Alan Turing"]);

        result.Should().BeNull();
    }

    [Fact]
    public void FillFromColumns_FirstLastWithSpace_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["Ada Lovelace", "Grace Hopper"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_LastFirstWithComma_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["Lovelace, Ada", "Hopper, Grace"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["Turing, Alan"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastWithPeriod_CombinesSourceColumns()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["ada", "lovelace"],
                ["grace", "hopper"]
            ],
            ["ada.lovelace", "grace.hopper"],
            [
                ["alan", "turing"]
            ]);

        result.Should().BeEquivalentTo(["alan.turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastWithPeriodLowercase_NormalizesProperCaseNames()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["ada.lovelace", "grace.hopper"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["alan.turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void FillFromColumns_FirstLastInitials_BuildsInitials()
    {
        var result = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["AL", "GH"],
            [
                ["Alan", "Turing"]
            ]);

        result.Should().BeEquivalentTo(["AT"], o => o.WithStrictOrdering());
    }

}
