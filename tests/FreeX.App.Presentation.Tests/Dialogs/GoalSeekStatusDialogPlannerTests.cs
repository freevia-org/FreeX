using FluentAssertions;
using FreeX.App.Presentation.Dialogs;

namespace FreeX.App.Presentation.Tests.Dialogs;

public sealed class GoalSeekStatusDialogPlannerTests
{
    [Fact]
    public void Uses_wpf_authority_geometry_and_action_metrics()
    {
        GoalSeekStatusDialogPlanner.WindowWidth.Should().Be(380);
        GoalSeekStatusDialogPlanner.WindowHeight(converged: true).Should().Be(190);
        GoalSeekStatusDialogPlanner.WindowHeight(converged: false).Should().Be(170);
        GoalSeekStatusDialogPlanner.KeepResultButtonWidth.Should().Be(104);
        GoalSeekStatusDialogPlanner.RestoreOriginalValuesButtonWidth.Should().Be(152);
        GoalSeekStatusDialogPlanner.OkButtonWidth.Should().Be(76);
        GoalSeekStatusDialogPlanner.ButtonGap.Should().Be(8);
    }
}
