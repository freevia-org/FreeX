namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowCustomShowDragTrackerTests
{
    [Fact]
    public void New_tracker_has_no_press()
    {
        var tracker = new SlideShowCustomShowDragTracker();

        tracker.HasPress.Should().BeFalse();
        tracker.IsActive.Should().BeFalse();
        tracker.SourceIndex.Should().Be(-1);
        tracker.ShouldBegin(100, 100, 4, 4).Should().BeFalse();
    }

    [Fact]
    public void Press_records_the_source_and_clears_a_previous_active_drag()
    {
        var tracker = new SlideShowCustomShowDragTracker();
        tracker.Press(10, 10, 2);
        tracker.MarkActive();

        tracker.Press(20, 30, 5);

        tracker.HasPress.Should().BeTrue();
        tracker.SourceIndex.Should().Be(5);
        tracker.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(10, 10, false)]
    [InlineData(13.9, 10, false)]
    [InlineData(6.1, 10, false)]
    [InlineData(14, 10, true)]
    [InlineData(6, 10, true)]
    [InlineData(10, 14, true)]
    [InlineData(10, 6, true)]
    [InlineData(13.9, 13.9, false)]
    public void Should_begin_once_either_axis_reaches_the_host_threshold(double x, double y, bool expected)
    {
        var tracker = new SlideShowCustomShowDragTracker();
        tracker.Press(10, 10, 1);

        tracker.ShouldBegin(x, y, 4, 4).Should().Be(expected);
    }

    [Fact]
    public void Thresholds_are_supplied_per_axis_by_the_host()
    {
        var tracker = new SlideShowCustomShowDragTracker();
        tracker.Press(0, 0, 0);

        tracker.ShouldBegin(5, 0, thresholdX: 4, thresholdY: 20).Should().BeTrue();
        tracker.ShouldBegin(0, 5, thresholdX: 4, thresholdY: 20).Should().BeFalse();
        tracker.ShouldBegin(0, 20, thresholdX: 4, thresholdY: 20).Should().BeTrue();
    }

    [Fact]
    public void Reset_clears_press_source_and_active_state()
    {
        var tracker = new SlideShowCustomShowDragTracker();
        tracker.Press(1, 1, 3);
        tracker.MarkActive();

        tracker.Reset();

        tracker.HasPress.Should().BeFalse();
        tracker.IsActive.Should().BeFalse();
        tracker.SourceIndex.Should().Be(-1);
        tracker.ShouldBegin(500, 500, 4, 4).Should().BeFalse();
    }

    [Fact]
    public void Press_on_a_negative_index_is_not_a_tracked_press()
    {
        var tracker = new SlideShowCustomShowDragTracker();

        tracker.Press(1, 1, -1);

        tracker.HasPress.Should().BeFalse();
        tracker.ShouldBegin(500, 500, 4, 4).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 0, 40, 5, 0)]
    [InlineData(0, 19.9, 40, 5, 0)]
    [InlineData(0, 20, 40, 5, 0)]
    [InlineData(0, 20.1, 40, 5, 1)]
    [InlineData(0, 40, 40, 5, 1)]
    [InlineData(4, 21, 40, 5, 5)]
    [InlineData(4, 19, 40, 5, 4)]
    [InlineData(-1, 0, 0, 5, 5)]
    [InlineData(-1, 30, 40, 0, 0)]
    public void Drop_index_splits_at_half_height_and_appends_off_the_items(
        int itemIndex, double yInItem, double itemHeight, int count, int expected)
    {
        SlideShowCustomShowDragTracker.ResolveDropIndex(itemIndex, yInItem, itemHeight, count)
            .Should().Be(expected);
    }
}
