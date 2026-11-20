using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public class StructuredFormulaColumnRenameTests
{
    private static readonly IReadOnlyDictionary<string, string> Swap =
        new Dictionary<string, string> { ["Sales"] = "Cost", ["Cost"] = "Sales" };

    [Fact]
    public void PreservesFormulaTextAndRewritesOnlyTargetTable()
    {
        const string source = "= SUM( SalesTable[Sales] ,Other[Sales],\"SalesTable[Sales]\", SalesTable[[#Data],[Cost]] )";
        const string expected = "= SUM( SalesTable[Cost] ,Other[Sales],\"SalesTable[Sales]\", SalesTable[[#Data],[Sales]] )";
        StructuredFormulaColumnRename.TryRewrite(source, "salestable", null, Swap, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("SalesTable", "=SUM([Sales],[@Cost],SalesTable[Sales])", "=SUM([Cost],[@Sales],SalesTable[Cost])")]
    [InlineData("Other", "=SUM([Sales],[@Cost],SalesTable[Sales])", "=SUM([Sales],[@Cost],SalesTable[Cost])")]
    public void UnqualifiedReferencesRequireMatchingOwner(string owner, string source, string expected)
    {
        StructuredFormulaColumnRename.TryRewrite(source, "SalesTable", owner, Swap, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Fact]
    public void MissingOwnerMakesUnqualifiedReferenceUnsupportedAtomically()
    {
        const string source = "=SalesTable[Sales]+[Cost]";
        StructuredFormulaColumnRename.TryRewrite(source, "SalesTable", null, Swap, out var result).Should().BeFalse();
        result.Should().Be(source);
    }

    [Fact]
    public void UnsafeTargetMakesEntireFormulaUnsupported()
    {
        const string source = "=SalesTable[Sales]+SalesTable[Cost]";
        var map = new Dictionary<string, string> { ["Sales"] = "Revenue", ["Cost"] = "#Data" };
        StructuredFormulaColumnRename.TryRewrite(source, "SalesTable", null, map, out var result).Should().BeFalse();
        result.Should().Be(source);
    }

    [Theory]
    [InlineData("=SalesTable[Sales]+")]
    [InlineData("='[1]Sheet1'!SalesTable[Sales]")]
    [InlineData("=SalesTable[[Sales]:[#Data]]")]
    [InlineData("=SalesTable[ Sales ]")]
    [InlineData("=SalesTable[Sales:Cost]")]
    [InlineData("=SalesTable[Sales,Cost]")]
    public void MalformedOrUnsupportedFormulaIsUnchanged(string source)
    {
        StructuredFormulaColumnRename.TryRewrite(source, "SalesTable", null, Swap, out var result).Should().BeFalse();
        result.Should().Be(source);
    }

    [Fact]
    public void EscapedSelectorIsRewrittenWithoutReserializingSurroundingFormula()
    {
        const string source = "=sum(SalesTable[It''s])+1.00";
        StructuredFormulaColumnRename.TryRewrite(source, "SalesTable", null,
            new Dictionary<string, string> { ["It's"] = "Amount" }, out var result).Should().BeTrue();
        result.Should().Be("=sum(SalesTable[Amount])+1.00");
    }
    [Fact]
    public void RewrittenFormulaEvaluatesAgainstRenamedColumn()
    {
        var workbook = new FreeX.Core.Model.Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new FreeX.Core.Model.CellAddress(sheet.Id, 1, 1), new FreeX.Core.Model.TextValue("Revenue"));
        sheet.SetCell(new FreeX.Core.Model.CellAddress(sheet.Id, 1, 2), new FreeX.Core.Model.TextValue("Other"));
        sheet.SetCell(new FreeX.Core.Model.CellAddress(sheet.Id, 2, 1), new FreeX.Core.Model.NumberValue(10));
        sheet.SetCell(new FreeX.Core.Model.CellAddress(sheet.Id, 2, 2), new FreeX.Core.Model.NumberValue(100));
        var table = new FreeX.Core.Model.StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable",
            Range = new FreeX.Core.Model.GridRange(new(sheet.Id, 1, 1), new(sheet.Id, 2, 2)) };
        table.Columns.Add(new FreeX.Core.Model.StructuredTableColumnModel(1, "Revenue"));
        table.Columns.Add(new FreeX.Core.Model.StructuredTableColumnModel(2, "Other"));
        sheet.StructuredTables.Add(table);
        StructuredFormulaColumnRename.TryRewrite("=SUM(SalesTable[Sales])", "SalesTable", null,
            new Dictionary<string, string> { ["Sales"] = "Revenue" }, out var result).Should().BeTrue();
        new FormulaEvaluator().Evaluate(result, sheet, workbook).Should().Be(new FreeX.Core.Model.NumberValue(10));
    }
    [Fact]
    public void ExternalCellReferenceRemainsByteIdentical()
    {
        const string source = "='[1]External Sheet'!A1+SalesTable[Sales]";
        StructuredFormulaColumnRename.TryRewrite(source, "SalesTable", null, Swap, out var result).Should().BeTrue();
        result.Should().Be("='[1]External Sheet'!A1+SalesTable[Cost]");
    }
    [Theory]
    [InlineData("InternalName", "=InternalName[Sales]+VisibleName[Sales]+[Sales]", "=InternalName[Cost]+VisibleName[Cost]+[Cost]")]
    [InlineData("VisibleName", "=InternalName[Sales]+VisibleName[Sales]+[Sales]", "=InternalName[Cost]+VisibleName[Cost]+[Cost]")]
    [InlineData("Other", "=InternalName[Sales]+VisibleName[Sales]+[Sales]", "=InternalName[Cost]+VisibleName[Cost]+[Sales]")]
    public void NameAndDisplayNameIdentifySameTable(string owner, string source, string expected)
    {
        StructuredFormulaColumnRename.TryRewrite(source, "InternalName", "VisibleName", owner, Swap, out var result)
            .Should().BeTrue();
        result.Should().Be(expected);
    }

    [Fact]
    public void UnknownOwnershipWithDisplayNameFailsAtomically()
    {
        const string source = "=VisibleName[Sales]+[Cost]";
        StructuredFormulaColumnRename.TryRewrite(source, "InternalName", "VisibleName", null, Swap, out var result)
            .Should().BeFalse();
        result.Should().Be(source);
    }
    [Theory]
    [InlineData(" Revenue")]
    [InlineData("Revenue ")]
    public void WhitespaceDestinationLeavesEntireFormulaUnchanged(string name)
    {
        const string source = "=SalesTable[Cost]+SalesTable[Sales]";
        var map = new Dictionary<string, string> { ["Cost"] = "Expense", ["Sales"] = name };
        StructuredFormulaColumnRename.TryRewrite(source, "SalesTable", null, map, out var result).Should().BeFalse();
        result.Should().Be(source);
    }
}
