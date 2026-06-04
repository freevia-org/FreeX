using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FlashFillServiceTests
{
    [Fact]
    public void Fill_DelimitedWordsInitials_BuildsInitials()
    {
        var result = FlashFillService.Fill(
            [("Ada Lovelace", "AL"), ("Grace Hopper", "GH")],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["AT"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_DelimitedWordsInitials_UppercasesLowercaseSourceInitialsWhenExamplesDo()
    {
        var result = FlashFillService.Fill(
            [("ada lovelace", "AL"), ("grace hopper", "GH")],
            ["alan turing"]);

        result.Should().BeEquivalentTo(["AT"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNameLastCommaFirst_ReordersDelimitedNameParts()
    {
        var result = FlashFillService.Fill(
            [("Ada Lovelace", "Lovelace, Ada"), ("Grace Hopper", "Hopper, Grace")],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["Turing, Alan"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_LastCommaFirstFullName_ReordersDelimitedNameParts()
    {
        var result = FlashFillService.Fill(
            [("Lovelace, Ada", "Ada Lovelace"), ("Hopper, Grace", "Grace Hopper")],
            ["Turing, Alan"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_LastCommaFirstFullName_ReturnsNullForMalformedRemainingSource()
    {
        var result = FlashFillService.Fill(
            [("Lovelace, Ada", "Ada Lovelace"), ("Hopper, Grace", "Grace Hopper")],
            ["Alan Turing"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_ThreePartNames_ExtractsFirstAndLast()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada Lovelace"),
                ("Grace Brewster Hopper", "Grace Hopper")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_ReordersLastCommaFirst()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Lovelace, Ada"),
                ("Grace Brewster Hopper", "Hopper, Grace")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Turing, Alan"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_ReordersLastCommaFirstMiddle()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Lovelace, Ada Byron"),
                ("Grace Brewster Hopper", "Hopper, Grace Brewster")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Turing, Alan Mathison"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_DropsFirstName()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Byron Lovelace"),
                ("Grace Brewster Hopper", "Brewster Hopper")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Mathison Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_DropsLastName()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada Byron"),
                ("Grace Brewster Hopper", "Grace Brewster")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Alan Mathison"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_AbbreviatesFirstInitialLastName()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "A. Lovelace"),
                ("Grace Hopper", "G. Hopper")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["A. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_AbbreviatesFirstNameLastInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "Ada L."),
                ("Grace Hopper", "Grace H.")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["Alan T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_AbbreviatesAllInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "A. L."),
                ("Grace Hopper", "G. H.")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["A. T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_AbbreviatesLowercaseNamesAsUppercaseInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("ada lovelace", "A. L."),
                ("grace hopper", "G. H.")
            ],
            ["alan turing"]);

        result.Should().BeEquivalentTo(["A. T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_AbbreviatesLastNameFirstInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "Lovelace A."),
                ("Grace Hopper", "Hopper G.")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["Turing A."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_AbbreviatesLastCommaFirstInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "Lovelace, A."),
                ("Grace Hopper", "Hopper, G.")
            ],
            ["Alan Turing"]);

        result.Should().BeEquivalentTo(["Turing, A."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesMiddleInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada B. Lovelace"),
                ("Grace Brewster Hopper", "Grace B. Hopper")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Alan M. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesFirstInitialLastName()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "A. Lovelace"),
                ("Grace Brewster Hopper", "G. Hopper")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["A. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesFirstNameLastInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada L."),
                ("Grace Brewster Hopper", "Grace H.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Alan T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesLastNameFirstInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Lovelace A."),
                ("Grace Brewster Hopper", "Hopper G.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Turing A."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesMiddleInitialLastName()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "B. Lovelace"),
                ("Grace Brewster Hopper", "B. Hopper")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["M. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesMiddleNameLastInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Byron L."),
                ("Grace Brewster Hopper", "Brewster H.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Mathison T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesFirstAndMiddleInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "A. B. Lovelace"),
                ("Grace Brewster Hopper", "G. B. Hopper")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["A. M. Turing"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesAllInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "A. B. L."),
                ("Grace Brewster Hopper", "G. B. H.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["A. M. T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesLowercaseNamesAsUppercaseInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("ada byron lovelace", "A. B. L."),
                ("grace brewster hopper", "G. B. H.")
            ],
            ["alan mathison turing"]);

        result.Should().BeEquivalentTo(["A. M. T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesFirstNameMiddleInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada B."),
                ("Grace Brewster Hopper", "Grace B.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Alan M."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesLastCommaFirstAndMiddleInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Lovelace, A. B."),
                ("Grace Brewster Hopper", "Hopper, G. B.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Turing, A. M."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesLastCommaFirstNameMiddleInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Lovelace, Ada B."),
                ("Grace Brewster Hopper", "Hopper, Grace B.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Turing, Alan M."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesLastInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada Byron L."),
                ("Grace Brewster Hopper", "Grace Brewster H.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Alan Mathison T."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_AbbreviatesLastFirstAndMiddleInitials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Lovelace A. B."),
                ("Grace Brewster Hopper", "Hopper G. B.")
            ],
            ["Alan Mathison Turing"]);

        result.Should().BeEquivalentTo(["Turing A. M."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_ThreePartNames_ExtractsMiddleInitial()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "B."),
                ("Grace Murray Hopper", "M.")
            ],
            ["Katherine Coleman Johnson"]);

        result.Should().BeEquivalentTo(["C."], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_FullNames_ExtractsLastNameAcrossVariableTokenCounts()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace", "Lovelace"),
                ("Grace Hopper", "Hopper")
            ],
            ["Katherine Coleman Johnson"]);

        result.Should().BeEquivalentTo(["Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameTitles_RemovesTitleFromVariableLengthNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Dr. Ada Lovelace", "Ada Lovelace"),
                ("Prof Grace Brewster Hopper", "Grace Brewster Hopper")
            ],
            ["Ms. Katherine Coleman Johnson"]);

        result.Should().BeEquivalentTo(["Katherine Coleman Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameTitles_RemovesTitleFromSingleTokenNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Dr. Lovelace", "Lovelace"),
                ("Prof Hopper", "Hopper")
            ],
            ["Ms. Johnson"]);

        result.Should().BeEquivalentTo(["Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameTitles_ReturnsNullForUntitledRemainingNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Dr. Ada Lovelace", "Ada Lovelace"),
                ("Prof Grace Hopper", "Grace Hopper")
            ],
            ["Katherine Johnson"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_KnownNameTitlesAndSuffixes_RemovesBothFromVariableLengthNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Dr. Ada Lovelace Jr.", "Ada Lovelace"),
                ("Prof Grace Brewster Hopper, III", "Grace Brewster Hopper")
            ],
            ["Ms. Katherine Coleman Johnson Sr."]);

        result.Should().BeEquivalentTo(["Katherine Coleman Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameTitlesAndSuffixes_RemovesMultipleTrailingSuffixesWithPunctuation()
    {
        var result = FlashFillService.Fill(
            [
                ("Dr. Ada Lovelace Jr., Ph.D.", "Ada Lovelace"),
                ("Prof Grace Brewster Hopper Sr., M.D.", "Grace Brewster Hopper")
            ],
            ["Ms. Katherine Coleman Johnson III, CPA."]);

        result.Should().BeEquivalentTo(["Katherine Coleman Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameTitlesAndSuffixes_RemovesCommaAttachedTrailingSuffixes()
    {
        var result = FlashFillService.Fill(
            [
                ("Dr. Ada Lovelace,Jr.,Ph.D.", "Ada Lovelace"),
                ("Prof Grace Brewster Hopper,Sr.,M.D.", "Grace Brewster Hopper")
            ],
            ["Ms. Katherine Coleman Johnson,III,CPA"]);

        result.Should().BeEquivalentTo(["Katherine Coleman Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameTitlesAndSuffixes_AbbreviatesCleanedNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Dr. Ada Lovelace Jr.", "A. Lovelace"),
                ("Prof Grace Hopper Sr.", "G. Hopper")
            ],
            ["Ms. Katherine Johnson III"]);

        result.Should().BeEquivalentTo(["K. Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameTitlesAndSuffixes_GeneratesEmailFromCleanedNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Dr. Ada Byron Lovelace Jr.", "ada.lovelace@contoso.com"),
                ("Prof Grace Brewster Hopper Sr.", "grace.hopper@contoso.com")
            ],
            ["Ms. Katherine Coleman Johnson III"]);

        result.Should().BeEquivalentTo(["katherine.johnson@contoso.com"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameTitlesAndSuffixes_ReturnsNullUnlessRemainingHasBoth()
    {
        var result = FlashFillService.Fill(
            [
                ("Dr. Ada Lovelace Jr.", "Ada Lovelace"),
                ("Prof Grace Hopper Sr.", "Grace Hopper")
            ],
            ["Ms. Katherine Johnson"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_KnownNameSuffixes_RemovesSuffixFromVariableLengthNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace Jr.", "Ada Lovelace"),
                ("Grace Brewster Hopper, III", "Grace Brewster Hopper")
            ],
            ["Katherine Coleman Johnson Sr."]);

        result.Should().BeEquivalentTo(["Katherine Coleman Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameSuffixes_RemovesCommaAttachedSuffixes()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace,Jr.", "Ada Lovelace"),
                ("Grace Brewster Hopper,Sr.", "Grace Brewster Hopper")
            ],
            ["Katherine Coleman Johnson,III"]);

        result.Should().BeEquivalentTo(["Katherine Coleman Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameSuffixes_ExtractsLastNameFromCleanedVariableLengthNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace Jr.", "Lovelace"),
                ("Grace Hopper Sr.", "Hopper")
            ],
            ["Katherine Coleman Johnson III"]);

        result.Should().BeEquivalentTo(["Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameSuffixes_RemovesProfessionalSuffixes()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace, Ph.D.", "Ada Lovelace"),
                ("Grace Brewster Hopper M.D.", "Grace Brewster Hopper")
            ],
            ["Katherine Coleman Johnson Esq."]);

        result.Should().BeEquivalentTo(["Katherine Coleman Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameSuffixes_RemovesOnlyFinalSuffixForSuffixOnlyPatterns()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace Jr., Ph.D.", "Ada Lovelace Jr"),
                ("Grace Brewster Hopper Sr., M.D.", "Grace Brewster Hopper Sr")
            ],
            ["Katherine Coleman Johnson III, CPA."]);

        result.Should().BeEquivalentTo(["Katherine Coleman Johnson III"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameSuffixes_RemovesBusinessAndMedicalCredentials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace, CPA", "Ada Lovelace"),
                ("Grace Brewster Hopper M.B.A.", "Grace Brewster Hopper")
            ],
            ["Katherine Coleman Johnson DVM"]);

        result.Should().BeEquivalentTo(["Katherine Coleman Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameSuffixes_RemovesCommaAttachedProfessionalCredentials()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace,CPA", "Ada Lovelace"),
                ("Grace Brewster Hopper,M.B.A.", "Grace Brewster Hopper")
            ],
            ["Katherine Coleman Johnson,DVM"]);

        result.Should().BeEquivalentTo(["Katherine Coleman Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameSuffixes_RemovesSuffixFromSingleTokenNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Lovelace Jr.", "Lovelace"),
                ("Hopper III", "Hopper")
            ],
            ["Johnson Sr."]);

        result.Should().BeEquivalentTo(["Johnson"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownNameSuffixes_ReturnsNullForUnsuffixedRemainingNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Lovelace Jr.", "Ada Lovelace"),
                ("Grace Hopper Sr.", "Grace Hopper")
            ],
            ["Katherine Johnson"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_KnownOrganizationSuffixes_RemovesVariableLegalSuffixesFromMultiTokenNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Northwind Traders LLC", "Northwind Traders"),
                ("Adventure Works Inc.", "Adventure Works")
            ],
            ["Contoso Ltd", "Fabrikam Research Corporation"]);

        result.Should().BeEquivalentTo(["Contoso", "Fabrikam Research"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownOrganizationSuffixes_RemovesCommaAttachedLegalSuffixes()
    {
        var result = FlashFillService.Fill(
            [
                ("Northwind Traders,LLC", "Northwind Traders"),
                ("Adventure Works,Inc.", "Adventure Works")
            ],
            ["Contoso,Ltd", "Fabrikam Research,Corporation"]);

        result.Should().BeEquivalentTo(["Contoso", "Fabrikam Research"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_KnownOrganizationSuffixes_ReturnsNullForUnsuffixedRemainingNames()
    {
        var result = FlashFillService.Fill(
            [
                ("Northwind Traders LLC", "Northwind Traders"),
                ("Adventure Works Inc.", "Adventure Works")
            ],
            ["Contoso Retail"]);

        result.Should().BeNull();
    }

    [Fact]
    public void Fill_ThreePartNames_ReturnsNullForAmbiguousTokenCounts()
    {
        var result = FlashFillService.Fill(
            [
                ("Ada Byron Lovelace", "Ada Lovelace"),
                ("Grace Brewster Hopper", "Grace Hopper")
            ],
            ["Alan Turing"]);

        result.Should().BeNull();
    }
}
