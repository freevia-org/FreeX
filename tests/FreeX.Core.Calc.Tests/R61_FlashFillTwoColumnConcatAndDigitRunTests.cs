using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-61 flash-fill findings:
/// R61-commands-flash-fill-6-1: plain (no-separator) two-column concatenation had no matching
/// pattern in FlashFillService.FillFromColumns, so FlashFillCommand fell back to the
/// single-column digit-mask fallback which baked the first example's OTHER column text in as a
/// false constant instead of recognizing the concatenation.
/// R61-commands-flash-fill-6-2: only the FINAL embedded digit run could ever be extracted; a
/// leading/middle digit run embedded before trailing letters/digits (e.g. "12" from
/// "Room12-Wing3") was never inferred.
/// </summary>
public sealed class R61_FlashFillTwoColumnConcatAndDigitRunTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ── R61-commands-flash-fill-6-1 ─────────────────────────────────────────────

    [Fact]
    public void FlashFillCommand_SingleExample_TwoColumnNoSeparatorConcat_UsesActualColumnAValues()
    {
        // A: "AB","CD","EF"  B: "123","456","789"  C1 = "AB123" (single example, no separator)
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("AB"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("CD"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("EF"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("123"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("456"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("789"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("AB123"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Excel-correct: joins column A's ACTUAL per-row value with column B, not the first
        // example's literal "AB" baked in as a false constant (which would give "AB456"/"AB789").
        sheet.GetCell(2, 3)!.Value.Should().Be(new TextValue("CD456"));
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("EF789"));
    }

    [Fact]
    public void FlashFillCommand_TwoExamples_TwoColumnNoSeparatorConcat_FillsRemainingRow()
    {
        // Two unambiguous examples of the same no-separator concatenation.
        var (wb, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Widget"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Gadget"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Doohickey"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("100"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("200"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("300"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Widget100"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Gadget200"));

        var cmd = new FlashFillCommand(sheet.Id, fillColIndex: 3, sourceColIndex: 2, startRow: 1, endRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(3, 3)!.Value.Should().Be(new TextValue("Doohickey300"));
    }

    [Fact]
    public void FillFromColumns_ReverseOrderNoSeparatorConcat_IsRecognized()
    {
        // Sibling/no-regression check: the s[1]+s[0] bare-reverse-concat pattern still works
        // and other prior column-pattern behavior (space-joined) is unaffected.
        var reversed = FlashFillService.FillFromColumns(
            [
                ["100", "Widget"],
                ["200", "Gadget"]
            ],
            ["Widget100", "Gadget200"],
            [
                ["300", "Doohickey"]
            ]);

        reversed.Should().BeEquivalentTo(["Doohickey300"], o => o.WithStrictOrdering());

        var spaceJoined = FlashFillService.FillFromColumns(
            [
                ["Ada", "Lovelace"],
                ["Grace", "Hopper"]
            ],
            ["Ada Lovelace", "Grace Hopper"],
            [
                ["Alan", "Turing"]
            ]);

        spaceJoined.Should().BeEquivalentTo(["Alan Turing"], o => o.WithStrictOrdering());
    }

    // ── R61-commands-flash-fill-6-2 ─────────────────────────────────────────────

    [Fact]
    public void Fill_LeadingEmbeddedDigitRun_ExtractsFirstRunNotFinalRun()
    {
        // "Room12-Wing3" -> "12" (leading room number, not the trailing wing number "3").
        var result = FlashFillService.Fill(
            [("Room12-Wing3", "12")],
            ["Room45-Wing1", "Room7-Wing2"]);

        result.Should().BeEquivalentTo(["45", "7"], o => o.WithStrictOrdering());
    }

    [Fact]
    public void Fill_TrailingEmbeddedDigitRun_StillExtractsFinalRun_NoRegression()
    {
        // Sibling no-regression check: the pre-existing final-digit-run pattern (trailing run,
        // with an earlier digit run present) must still work unaffected by the new leading-run pattern.
        var result = FlashFillService.Fill(
            [("Item12-Unit34", "34")],
            ["Item56-Unit78"]);

        result.Should().BeEquivalentTo(["78"], o => o.WithStrictOrdering());
    }
}
