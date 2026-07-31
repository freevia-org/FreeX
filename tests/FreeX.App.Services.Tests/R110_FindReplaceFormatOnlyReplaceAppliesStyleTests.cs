using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R110-commands-find-replace: Excel's format-only Replace (blank "Find what"/"Replace with", a
/// Format criterion on Find, a different Format on Replace) must actually reformat the
/// Find-format-matching cells, not just report a match count. R64_FindReplaceFormatOnlySearchTests
/// established that the blank-search guards on WorkbookSession.ReplaceAllValues/ReplaceNextValue
/// let a format-only search proceed to Find() -- but its own comment documented that "the per-cell
/// text-substitution step still requires non-empty search text to build a replacement command...
/// out of scope here", i.e. nothing was ever actually reformatted. These tests close that gap
/// through the real WorkbookSession entry point (the Avalonia shell's product surface).
/// </summary>
public sealed class R110_FindReplaceFormatOnlyReplaceAppliesStyleTests
{
    [Fact]
    public void ReplaceAllValues_FormatOnlyBlankSearchAndReplace_AppliesReplacementFormatWithoutChangingText()
    {
        var (workbook, boldAddress, plainAddress) = CreateWorkbookWithOneBoldCell();
        var session = CreateSession(workbook);
        var findOptions = new FindOptions(RequiredFormat: new StyleDiff(Bold: true));
        var replacementFormat = new StyleDiff(FillColor: new CellColor(255, 0, 0));

        var result = session.ReplaceAllValues("", "", findOptions, replacementFormat: replacementFormat);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.MatchCount.Should().Be(1);
        result.ReplacedCount.Should().Be(1);

        var sheet = workbook.GetSheet(boldAddress.Sheet)!;
        // Text is untouched -- this was a format-only replace.
        sheet.GetCell(boldAddress)!.Value.Should().Be(new TextValue("Alpha"));
        sheet.GetCell(plainAddress)!.Value.Should().Be(new TextValue("Beta"));

        // The Find-format-matching cell picked up the Replace format...
        var matchedStyle = workbook.GetStyle(sheet.GetCell(boldAddress)!.StyleId);
        matchedStyle.FillColor.Should().Be(new CellColor(255, 0, 0));
        matchedStyle.Bold.Should().BeTrue();

        // ...and the non-matching cell was left alone entirely.
        workbook.GetStyle(sheet.GetCell(plainAddress)!.StyleId).FillColor.Should().BeNull();
    }

    [Fact]
    public void ReplaceNextValue_FormatOnlyBlankSearchAndReplace_AppliesReplacementFormatWithoutChangingText()
    {
        var (workbook, boldAddress, _) = CreateWorkbookWithOneBoldCell();
        var session = CreateSession(workbook);
        var findOptions = new FindOptions(RequiredFormat: new StyleDiff(Bold: true));
        var replacementFormat = new StyleDiff(FillColor: new CellColor(0, 255, 0));

        var result = session.ReplaceNextValue("", "", findOptions, replacementFormat: replacementFormat);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ReplacedCount.Should().Be(1);

        var sheet = workbook.GetSheet(boldAddress.Sheet)!;
        sheet.GetCell(boldAddress)!.Value.Should().Be(new TextValue("Alpha"));
        var matchedStyle = workbook.GetStyle(sheet.GetCell(boldAddress)!.StyleId);
        matchedStyle.FillColor.Should().Be(new CellColor(0, 255, 0));
        matchedStyle.Bold.Should().BeTrue();
    }

    [Fact]
    public void ReplaceAllValues_EmptySearchWithNoFormatOrReplacementFormat_StillFails()
    {
        // No-regression sibling: a truly blank search (no Find-side RequiredFormat, no
        // Replace-side replacementFormat either) must keep failing exactly as before this fix --
        // the allowFormatOnly plumbing must never let a plain blank search through.
        var (workbook, _, _) = CreateWorkbookWithOneBoldCell();
        var session = CreateSession(workbook);

        var result = session.ReplaceAllValues("", "New");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Find text is required.");
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static (Workbook Workbook, CellAddress BoldAddress, CellAddress PlainAddress) CreateWorkbookWithOneBoldCell()
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

        return (workbook, boldAddress, plainAddress);
    }
}
