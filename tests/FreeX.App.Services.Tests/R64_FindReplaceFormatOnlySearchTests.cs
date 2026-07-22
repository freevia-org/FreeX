using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R64-commands-find-replace-6-1: Excel allows "Find what" to be left blank as long as a Format
/// criterion is set -- Find All/Find Next/Replace then operate purely on format (e.g. "find every
/// bold cell"). WorkbookSession's empty-search guards on FindNext/FindAll/ReplaceAllValues/
/// ReplaceNextValue used to reject an empty search unconditionally, making that workflow
/// unreachable even though FindReplaceService.Find already supports an empty search text combined
/// with a RequiredFormat (it matches every cell then filters by format). The guards now only
/// reject the empty search when there is ALSO no format criterion.
/// </summary>
public sealed class R64_FindReplaceFormatOnlySearchTests
{
    [Fact]
    public void FindNext_EmptySearchWithRequiredFormat_SelectsTheFormatMatchingCell()
    {
        var (workbook, boldAddress) = CreateWorkbookWithOneBoldCell();
        var session = CreateSession(workbook);
        var options = new FindOptions(RequiredFormat: new StyleDiff(Bold: true));

        var result = session.FindNext(searchText: "", options: options);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.SelectedRange.Should().Be(new GridRange(boldAddress, boldAddress));
    }

    [Fact]
    public void FindNext_EmptySearchWithNoFormat_StillFails()
    {
        // Sibling no-regression case: a truly empty search (no format criterion either) must keep
        // failing exactly as before this fix.
        var (workbook, _) = CreateWorkbookWithOneBoldCell();
        var session = CreateSession(workbook);

        var result = session.FindNext(searchText: "");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Find text is required.");
    }

    [Fact]
    public void FindAll_EmptySearchWithRequiredFormat_ReturnsOnlyTheFormatMatchingCell()
    {
        var (workbook, boldAddress) = CreateWorkbookWithOneBoldCell();
        var session = CreateSession(workbook);
        var options = new FindOptions(RequiredFormat: new StyleDiff(Bold: true));

        var result = session.FindAll(searchText: "", options: options);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Matches.Should().ContainSingle(match => match.Address == boldAddress);
    }

    [Fact]
    public void FindAll_EmptySearchWithNoFormat_StillFails()
    {
        var (workbook, _) = CreateWorkbookWithOneBoldCell();
        var session = CreateSession(workbook);

        var result = session.FindAll(searchText: "");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Find text is required.");
    }

    [Fact]
    public void ReplaceAllValues_EmptySearchWithRequiredFormat_ProceedsAndReportsFormatMatchingCells()
    {
        // The blank-search guard must let this reach FindReplaceService.Find, which returns the
        // one bold cell as a match count (the per-cell text-substitution step still requires
        // non-empty search text to build a replacement command -- an existing, separate engine
        // constraint in FindReplaceService.TryCreateReplacementCommand, out of scope here). The
        // key regression this guards against is the call failing outright with
        // "Find text is required.".
        var (workbook, _) = CreateWorkbookWithOneBoldCell();
        var session = CreateSession(workbook);
        var options = new FindOptions(RequiredFormat: new StyleDiff(Bold: true));

        var result = session.ReplaceAllValues("", "New", options, replacementFormat: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.MatchCount.Should().Be(1);
    }

    [Fact]
    public void ReplaceAllValues_EmptySearchWithNoFormat_StillFails()
    {
        var (workbook, _) = CreateWorkbookWithOneBoldCell();
        var session = CreateSession(workbook);

        var result = session.ReplaceAllValues("", "New");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Find text is required.");
    }

    [Fact]
    public void ReplaceNextValue_EmptySearchWithRequiredFormat_ProceedsAndReportsFormatMatchingCell()
    {
        var (workbook, boldAddress) = CreateWorkbookWithOneBoldCell();
        var session = CreateSession(workbook);
        var options = new FindOptions(RequiredFormat: new StyleDiff(Bold: true));

        var result = session.ReplaceNextValue("", "New", options, replacementFormat: null);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.MatchCount.Should().Be(1);
        result.ReplacedRange.Should().Be(new GridRange(boldAddress, boldAddress));
    }

    [Fact]
    public void ReplaceNextValue_EmptySearchWithNoFormat_StillFails()
    {
        var (workbook, _) = CreateWorkbookWithOneBoldCell();
        var session = CreateSession(workbook);

        var result = session.ReplaceNextValue("", "New");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Find text is required.");
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static (Workbook Workbook, CellAddress BoldAddress) CreateWorkbookWithOneBoldCell()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;

        var boldStyle = workbook.RegisterStyle(new CellStyle { Bold = true });
        var boldAddress = new CellAddress(sheet.Id, 1, 1);
        var boldCell = Cell.FromValue(new TextValue("Alpha"));
        boldCell.StyleId = boldStyle;
        sheet.SetCell(boldAddress, boldCell);

        var plainAddress = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(plainAddress, Cell.FromValue(new TextValue("Beta")));

        return (workbook, boldAddress);
    }
}
