using System.IO;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class SelectionMoveOverwritePlannerTests
{
    [Fact]
    public void FindOverwriteTargets_ReportsExistingDestinationValue()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = Range(sheet, 1, 1, 1, 1);
        var target = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(target, new TextValue("Existing"));

        SelectionMoveOverwritePlanner.FindOverwriteTargets(sheet, sourceRange, Range(sheet, 3, 3, 3, 3))
            .Should()
            .Equal(target);
    }

    [Fact]
    public void FindOverwriteTargets_IgnoresTargetsInsideSourceRange()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = Range(sheet, 1, 1, 1, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Moved source"));

        SelectionMoveOverwritePlanner.FindOverwriteTargets(sheet, sourceRange, Range(sheet, 1, 2, 1, 3))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void FindOverwriteTargets_ReportsFormulaCommentsHyperlinksAndSpillValues()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = Range(sheet, 1, 1, 1, 1);
        var formula = new CellAddress(sheet.Id, 3, 3);
        var comment = new CellAddress(sheet.Id, 3, 4);
        var threadedComment = new CellAddress(sheet.Id, 3, 5);
        var hyperlink = new CellAddress(sheet.Id, 3, 6);
        var hyperlinkMetadata = new CellAddress(sheet.Id, 3, 7);
        var spillValue = new CellAddress(sheet.Id, 3, 8);

        sheet.SetCell(formula, Cell.FromFormula("A1"));
        sheet.Comments[comment] = "comment";
        sheet.ThreadedComments[threadedComment] = new ThreadedComment("thread");
        sheet.Hyperlinks[hyperlink] = "https://example.com";
        sheet.HyperlinkMetadata[hyperlinkMetadata] = new HyperlinkMetadata(ScreenTip: "Example");
        sheet.SetSpillRange(
            new CellAddress(sheet.Id, 2, 8),
            new RangeValue(new ScalarValue[,]
            {
                { new NumberValue(1) },
                { new TextValue("Spill") }
            }));

        SelectionMoveOverwritePlanner.FindOverwriteTargets(sheet, sourceRange, Range(sheet, 3, 3, 3, 8))
            .Should()
            .Equal(formula, comment, threadedComment, hyperlink, hyperlinkMetadata, spillValue);
    }

    [Fact]
    public void FindOverwriteTargets_IgnoresStyleOnlyDestinationCells()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var sourceRange = Range(sheet, 1, 1, 1, 1);
        sheet.SetStyleOnly(3, 3, new StyleId(42));

        SelectionMoveOverwritePlanner.FindOverwriteTargets(sheet, sourceRange, Range(sheet, 3, 3, 3, 3))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void MainWindowSelectionMove_WarnsBeforeOverwritingDestinationData()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.CellsCommands.cs"));
        var method = SourceMethodExtractor.ExtractMethodSource(
            source,
            "private void OnSelectionMoveRequested(");

        method.Should().Contain("SelectionMoveOverwritePlanner.HasOverwriteTargets(sheet, sourceRange, targetRange)");
        method.Should().Contain("UiText.Get(\"MainWindowMessage_TextToColumnsReplaceDataPrompt\")");
        method.Should().Contain("_messageService.AskYesNo");
        method.Should().Contain("new MoveRangeCommand(_currentSheetId, sourceRange, targetRange.Start)");
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
