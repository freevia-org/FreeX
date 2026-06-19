using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotCalculatedFieldPlannerTests
{
    private static PivotTableModel PivotWith(params PivotCalculatedFieldModel[] fields)
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.CalculatedFields.AddRange(fields);
        return pivot;
    }

    [Fact]
    public void ExistingFieldNames_ReturnsNamesInOrder()
    {
        var pivot = PivotWith(new PivotCalculatedFieldModel("Margin", "Revenue-Cost"), new PivotCalculatedFieldModel("Tax", "Revenue*0.1"));
        PivotCalculatedFieldPlanner.ExistingFieldNames(pivot).Should().Equal("Margin", "Tax");
    }

    [Fact]
    public void AvailableFieldReferences_TrimsBlanksAndDeduplicates()
    {
        PivotCalculatedFieldPlanner.AvailableFieldReferences(["Revenue", " ", "Cost", "revenue"])
            .Should().Equal("Revenue", "Cost");
    }

    [Fact]
    public void FindByName_IsCaseInsensitive()
    {
        var pivot = PivotWith(new PivotCalculatedFieldModel("Margin", "Revenue-Cost"));
        PivotCalculatedFieldPlanner.FindByName(pivot, "margin")!.Formula.Should().Be("Revenue-Cost");
        PivotCalculatedFieldPlanner.FindByName(pivot, "missing").Should().BeNull();
        PivotCalculatedFieldPlanner.FindByName(pivot, "  ").Should().BeNull();
    }

    [Fact]
    public void TryCreateResult_RejectsEmptyName()
    {
        var ok = PivotCalculatedFieldPlanner.TryCreateResult("  ", "Revenue", out var result, out var error);
        ok.Should().BeFalse();
        result.Should().BeNull();
        error.Should().Be(PivotCalculatedFieldPlanner.EmptyNameMessage);
    }

    [Fact]
    public void TryCreateResult_RejectsEmptyFormula()
    {
        var ok = PivotCalculatedFieldPlanner.TryCreateResult("Margin", "  ", out var result, out var error);
        ok.Should().BeFalse();
        result.Should().BeNull();
        error.Should().Be(PivotCalculatedFieldPlanner.EmptyFormulaMessage);
    }

    [Fact]
    public void TryCreateResult_TrimsNameAndFormula()
    {
        var ok = PivotCalculatedFieldPlanner.TryCreateResult(" Margin ", " Revenue-Cost ", out var result, out var error);
        ok.Should().BeTrue();
        error.Should().BeNull();
        result!.Name.Should().Be("Margin");
        result.Formula.Should().Be("Revenue-Cost");
    }

    [Fact]
    public void Upsert_AddsANewField()
    {
        var pivot = PivotWith(new PivotCalculatedFieldModel("Margin", "Revenue-Cost"));
        var result = new PivotCalculatedFieldPlanner.PivotCalculatedFieldResult("Tax", "Revenue*0.1");

        var updated = PivotCalculatedFieldPlanner.Upsert(pivot, result);

        updated.Select(field => field.Name).Should().Equal("Margin", "Tax");
    }

    [Fact]
    public void Upsert_ReplacesAnExistingFieldInPlace()
    {
        var pivot = PivotWith(new PivotCalculatedFieldModel("Margin", "old"), new PivotCalculatedFieldModel("Tax", "Revenue*0.1"));
        var result = new PivotCalculatedFieldPlanner.PivotCalculatedFieldResult("margin", "Revenue-Cost");

        var updated = PivotCalculatedFieldPlanner.Upsert(pivot, result);

        updated.Should().HaveCount(2);
        updated[0].Name.Should().Be("margin");
        updated[0].Formula.Should().Be("Revenue-Cost");
        updated[1].Name.Should().Be("Tax");
    }

    [Fact]
    public void TryRemove_RemovesAMatchingField()
    {
        var pivot = PivotWith(new PivotCalculatedFieldModel("Margin", "Revenue-Cost"), new PivotCalculatedFieldModel("Tax", "Revenue*0.1"));

        var ok = PivotCalculatedFieldPlanner.TryRemove(pivot, "tax", out var remaining, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        remaining.Select(field => field.Name).Should().Equal("Margin");
    }

    [Fact]
    public void TryRemove_ReportsWhenNoFieldMatches()
    {
        var pivot = PivotWith(new PivotCalculatedFieldModel("Margin", "Revenue-Cost"));

        var ok = PivotCalculatedFieldPlanner.TryRemove(pivot, "Missing", out var remaining, out var error);

        ok.Should().BeFalse();
        error.Should().Be(PivotCalculatedFieldPlanner.NoFieldToDeleteMessage);
        remaining.Should().HaveCount(1);
    }

    [Fact]
    public void InsertReference_ReplacesSelectionAndReportsCaret()
    {
        var (text, caret) = PivotCalculatedFieldPlanner.InsertReference("= XX + 1", "Revenue", 2, 2);
        text.Should().Be("= Revenue + 1");
        caret.Should().Be(2 + "Revenue".Length);
    }

    [Fact]
    public void InsertReference_ClampsOutOfRangeSelection()
    {
        var (text, caret) = PivotCalculatedFieldPlanner.InsertReference("=", "Revenue", 99, 99);
        text.Should().Be("=Revenue");
        caret.Should().Be("=Revenue".Length);
    }
}
