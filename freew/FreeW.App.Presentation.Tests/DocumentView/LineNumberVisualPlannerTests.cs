using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.DocumentView;

public sealed class LineNumberVisualPlannerTests
{
    [Fact]
    public void Continuous_mode_preserves_sequence_through_suppressed_lines_and_honors_start_and_interval()
    {
        var items = LineNumberVisualPlanner.Build(
            LineNumberMode.Continuous,
            startAt: 3,
            countBy: 2,
            [
                new LineNumberVisualSourceLine(0, SuppressNumber: false),
                new LineNumberVisualSourceLine(0, SuppressNumber: true),
                new LineNumberVisualSourceLine(1, SuppressNumber: false),
                new LineNumberVisualSourceLine(1, SuppressNumber: false),
            ]);

        items.Select(item => (item.PageIndex, item.Number, item.IsVisible)).Should().Equal(
            (0, 3, true),
            (0, 4, false),
            (1, 5, true),
            (1, 6, false));
    }

    [Fact]
    public void Restart_each_page_restarts_at_the_configured_start_value()
    {
        var items = LineNumberVisualPlanner.Build(
            LineNumberMode.RestartEachPage,
            startAt: 7,
            countBy: 3,
            [
                new LineNumberVisualSourceLine(0, SuppressNumber: false),
                new LineNumberVisualSourceLine(0, SuppressNumber: false),
                new LineNumberVisualSourceLine(1, SuppressNumber: false),
                new LineNumberVisualSourceLine(1, SuppressNumber: false),
            ]);

        items.Select(item => (item.PageIndex, item.Number, item.IsVisible)).Should().Equal(
            (0, 7, true),
            (0, 8, false),
            (1, 7, true),
            (1, 8, false));
    }
}
