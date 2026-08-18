using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public class AutofillCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void FillValue_Down_RepeatsSourceValue()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, new NumberValue(42));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 4, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(42));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(42));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void FillNumberSeries_Down_ContinuesStepFromSourceRange()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 5, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetValue(3, 1).Should().Be(new NumberValue(5));
        sheet.GetValue(4, 1).Should().Be(new NumberValue(7));
        sheet.GetValue(5, 1).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void FillNumberSeries_Down_NonConstantStep_UsesLinearFitAcrossAllValues()
    {
        // Excel's fill handle fits a least-squares trend line across ALL selected source
        // values (not just the last two) when the step is non-constant, and continues the
        // FITTED line itself rather than stepping off the raw last source value. For 1, 2, 4
        // the best-fit line is y = 5/6 + 1.5x (slope 1.5, intercept 5/6), so at x=3 and x=4
        // (the two filled cells) it evaluates to 16/3 and 41/6 -- not 5.5, 7 (which would be
        // the raw last value 4 stepped by the fitted slope, ignoring the fitted intercept).
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(4));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 4, 1),
            new CellAddress(sheet.Id, 5, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        ((NumberValue)sheet.GetValue(4, 1)).Value.Should().BeApproximately(16.0 / 3.0, 1e-9);
        ((NumberValue)sheet.GetValue(5, 1)).Value.Should().BeApproximately(41.0 / 6.0, 1e-9);
    }

    [Fact]
    public void FillNumberSeries_Right_ContinuesStepFromSourceRange()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, 1, 5));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetValue(1, 3).Should().Be(new NumberValue(8));
        sheet.GetValue(1, 4).Should().Be(new NumberValue(11));
        sheet.GetValue(1, 5).Should().Be(new NumberValue(14));
    }

    [Fact]
    public void FillNumberSeries_Up_ContinuesStepFromSourceRange()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(7));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(3));
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void FillNumberSeries_Left_ContinuesStepFromSourceRange()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(11));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, 1, 4));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetValue(1, 2).Should().Be(new NumberValue(5));
        sheet.GetValue(1, 1).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void FillDateSeries_Down_ContinuesDayStepFromSourceRange()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 1)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), DateTimeValue.FromDateTime(new DateTime(2026, 5, 3)));

        var sourceRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        ((DateTimeValue)sheet.GetValue(3, 1)).ToDateTime().Should().Be(new DateTime(2026, 5, 5));
        ((DateTimeValue)sheet.GetValue(4, 1)).ToDateTime().Should().Be(new DateTime(2026, 5, 7));
    }

    [Fact]
    public void FillFormula_Down_IncrementsRowReferences()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromFormula("A1+B1"));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetCell(2, 1)!.FormulaText.Should().Be("A2+B2");
        sheet.GetCell(3, 1)!.FormulaText.Should().Be("A3+B3");
    }

    [Fact]
    public void FillFormula_PreservesFunctionNames_WithDigitSuffix()
    {
        // Regression: regex shift incorrectly incremented digits inside function names
        // e.g. =LOG10(A1) shifted down 1 row would become =LOG11(A2) with the old regex.
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromFormula("LOG10(A1)"));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetCell(2, 1)!.FormulaText.Should().Be("LOG10(A2)");
    }

    [Fact]
    public void FillFormula_PreservesAbsoluteRefs()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromFormula("$A$1+B1"));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetCell(2, 1)!.FormulaText.Should().Be("$A$1+B2");
    }

    [Fact]
    public void FillFormula_Up_DecrementsRowReferences()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(source, Cell.FromFormula("A3+B3"));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetCell(2, 1)!.FormulaText.Should().Be("A2+B2");
        sheet.GetCell(1, 1)!.FormulaText.Should().Be("A1+B1");
    }

    [Fact]
    public void FillFormula_Left_DecrementsColumnReferences()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(source, Cell.FromFormula("C1+D1"));

        var sourceRange = new GridRange(source, source);
        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2));

        new AutofillCommand(sheet.Id, sourceRange, fillRange).Apply(ctx);

        sheet.GetCell(1, 2)!.FormulaText.Should().Be("B1+C1");
        sheet.GetCell(1, 1)!.FormulaText.Should().Be("A1+B1");
    }

    [Fact]
    public void Autofill_RejectsDetachedFillRange()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, new NumberValue(10));
        var target = new CellAddress(sheet.Id, 4, 1);

        var outcome = new AutofillCommand(
            sheet.Id,
            new GridRange(source, source),
            new GridRange(target, target)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("adjacent");
        sheet.GetCell(target).Should().BeNull();
    }

    [Fact]
    public void FillRevert_RestoresOriginalCells()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, new NumberValue(10));
        sheet.SetCell(target, new NumberValue(99));

        var cmd = new AutofillCommand(
            sheet.Id,
            new GridRange(source, source),
            new GridRange(target, target));
        cmd.Apply(ctx);
        cmd.Revert(ctx);

        sheet.GetValue(2, 1).Should().Be(new NumberValue(99));
    }

    [Fact]
    public void Autofill_RejectsLockedTargetsOnProtectedSheet()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, new TextValue("source"));
        sheet.SetCell(target, new TextValue("target"));
        sheet.IsProtected = true;

        var outcome = new AutofillCommand(
            sheet.Id,
            new GridRange(source, source),
            new GridRange(target, target)).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GetValue(target).Should().Be(new TextValue("target"));
    }

    [Fact]
    public void Autofill_AllowsUnlockedTargetsOnProtectedSheet()
    {
        var (workbook, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        var unlockedStyle = workbook.RegisterStyle(new CellStyle { Locked = false });
        sheet.SetCell(source, new TextValue("source"));
        var targetCell = Cell.FromValue(new TextValue("target"));
        targetCell.StyleId = unlockedStyle;
        sheet.SetCell(target, targetCell);
        sheet.IsProtected = true;

        var outcome = new AutofillCommand(
            sheet.Id,
            new GridRange(source, source),
            new GridRange(target, target)).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(target).Should().Be(new TextValue("source"));
    }

    [Fact]
    public void FillRevert_RestoresStyleOnlyEntries()
    {
        // Autofill over a blank-but-styled cell wipes the style-only entry.
        // Undo must restore the original style-only entry, not leave the cell plain.
        var (workbook, sheet, ctx) = Setup();

        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);

        var greenStyle = workbook.RegisterStyle(new CellStyle { FillColor = new CellColor(0, 255, 0) });

        // Source has a value; target is blank but has a style-only fill.
        sheet.SetCell(source, new NumberValue(10));
        sheet.SetStyleOnly(target.Row, target.Col, greenStyle);

        var cmd = new AutofillCommand(
            sheet.Id,
            new GridRange(source, source),
            new GridRange(target, target));

        cmd.Apply(ctx);

        // After apply: target has the source value; style-only is gone (SetCell cleared it)
        sheet.GetValue(target).Should().Be(new NumberValue(10));

        cmd.Revert(ctx);

        // After undo: target is blank again and style-only is restored
        sheet.GetCell(target).Should().BeNull();
        sheet.GetStyleOnly(target.Row, target.Col).Should().Be(greenStyle);
    }

    [Fact]
    public void Apply_ScansFillTargetsWithoutMaterializingAddressList()
    {
        var source = ModelSourceTestSupport.ReadCommandsSource("AutofillCommand.cs");
        var apply = source[
            source.IndexOf("public CommandOutcome Apply", StringComparison.Ordinal)..
            source.IndexOf("public void Revert", StringComparison.Ordinal)];

        apply.Should().NotContain("_fillRange.AllCells().ToList()");
        // Both the snapshot and the writtenCells list are capacity-hinted from GetFillCellCapacity():
        apply.Should().Contain("var capacity = GetFillCellCapacity()");
        apply.Should().Contain("new List<(CellAddress Addr, Cell? OldCell, StyleId? OldStyleOnly)>(capacity)");
        apply.Should().Contain("new List<CellAddress>(capacity)");
        apply.Should().Contain("for (var row = _fillRange.Start.Row; row <= _fillRange.End.Row; row++)");
        apply.Should().Contain("for (var col = _fillRange.Start.Col; col <= _fillRange.End.Col; col++)");
    }

    [Fact]
    public void Apply_ReturnsAffectedCellsMatchingFillRange()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, new NumberValue(10));

        var fillRange = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 4, 1));

        var outcome = new AutofillCommand(
            sheet.Id,
            new GridRange(source, source),
            fillRange).Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().NotBeNull();
        outcome.AffectedCells!.Count.Should().Be(3);
        outcome.AffectedCells.Should().Contain(new CellAddress(sheet.Id, 2, 1));
        outcome.AffectedCells.Should().Contain(new CellAddress(sheet.Id, 3, 1));
        outcome.AffectedCells.Should().Contain(new CellAddress(sheet.Id, 4, 1));
    }

    // R142-comments-notes-1: the fill handle (AutofillCommand) must carry a source cell's legacy
    // note (Comments/CommentAuthors/ShownComments) and threaded comment to every destination cell
    // it fills, exactly like it already does for Hyperlinks, and undo must restore precisely what
    // was at the destination beforehand rather than leaving the fill's comment behind or wiping a
    // pre-existing destination comment outright.
    [Fact]
    public void FillDown_CopiesLegacyNoteAndThreadedCommentAndUndoRestoresDestinationOriginals()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(source, new NumberValue(42));
        sheet.Comments[source] = "Source note";
        sheet.CommentAuthors[source] = "Alice";
        sheet.ShownComments.Add(source);
        sheet.ThreadedComments[source] = new ThreadedComment("Source thread") { Id = "{SRC-ID}" };

        // The destination already has its OWN pre-existing note before the fill overwrites it --
        // undo must restore exactly this, not just blank the destination out.
        sheet.SetCell(target, new NumberValue(0));
        sheet.Comments[target] = "Original destination note";
        sheet.CommentAuthors[target] = "Bob";
        sheet.ThreadedComments[target] = new ThreadedComment("Original destination thread") { Id = "{DST-ID}" };

        var command = new AutofillCommand(
            sheet.Id,
            new GridRange(source, source),
            new GridRange(target, target));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(2, 1).Should().Be(new NumberValue(42));
        sheet.Comments[target].Should().Be("Source note");
        sheet.CommentAuthors[target].Should().Be("Alice");
        sheet.ShownComments.Should().Contain(target);
        sheet.ThreadedComments[target].Text.Should().Be("Source thread");
        // The copy must mint its own thread id rather than duplicating the source's persisted id
        // (mirrors CopyRangeCommand.ClonedThreadedCommentForNewAddress -- otherwise two threads
        // sharing one id collide on reload).
        sheet.ThreadedComments[target].Id.Should().BeNull();

        command.Revert(ctx);

        sheet.Comments[target].Should().Be("Original destination note");
        sheet.CommentAuthors[target].Should().Be("Bob");
        sheet.ShownComments.Should().NotContain(target);
        sheet.ThreadedComments[target].Text.Should().Be("Original destination thread");
        sheet.ThreadedComments[target].Id.Should().Be("{DST-ID}");
    }

    // Sibling test: an unrelated cell outside the fill range keeps its own note completely
    // untouched by a fill (and by that fill's undo), proving the new comment-carry logic is
    // scoped to the actual fill destination and doesn't leak into neighbouring cells.
    [Fact]
    public void FillDown_LeavesUnrelatedCellsNoteUntouched()
    {
        var (_, sheet, ctx) = Setup();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 2, 1);
        var unrelated = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(source, new NumberValue(7));
        sheet.Comments[source] = "Source note";
        sheet.SetCell(unrelated, new NumberValue(99));
        sheet.Comments[unrelated] = "Unrelated note";
        sheet.CommentAuthors[unrelated] = "Carol";

        var command = new AutofillCommand(
            sheet.Id,
            new GridRange(source, source),
            new GridRange(target, target));

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.Comments[unrelated].Should().Be("Unrelated note");
        sheet.CommentAuthors[unrelated].Should().Be("Carol");

        command.Revert(ctx);
        sheet.Comments[unrelated].Should().Be("Unrelated note");
        sheet.CommentAuthors[unrelated].Should().Be("Carol");
    }
}
