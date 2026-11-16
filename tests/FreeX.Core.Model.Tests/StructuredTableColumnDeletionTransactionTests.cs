using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class StructuredTableColumnDeletionTransactionTests
{
    [Theory]
    [InlineData("SUM(SalesTable[X])", false)]
    [InlineData("SUM(SalesTable[#All])", true)]
    [InlineData("SUM(OtherTable[X])", true)]
    public void MetadataLessDuplicateHeaders_RejectsOnlyReferencesWithAmbiguousOwnership(string formulaText, bool succeeds)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var formula = new CellAddress(other.Id, 1, 1);
        sheet.SetCell(a, new TextValue("X"));
        sheet.SetCell(b, new TextValue("X"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)) });
        other.SetCell(formula, Cell.FromFormula(formulaText));
        var command = new DeleteColumnsCommand(sheet.Id, 2);
        var ctx = new TestCommandContext(workbook);
        var result = command.Apply(ctx);
        result.Success.Should().Be(succeeds, result.ErrorMessage);
        other.GetCell(formula)!.FormulaText.Should().Be(formulaText);
        if (succeeds) command.Revert(ctx);
        sheet.GetValue(b).Should().Be(new TextValue("X"));
        sheet.StructuredTables[0].Columns.Should().BeEmpty();
    }

    [Fact]
    public void DeleteColumnFromPartiallyDescribedTable_RejectsWithoutMutation()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a, new TextValue("Alpha"));
        sheet.SetCell(b, new TextValue("Bravo"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)), Columns = { new StructuredTableColumnModel(1, "Alpha") } });
        var result = new DeleteColumnsCommand(sheet.Id, 2).Apply(new TestCommandContext(workbook));
        result.Success.Should().BeFalse();
        sheet.GetValue(a).Should().Be(new TextValue("Alpha"));
        sheet.GetValue(b).Should().Be(new TextValue("Bravo"));
        sheet.StructuredTables[0].Columns.Should().ContainSingle().Which.Name.Should().Be("Alpha");
    }

    [Fact]
    public void DeleteColumnFromMetadataLessTable_UsesLiveHeaderAndRestoresOnUndo()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var c = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a, new TextValue("Alpha"));
        sheet.SetCell(b, new TextValue("Bravo"));
        sheet.SetCell(c, new TextValue("Charlie"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 3)) });
        var deleted = new CellAddress(other.Id, 1, 1);
        var retained = new CellAddress(other.Id, 2, 1);
        var whole = new CellAddress(other.Id, 3, 1);
        other.SetCell(deleted, Cell.FromFormula("SUM(SalesTable[Bravo])"));
        other.SetCell(retained, Cell.FromFormula("SUM(SalesTable[Charlie])"));
        other.SetCell(whole, Cell.FromFormula("SUM(SalesTable[#All])"));
        var command = new DeleteColumnsCommand(sheet.Id, 2);
        var ctx = new TestCommandContext(workbook);
        var result = command.Apply(ctx);
        result.Success.Should().BeTrue(result.ErrorMessage);
        other.GetCell(deleted)!.FormulaText.Should().Be("SUM(#REF!)");
        other.GetCell(retained)!.FormulaText.Should().Be("SUM(SalesTable[Charlie])");
        other.GetCell(whole)!.FormulaText.Should().Be("SUM(SalesTable[#All])");
        command.Revert(ctx);
        sheet.StructuredTables[0].Columns.Should().BeEmpty();
        other.GetCell(deleted)!.FormulaText.Should().Be("SUM(SalesTable[Bravo])");
    }

    [Fact]
    public void DeleteEscapedColumn_WithUnknownImplicitOwner_RejectsWithoutMutation()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a, new TextValue("A]B"));
        sheet.SetCell(b, new TextValue("Other"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)), Columns = { new StructuredTableColumnModel(1, "A]B"), new StructuredTableColumnModel(2, "Other") } });
        workbook.NamedFormulas["Ambiguous"] = "SUM([A]]B])";
        var result = new DeleteColumnsCommand(sheet.Id, 1).Apply(new TestCommandContext(workbook));
        result.Success.Should().BeFalse();
        sheet.GetValue(a).Should().Be(new TextValue("A]B"));
        workbook.NamedFormulas["Ambiguous"].Should().Be("SUM([A]]B])");
    }

    [Fact]
    public void DeleteEscapedClosingBracketColumn_InvalidatesImplicitReferenceAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a, new TextValue("A]B"));
        sheet.SetCell(b, new TextValue("Other"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromFormula("[@A]]B]+1"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)), Columns = { new StructuredTableColumnModel(1, "A]B"), new StructuredTableColumnModel(2, "Other") } });
        var command = new DeleteColumnsCommand(sheet.Id, 1);
        var ctx = new TestCommandContext(workbook);
        var result = command.Apply(ctx);
        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetCell(2, 1)!.FormulaText.Should().Be("#REF!+1");
        command.Revert(ctx);
        sheet.GetCell(2, 2)!.FormulaText.Should().Be("[@A]]B]+1");
    }

    [Fact]
    public void DeleteRenamedColumn_DoesNotInvalidateNameOwnedByCanonicalOtherColumn()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a, new TextValue("New"));
        sheet.SetCell(b, new TextValue("Old"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "Alias", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 2)), Columns = { new StructuredTableColumnModel(1, "Old"), new StructuredTableColumnModel(2, "Old") } });
        var formula = new CellAddress(other.Id, 1, 1);
        var internalNameFormula = new CellAddress(other.Id, 2, 1);
        other.SetCell(formula, Cell.FromFormula("SUM(Alias[Old])"));
        other.SetCell(internalNameFormula, Cell.FromFormula("SUM(SalesTable[Old])"));
        var command = new DeleteColumnsCommand(sheet.Id, 1);
        command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        other.GetCell(formula)!.FormulaText.Should().Be("SUM(Alias[Old])");
        other.GetCell(internalNameFormula)!.FormulaText.Should().Be("SUM(SalesTable[Old])");
    }

    [Fact]
    public void DeleteRenamedColumn_InvalidatesLiveAndStaleReferencesButPreservesCanonicalReusedName()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var c = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a, new TextValue("New"));
        sheet.SetCell(b, new TextValue("Taken"));
        sheet.SetCell(c, new TextValue("Remain"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "Alias", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 3)), Columns = { new StructuredTableColumnModel(1, "Old"), new StructuredTableColumnModel(2, "Taken"), new StructuredTableColumnModel(3, "Remain") } });
        var live = new CellAddress(other.Id, 1, 1);
        var stale = new CellAddress(other.Id, 2, 1);
        var retained = new CellAddress(other.Id, 3, 1);
        other.SetCell(live, Cell.FromFormula("SUM(Alias[New])"));
        other.SetCell(stale, Cell.FromFormula("SUM(Alias[Old])"));
        other.SetCell(retained, Cell.FromFormula("SUM(Alias[Taken])"));
        var command = new DeleteColumnsCommand(sheet.Id, 1);
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();
        other.GetCell(live)!.FormulaText.Should().Be("SUM(#REF!)");
        other.GetCell(stale)!.FormulaText.Should().Be("SUM(#REF!)");
        other.GetCell(retained)!.FormulaText.Should().Be("SUM(Alias[Taken])");
        command.Revert(ctx);
        other.GetCell(stale)!.FormulaText.Should().Be("SUM(Alias[Old])");
    }

    [Fact]
    public void WholeTableDeletion_InvalidatesAliasAndUndoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        var formula = new CellAddress(other.Id, 1, 1);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "Alias", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 1)), Columns = { new StructuredTableColumnModel(1, "Sales") } });
        other.SetCell(formula, Cell.FromFormula("SUM(Alias[#All])"));
        var command = new DeleteColumnsCommand(sheet.Id, 1);
        var ctx = new TestCommandContext(workbook);
        command.Apply(ctx).Success.Should().BeTrue();
        sheet.StructuredTables.Should().BeEmpty();
        other.GetCell(formula)!.FormulaText.Should().Be("SUM(#REF!)");
        command.Revert(ctx);
        other.GetCell(formula)!.FormulaText.Should().Be("SUM(Alias[#All])");
    }

    [Fact]
    public void UnsupportedAffectedSelector_RollsBackEarlierChangesBeforeDeletion()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        var a = new CellAddress(sheet.Id, 1, 1);
        var b = new CellAddress(sheet.Id, 1, 2);
        var c = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a, new TextValue("Sales"));
        sheet.SetCell(b, new TextValue("A[B"));
        sheet.SetCell(c, new TextValue("Profit"));
        sheet.StructuredTables.Add(new StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable", Range = new GridRange(a, new CellAddress(sheet.Id, 2, 3)), Columns = { new StructuredTableColumnModel(1, "Sales"), new StructuredTableColumnModel(2, "A[B"), new StructuredTableColumnModel(3, "Profit") } });
        var first = new CellAddress(other.Id, 1, 1);
        var second = new CellAddress(other.Id, 2, 1);
        other.SetCell(first, Cell.FromFormula("SUM(SalesTable[Sales])"));
        other.SetCell(second, Cell.FromFormula("SUM(SalesTable[[#Data],[A'[B]])"));
        var result = new DeleteColumnsCommand(sheet.Id, 1, 2).Apply(new TestCommandContext(workbook));
        result.Success.Should().BeFalse();
        other.GetCell(first)!.FormulaText.Should().Be("SUM(SalesTable[Sales])");
        other.GetCell(second)!.FormulaText.Should().Be("SUM(SalesTable[[#Data],[A'[B]])");
        sheet.GetValue(a).Should().Be(new TextValue("Sales"));
        sheet.GetValue(b).Should().Be(new TextValue("A[B"));
        sheet.StructuredTables[0].Columns.Count.Should().Be(3);
    }

    [Fact]
    public void DeleteColumn_InvalidatesReferencesAndMetadataAcrossWorkbook_UndoRedoRestores()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Data");
        var other = workbook.AddSheet("Other");
        CellAddress At(Sheet s, uint row, uint col) => new(s.Id, row, col);
        sheet.SetCell(At(sheet, 1, 1), new TextValue("Sales"));
        sheet.SetCell(At(sheet, 1, 2), new TextValue("Cost"));
        sheet.SetCell(At(sheet, 1, 3), new TextValue("Profit"));
        sheet.SetCell(At(sheet, 2, 3), Cell.FromFormula("[@Cost]+1"));
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1, Name = "SalesTable", DisplayName = "Alias",
            Range = new GridRange(At(sheet, 1, 1), At(sheet, 3, 3)),
            Columns = { new StructuredTableColumnModel(1, "Sales"), new StructuredTableColumnModel(2, "Cost"), new StructuredTableColumnModel(3, "Profit", CalculatedColumnFormula: "[@Cost]+1", TotalsRowFormula: "SUM(Alias[Cost])") }
        });
        var f1 = At(other, 1, 1);
        var f2 = At(other, 2, 1);
        var f3 = At(other, 3, 1);
        sheet.SetCell(At(sheet, 3, 1), new NumberValue(1));
        other.SetCell(f1, Cell.FromFormula("SUM(Alias[[#Data],[Cost]])"));
        other.SetCell(f2, Cell.FromFormula("SUM(SalesTable[[Sales]:[Cost]])"));
        other.SetCell(f3, Cell.FromFormula("SUM(SalesTable[[Sales]:[Profit]])"));
        workbook.NamedFormulas["TotalCost"] = "SUM(Alias[Cost])";
        workbook.DefineNamedFormula("ScopedCost", "SUM(SalesTable[Cost])", other.Id);
        var otherRange = new GridRange(At(other, 4, 1), At(other, 4, 1));
        var cf = new ConditionalFormat { AppliesTo = otherRange, FormulaText = "SUM(Alias[Cost])>0", MinThresholdType = CfThresholdType.Formula, MinThresholdValue = "SUM(SalesTable[Cost])" };
        var dv = new DataValidation { AppliesTo = otherRange, Formula1 = "SalesTable[Cost]" };
        other.ConditionalFormats.Add(cf);
        other.DataValidations.Add(dv);
        var sourceRuleRange = new GridRange(At(sheet, 4, 4), At(sheet, 4, 4));
        sheet.ConditionalFormats.Add(new ConditionalFormat { AppliesTo = sourceRuleRange, FormulaText = "SUM(SalesTable[Cost])>0" });
        sheet.DataValidations.Add(new DataValidation { AppliesTo = sourceRuleRange, Formula1 = "SalesTable[Cost]" });
        var chart = new ChartModel { Type = ChartType.Column, VerbatimSeriesFormulas = [new ChartSeriesVerbatimFormulas(0, "SalesTable[Cost]", null, null, null)] };
        other.Charts.Add(chart);
        var command = new DeleteColumnsCommand(sheet.Id, 2);
        var ctx = new TestCommandContext(workbook);

        var result = command.Apply(ctx);
        result.Success.Should().BeTrue(result.ErrorMessage);
        other.GetCell(f1)!.FormulaText.Should().Be("SUM(#REF!)");
        other.GetCell(f2)!.FormulaText.Should().Be("SUM(#REF!)");
        other.GetCell(f3)!.FormulaText.Should().Be("SUM(SalesTable[[Sales]:[Profit]])");
        sheet.GetCell(2, 2)!.FormulaText.Should().Be("#REF!+1");
        var column = sheet.StructuredTables[0].Columns[1];
        column.CalculatedColumnFormula.Should().Be("#REF!+1");
        column.TotalsRowFormula.Should().Be("SUM(#REF!)");
        workbook.NamedFormulas["TotalCost"].Should().Be("SUM(#REF!)");
        workbook.ScopedNamedFormulas[("ScopedCost", other.Id)].Should().Be("SUM(#REF!)");
        cf.FormulaText.Should().Be("SUM(#REF!)>0");
        cf.MinThresholdValue.Should().Be("SUM(#REF!)");
        dv.Formula1.Should().Be("#REF!");
        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("#REF!");
        sheet.ConditionalFormats.Single().FormulaText.Should().Be("SUM(#REF!)>0");
        sheet.DataValidations.Single().Formula1.Should().Be("#REF!");
        command.AffectedCells.Should().Contain(f1);

        command.Revert(ctx);
        command.AffectedCells.Should().Contain(f1);
        other.GetCell(f1)!.FormulaText.Should().Be("SUM(Alias[[#Data],[Cost]])");
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("[@Cost]+1");
        sheet.StructuredTables[0].Columns[2].TotalsRowFormula.Should().Be("SUM(Alias[Cost])");
        workbook.NamedFormulas["TotalCost"].Should().Be("SUM(Alias[Cost])");
        workbook.ScopedNamedFormulas[("ScopedCost", other.Id)].Should().Be("SUM(SalesTable[Cost])");
        cf.FormulaText.Should().Be("SUM(Alias[Cost])>0");
        cf.MinThresholdValue.Should().Be("SUM(SalesTable[Cost])");
        dv.Formula1.Should().Be("SalesTable[Cost]");
        chart.VerbatimSeriesFormulas![0].ValFormula.Should().Be("SalesTable[Cost]");
        sheet.ConditionalFormats.Single().FormulaText.Should().Be("SUM(SalesTable[Cost])>0");
        sheet.DataValidations.Single().Formula1.Should().Be("SalesTable[Cost]");
        command.Apply(ctx).Success.Should().BeTrue();
        other.GetCell(f1)!.FormulaText.Should().Be("SUM(#REF!)");
        sheet.ConditionalFormats.Single().FormulaText.Should().Be("SUM(#REF!)>0");
        sheet.DataValidations.Single().Formula1.Should().Be("#REF!");
    }
}
