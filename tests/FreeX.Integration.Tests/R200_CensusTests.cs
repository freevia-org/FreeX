using FluentAssertions;
using Free.Shared.IO;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r200: the two questions with the longest yield records were retired by CENSUS rather than asked
/// again. Enumerating 767 sites found in one pass roughly what four rounds of sampling had found in
/// total, which says the earlier rounds' single-finding-per-question results were an artifact of
/// sampling and not a measure of what was left.
/// </summary>
public sealed class R200_CensusTests
{
    private const string Astral = "\U0001F600";

    // ── The cell-text cap had THREE implementations, all cutting mid-surrogate ────────────────
    // Typed entry, external-clipboard paste and delimited-text import each declared the constant
    // and the one-line slice again. This is the same shape as r199's Flash Fill finding: fixing one
    // copy of a helper leaves the others, so the fix here is to delete two of the three.

    [Fact]
    public void TheCellTextCap_CutsOnACharacterBoundary()
    {
        // An astral character straddling the limit: 32766 filler units, then a 2-unit character.
        var text = new string('a', SurrogateSafeTruncation.SpreadsheetCellTextLimit - 1) + Astral;

        var capped = SurrogateSafeTruncation.LimitToCellText(text);

        capped.Length.Should().Be(SurrogateSafeTruncation.SpreadsheetCellTextLimit - 1,
            "the character that would not fit whole is dropped entirely");
        HasLoneSurrogate(capped).Should().BeFalse();
    }

    [Fact]
    public void TheCellTextCap_LeavesTextUnderTheLimitAlone()
    {
        SurrogateSafeTruncation.LimitToCellText("hello").Should().Be("hello");
    }
    [Fact]
    public void TypedCellEntry_CapsThroughTheSameHelper()
    {
        // The mainline path, through the copy that lived in CellEntryParser.
        var workbook = new Workbook("t");
        var sheet = workbook.AddSheet("S");
        var text = new string('a', SurrogateSafeTruncation.SpreadsheetCellTextLimit - 1) + Astral;

        var cell = FreeX.App.Services.CellEntryParser.CreateCell(
            text, new CellAddress(sheet.Id, 1, 1), useR1C1ReferenceStyle: false, workbook);

        cell.Value.Should().BeOfType<TextValue>();
        HasLoneSurrogate(((TextValue)cell.Value!).Value).Should().BeFalse(
            "typed entry must not store half a character");
    }

    // ── Commands that changed nothing and still cleared redo ──────────────────────────────────

    [Fact]
    public void ClearOutline_OnASheetWithNoOutline_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ClearWorksheetOutlineCommand(sheet.Id).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ClearOutline_OnASheetWithAnOutline_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.RowOutlineLevels[2] = 1;

        var outcome = new ClearWorksheetOutlineCommand(sheet.Id).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.RowOutlineLevels.Should().BeEmpty();
    }

    [Fact]
    public void UnhideRows_OnAnAllVisibleSelection_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new SetRowsHiddenCommand(sheet.Id, 1, 5, hidden: false).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void HideRows_OnAnAlreadyHiddenSelection_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        for (uint row = 1; row <= 5; row++)
            sheet.HiddenRows.Add(row);

        new SetRowsHiddenCommand(sheet.Id, 1, 5, hidden: true).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void HidingRowsThatAreVisible_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();

        var outcome = new SetRowsHiddenCommand(sheet.Id, 1, 5, hidden: true).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.HiddenRows.Should().Contain(3u);
    }

    [Fact]
    public void UnhideColumns_OnAnAllVisibleSelection_ReportsNoOp()
    {
        // The sibling that was fixed alongside: one of the pair left behind is how this class hides.
        var (sheet, ctx) = Fixture();

        new SetColumnsHiddenCommand(sheet.Id, 1, 5, hidden: false).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void HidingColumnsThatAreVisible_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();

        new SetColumnsHiddenCommand(sheet.Id, 1, 5, hidden: true).Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void ClearDataValidation_OverASelectionWithNoRules_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ClearDataValidationCommand(
                sheet.Id,
                new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 5)))
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ReplacingConditionalFormatsWithTheSameList_ReportsNoOp()
    {
        // Closing Manage Rules with OK after only looking at it.
        var (sheet, ctx) = Fixture();
        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>5",
        };
        sheet.ConditionalFormats.Add(rule);

        new ReplaceAllConditionalFormatsCommand(sheet.Id, [rule]).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ReplacingConditionalFormatsWithAnEditedList_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>5",
        });

        var edited = new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>6",
        };

        new ReplaceAllConditionalFormatsCommand(sheet.Id, [edited]).Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return true;
                i++;
                continue;
            }

            if (char.IsLowSurrogate(text[i]))
                return true;
        }

        return false;
    }
}
