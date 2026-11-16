using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class TableHeaderRenameCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedTableHeader_EditOrClearRejectsAndRestoresCell(bool clear)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var formula = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.SetCell(b, new TextValue("Cost"));
        sheet.SetCell(formula, Cell.FromFormula("SUM(SalesTable[Sales])"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)), Columns = { new StructuredTableColumnModel(1, "Sales") } });
        IWorkbookCommand command = clear
            ? new ClearContentsCommand(sheet.Id, new GridRange(b, b))
            : EditCellsCommand.ForValue(sheet.Id, b, new TextValue("Revenue"));
        var outcome = command.Apply(new TestCommandContext(workbook));
        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("column metadata");
        sheet.GetValue(1, 2).Should().Be(new TextValue("Cost"));
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(SalesTable[Sales])");
        sheet.StructuredTables[0].Columns.Should().ContainSingle().Which.Name.Should().Be("Sales");
    }

    [Fact]
    public void DataTableOutputCannotOverwriteStructuredHeader()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var master = new CellAddress(sheet.Id, 1, 1);
        var header = new CellAddress(sheet.Id, 2, 2);
        var input = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(master, Cell.FromFormula("C1*2"));
        sheet.SetCell(header, new TextValue("Sales"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(header, new CellAddress(sheet.Id, 3, 2)), Columns = { new StructuredTableColumnModel(1, "Sales") } });
        var command = new OneVariableDataTableCommand(new GridRange(master, header), master, input);
        var outcome = command.Apply(new TestCommandContext(workbook));
        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("structured-table headers");
        sheet.GetValue(2, 2).Should().Be(new TextValue("Sales"));
    }

    [Fact]
    public void RemoveDuplicatesThatCompactsAcrossHeader_ReconcilesAndUndoes()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var header = new CellAddress(sheet.Id, 2, 1);
        var formula = new CellAddress(sheet.Id, 6, 1);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sales"));
        sheet.SetCell(header, new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Revenue"));
        sheet.SetCell(formula, Cell.FromFormula("SUM(SalesTable[Sales])"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(header, new CellAddress(sheet.Id, 3, 1)), Columns = { new StructuredTableColumnModel(1, "Sales") } });
        var command = new RemoveDuplicateRowsCommand(sheet.Id, new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)));
        var ctx = new TestCommandContext(workbook);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Revenue"));
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(SalesTable[Revenue])");
        command.Revert(ctx);
        sheet.GetValue(2, 1).Should().Be(new TextValue("Sales"));
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(SalesTable[Sales])");
    }

    [Fact]
    public void UnsupportedLaterReference_RollsBackEarlierFormulaRewrite()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var first = new CellAddress(sheet.Id, 4, 1);
        var second = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.SetCell(b, new TextValue("A[B"));
        sheet.SetCell(first, Cell.FromFormula("SUM(SalesTable[Sales])"));
        sheet.SetCell(second, Cell.FromFormula("SUM(SalesTable[A'[B])"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)), Columns = { new StructuredTableColumnModel(1, "Sales"), new StructuredTableColumnModel(2, "A[B") } });
        var command = new EditCellsCommand(sheet.Id, [(a, Cell.FromValue(new TextValue("Revenue"))), (b, Cell.FromValue(new TextValue("#Data")))]);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeFalse();
        sheet.GetCell(first)!.FormulaText.Should().Be("SUM(SalesTable[Sales])");
        sheet.GetValue(1, 1).Should().Be(new TextValue("Sales"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("A[B"));
    }

    [Fact]
    public void CompositeCutWithinTable_ReconcilesSourceAndDestinationTogether()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var formula = new CellAddress(sheet.Id, 5, 1);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.SetCell(b, new TextValue("Cost"));
        sheet.SetCell(formula, Cell.FromFormula("SUM(SalesTable[Sales])+SUM(SalesTable[Cost])"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)), Columns = { new StructuredTableColumnModel(1, "Sales"), new StructuredTableColumnModel(2, "Cost") } });
        var source = new GridRange(a, a);
        var destination = new GridRange(b, b);
        var composite = new CompositeWorkbookCommand("Cut", [new PasteCellsCommand(sheet.Id, [(b, Cell.FromValue(new TextValue("Sales")))]), new ClearContentsCommand(sheet.Id, source, isCutSource: true)]);
        var command = new TableHeaderCutCommand(composite, [source, destination]);
        var ctx = new TestCommandContext(workbook);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(1, 1).Should().Be(new TextValue("Column1"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("Sales"));
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(SalesTable[Column1])+SUM(SalesTable[Sales])");
        command.Revert(ctx);
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(SalesTable[Sales])+SUM(SalesTable[Cost])");
        sheet.GetValue(1, 1).Should().Be(new TextValue("Sales"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("Cost"));
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(formula)!.FormulaText.Should().Be("SUM(SalesTable[Column1])+SUM(SalesTable[Sales])");
    }

    [Fact]
    public void CutBetweenTableHeaders_RewritesBothTablesAndUndoesTogether()
    {
        var workbook = new Workbook("test");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        var a = new CellAddress(first.Id, 1, 1);
        var b = new CellAddress(second.Id, 1, 1);
        var formula = new CellAddress(first.Id, 5, 1);
        first.SetCell(a, new TextValue("Sales"));
        second.SetCell(b, new TextValue("Cost"));
        first.SetCell(formula, Cell.FromFormula("SUM(FirstTable[Sales])+SUM(SecondTable[Cost])"));
        first.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "FirstTable", DisplayName = "FirstTable", Range = new GridRange(a, new CellAddress(first.Id, 2, 1)), Columns = { new StructuredTableColumnModel(1, "Sales") } });
        second.StructuredTables.Add(new StructuredTableModel { Id = 2, Name = "SecondTable", DisplayName = "SecondTable", Range = new GridRange(b, new CellAddress(second.Id, 2, 1)), Columns = { new StructuredTableColumnModel(1, "Cost") } });
        var command = new MoveRangeCommand(first.Id, new GridRange(a, a), b);
        var ctx = new TestCommandContext(workbook);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        first.GetValue(1, 1).Should().Be(new TextValue("Column1"));
        second.GetValue(1, 1).Should().Be(new TextValue("Sales"));
        first.GetCell(formula)!.FormulaText.Should().Be("SUM(FirstTable[Column1])+SUM(SecondTable[Sales])");
        command.Revert(ctx);
        first.GetCell(formula)!.FormulaText.Should().Be("SUM(FirstTable[Sales])+SUM(SecondTable[Cost])");
        first.GetValue(1, 1).Should().Be(new TextValue("Sales"));
        second.GetValue(1, 1).Should().Be(new TextValue("Cost"));
    }

    [Fact]
    public void CutSingleHeaderToAnotherSheet_GeneratesReplacementAndRewritesReferences()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(other.Id, 1, 1);
        var formulaAddress = new CellAddress(sheet.Id, 4, 1);
        sheet.SetCell(a, new TextValue("Cost"));
        sheet.SetCell(formulaAddress, Cell.FromFormula("SUM(SalesTable[Cost])"));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1, Name = "SalesTable", DisplayName = "SalesTable",
            Range = new GridRange(a, new CellAddress(sheet.Id, 2, 1)),
            Columns = { new StructuredTableColumnModel(1, "Cost") }
        });
        var command = new MoveRangeCommand(sheet.Id, new GridRange(a, a), destination);
        var ctx = new TestCommandContext(workbook);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(1, 1).Should().Be(new TextValue("Column1"));
        other.GetValue(1, 1).Should().Be(new TextValue("Cost"));
        sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("SUM(SalesTable[Column1])");
        command.Revert(ctx);
        sheet.GetValue(1, 1).Should().Be(new TextValue("Cost"));
        sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("SUM(SalesTable[Cost])");
    }

    [Fact]
    public void DuplicateName_NormalizesOtherHeaderAndRestoresItsCompanionsOnUndo()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.SetCell(b, new TextValue("Cost"));
        sheet.Hyperlinks[b] = "https://example.test/";
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1, Name = "SalesTable", DisplayName = "SalesTable",
            Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)),
            Columns = { new StructuredTableColumnModel(1, "Sales"), new StructuredTableColumnModel(2, "Cost") }
        });
        var command = EditCellsCommand.ForValue(sheet.Id, a, new TextValue("Cost"));
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(1, 2).Should().Be(new TextValue("Cost2"));
        sheet.Hyperlinks.Should().NotContainKey(b);
        command.Revert(ctx);
        sheet.GetValue(1, 2).Should().Be(new TextValue("Cost"));
        sheet.Hyperlinks[b].Should().Be("https://example.test/");
    }

    [Fact]
    public void AutofillHeaderAcrossExistingAndAdjacentColumn_AllowsExpansionAndUndo()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var c = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.SetCell(b, new TextValue("Cost"));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1, Name = "SalesTable", DisplayName = "SalesTable",
            Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)),
            Columns = { new StructuredTableColumnModel(1, "Sales"), new StructuredTableColumnModel(2, "Cost") }
        });
        var command = new AutofillCommand(sheet.Id, new GridRange(a, a), new GridRange(b, c));
        var ctx = new TestCommandContext(workbook);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.StructuredTables[0].Columns.Count.Should().Be(3);
        command.Revert(ctx);
        sheet.StructuredTables[0].Columns.Select(x => x.Name).Should().Equal("Sales", "Cost");
        sheet.GetValue(1, 1).Should().Be(new TextValue("Sales"));
        sheet.GetValue(1, 2).Should().Be(new TextValue("Cost"));
    }

    [Fact]
    public void Rename_UpdatesAndRestoresWorkbookFormulaOwners()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1, Name = "SalesTable", DisplayName = "SalesTable",
            Range = new GridRange(a, new CellAddress(sheet.Id, 2, 1)),
            Columns = { new StructuredTableColumnModel(1, "Sales", CalculatedColumnFormula: "SUM(SalesTable[Sales])", TotalsRowFormula: "SUM(SalesTable[Sales])") }
        });
        workbook.NamedFormulas["Global"] = "SUM(SalesTable[Sales])";
        workbook.DefineNamedFormula("Local", "SUM(SalesTable[Sales])", other.Id);
        var otherRange = new GridRange(new CellAddress(other.Id, 1, 1), new CellAddress(other.Id, 1, 1));
        var cf = new ConditionalFormat { AppliesTo = otherRange, FormulaText = "SUM(SalesTable[Sales])>0", MinThresholdType = CfThresholdType.Formula, MinThresholdValue = "SUM(SalesTable[Sales])" };
        var dv = new DataValidation { AppliesTo = otherRange, Formula1 = "SalesTable[Sales]" };
        other.ConditionalFormats.Add(cf);
        other.DataValidations.Add(dv);
        var chart = new ChartModel { Type = ChartType.Column, VerbatimSeriesFormulas = [new ChartSeriesVerbatimFormulas(0, "SalesTable[Sales]", null, null, null)] };
        other.Charts.Add(chart);
        var command = EditCellsCommand.ForValue(sheet.Id, a, new TextValue("Revenue"));
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();
        workbook.NamedFormulas["Global"].Should().Be("SUM(SalesTable[Revenue])");
        workbook.ScopedNamedFormulas[("Local", other.Id)].Should().Be("SUM(SalesTable[Revenue])");
        cf.FormulaText.Should().Be("SUM(SalesTable[Revenue])>0");
        cf.MinThresholdValue.Should().Be("SUM(SalesTable[Revenue])");
        dv.Formula1.Should().Be("SalesTable[Revenue]");
        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("SalesTable[Revenue]");
        sheet.StructuredTables[0].Columns[0].TotalsRowFormula.Should().Be("SUM(SalesTable[Revenue])");
        command.Revert(ctx);
        workbook.NamedFormulas["Global"].Should().Be("SUM(SalesTable[Sales])");
        workbook.ScopedNamedFormulas[("Local", other.Id)].Should().Be("SUM(SalesTable[Sales])");
        cf.FormulaText.Should().Be("SUM(SalesTable[Sales])>0");
        cf.MinThresholdValue.Should().Be("SUM(SalesTable[Sales])");
        dv.Formula1.Should().Be("SalesTable[Sales]");
        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("SalesTable[Sales]");
        sheet.StructuredTables[0].Columns[0].TotalsRowFormula.Should().Be("SUM(SalesTable[Sales])");
    }

    [Fact]
    public void UnsupportedEscapedReference_RollsBackHeaderAndTable()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        var formulaAddress = new CellAddress(sheet.Id, 4, 1);
        sheet.SetCell(a, new TextValue("A[B"));
        sheet.SetCell(formulaAddress, Cell.FromFormula("SUM(SalesTable[A'[B])"));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1, Name = "SalesTable", DisplayName = "SalesTable",
            Range = new GridRange(a, new CellAddress(sheet.Id, 2, 1)),
            Columns = { new StructuredTableColumnModel(1, "A[B") }
        });
        var ctx = new TestCommandContext(workbook);
        var command = EditCellsCommand.ForValue(sheet.Id, a, new TextValue("#Data"));
        command.Apply(ctx).Success.Should().BeFalse();
        sheet.GetValue(1, 1).Should().Be(new TextValue("A[B"));
        sheet.StructuredTables[0].Columns[0].Name.Should().Be("A[B");
        sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("SUM(SalesTable[A'[B])");
    }

    [Fact]
    public void ClearDefaultHeader_RestoresNormalizedName()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a, new TextValue("Column1"));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1, Name = "Table1", DisplayName = "Table1",
            Range = new GridRange(a, new CellAddress(sheet.Id, 2, 1)),
            Columns = { new StructuredTableColumnModel(1, "Column1") }
        });
        var command = new ClearContentsCommand(sheet.Id, new GridRange(a, a));
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetValue(1, 1).Should().Be(new TextValue("Column1"));
        command.Revert(ctx);
        sheet.GetValue(1, 1).Should().Be(new TextValue("Column1"));
    }

    [Fact]
    public void EditSwappingHeaders_RewritesSimultaneously_AndUndoRedoRestoresEverything()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var formulaAddress = new CellAddress(sheet.Id, 4, 1);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.SetCell(b, new TextValue("Cost"));
        sheet.SetCell(formulaAddress, Cell.FromFormula("SUM(SalesTable[Sales])-SUM(SalesTable[Cost])"));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1, Name = "SalesTable", DisplayName = "SalesTable",
            Range = new GridRange(a, new CellAddress(sheet.Id, 3, 2)),
            Columns = { new StructuredTableColumnModel(1, "Sales"), new StructuredTableColumnModel(2, "Cost") }
        });
        var ctx = new TestCommandContext(workbook);
        var command = new EditCellsCommand(sheet.Id, [(a, Cell.FromValue(new TextValue("Cost"))), (b, Cell.FromValue(new TextValue("Sales")))]);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("SUM(SalesTable[Cost])-SUM(SalesTable[Sales])");
        sheet.StructuredTables[0].Columns.Select(c => c.Name).Should().Equal("Cost", "Sales");
        command.AffectedCells.Should().Contain(formulaAddress);
        command.Revert(ctx);
        sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("SUM(SalesTable[Sales])-SUM(SalesTable[Cost])");
        sheet.StructuredTables[0].Columns.Select(c => c.Name).Should().Equal("Sales", "Cost");
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.GetCell(formulaAddress)!.FormulaText.Should().Be("SUM(SalesTable[Cost])-SUM(SalesTable[Sales])");
    }
}
