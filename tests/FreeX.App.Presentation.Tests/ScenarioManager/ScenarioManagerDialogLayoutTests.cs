using FluentAssertions;
using FreeX.App.Presentation.ScenarioManager;

namespace FreeX.App.Presentation.Tests.ScenarioManager;

public sealed class ScenarioManagerDialogLayoutTests
{
    [Fact]
    public void FixedDialogMetricsLeaveValidationAndCloseRowsOutsideBody()
    {
        ScenarioManagerDialogLayout.RootRowCount.Should().Be(3);
        ScenarioManagerDialogLayout.FieldRowCount.Should().Be(6);
        ScenarioManagerDialogLayout.DialogWidth.Should().Be(360);
        ScenarioManagerDialogLayout.DialogHeight.Should().Be(420);
        ScenarioManagerDialogLayout.ScenarioListHeight.Should().Be(118);
        ScenarioManagerDialogLayout.ActionButtonWidth.Should().Be(82);
        ScenarioManagerDialogLayout.CloseButtonWidth.Should().Be(72);
        ScenarioManagerDialogLayout.CloseButtonWidth.Should().BeLessThan(ScenarioManagerDialogLayout.ActionButtonWidth);
        ScenarioManagerDialogLayout.ScenarioListHeaderBottomMargin.Should().Be(4);
        ScenarioManagerDialogLayout.FieldBottomMargin.Should().Be(8);
        ScenarioManagerDialogLayout.LockedCheckBoxBottomMargin.Should().Be(6);
        ScenarioManagerDialogLayout.HiddenCheckBoxBottomMargin.Should().Be(8);
        ScenarioManagerDialogLayout.GroupTopMargin.Should().Be(12);
        ScenarioManagerDialogLayout.CloseRowTopMargin.Should().Be(12);
    }
}
