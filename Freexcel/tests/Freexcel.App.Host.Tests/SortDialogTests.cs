using System.IO;
using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class SortDialogTests
{
    [Fact]
    public void BuildSortKeys_ReturnsTypedSortKeysInLevelOrder()
    {
        var levels = new[]
        {
            new SortDialogLevel(2, true) { SortOn = "Cell Values" },
            new SortDialogLevel(1, true) { SortOn = "Cell Color" },
            new SortDialogLevel(0, false) { SortOn = "Font Color" }
        };

        var keys = SortDialog.BuildSortKeys(levels);

        keys.Should().Equal(
            new SortKey(2, true),
            new SortKey(1, true, SortOn.CellColor),
            new SortKey(0, false, SortOn.FontColor));
    }

    [Fact]
    public void AddLevel_AppendsAscendingFirstColumnLevelByDefault()
    {
        var levels = new[] { new SortDialogLevel(1, false) };

        var updated = SortDialog.AddLevel(levels);

        updated.Should().Equal(
            new SortDialogLevel(1, false),
            new SortDialogLevel(0, true));
    }

    [Fact]
    public void RemoveLevel_RemovesRequestedLevelButKeepsAtLeastOneDefaultLevel()
    {
        var levels = new[]
        {
            new SortDialogLevel(1, false),
            new SortDialogLevel(2, true)
        };

        SortDialog.RemoveLevel(levels, 0).Should().Equal(new SortDialogLevel(2, true));
        SortDialog.RemoveLevel([new SortDialogLevel(3, false)], 0)
            .Should()
            .Equal(new SortDialogLevel(0, true));
    }

    [Fact]
    public void DialogCommands_ExposeKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SortDialog.cs"));

        foreach (var content in new[]
        {
            "_Add Level",
            "_Delete Level",
            "_Copy Level",
            "Move _Up",
            "Move Do_wn",
            "_Options...",
            "_OK",
            "_Cancel"
        })
            source.Should().Contain($"Content = \"{content}\"");
    }

    [Fact]
    public void DialogLayout_ExposesExcelCustomSortFields()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SortDialog.cs"));

        source.Should().Contain("My data has _headers");
        source.Should().Contain("IsChecked = hasHeaders");
        source.Should().Contain("ResultHasHeaders");
        source.Should().Contain("Sort levels");
        source.Should().Contain("Header = \"Sort by\"");
        source.Should().Contain("Header = \"Sort On\"");
        source.Should().Contain("Header = \"Order\"");
        source.Should().Contain("Cell Values");
        source.Should().Contain("Cell Color");
        source.Should().Contain("Font Color");
        source.Should().Contain("UpdateColumnChoices");
        source.Should().Contain("SortOptionsDialog");
    }

    [Fact]
    public void BuildColumnChoices_UsesSelectedRangeColumnsInDisplayOrder()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 7, 5));

        SortDialog.BuildColumnChoices(range).Should().Equal(
            new SortColumnChoice("Column C", 0),
            new SortColumnChoice("Column D", 1),
            new SortColumnChoice("Column E", 2));
    }

    [Fact]
    public void BuildColumnChoices_UsesHeaderValuesWhenHeaderRowIsEnabled()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sales");
        sheet.SetCell(new CellAddress(sheetId, 4, 2), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheetId, 4, 3), new TextValue("Revenue"));
        var range = new GridRange(
            new CellAddress(sheetId, 4, 2),
            new CellAddress(sheetId, 12, 4));

        SortDialog.BuildColumnChoices(sheet, range, hasHeaders: true).Should().Equal(
            new SortColumnChoice("Region", 0),
            new SortColumnChoice("Revenue", 1),
            new SortColumnChoice("Column D", 2));
    }

    [Fact]
    public void ExcludeHeaderRow_RemovesFirstRowOnlyWhenHeaderRowIsEnabled()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 7, 5));

        SortDialog.ExcludeHeaderRow(range, hasHeaders: true).Should().Be(new GridRange(
            new CellAddress(sheetId, 3, 3),
            new CellAddress(sheetId, 7, 5)));

        SortDialog.ExcludeHeaderRow(range, hasHeaders: false).Should().Be(range);
        SortDialog.ExcludeHeaderRow(new GridRange(range.Start, range.Start), hasHeaders: true)
            .Should()
            .Be(new GridRange(range.Start, range.Start));
    }

    [Fact]
    public void UpdateLevel_ReplacesRequestedSortLevel()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true),
            new SortDialogLevel(1, false)
        };

        SortDialog.UpdateLevel(levels, 1, columnOffset: 2, ascending: true)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(2, true));
    }

    [Fact]
    public void UpdateLevel_PreservesSortOnChoice()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true),
            new SortDialogLevel(1, false) { SortOn = "Font Color" }
        };

        SortDialog.UpdateLevel(levels, 1, columnOffset: 2, ascending: true)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(2, true) { SortOn = "Font Color" });
    }

    [Fact]
    public void CopyLevel_InsertsDuplicateAfterRequestedLevel()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true),
            new SortDialogLevel(2, false)
        };

        SortDialog.CopyLevel(levels, 1)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(2, false),
                new SortDialogLevel(2, false));
    }

    [Fact]
    public void CopyLevel_PreservesSortOnChoice()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true),
            new SortDialogLevel(2, false) { SortOn = "Cell Color" }
        };

        SortDialog.CopyLevel(levels, 1)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(2, false) { SortOn = "Cell Color" },
                new SortDialogLevel(2, false) { SortOn = "Cell Color" });
    }

    [Fact]
    public void MoveLevel_ReordersRequestedLevelWithinBounds()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true),
            new SortDialogLevel(1, false),
            new SortDialogLevel(2, true)
        };

        SortDialog.MoveLevel(levels, 2, -1)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(2, true),
                new SortDialogLevel(1, false));

        SortDialog.MoveLevel(levels, 0, -1).Should().Equal(levels);
        SortDialog.MoveLevel(levels, 2, 1).Should().Equal(levels);
    }

    [Fact]
    public void MainWindowCustomSort_UsesHeaderAwareChoicesAndExcludesHeaderRowWhenChecked()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.DataFilterCommands.cs"));

        source.Should().Contain("SortDialog.BuildColumnChoices(sheet, range, hasHeaders: true)");
        source.Should().Contain("SortDialog.BuildColumnChoices(sheet, range, hasHeaders: false)");
        source.Should().Contain("SortDialog.BuildRowChoices(range)");
        source.Should().Contain("SortDialog.ExcludeHeaderRow(currentRange, dialog.ResultHasHeaders)");
        source.Should().Contain("new SortOptions(dialog.ResultOptions.CaseSensitive, dialog.ResultOptions.LeftToRight)");
    }

    [Fact]
    public void SortOptionsDialog_ExposesExcelOptionsAsRealChoices()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "SortDialog.cs"));
        var optionsSource = source[source.IndexOf("public sealed class SortOptionsDialog", StringComparison.Ordinal)..];

        optionsSource.Should().Contain("Title = \"Sort Options\"");
        optionsSource.Should().Contain("_Case sensitive");
        optionsSource.Should().Contain("Sort top to _bottom");
        optionsSource.Should().Contain("Sort left to _right");
        optionsSource.Should().Contain("Result = new SortDialogOptions");
        optionsSource.Should().NotContain("IsEnabled = false");
        optionsSource.Should().NotContain("Unsupported Excel options");
    }

    [Fact]
    public void BuildRowChoices_LabelsRowsForLeftToRightSorting()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(new CellAddress(sheetId, 3, 2), new CellAddress(sheetId, 5, 4));

        SortDialog.BuildRowChoices(range).Should().Equal(
            new SortColumnChoice("Row 3", 0),
            new SortColumnChoice("Row 4", 1),
            new SortColumnChoice("Row 5", 2));
    }
}
