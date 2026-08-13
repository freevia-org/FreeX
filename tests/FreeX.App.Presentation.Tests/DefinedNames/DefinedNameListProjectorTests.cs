using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.DefinedNames;

public sealed class DefinedNameListProjectorTests
{
    private static DefinedNameRow Row(
        string name,
        DefinedNameScope? scope = null,
        string refersTo = "=Sheet1!$A$1",
        string value = "",
        string comment = "") =>
        DefinedNameListProjector.CreateRow(name, scope ?? DefinedNameScope.Workbook, refersTo, value, comment);

    private static DefinedNameScope SheetScope(string label) => DefinedNameScope.ForSheet(SheetId.New(), label);

    [Fact]
    public void CreateRow_DerivesRangeKindForPlainReference()
    {
        Row("Sales", refersTo: "=Sheet1!$A$1:$A$10").Kind.Should().Be(DefinedNameKind.Range);
        Row("Cell", refersTo: "Sheet1!B2").Kind.Should().Be(DefinedNameKind.Range);
        Row("Bare", refersTo: "$A$1").Kind.Should().Be(DefinedNameKind.Range);
    }

    [Fact]
    public void CreateRow_DerivesFormulaKindForExpressions()
    {
        Row("Total", refersTo: "=SUM(Sheet1!A1:A10)").Kind.Should().Be(DefinedNameKind.Formula);
        Row("Tax", refersTo: "=0.2").Kind.Should().Be(DefinedNameKind.Formula);
        Row("Calc", refersTo: "=A1*2").Kind.Should().Be(DefinedNameKind.Formula);
    }

    [Fact]
    public void CreateRow_DerivesErrorKindFromRefersToOrValue()
    {
        Row("Broken", refersTo: "=#REF!").Kind.Should().Be(DefinedNameKind.Error);
        Row("BadValue", refersTo: "=Sheet1!$A$1", value: "#NAME?").Kind.Should().Be(DefinedNameKind.Error);
    }

    [Fact]
    public void Filter_Workbook_KeepsOnlyWorkbookScope()
    {
        var rows = new[]
        {
            Row("A"),
            Row("B", scope: SheetScope("Sheet1")),
            Row("C")
        };

        DefinedNameListProjector.Filter(rows, DefinedNameFilter.Workbook)
            .Select(r => r.Name).Should().Equal("A", "C");
    }

    [Fact]
    public void Filter_Worksheet_KeepsOnlySheetScope()
    {
        var rows = new[]
        {
            Row("A"),
            Row("B", scope: SheetScope("Sheet1")),
            Row("C", scope: SheetScope("Sheet2"))
        };

        DefinedNameListProjector.Filter(rows, DefinedNameFilter.Worksheet)
            .Select(r => r.Name).Should().Equal("B", "C");
    }

    [Fact]
    public void Filter_Errors_AndNoErrors_PartitionByErrorKind()
    {
        var rows = new[]
        {
            Row("Good", refersTo: "=Sheet1!$A$1"),
            Row("Bad", refersTo: "=#REF!"),
            Row("AlsoBad", value: "#DIV/0!")
        };

        DefinedNameListProjector.Filter(rows, DefinedNameFilter.Errors)
            .Select(r => r.Name).Should().BeEquivalentTo(["Bad", "AlsoBad"]);
        DefinedNameListProjector.Filter(rows, DefinedNameFilter.NoErrors)
            .Select(r => r.Name).Should().Equal("Good");
    }

    [Fact]
    public void Filter_All_KeepsEverythingInOrder()
    {
        var rows = new[] { Row("A"), Row("B"), Row("C") };

        DefinedNameListProjector.Filter(rows, DefinedNameFilter.All)
            .Select(r => r.Name).Should().Equal("A", "B", "C");
    }

    [Fact]
    public void Sort_ByName_IsCaseInsensitive()
    {
        var rows = new[] { Row("beta"), Row("Alpha"), Row("Gamma") };

        DefinedNameListProjector.Sort(rows, DefinedNameSortColumn.Name)
            .Select(r => r.Name).Should().Equal("Alpha", "beta", "Gamma");
    }

    [Fact]
    public void Sort_Descending_ReversesOrder()
    {
        var rows = new[] { Row("Alpha"), Row("Gamma"), Row("beta") };

        DefinedNameListProjector.Sort(rows, DefinedNameSortColumn.Name, descending: true)
            .Select(r => r.Name).Should().Equal("Gamma", "beta", "Alpha");
    }

    [Fact]
    public void Sort_ByScope_OrdersByScopeLabel()
    {
        var rows = new[]
        {
            Row("X", scope: SheetScope("Sheet2")),
            Row("Y"),
            Row("Z", scope: SheetScope("Sheet1"))
        };

        DefinedNameListProjector.Sort(rows, DefinedNameSortColumn.Scope)
            .Select(r => r.ScopeLabel).Should().Equal("Sheet1", "Sheet2", "Workbook");
    }

    [Fact]
    public void Project_FiltersThenSorts()
    {
        var rows = new[]
        {
            Row("zebra", scope: SheetScope("Sheet1")),
            Row("apple"),
            Row("mango")
        };

        DefinedNameListProjector.Project(rows, DefinedNameFilter.Workbook, DefinedNameSortColumn.Name)
            .Select(r => r.Name).Should().Equal("apple", "mango");
    }

    [Theory]
    [InlineData("#REF!", true)]
    [InlineData("a #name? b", true)]
    [InlineData("#DIV/0!", true)]
    [InlineData("ordinary text", false)]
    [InlineData("", false)]
    public void ContainsFormulaError_DetectsTokens(string text, bool expected)
    {
        DefinedNameListProjector.ContainsFormulaError(text).Should().Be(expected);
    }
}
