using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-51 commands-bucket findings:
///
/// R51-meta-1: RemoveSheetCommand.Apply had its own pre-loop that bare-removed global named
/// ranges targeting the deleted sheet BEFORE calling Workbook.RemoveSheet, shadowing the r50
/// #REF!-conversion fix on the real user-facing Delete-Sheet command path (Workbook.RemoveSheet
/// itself, called directly rather than through the command, already worked correctly — see
/// Round50NameManagerCrudTests — which is exactly why this regression went unnoticed).
///
/// R51-commands-freeze-split-view-3-3: SetSplitPanesCommand.Apply unconditionally zeroed
/// FrozenRows/FrozenCols even when both the incoming split row and split column were null (a
/// "no split established" invocation), silently destroying an unrelated, pre-existing freeze.
///
/// R51-commands-sort-custom-multilevel-3-2: CustomSortOrder.Compare's non-custom-list-member
/// fallback used raw StringComparison.Ordinal for the case-sensitive option, clumping all
/// uppercase-leading words ahead of lowercase-leading ones instead of Excel's
/// alphabetical-first / case-only-tiebreak behavior.
///
/// R51-io-picture-fill-shape-3-1: SetDrawingShapeColorsCommand/SetDrawingShapeGradientCommand/
/// SetDrawingShapeEffectCommand mutated fill/gradient/effect fields on a shape loaded verbatim
/// from an existing .xlsx (IsSourceLoaded == true) without ever clearing that flag, so the xlsx
/// writer's IsSupportedShape gate (!IsSourceLoaded) kept skipping the shape and the ORIGINAL
/// source fill/gradient/effect XML silently survived every save.
/// </summary>
public sealed class Round51CommandsBucketTests
{
    // ── R51-meta-1 ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RemoveSheetCommand_GlobalNameFullyOnDeletedSheet_BecomesRefErrorNotDropped()
    {
        var wb = new Workbook();
        var ctx = new TestCommandContext(wb);
        var keep = wb.AddSheet("Keep");
        var remove = wb.AddSheet("Remove");
        var range = new GridRange(
            new CellAddress(remove.Id, 0, 0),
            new CellAddress(remove.Id, 9, 0));
        wb.DefineNamedRange("SalesQ2", range);

        // Exercise the REAL command dispatched by the Delete-Sheet UI path, not Workbook.RemoveSheet
        // directly (which already worked correctly and is why the r50 regression test missed this).
        var outcome = new RemoveSheetCommand(remove.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.NamedRanges.Should().NotContainKey("SalesQ2");
        wb.NamedFormulas.Should().ContainKey("SalesQ2", "Excel keeps the name in the Name Manager, it does not delete it");
        wb.NamedFormulas["SalesQ2"].Should().Be("#REF!");
    }

    [Fact]
    public void RemoveSheetCommand_GlobalNameNotOnDeletedSheet_IsUntouched()
    {
        // Sibling no-regression: a name referring to a surviving sheet must not be touched by
        // deleting an unrelated sheet, whether reached via the command or the model API directly.
        var wb = new Workbook();
        var ctx = new TestCommandContext(wb);
        var keep = wb.AddSheet("Keep");
        var remove = wb.AddSheet("Remove");
        var keepRange = new GridRange(
            new CellAddress(keep.Id, 0, 0),
            new CellAddress(keep.Id, 1, 0));
        wb.DefineNamedRange("KeepRange", keepRange);

        var outcome = new RemoveSheetCommand(remove.Id).Apply(ctx);

        outcome.Success.Should().BeTrue();
        wb.NamedRanges.Should().ContainKey("KeepRange");
        wb.NamedRanges["KeepRange"].Should().Be(keepRange);
        wb.NamedFormulas.Should().NotContainKey("KeepRange");
    }

    // ── R51-commands-freeze-split-view-3-3 ──────────────────────────────────────────────────────

    [Fact]
    public void SetSplitPanesCommand_NullSplitPosition_DoesNotDestroyExistingFreeze()
    {
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();
        new SetFreezePanesCommand(sheet.Id, frozenRows: 5, frozenCols: 0).Apply(ctx).Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(5);

        // Mirrors SplitViewBtn_Click's A1-active-cell path: no valid split row/column could be
        // computed, so both arguments are null — this must be a no-op with respect to the
        // unrelated, already-established freeze.
        var outcome = new SetSplitPanesCommand(sheet.Id, null, null).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(5, "a fully-null split invocation must not silently wipe an existing freeze");
        sheet.FrozenCols.Should().Be(0);
        sheet.SplitRow.Should().BeNull();
        sheet.SplitColumn.Should().BeNull();
    }

    [Fact]
    public void SetSplitPanesCommand_RealSplitPosition_StillClearsExistingFreeze()
    {
        // Sibling no-regression: establishing an ACTUAL split must still clear any existing freeze,
        // since Excel's freeze and split panes are mutually exclusive.
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();
        new SetFreezePanesCommand(sheet.Id, frozenRows: 5, frozenCols: 2).Apply(ctx).Success.Should().BeTrue();

        var outcome = new SetSplitPanesCommand(sheet.Id, 10u, 4u).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);
        sheet.SplitRow.Should().Be(10u);
        sheet.SplitColumn.Should().Be(4u);
    }

