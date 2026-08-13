using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SlicerTimeline;

public sealed class TimelineSemanticPresentationTests
{
    [Theory]
    [InlineData(0, "YEARS \u25BE")]
    [InlineData(1, "QUARTERS \u25BE")]
    [InlineData(2, "MONTHS \u25BE")]
    [InlineData(3, "DAYS \u25BE")]
    public void Build_OwnsGranularityAndClearFilterLabels(int level, string expectedLabel)
    {
        var timeline = new TimelineModel
        {
            Name = "Timeline1",
            Caption = "Order Date",
            StartDate = "2024-01-01",
            EndDate = "2024-12-31",
            SelectedStartDate = "2024-02-01",
            SelectedEndDate = "2024-03-31",
            Level = level
        };

        var layout = TimelineLayoutBuilder.Build(timeline, new LayoutRect(0, 0, 220, 100));

        layout.GranularityLabel.Should().Be(expectedLabel);
        layout.ClearFilterGlyph.Should().Be("\u00D7");
    }
}
