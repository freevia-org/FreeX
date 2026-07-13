using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R37-commands-find-replace-2-1: "Look in: Formulas" + "Match entire cell contents" must match
/// Excel's formula-bar text, which always includes the leading '=' that Cell.FormulaText itself
/// omits -- so entire-cell matching must compare against "=" + FormulaText, not the bare text.
///
/// R37-commands-find-replace-2-2: Replace on a literal (non-formula) date/time cell under
/// "Look in: Formulas" must not silently truncate the time-of-day to midnight -- a replacement
/// whose text does not itself specify a time must preserve the cell's original fractional day.
/// </summary>
public class R37_FindReplaceFormulaEqualsAndDateTimeTests
{
    private static (Workbook Workbook, Sheet Sheet, ICommandBus CommandBus) Setup()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var commandBus = new CommandBus(id => new TestCommandContext(workbook));
        return (workbook, sheet, commandBus);
    }

    [Fact]
    public void Find_FormulasMode_EntireCell_RequiresLeadingEqualsToMatchFormulaText()
    {
        var (wb, sheet, _) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "SUM(B1:B5)");

        // Real Excel's formula-bar text for this cell is "=SUM(B1:B5)" -- an entire-cell search
        // for exactly that text (with the leading '=') must match.
        var withEquals = FindReplaceService.Find(
            wb, "=SUM(B1:B5)", new FindOptions(LookIn: FindLookIn.Formulas), matchEntireCell: true);
        withEquals.Should().ContainSingle().Which.Address.Should().Be(a1);

        // Conversely, a bare search omitting the leading '=' does NOT equal the full formula-bar
        // text and must NOT match under Match-entire-cell-contents (this was backwards pre-fix).
        var withoutEquals = FindReplaceService.Find(
            wb, "SUM(B1:B5)", new FindOptions(LookIn: FindLookIn.Formulas), matchEntireCell: true);
        withoutEquals.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceAll_FormulasMode_EntireCell_WithLeadingEquals_ReplacesFormulaText()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "SUM(B1:B5)");

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "=SUM(B1:B5)",
            "=MAX(B1:B10)",
            new FindOptions(LookIn: FindLookIn.Formulas),
            matchEntireCell: true);

        count.Should().Be(1);
        // FormulaText storage still omits the leading '=' (Cell.cs convention) even though the
        // match/replace text carried it.
        sheet.GetCell(a1)!.FormulaText.Should().Be("MAX(B1:B10)");
    }

    // Sibling no-regression case: substring (non-entire-cell) matching in Formulas mode is
    // unaffected by the leading-'=' fix, since Contains/Replace tolerate the extra prefix
    // character either way -- this must keep working exactly as before.
    [Fact]
    public void ReplaceAll_FormulasMode_SubstringMatch_StillReplacesFormulaText()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "SUM(B1:B5)");

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "SUM",
            "MAX",
            new FindOptions(LookIn: FindLookIn.Formulas));

        count.Should().Be(1);
        sheet.GetCell(a1)!.FormulaText.Should().Be("MAX(B1:B5)");
    }

    [Fact]
    public void ReplaceAll_FormulasMode_OnLiteralDateTimeCell_PreservesOriginalTimeOfDay()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var originalDateTime = new DateTime(2024, 6, 15, 14, 30, 0);
        sheet.SetCell(a1, new DateTimeValue(originalDateTime.ToOADate()));

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "2024-06-15",
            "2024-06-20",
            new FindOptions(LookIn: FindLookIn.Formulas),
            matchEntireCell: true);

        count.Should().Be(1);
        var newValue = sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>().Subject;
        var expectedSerial = new DateTime(2024, 6, 20, 14, 30, 0).ToOADate();
        newValue.Value.Should().BeApproximately(expectedSerial, 1e-9);
    }

    // Sibling no-regression case: when the replacement text itself specifies a time, that
    // explicit time must be honored rather than overridden by the original fractional day.
    [Fact]
    public void ReplaceAll_FormulasMode_OnLiteralDateTimeCell_WhenReplacementSpecifiesTime_UsesThatTime()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var originalDateTime = new DateTime(2024, 6, 15, 14, 30, 0);
        sheet.SetCell(a1, new DateTimeValue(originalDateTime.ToOADate()));

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "2024-06-15",
            "2024-06-20 09:15:00",
            new FindOptions(LookIn: FindLookIn.Formulas),
            matchEntireCell: true);

        count.Should().Be(1);
        var newValue = sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>().Subject;
        var expectedSerial = new DateTime(2024, 6, 20, 9, 15, 0).ToOADate();
        newValue.Value.Should().BeApproximately(expectedSerial, 1e-9);
    }

    // Sibling no-regression case: a literal date cell with NO time-of-day component must
    // continue to replace cleanly to a plain date (midnight), unaffected by the preservation fix.
    [Fact]
    public void ReplaceAll_FormulasMode_OnLiteralDateOnlyCell_StillReplacesToPlainDate()
    {
        var (wb, sheet, commandBus) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var originalDate = new DateTime(2024, 6, 15, 0, 0, 0);
        sheet.SetCell(a1, new DateTimeValue(originalDate.ToOADate()));

        var count = FindReplaceService.ReplaceAll(
            wb,
            commandBus,
            "2024-06-15",
            "2024-06-20",
            new FindOptions(LookIn: FindLookIn.Formulas),
            matchEntireCell: true);

        count.Should().Be(1);
        var newValue = sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>().Subject;
        var expectedSerial = new DateTime(2024, 6, 20).ToOADate();
        newValue.Value.Should().BeApproximately(expectedSerial, 1e-9);
    }
}