    // ── R51-commands-sort-custom-multilevel-3-2 ─────────────────────────────────────────────────

    [Fact]
    public void CustomSortOrder_CaseSensitive_NonMemberTieBreak_IsAlphabeticalNotOrdinal()
    {
        CustomSortOrder.TryParse("Mon,Tue,Wed", out var order).Should().BeTrue();

        // Neither "Zebra" nor "apple" is a list member, so this exercises the non-member fallback.
        // Real Excel (and FreeX's own SortCommand.CompareCaseSensitiveText, documented as matching
        // Excel) sorts alphabetically first: "apple" before "Zebra". Raw ordinal comparison would
        // instead put "Zebra" (0x5A) before "apple" (0x61).
        var result = order!.Compare("Zebra", "apple", caseSensitive: true);

        result.Should().BeGreaterThan(0, "\"apple\" must sort before \"Zebra\" even with Case Sensitive checked");
    }

    [Fact]
    public void CustomSortOrder_CaseSensitive_SameLetterTiebreak_LowercaseBeforeUppercase()
    {
        // Sibling no-regression: the ONE place case-sensitivity is meant to matter — two strings
        // that are letter-for-letter identical except for case — must still resolve
        // lowercase-before-uppercase, exactly like SortCommand.CompareCaseSensitiveText.
        CustomSortOrder.TryParse("Mon,Tue,Wed", out var order).Should().BeTrue();

        var result = order!.Compare("apple", "Apple", caseSensitive: true);

        result.Should().BeLessThan(0, "\"apple\" (lowercase) must sort before \"Apple\" as a same-letter tiebreak");
    }

    // ── R51-io-picture-fill-shape-3-1 ────────────────────────────────────────────────────────────

    [Fact]
    public void SetDrawingShapeColorsCommand_OnSourceLoadedShape_ClearsIsSourceLoadedSoWriterEmitsIt()
    {
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            HasFill = true,
            FillColor = new CellColor(0xFF, 0, 0), // red, as if authored in Excel
            IsSourceLoaded = true,
        };
        sheet.DrawingShapes.Add(shape);

        var outcome = new SetDrawingShapeColorsCommand(
            sheet.Id, shape.Id, fillColor: new CellColor(0, 0, 0xFF), outlineColor: null).Apply(ctx);

        outcome.Success.Should().BeTrue();
        shape.FillColor.Should().Be(new CellColor(0, 0, 0xFF));
        shape.IsSourceLoaded.Should().BeFalse(
            "the xlsx writer skips any shape with IsSourceLoaded==true, so a fill edit made through the UI " +
            "would otherwise never reach the saved file");
    }

    [Fact]
    public void SetDrawingShapeColorsCommand_Revert_RestoresIsSourceLoaded()
    {
        // Sibling no-regression: undo must restore the shape to its exact pre-edit state, including
        // the IsSourceLoaded flag the fix now touches, not just the color fields.
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            HasFill = true,
            FillColor = new CellColor(0xFF, 0, 0),
            IsSourceLoaded = true,
        };
        sheet.DrawingShapes.Add(shape);

        var command = new SetDrawingShapeColorsCommand(
            sheet.Id, shape.Id, fillColor: new CellColor(0, 0, 0xFF), outlineColor: null);
        command.Apply(ctx).Success.Should().BeTrue();
        shape.IsSourceLoaded.Should().BeFalse();

        command.Revert(ctx);

        shape.FillColor.Should().Be(new CellColor(0xFF, 0, 0));
        shape.IsSourceLoaded.Should().BeTrue("undo must fully restore the shape's prior state");
    }
}
