using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression tests for R25-spill-dynamic-deep-3: selecting ONLY the anchor cell of a live
/// dynamic-array spill and pressing Delete must clear the formula and the whole spill, matching
/// Excel and mirroring the analogous R25-spill-dynamic-deep-1 fix already applied to
/// MoveRangeCommand. RejectIfSplitsArray must still block every other "partial array" shape:
/// a non-anchor member alone, or the anchor together with only some of the body.
/// </summary>
public class ClearContentsSpillAnchorTests
{
    private const string CannotChangePartOfArrayMessage = "You cannot change part of an array.";

    private static (Workbook Workbook, Sheet Sheet, CellAddress Anchor, ICommandContext Ctx) MakeLiveSpillSetup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(anchor, Cell.FromFormula("SEQUENCE(3,1)"));
        var cells = new ScalarValue[3, 1]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
            { new NumberValue(3) },
        };
        sheet.SetSpillRange(anchor, new RangeValue(cells)); // spills A1:A3 = 1,2,3
        return (wb, sheet, anchor, new TestCommandContext(wb));
    }

    [Fact]
    public void ClearContentsCommand_OnSpillAnchorAlone_ClearsWholeSpill()
    {
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var row2 = new CellAddress(sheet.Id, 2, 1); // A2
        var row3 = new CellAddress(sheet.Id, 3, 1); // A3

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(anchor, anchor)).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(anchor).Should().Be(BlankValue.Instance);
        sheet.GetValue(row2).Should().Be(BlankValue.Instance);
        sheet.GetValue(row3).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void ClearContentsCommand_OnNonAnchorSpillMemberAlone_IsStillBlocked()
    {
        // Sibling/opposite case from the finding: only the anchor is special-cased. A single
        // non-anchor member selected alone must still be rejected, exactly as before the fix.
        var (_, sheet, _, ctx) = MakeLiveSpillSetup();
        var row2 = new CellAddress(sheet.Id, 2, 1); // A2 - covered, non-anchor

        var outcome = new ClearContentsCommand(sheet.Id, new GridRange(row2, row2)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
        sheet.GetValue(row2).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void ClearContentsCommand_OnAnchorPlusPartialBody_IsStillBlocked()
    {
        // Another still-rejected shape: the anchor together with only SOME of the body (missing
        // A3) must not be treated the same as the anchor-alone allowance.
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var row2 = new CellAddress(sheet.Id, 2, 1); // A2
        var partialRange = new GridRange(anchor, row2); // A1:A2, missing A3

        var outcome = new ClearContentsCommand(sheet.Id, partialRange).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Be(CannotChangePartOfArrayMessage);
    }

    [Fact]
    public void ClearContentsCommand_OnWholeSpillRangeAsUnit_IsStillAllowed()
    {
        // Already-working case that must not regress: selecting the full spill footprint and
        // clearing it as a unit continues to work.
        var (_, sheet, anchor, ctx) = MakeLiveSpillSetup();
        var row3 = new CellAddress(sheet.Id, 3, 1); // A3
        var wholeRange = new GridRange(anchor, row3); // A1:A3

        var outcome = new ClearContentsCommand(sheet.Id, wholeRange).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
    }
}
