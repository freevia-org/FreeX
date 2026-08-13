using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisShellRequestPlannerTests
{
    [Fact]
    public void Build_MissingSelectionReturnsStatusWithoutShellPlan()
    {
        var plan = QuickAnalysisShellRequestPlanner.Build(
            sheet: CreateSheet(),
            selection: null,
            QuickAnalysisShellCapabilities.DialogBacked);

        plan.Status.Should().Be(QuickAnalysisShellRequestStatus.MissingSelection);
        plan.CanOpen.Should().BeFalse();
        plan.ShellPlan.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Build_SingleCellSelectionReturnsUnsupportedSelection()
    {
        var sheet = CreateSheet();

        var plan = QuickAnalysisShellRequestPlanner.Build(
            sheet,
            Range(sheet, 1, 1, 1, 1),
            QuickAnalysisShellCapabilities.DialogBacked);

        plan.Status.Should().Be(QuickAnalysisShellRequestStatus.UnsupportedSelection);
        plan.CanOpen.Should().BeFalse();
    }

    [Fact]
    public void Build_WholeColumnSelectionReturnsUnsupportedSelection()
    {
        var sheet = CreateSheet();

        var plan = QuickAnalysisShellRequestPlanner.Build(
            sheet,
            Range(sheet, 1, 1, CellAddress.MaxRow, 1),
            QuickAnalysisShellCapabilities.DialogBacked);

        plan.Status.Should().Be(QuickAnalysisShellRequestStatus.UnsupportedSelection);
        plan.SelectionDescription.Should().BeNull();
        plan.CanOpen.Should().BeFalse();
    }

    [Fact]
    public void Build_NumericSelectionReturnsSharedShellPlan()
    {
        var sheet = CreateSheet();
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var plan = QuickAnalysisShellRequestPlanner.Build(
            sheet,
            Range(sheet, 1, 1, 3, 1),
            QuickAnalysisShellCapabilities.DialogBacked);

        plan.Status.Should().Be(QuickAnalysisShellRequestStatus.Ready);
        plan.CanOpen.Should().BeTrue();
        plan.SelectionDescription.Should().NotBeNull();
        plan.ShellPlan.Groups.Should().Contain(group => group.Group == QuickAnalysisGroup.Formatting);
        plan.ShellPlan.AllItems().Should().Contain(item => item.Id == "format.databars");
    }

    [Fact]
    public void TryBuildTotalFormulaEdits_UsesOperationTotalKind()
    {
        var sheet = CreateSheet();
        for (uint row = 1; row <= 3; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var selection = Range(sheet, 1, 1, 3, 1);
        var item = QuickAnalysisShellRequestPlanner
            .Build(sheet, selection, QuickAnalysisShellCapabilities.DialogBacked)
            .ShellPlan
            .AllItems()
            .Single(item => item.Id == "total.percenttotal");
        var operation = QuickAnalysisHostOperationPlanner.Plan(item);

        var result = QuickAnalysisHostOperationPlanner.TryBuildTotalFormulaEdits(
            operation,
            selection,
            out var edits);

        result.Should().BeTrue();
        edits.Should().NotBeEmpty();
        operation.TotalFormulaKind.Should().Be(QuickAnalysisTotalFormulaKind.PercentTotal);
    }

    private static Sheet CreateSheet() => new Workbook("Book").AddSheet("Sheet1");

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));
}
