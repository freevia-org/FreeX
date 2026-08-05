using FluentAssertions;
using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.QuickAnalysis;

public sealed class QuickAnalysisShellSessionTests
{
    [Fact]
    public void PlanOpen_BuildsSharedShellItemsWithSupportAndIconState()
    {
        var (sheet, selection) = CreateNumericSelection();
        var session = new QuickAnalysisShellSession();

        var openPlan = session.PlanOpen(
            sheet,
            selection,
            QuickAnalysisShellCapabilities.DialogBacked);

        openPlan.CanOpen.Should().BeTrue();
        var item = openPlan.ShellPlan.AllItems().Single(entry => entry.Id == "format.databars");
        item.IsSupported.Should().BeTrue();
        item.IsEnabled.Should().BeTrue();
        item.PreviewIcon.Should().BeEquivalentTo(
            QuickAnalysisPreviewIconPlanner.Plan(item.HoverPreview.PreviewVisual));
    }

    [Fact]
    public void PlanOpenIssue_PreservesIssueStatusUntilAValidInteractionStarts()
    {
        var (sheet, selection) = CreateNumericSelection();
        var session = new QuickAnalysisShellSession();

        session.PlanOpen(sheet, selection: null, QuickAnalysisShellCapabilities.DialogBacked)
            .CanOpen.Should().BeFalse();
        session.PlanPreviewClear().ShouldResetStatus.Should().BeFalse();

        session.PlanOpen(sheet, selection, QuickAnalysisShellCapabilities.DialogBacked)
            .CanOpen.Should().BeTrue();
        session.PlanPreviewClear().ShouldResetStatus.Should().BeTrue();
    }

    [Fact]
    public void PlanPreview_ProjectsItemHoverStateAndClearTransition()
    {
        var (sheet, selection) = CreateNumericSelection();
        var session = new QuickAnalysisShellSession();
        var item = session.PlanOpen(sheet, selection, QuickAnalysisShellCapabilities.DialogBacked)
            .ShellPlan
            .AllItems()
            .Single(entry => entry.Id == "total.sum");

        var preview = session.PlanPreview(item);

        preview.IsVisible.Should().BeTrue();
        preview.Range.Should().Be(item.HoverPreview.Range);
        preview.Visual.Should().Be(item.HoverPreview.PreviewVisual.Kind);
        preview.StatusText.Should().Be(item.HoverPreview.StatusText);
        preview.ShouldResetStatus.Should().BeFalse();

        var cleared = session.PlanPreviewClear();
        cleared.IsVisible.Should().BeFalse();
        cleared.Range.Should().BeNull();
        cleared.Visual.Should().Be(QuickAnalysisPreviewVisualKind.None);
        cleared.ShouldResetStatus.Should().BeTrue();
    }

    [Fact]
    public void PlanSelection_UsesSharedEnablementAndOperationPlanning()
    {
        var (sheet, selection) = CreateNumericSelection();
        var session = new QuickAnalysisShellSession();
        var item = session.PlanOpen(sheet, selection, QuickAnalysisShellCapabilities.DirectApplyLimited)
            .ShellPlan
            .AllItems()
            .Single(entry => entry.Id == "total.percenttotal");

        item.IsSupported.Should().BeFalse();
        item.IsEnabled.Should().BeTrue("deferred items remain actionable so the shell can explain the limitation");
        session.PlanSelection(item).Should().BeEquivalentTo(QuickAnalysisHostOperationPlanner.Plan(item));

        var disabled = item with
        {
            Action = new QuickAnalysisShellAction(
                QuickAnalysisShellActionKind.Deferred,
                item.Action.Route)
        };
        disabled.IsEnabled.Should().BeFalse();
        session.PlanSelection(disabled).Should().BeNull();
    }

    private static (Sheet Sheet, GridRange Selection) CreateNumericSelection()
    {
        var sheet = new Workbook("Book").AddSheet("Sheet1");
        for (uint row = 1; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        return (
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)));
    }
}
