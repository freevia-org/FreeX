using FluentAssertions;
using Free.Shared.Shell;

namespace FreeX.App.Host.Logic.Tests;

public sealed class BackstageProgressOverlayPlannerTests
{
    [Fact]
    public void FormatStatusText_PrefixesTitleWhenPresent()
    {
        BackstageProgressOverlayPlanner.FormatStatusText("Saving workbook", "Saving file (writing)")
            .Should().Be("Saving workbook: Saving file (writing)");
    }

    [Fact]
    public void FormatStatusText_OmitsPrefixWhenTitleEmpty()
    {
        BackstageProgressOverlayPlanner.FormatStatusText(string.Empty, "Book1.xlsx — Loading file (parsing)")
            .Should().Be("Book1.xlsx — Loading file (parsing)");
    }

    [Fact]
    public void Plan_MarksIndeterminateWhenPercentMissing()
    {
        var state = BackstageProgressOverlayPlanner.Plan(
            "Saving workbook", "writing", percent: null, minimum: 0, maximum: 100);

        state.StatusText.Should().Be("Saving workbook: writing");
        state.IsIndeterminate.Should().BeTrue();
    }

    [Fact]
    public void Plan_ClampsValueIntoBarRange()
    {
        BackstageProgressOverlayPlanner.Plan(string.Empty, "d", percent: -10, minimum: 0, maximum: 100)
            .Value.Should().Be(0);
        BackstageProgressOverlayPlanner.Plan(string.Empty, "d", percent: 150, minimum: 0, maximum: 100)
            .Value.Should().Be(100);
        var mid = BackstageProgressOverlayPlanner.Plan(string.Empty, "d", percent: 42, minimum: 0, maximum: 100);
        mid.Value.Should().Be(42);
        mid.IsIndeterminate.Should().BeFalse();
    }
}
