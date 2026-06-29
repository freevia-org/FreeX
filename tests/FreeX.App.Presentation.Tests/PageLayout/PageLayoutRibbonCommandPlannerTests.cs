using System.Collections.Generic;

using FluentAssertions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageLayoutRibbonCommandPlannerTests
{
    [Fact]
    public void BuildSetPrintAreaCommand_RemapsSelectionToTargetSheet()
    {
        var workbook = new Workbook("Book");
        var source = workbook.AddSheet("Sheet1");
        var target = workbook.AddSheet("Sheet2");
        var selection = Range(source.Id, 2, 3, 8, 5);

        var command = PageLayoutRibbonCommandPlanner.BuildSetPrintAreaCommand(target.Id, selection);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        target.PrintArea.Should().Be(Range(target.Id, 2, 3, 8, 5));
    }

    [Fact]
    public void BuildClearPrintAreaCommand_ClearsExistingPrintArea()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintArea = Range(sheet.Id, 1, 1, 4, 4);

        var command = PageLayoutRibbonCommandPlanner.BuildClearPrintAreaCommand(sheet.Id);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.PrintArea.Should().BeNull();
    }

    [Fact]
    public void BuildSetBackgroundCommand_SetsSheetBackground()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var background = new WorksheetBackgroundImage([1, 2, 3], "image/png", "background.png");

        var command = PageLayoutRibbonCommandPlanner.BuildSetBackgroundCommand(sheet.Id, background);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.BackgroundImage.Should().Be(background);
    }

    [Fact]
    public void BuildClearBackgroundCommand_ClearsSheetBackground()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.BackgroundImage = new WorksheetBackgroundImage([1, 2, 3], "image/png", "background.png");

        var command = PageLayoutRibbonCommandPlanner.BuildClearBackgroundCommand(sheet.Id);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.BackgroundImage.Should().BeNull();
    }

    [Fact]
    public void ResolveScaleToFitFromPageDimensions_PrefersFitModeWhenEitherAxisProvided()
    {
        var current = new WorksheetScaleToFit(85, null, null);

        var scale = PageLayoutRibbonCommandPlanner.ResolveScaleToFitFromPageDimensions(current, pagesWide: 1, pagesTall: null);

        scale.Should().Be(new WorksheetScaleToFit(null, 1, null));
    }

    [Fact]
    public void ResolveScaleToFitFromPageDimensions_FallsBackToCurrentPercentWhenBothAxesAutomatic()
    {
        var current = new WorksheetScaleToFit(125, null, null);

        var scale = PageLayoutRibbonCommandPlanner.ResolveScaleToFitFromPageDimensions(current, pagesWide: null, pagesTall: null);

        scale.Should().Be(new WorksheetScaleToFit(125, null, null));
    }

    [Fact]
    public void BuildPrintGridlinesCommand_PreservesCurrentPrintHeadings()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintHeadings = true;

        var command = PageLayoutRibbonCommandPlanner.BuildPrintGridlinesCommand(sheet, printGridlines: true);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.PrintGridlines.Should().BeTrue();
        sheet.PrintHeadings.Should().BeTrue();
    }

    [Fact]
    public void BuildPrintHeadingsCommand_PreservesCurrentPrintGridlines()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PrintGridlines = true;

        var command = PageLayoutRibbonCommandPlanner.BuildPrintHeadingsCommand(sheet, printHeadings: true);

        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.PrintGridlines.Should().BeTrue();
        sheet.PrintHeadings.Should().BeTrue();
    }

    [Fact]
    public void BuildPageBreaksCommand_AppliesPlannedBreakSets()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var plan = PageLayoutRibbonCommandPlanner.PlanPageBreakAction(
            PageBreakMenuAction.Insert,
            Range(sheet.Id, 6, 4, 6, 4),
            existingRowBreaks: [2u],
            existingColumnBreaks: [3u]);

        var command = PageLayoutRibbonCommandPlanner.BuildPageBreaksCommand(sheet.Id, plan);

        plan.Status.Should().Be("Inserted page breaks");
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        sheet.RowPageBreaks.Should().Equal(2u, 6u);
        sheet.ColumnPageBreaks.Should().Equal(3u, 4u);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
