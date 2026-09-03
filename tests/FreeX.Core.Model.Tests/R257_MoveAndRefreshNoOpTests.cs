using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r257: three commands that r221/r225/r232 left on the debt for the same stated reason -- each
/// "needs a real before/after comparison, not a guard on the arguments". r255 supplied that
/// comparison, so this round is applying it rather than building anything new.
///
/// <para>Both directions per command. The changed direction is the load-bearing one: all three write
/// cell content, so a decision that wrongly reported a no-op would drop real data from the undo
/// stack.</para>
/// </summary>
public sealed class R257_MoveAndRefreshNoOpTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Workbook Wb, Sheet Sheet, TestCommandContext Ctx, PivotTableModel Pivot) SetUpPivot()
    {
        var workbook = new Workbook("MovePivotNoOpTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B3"),
            TargetRange = Range(sheet, "D3", "F8"),
            ReportLayout = PivotReportLayout.Tabular,
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var ctx = new TestCommandContext(workbook);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);

        return (workbook, sheet, ctx, pivot);
    }

    [Fact]
    public void MovePivotTableCommand_DroppingThePivotWhereItStartedIsANoOp()
    {
        var (_, sheet, ctx, pivot) = SetUpPivot();
        var renderedBefore = pivot.LastRenderedRange;

        var outcome = new MovePivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "D3")).Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue(
            "the destination equals the pivot's current start, so the whole move block is skipped");
        pivot.TargetRange.Start.Should().Be(Addr(sheet, "D3"));
        pivot.LastRenderedRange.Should().Be(renderedBefore);
    }

    [Fact]
    public void MovePivotTableCommand_MovingThePivotElsewhereIsNotANoOp()
    {
        var (_, sheet, ctx, pivot) = SetUpPivot();

        new MovePivotTableCommand(sheet.Id, "PivotTable1", Addr(sheet, "H3")).Apply(ctx)
            .IsNoOp.Should().BeFalse("the pivot's cells move to a different block");
        pivot.TargetRange.Start.Should().Be(Addr(sheet, "H3"));
    }

    private static (Sheet Sheet, TestCommandContext Ctx) SetUpTextTarget()
    {
        var workbook = new Workbook("PasteNoOpTest");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        return (sheet, ctx);
    }

    [Fact]
    public void ExternalTextPasteSpecialCommand_PastingOverIdenticalTextIsANoOp()
    {
        var (sheet, ctx) = SetUpTextTarget();
        var target = Addr(sheet, "A1");

        Paste(sheet, target, "5", "6").Apply(ctx)
            .IsNoOp.Should().BeFalse("the cells were empty");
        sheet.GetValue(1, 1).Should().Be(new NumberValue(5));

        Paste(sheet, target, "5", "6").Apply(ctx)
            .IsNoOp.Should().BeTrue(
                "the same clipboard text pasted over itself writes what is already there");
    }

    [Fact]
    public void ExternalTextPasteSpecialCommand_PastingDifferentTextIsNotANoOp()
    {
        var (sheet, ctx) = SetUpTextTarget();
        var target = Addr(sheet, "A1");

        Paste(sheet, target, "5", "6").Apply(ctx);

        Paste(sheet, target, "5", "7").Apply(ctx)
            .IsNoOp.Should().BeFalse("the second column's value differs");
        sheet.GetValue(1, 2).Should().Be(new NumberValue(7));
    }

    /// <summary>
    /// Two adjacent cells pasted from external text, the shape a tab-separated paste produces.
    /// NUMERIC text deliberately: PasteArithmetic.ApplyOperation returns null for a non-numeric
    /// operand -- Excel leaves the destination untouched rather than writing text through an
    /// Operation -- so a text paste through this command writes nothing at all and is a no-op for a
    /// reason that has nothing to do with what is already in the cells.
    /// </summary>
    private static ExternalTextPasteSpecialCommand Paste(Sheet sheet, CellAddress target, string first, string second) =>
        new(
            sheet.Id,
            [(target, first), (new CellAddress(sheet.Id, target.Row, target.Col + 1), second)],
            PasteSpecialOperation.None);


    /// <summary>
    /// The Data Table body refresh runs from the cell-edit choke points whenever a cell the table
    /// depends on changes, so it fires routinely on edits that leave every computed result the same.
    /// r240 wrote this guard and reverted it for want of exactly these tests.
    /// </summary>
    [Fact]
    public void DataTableBodyRefreshCommand_RefreshingUnchangedInputsIsANoOp()
    {
        var (sheet, ctx, registration) = SetUpOneVariableDataTable();
        var beforeFirstResult = sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText;

        new DataTableBodyRefreshCommand(registration).Apply(ctx)
            .IsNoOp.Should().BeTrue("nothing the table reads has changed since it was built");
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText.Should().Be(beforeFirstResult);
    }

    [Fact]
    public void DataTableBodyRefreshCommand_RefreshingAfterTheMasterFormulaChangesIsNotANoOp()
    {
        var (sheet, ctx, registration) = SetUpOneVariableDataTable();

        // The master formula now triples rather than doubles, so every substituted result differs.
        sheet.SetCell(registration.FormulaCell, Cell.FromFormula("A10*3"));

        new DataTableBodyRefreshCommand(registration).Apply(ctx)
            .IsNoOp.Should().BeFalse("every body cell recomputes to a different value");
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText.Should().Contain("*3");
    }

    private static (Sheet Sheet, TestCommandContext Ctx, DataTableRegistration Registration) SetUpOneVariableDataTable()
    {
        var wb = new Workbook("DataTableNoOpTest");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var formulaCell = new CellAddress(sheet.Id, 1, 2);
        var inputCell = new CellAddress(sheet.Id, 10, 1);
        sheet.SetCell(formulaCell, Cell.FromFormula("A10*2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));

        var tableRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        new OneVariableDataTableCommand(tableRange, formulaCell, inputCell, DataTableInputOrientation.Column)
            .Apply(ctx).Success.Should().BeTrue("the table must build, or the refresh tests below are vacuous");
        sheet.GetCell(new CellAddress(sheet.Id, 2, 2))!.FormulaText.Should().Contain("*2",
            "the body must be written, or the refresh tests below would compare an empty snapshot");

        return (sheet, ctx, new DataTableRegistration(
            tableRange, formulaCell, inputCell, SecondInputCell: null, IsRowOriented: false));
    }
}
