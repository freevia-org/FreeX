using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R66-commands-clear-delete-6-1: Home&gt;Clear&gt;Clear Formats did not remove conditional-formatting
/// rules on the selection, even though Excel's Clear Formats does (CF is itself a form of formatting).
/// <see cref="WorkbookSession.ClearSelectedRangeFormats"/> used to be a bare
/// <c>ApplyStyleCommand(ClearFormatsDiff)</c> style-only apply; the fix composes it with
/// <c>ClearConditionalFormatsCommand</c> the same way <see cref="WorkbookSession.ClearSelectedRangeAll"/>
/// already does.
/// </summary>
public sealed class R66_ClearFormatsRemovesConditionalFormatTests
{
    [Fact]
    public void ClearSelectedRangeFormats_RemovesConditionalFormatRuleOnSelection_AndStillClearsStyle()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a5 = new CellAddress(sheet.Id, 5, 1);
        var selectedRange = new GridRange(a1, a5);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = selectedRange,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
        });
        var style = new CellStyle { Bold = true };
        sheet.SetCell(a1, new NumberValue(1));
        sheet.GetCell(a1)!.StyleId = workbook.RegisterStyle(style);

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        session.SelectRange(selectedRange);

        var result = session.ClearSelectedRangeFormats();

        result.Success.Should().BeTrue();
        sheet.ConditionalFormats.Should().BeEmpty(
            "Clear Formats must remove conditional-formatting rules on the cleared selection, matching Excel");
        workbook.GetStyle(sheet.GetCell(a1)!.StyleId).Bold.Should().NotBe(true,
            "Clear Formats must still clear the selection's own style (no regression)");
    }

    [Fact]
    public void ClearSelectedRangeFormats_LeavesConditionalFormatRuleOutsideSelectionUntouched()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a5 = new CellAddress(sheet.Id, 5, 1);
        var clearedRange = new GridRange(a1, a5);
        var untouchedRange = new GridRange(
            new CellAddress(sheet.Id, 1, 10), new CellAddress(sheet.Id, 5, 10));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = untouchedRange,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
        });

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        session.SelectRange(clearedRange);

        var result = session.ClearSelectedRangeFormats();

        result.Success.Should().BeTrue();
        sheet.ConditionalFormats.Should().ContainSingle(
            f => f.AppliesTo.Equals(untouchedRange),
            "a conditional-formatting rule entirely outside the cleared selection must be untouched");
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
