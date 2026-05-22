using System.IO;
using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class GoToDialogsTests
{
    [Fact]
    public void TryParseAddress_AcceptsA1ReferenceOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        GoToDialog.TryParseAddress("B5", sheetId, out var address).Should().BeTrue();

        address.Should().Be(new CellAddress(sheetId, 5, 2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NotACell")]
    [InlineData("A0")]
    public void TryParseAddress_RejectsInvalidReference(string input)
    {
        GoToDialog.TryParseAddress(input, SheetId.New(), out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseReference_ResolvesDefinedNameToRangeStart()
    {
        var sheetId = SheetId.New();
        var names = new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sales_Total"] = new(
                new CellAddress(sheetId, 10, 2),
                new CellAddress(sheetId, 12, 4))
        };

        GoToDialog.TryParseReference("sales_total", sheetId, names, out var address).Should().BeTrue();

        address.Should().Be(new CellAddress(sheetId, 10, 2));
    }

    [Fact]
    public void BuildReferenceChoices_PutsDefaultThenRecentThenSortedNamesWithoutDuplicates()
    {
        var choices = GoToDialog.BuildReferenceChoices(
            "B5",
            ["B5", "D10"],
            ["zName", "Alpha"]);

        choices.Should().Equal("B5", "D10", "Alpha", "zName");
    }

    [Fact]
    public void GoToDialog_ExposesKeyboardAccessKeysForReferenceAndButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "GoToDialog.cs"));

        source.Should().Contain("Content = \"_Go to:\"");
        source.Should().Contain("Recent references and defined names");
        source.Should().Contain("Content = \"_Reference:\"");
        source.Should().Contain("Target = _addressBox");
        source.Should().Contain("Content = \"S_pecial...\"");
        source.Should().Contain("new GoToSpecialDialog");
        source.Should().Contain("SelectedSpecialKind");
        source.Should().Contain("Content = \"_OK\"");
        source.Should().Contain("Content = \"_Cancel\"");
        source.Should().NotContain("Select a named or recently used reference");
    }

    [Fact]
    public void MainWindow_GoToDialogRoutesSpecialSelectionThroughGoToSpecialService()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.HomeEditing.cs"));

        source.Should().Contain("new GoToDialog(_currentSheetId, defaultAddress, _workbook.NamedRanges)");
        source.Should().Contain("dialog.SelectedSpecialKind is { } specialKind");
        source.Should().Contain("SelectGoToSpecialMatches(specialKind, showEmptyMessage: true)");
    }

    [Fact]
    public void GetChoices_ExposesExcelGoToSpecialCoreChoices()
    {
        var choices = GoToSpecialDialog.GetChoices();

        choices.Select(choice => choice.Kind).Should().Contain([
            GoToSpecialKind.Blanks,
            GoToSpecialKind.Constants,
            GoToSpecialKind.Formulas,
            GoToSpecialKind.Comments,
            GoToSpecialKind.CurrentRegion,
            GoToSpecialKind.RowDifferences,
            GoToSpecialKind.ColumnDifferences,
            GoToSpecialKind.LastCell,
            GoToSpecialKind.ConditionalFormats,
            GoToSpecialKind.Objects,
            GoToSpecialKind.Precedents,
            GoToSpecialKind.Dependents,
            GoToSpecialKind.DataValidation,
            GoToSpecialKind.VisibleCellsOnly]);
    }

    [Fact]
    public void GoToSpecialDialog_ExposesKeyboardAccessKeysForChoicesAndButtons()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "GoToSpecialDialog.cs"));

        foreach (var expected in new[]
        {
            "new(GoToSpecialKind.Blanks, \"_Blanks\")",
            "new(GoToSpecialKind.Constants, \"_Constants\")",
            "new(GoToSpecialKind.Formulas, \"_Formulas\")",
            "new(GoToSpecialKind.Comments, \"Co_mments\")",
            "new(GoToSpecialKind.CurrentRegion, \"Current _region\")",
            "new(GoToSpecialKind.RowDifferences, \"Row _differences\")",
            "new(GoToSpecialKind.ColumnDifferences, \"Column dif_ferences\")",
            "new(GoToSpecialKind.LastCell, \"_Last cell\")",
            "new(GoToSpecialKind.ConditionalFormats, \"Conditional _formats\")",
            "new(GoToSpecialKind.Objects, \"_Objects\")",
            "new(GoToSpecialKind.Precedents, \"_Precedents\")",
            "new(GoToSpecialKind.Dependents, \"_Dependents\")",
            "new(GoToSpecialKind.DataValidation, \"_Data validation\")",
            "new(GoToSpecialKind.VisibleCellsOnly, \"_Visible cells only\")"
        })
            source.Should().Contain(expected);

        source.Should().Contain("Header = \"Go to special\"");
        source.Should().NotContain("Header = \"Additional Excel options\"");
        source.Should().NotContain("IsEnabled = false");
        source.Should().NotContain("shown for parity");
        source.Should().NotContain("The selectable options match");
        source.Should().Contain("DialogButtonRowFactory.Create");
    }

    [Fact]
    public void TryParseChoice_MapsDisplayTextThroughExistingParser()
    {
        GoToSpecialDialog.TryParseChoice("Data validation", out var kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.DataValidation);

        GoToSpecialDialog.TryParseChoice("conditional formats", out kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.ConditionalFormats);

        GoToSpecialDialog.TryParseChoice("objects", out kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.Objects);

        GoToSpecialDialog.TryParseChoice("precedents", out kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.Precedents);

        GoToSpecialDialog.TryParseChoice("dependents", out kind).Should().BeTrue();

        kind.Should().Be(GoToSpecialKind.Dependents);
    }
}
