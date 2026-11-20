using FreeX.Core.Formula;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public class StructuredSelectorRenameTests
{
    [Theory]
    [InlineData("Sales", "Revenue")]
    [InlineData("@Sales", "@Revenue")]
    [InlineData("[#Data],[Sales]", "[#Data],[Revenue]")]
    [InlineData("[Sales]:[Cost]", "[Revenue]:[Expense]")]
    [InlineData("@[Sales]:[Cost]", "@[Revenue]:[Expense]")]
    [InlineData("[#This Row],[Sales]", "[#This Row],[Revenue]")]
    public void RewritesOnlyColumnTokens(string selector, string expected)
    {
        var map = new Dictionary<string, string> { ["Sales"] = "Revenue", ["Cost"] = "Expense", ["#Data"] = "NotASection" };
        StructuredSelectorRename.TryRewrite(selector, map, out var actual).Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("It''s", "It's", "A[B", "A'[B")]
    [InlineData("A]]B", "A]B", "C#D", "C'#D")]
    public void PreservesAndProducesEscapedColumnNames(string selector, string oldName, string newName, string expected)
    {
        StructuredSelectorRename.TryRewrite(selector, new Dictionary<string, string> { [oldName] = newName }, out var actual)
            .Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Fact]
    public void SwapsAreSimultaneousAndUnchangedTokensAreByteStable()
    {
        var map = new Dictionary<string, string> { ["Sales"] = "Cost", ["Cost"] = "Sales" };
        StructuredSelectorRename.TryRewrite("[#Headers],[Sales]:[Cost]", map, out var actual).Should().BeTrue();
        actual.Should().Be("[#Headers],[Cost]:[Sales]");
    }

    [Theory]
    [InlineData("[#Unknown],[Sales]")]
    [InlineData("Sales:Cost")]
    [InlineData("Sales,Cost")]
    [InlineData("'#Data")]
    [InlineData("@[A'[B]")]
    [InlineData("[Sales],[Cost")]
    [InlineData("[Sales]:[#Data]")]
    [InlineData("[Sales],[Cost]")]
    [InlineData("[#Data]:[#Totals]")]
    [InlineData("[Sales]garbage")]
    [InlineData("[Sales]:")]
    [InlineData("[Sales],[[Cost]]")]
    [InlineData("\"Sales\"")]
    public void UnsupportedInputRemainsUnchanged(string selector)
    {
        StructuredSelectorRename.TryRewrite(selector, new Dictionary<string, string> { ["Sales"] = "Revenue" }, out var actual)
            .Should().BeFalse();
        actual.Should().Be(selector);
    }

    [Fact]
    public void UnsafeBareReplacementIsReportedUnsupported()
    {
        StructuredSelectorRename.TryRewrite("Sales", new Dictionary<string, string> { ["Sales"] = "Gross,Net" }, out var actual)
            .Should().BeFalse();
        actual.Should().Be("Sales");
    }
    [Fact]
    public void ConflictingCaseInsensitiveKeysAreUnsupported()
    {
        var map = new Dictionary<string, string> { ["Sales"] = "One", ["sales"] = "Two" };
        StructuredSelectorRename.TryRewrite("Sales", map, out var result).Should().BeFalse();
        result.Should().Be("Sales");
    }

    [Theory]
    [InlineData("Sales", "A[B")]
    [InlineData("Sales", "C]D")]
    [InlineData("[Sales]:[Cost]", "Revenue")]
    public void GeneratedSelectorCanBeParsed(string selector, string replacement)
    {
        StructuredSelectorRename.TryRewrite(selector, new Dictionary<string, string> { ["Sales"] = replacement }, out var result)
            .Should().BeTrue();
        var action = () => new Parser(new Lexer("=SalesTable[" + result + "]").Tokenize()).Parse();
        action.Should().NotThrow();
    }
    [Fact]
    public void SectionKeywordTargetIsRejectedBecauseEvaluationLosesItsEscape()
    {
        var workbook = new FreeX.Core.Model.Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new FreeX.Core.Model.CellAddress(sheet.Id, 1, 1), new FreeX.Core.Model.TextValue("#Data"));
        sheet.SetCell(new FreeX.Core.Model.CellAddress(sheet.Id, 1, 2), new FreeX.Core.Model.TextValue("Other"));
        sheet.SetCell(new FreeX.Core.Model.CellAddress(sheet.Id, 2, 1), new FreeX.Core.Model.NumberValue(10));
        sheet.SetCell(new FreeX.Core.Model.CellAddress(sheet.Id, 2, 2), new FreeX.Core.Model.NumberValue(100));
        var table = new FreeX.Core.Model.StructuredTableModel { Id = 1, Name = "SalesTable", DisplayName = "SalesTable",
            Range = new FreeX.Core.Model.GridRange(new(sheet.Id, 1, 1), new(sheet.Id, 2, 2)) };
        table.Columns.Add(new FreeX.Core.Model.StructuredTableColumnModel(1, "#Data"));
        table.Columns.Add(new FreeX.Core.Model.StructuredTableColumnModel(2, "Other"));
        sheet.StructuredTables.Add(table);
        // The existing lexer loses the escape: this selects the whole data body.
        new FormulaEvaluator().Evaluate("SUM(SalesTable['#Data])", sheet, workbook)
            .Should().Be(new FreeX.Core.Model.NumberValue(110));
        StructuredSelectorRename.TryRewrite("Sales", new Dictionary<string, string> { ["Sales"] = "#Data" }, out var result)
            .Should().BeFalse();
        result.Should().Be("Sales");
    }

    [Theory]
    [InlineData("[#Data],[Sales]", "A[B")]
    [InlineData("[Sales]:[Cost]", "A]B")]
    [InlineData("[#Data],[Sales]", "One,Two")]
    public void BracketedTargetsThatLoseIdentityAreRejected(string selector, string replacement)
    {
        StructuredSelectorRename.TryRewrite(selector, new Dictionary<string, string> { ["Sales"] = replacement }, out var result)
            .Should().BeFalse();
        result.Should().Be(selector);
    }
    [Theory]
    [InlineData("Sales", " Revenue")]
    [InlineData("Sales", "Revenue ")]
    [InlineData("[#Data],[Sales]", " Revenue")]
    [InlineData("[#Data],[Sales]", "Revenue ")]
    public void WhitespaceBoundariesOnDestinationAreUnsupported(string selector, string name)
    {
        StructuredSelectorRename.TryRewrite(selector, new Dictionary<string, string> { ["Sales"] = name }, out var result)
            .Should().BeFalse();
        result.Should().Be(selector);
    }
}
