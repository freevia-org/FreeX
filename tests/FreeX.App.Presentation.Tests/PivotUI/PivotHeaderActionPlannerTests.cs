using FluentAssertions;
using FreeX.App.Presentation.PivotUI;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotHeaderActionPlannerTests
{
    [Theory]
    [InlineData(PivotHeaderMenuAction.SortAscending)]
    [InlineData(PivotHeaderMenuAction.SortDescending)]
    [InlineData(PivotHeaderMenuAction.ClearSort)]
    [InlineData(PivotHeaderMenuAction.ClearFilter)]
    [InlineData(PivotHeaderMenuAction.MoveToRows)]
    [InlineData(PivotHeaderMenuAction.MoveToColumns)]
    [InlineData(PivotHeaderMenuAction.MoveToFilters)]
    [InlineData(PivotHeaderMenuAction.MoveToValues)]
    [InlineData(PivotHeaderMenuAction.MoveUp)]
    [InlineData(PivotHeaderMenuAction.MoveDown)]
    [InlineData(PivotHeaderMenuAction.RemoveField)]
    public void Plan_DirectActions_RouteToCommandFactory(PivotHeaderMenuAction action)
    {
        PivotHeaderActionPlanner.Plan(action).RouteKind.Should().Be(PivotHeaderActionRouteKind.CommandFactory);
    }

    [Theory]
    [InlineData(PivotHeaderMenuAction.LabelFilter, PivotHeaderDialogKind.LabelFilter)]
    [InlineData(PivotHeaderMenuAction.ValueFilter, PivotHeaderDialogKind.ValueFilter)]
    [InlineData(PivotHeaderMenuAction.MoreSortOptions, PivotHeaderDialogKind.MoreSortOptions)]
    [InlineData(PivotHeaderMenuAction.FieldSettings, PivotHeaderDialogKind.FieldSettings)]
    [InlineData(PivotHeaderMenuAction.ValueFieldSettings, PivotHeaderDialogKind.ValueFieldSettings)]
    public void Plan_DialogBackedActions_RouteToSharedDialogContinuations(
        PivotHeaderMenuAction action,
        PivotHeaderDialogKind dialogKind)
    {
        var plan = PivotHeaderActionPlanner.Plan(action);

        plan.RouteKind.Should().Be(PivotHeaderActionRouteKind.Dialog);
        plan.DialogKind.Should().Be(dialogKind);
        plan.DeferredReason.Should().BeNull();
    }

}
