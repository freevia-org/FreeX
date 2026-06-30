using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Presentation.Tests.FormulaBar;

public sealed class FormulaEditInteractionPlannerTests
{
    [Theory]
    [InlineData(false, StatusBarTextResourceKeys.EditMode)]
    [InlineData(true, StatusBarTextResourceKeys.PointMode)]
    public void EditModeStatusBarResourceKey_MapsFormulaEditModeToSharedStatusText(
        bool pointMode,
        string expectedResourceKey)
    {
        FormulaEditInteractionPlanner.EditModeStatusBarResourceKey(pointMode).Should().Be(expectedResourceKey);
    }

    [Fact]
    public void EnterModeStatusBarResourceKey_UsesSharedEnterModeText()
    {
        FormulaEditInteractionPlanner.EnterModeStatusBarResourceKey.Should().Be(StatusBarTextResourceKeys.EnterMode);
    }
}
