using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class SortDialogTests
{
    [Fact]
    public void BuildSortKeys_ReturnsTypedSortKeysInLevelOrder()
    {
        var levels = new[]
        {
            new SortDialogLevel(2, true) { SortOn = "Cell Values" },
            new SortDialogLevel(1, true) { SortOn = "Cell Color", TargetColor = "#FF0000" },
            new SortDialogLevel(0, false) { SortOn = "Font Color", TargetColor = "#0000FF" }
        };

        var keys = SortDialog.BuildSortKeys(levels);

        keys.Should().Equal(
            new SortKey(2, true),
            new SortKey(1, true, SortOn.CellColor, new CellColor(255, 0, 0)),
            new SortKey(0, false, SortOn.FontColor, new CellColor(0, 0, 255)));
    }

    [Fact]
    public void PlannerBuildSortKeys_MapsLabelsAndIgnoresColorForValueSorts()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true) { SortOn = "Cell Values", TargetColor = "#FF0000" },
            new SortDialogLevel(1, false) { SortOn = "Cell Color", TargetColor = "#00FF00" },
            new SortDialogLevel(2, true) { SortOn = "Font Color", TargetColor = "#0000FF" },
            new SortDialogLevel(3, true) { SortOn = "Unknown", TargetColor = "#FFFFFF" }
        };

        SortDialogPlanner.BuildSortKeys(levels).Should().Equal(
            new SortKey(0, true, SortOn.CellValues, null),
            new SortKey(1, false, SortOn.CellColor, new CellColor(0, 255, 0)),
            new SortKey(2, true, SortOn.FontColor, new CellColor(0, 0, 255)),
            new SortKey(3, true, SortOn.CellValues, null));
    }

    [Fact]
    public void PlannerHotPaths_AvoidLinqIteratorChains()
    {
        var source = WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "SortDialogPlanner.cs");

        source.Should().NotContain(".Append(");
        source.Should().NotContain(".Select(");
    }

    [Fact]
    public void PlannerBuildActiveColumnChoices_UsesRowsForLeftToRightAndHeaderAwareColumnsOtherwise()
    {
        var sheetId = SheetId.New();
        var sheet = new Sheet(sheetId, "Sales");
        sheet.SetCell(new CellAddress(sheetId, 4, 2), new TextValue("Region"));
        var range = new GridRange(
            new CellAddress(sheetId, 4, 2),
            new CellAddress(sheetId, 6, 4));
        var headerChoices = SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders: true);
        var genericChoices = SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders: false);
        var rowChoices = SortDialogPlanner.BuildRowChoices(range);

        SortDialogPlanner.BuildActiveColumnChoices(
                new SortDialogOptions(LeftToRight: false),
                hasHeaders: true,
                headerChoices,
                genericChoices,
                rowChoices)
            .Should()
            .Equal(headerChoices);
        SortDialogPlanner.BuildActiveColumnChoices(
                new SortDialogOptions(LeftToRight: false),
                hasHeaders: false,
                headerChoices,
                genericChoices,
                rowChoices)
            .Should()
            .Equal(genericChoices);
        SortDialogPlanner.BuildActiveColumnChoices(
                new SortDialogOptions(LeftToRight: true),
                hasHeaders: true,
                headerChoices,
                genericChoices,
                rowChoices)
            .Should()
            .Equal(rowChoices);
    }

    [Fact]
    public void SortDialogPlanningFacade_ForwardsPureWorkToPlanner()
    {
        var planningSource = DialogSourceTestSupport.ReadHostSources("SortDialog.Planning.cs");
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();

        planningSource.Should().Contain("using FreeX.App.Services;");
        planningSource.Should().NotContain("internal static class SortDialogPlanner");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "QuickSortRangePlanner.cs"))
            .Should()
            .BeFalse("quick sort range/header detection should live in Services instead of WPF Host");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Services", "QuickSortRangePlanner.cs"))
            .Should()
            .BeTrue("quick sort range/header detection should be available to all hosts");
        planningSource.Should().Contain("SortDialogPlanner.BuildSortKeys(levels, PlannerText)");
        planningSource.Should().Contain("SortDialogPlanner.CreateCommandPlan(levels, options, hasHeaders, PlannerText)");
        planningSource.Should().Contain("SortDialogPlanner.BuildOrderChoices(sortOn, PlannerText)");
        planningSource.Should().Contain("SortDialogPlanner.AddLevel(levels, columnOffset, ascending, PlannerText)");
        planningSource.Should().Contain("SortDialogPlanner.RemoveLevel(levels, index, PlannerText)");
        planningSource.Should().Contain("SortDialogPlanner.CopyLevel(levels, index, PlannerText)");
        planningSource.Should().Contain("SortDialogPlanner.MoveLevel(levels, index, direction, PlannerText)");
        planningSource.Should().Contain("SortDialogPlanner.UpdateLevel(levels, index, columnOffset, ascending, PlannerText)");
        planningSource.Should().Contain("SortDialogPlanner.BuildColumnChoices(range, PlannerText)");
        planningSource.Should().Contain("SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders, PlannerText)");
        planningSource.Should().Contain("SortDialogPlanner.BuildRowChoices(range, PlannerText)");
        planningSource.Should().Contain("SortDialogPlanner.BuildColorChoices(workbook, sheet, range)");
        planningSource.Should().Contain("SortDialogPlanner.BuildColorChoices(workbook, sheet, range, sortOn)");
        planningSource.Should().Contain("SortDialogPlanner.ExcludeHeaderRow(range, hasHeaders)");
    }

    [Fact]
    public void BuildOrderChoices_UsesExcelColorSortLabelsForColorSorts()
    {
        SortDialog.BuildOrderChoices("Cell Values").Should().Equal(
            new SortDirectionChoice("A to Z", true),
            new SortDirectionChoice("Z to A", false));

        SortDialog.BuildOrderChoices("Cell Color").Should().Equal(
            new SortDirectionChoice("On Top", true),
            new SortDirectionChoice("On Bottom", false));

        SortDialog.BuildOrderChoices("Font Color").Should().Equal(
            new SortDirectionChoice("On Top", true),
            new SortDirectionChoice("On Bottom", false));
    }

    [Fact]
    public void SortDialogLevel_RefreshesOrderChoicesWhenSortOnChanges()
    {
        var level = new SortDialogLevel(0, true);
        var changed = new List<string?>();
        level.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        level.SortOn = "Cell Color";

        level.OrderChoices.Should().Equal(
            new SortDirectionChoice("On Top", true),
            new SortDirectionChoice("On Bottom", false));
        changed.Should().Contain(nameof(SortDialogLevel.SortOn));
        changed.Should().Contain(nameof(SortDialogLevel.OrderChoices));
    }

    [Fact]
    public void BuildColorChoices_ListsDistinctFillAndFontColorsFromSelectedRange()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var red = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
        var blue = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0, 0, 255) });
        var redCell = Cell.FromValue(new TextValue("red"));
        redCell.StyleId = red;
        var blueCell = Cell.FromValue(new TextValue("blue"));
        blueCell.StyleId = blue;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), redCell);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), blueCell);

        SortDialog.BuildColorChoices(workbook, sheet, new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)))
            .Should()
            .Equal(new SortColorChoice(""), new SortColorChoice("#000000"), new SortColorChoice("#0000FF"), new SortColorChoice("#FF0000"));
    }

    [Fact]
    public void BuildColorChoices_ScopesChoicesToRequestedColorSortKind()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var fillStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
        var fontStyle = workbook.RegisterStyle(new CellStyle { FontColor = new CellColor(0, 0, 255) });
        var fillCell = Cell.FromValue(new TextValue("fill"));
        fillCell.StyleId = fillStyle;
        var fontCell = Cell.FromValue(new TextValue("font"));
        fontCell.StyleId = fontStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), fillCell);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), fontCell);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1));

        SortDialog.BuildColorChoices(workbook, sheet, range, SortOn.CellColor)
            .Should()
            .Equal(new SortColorChoice(""), new SortColorChoice("#FF0000"));
        SortDialog.BuildColorChoices(workbook, sheet, range, SortOn.FontColor)
            .Should()
            .Equal(new SortColorChoice(""), new SortColorChoice("#000000"), new SortColorChoice("#0000FF"));
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
