using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Editing;

/// <summary>
/// R53-commands-insert-copied-cut-cells-3-1/3-2/3-3: "Insert Cut Cells" must behave like a true Excel
/// move, not a copy-paste-then-clear-source combo -- own formula references must stay literal unless
/// their precedent was itself part of the cut, external formulas that reference a cut cell must follow
/// it to the destination, the vacated source's merge/hyperlink must not survive alongside the
/// destination's copy, and a CF/DV rule scoped entirely to the cut range must follow the move.
/// </summary>
public sealed class InsertCopiedCellsPlannerCutMoveSemanticsTests
{
    [Fact]
    public void CreateCommand_Cut_KeepsOwnFormulaLiteral_AndRepointsExternalReference()
    {
        // A1=5; B1="=A1+1" (=6, the cell being cut); C1="=B1*10" (=60, external ref TO the cut cell).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(5)));
        var b1Formula = Cell.FromFormula("A1+1");
        b1Formula.Value = new NumberValue(6);
        sheet.SetCell(b1, b1Formula);
        var c1Formula = Cell.FromFormula("B1*10");
        c1Formula.Value = new NumberValue(60);
        sheet.SetCell(c1, c1Formula);

        var source = new GridRange(b1, b1);
        var cells = new[] { (b1, sheet.GetCell(b1)!.Clone()) };

        // Insert Cut Cells at D1 (ShiftRight): B1's content moves to D1.
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var destination = new GridRange(d1, d1);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftRight, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        // The moved formula's own reference to A1 (never part of the cut) must stay LITERAL, not be
        // shifted by the paste's blanket relative-offset rewrite (the bug: paste alone would turn this
        // into "=C1+1").
        sheet.GetCell(d1)!.FormulaText.Should().Be("A1+1");

        // C1's external reference to the cut cell must follow it to D1 (the bug: ClearContentsCommand
        // alone never rewrites other cells' formulas, so C1 would be left as the stale "=B1*10").
        sheet.GetCell(c1)!.FormulaText.Should().Be("D1*10");
    }

    [Fact]
    public void CreateCommand_Cut_PlainValueFormula_StillMovesCorrectly_NoRegression()
    {
        // Sibling no-regression case: a formula-free cut (the pre-existing, already-tested behavior)
        // must keep moving plain values unaffected by the new formula-fixup follow-up command.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(42)));
        var source = new GridRange(a1, a1);
        var cells = new[] { (a1, sheet.GetCell(a1)!.Clone()) };

        var d1 = new CellAddress(sheet.Id, 1, 4);
        var destination = new GridRange(d1, d1);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftRight, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(d1).Should().Be(new NumberValue(42));
        sheet.GetValue(a1).Should().Be(BlankValue.Instance);
    }

    [Fact]
    public void CreateCommand_Cut_MergedCellWithHyperlink_UnmergesAndDetachesAtSource()
    {
        // A1:B1 merged, holding a hyperlinked cell at A1. Cut A1:B1, Insert Cut Cells shifting right
        // into D1:E1.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("Header")));
        sheet.AddMergedRegion(new GridRange(a1, b1));
        sheet.Hyperlinks[a1] = "https://example.com";

        var source = new GridRange(a1, b1);
        var cells = new[]
        {
            (a1, sheet.GetCell(a1)!.Clone()),
            (b1, Cell.FromValue(BlankValue.Instance))
        };

        var d1 = new CellAddress(sheet.Id, 1, 4);
        var e1 = new CellAddress(sheet.Id, 1, 5);
        var destination = new GridRange(d1, e1);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftRight, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        // The vacated source must NOT keep a phantom empty merge (the bug: isCutSource defaulted to
        // false, so the merge-removal gate in ClearContentsCommand never ran).
        sheet.MergedRegions.Should().NotContain(new GridRange(a1, b1));
        // The destination correctly received the (single) recreated merge.
        sheet.MergedRegions.Should().Contain(new GridRange(d1, e1));

        // The hyperlink must not remain attached to the vacated source (the bug: the same isCutSource
        // gate also skips hyperlink removal).
        sheet.Hyperlinks.Should().NotContainKey(a1);
        sheet.Hyperlinks.Should().ContainKey(d1);
    }

    [Fact]
    public void CreateCommand_Cut_NoMergeOrHyperlink_StillMovesPlainCell_NoRegression()
    {
        // Sibling no-regression case: a cut with no merge/hyperlink at all must be unaffected by the
        // isCutSource: true flag now being passed to ClearContentsCommand.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("plain")));
        var source = new GridRange(a1, a1);
        var cells = new[] { (a1, sheet.GetCell(a1)!.Clone()) };

        var d1 = new CellAddress(sheet.Id, 1, 4);
        var destination = new GridRange(d1, d1);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftRight, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(d1).Should().Be(new TextValue("plain"));
        sheet.GetValue(a1).Should().Be(BlankValue.Instance);
        sheet.MergedRegions.Should().BeEmpty();
    }

    [Fact]
    public void CreateCommand_Cut_ConditionalFormatScopedToSource_FollowsTheMove()
    {
        // A CF rule's AppliesTo is exactly B1:B3, matching the range about to be cut.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var b3 = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(b1, Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(b2, Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(b3, Cell.FromValue(new NumberValue(3)));

        var source = new GridRange(b1, b3);
        var rule = new ConditionalFormat
        {
            AppliesTo = source,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10"
        };
        sheet.ConditionalFormats.Add(rule);

        var cells = new[]
        {
            (b1, sheet.GetCell(b1)!.Clone()),
            (b2, sheet.GetCell(b2)!.Clone()),
            (b3, sheet.GetCell(b3)!.Clone())
        };

        var d1 = new CellAddress(sheet.Id, 1, 4);
        var destination = new GridRange(d1, d1);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftDown, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        var expectedDestination = new GridRange(d1, new CellAddress(sheet.Id, 3, 4));
        rule.AppliesTo.Should().Be(expectedDestination, "the CF rule's range must follow the moved cells");
    }

    [Fact]
    public void CreateCommand_Cut_ConditionalFormatNotScopedToSource_IsLeftUnchanged_NoRegression()
    {
        // Sibling no-regression case: a CF rule elsewhere on the sheet (not overlapping the cut range
        // at all) must not be touched by the new rule-translation follow-up.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(1)));
        var source = new GridRange(a1, a1);

        var unrelatedRange = new GridRange(
            new CellAddress(sheet.Id, 10, 10),
            new CellAddress(sheet.Id, 12, 10));
        var rule = new ConditionalFormat
        {
            AppliesTo = unrelatedRange,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5"
        };
        sheet.ConditionalFormats.Add(rule);

        var cells = new[] { (a1, sheet.GetCell(a1)!.Clone()) };
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var destination = new GridRange(d1, d1);

        var command = InsertCopiedCellsPlanner.CreateCommand(
            workbook, sheet.Id, source, cells, destination,
            KeyboardInsertDeleteDialogChoice.ShiftRight, isCut: true);

        command.Apply(ctx).Success.Should().BeTrue();

        rule.AppliesTo.Should().Be(unrelatedRange);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
