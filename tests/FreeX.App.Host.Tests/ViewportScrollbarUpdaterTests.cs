using System.Windows.Controls.Primitives;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ViewportScrollbarUpdaterTests
{
    [Fact]
    public void TryExtendFromArrowSmallIncrement_ExtendsAndMovesAtCurrentMaximum()
    {
        StaTestRunner.Run(() =>
        {
            var scrollBar = new ScrollBar
            {
                Value = 40,
                Maximum = 40,
                SmallChange = 1,
                ViewportSize = 10
            };

            ViewportScrollbarUpdater.TryExtendFromArrowSmallIncrement(scrollBar, absoluteLimit: 100)
                .Should()
                .BeTrue();

            scrollBar.Maximum.Should().Be(41);
            scrollBar.Value.Should().Be(41);
        });
    }

    [Fact]
    public void TryExtendFromArrowSmallIncrement_ReturnsFalseAtWorksheetLimit()
    {
        StaTestRunner.Run(() =>
        {
            var scrollBar = new ScrollBar
            {
                Value = 91,
                Maximum = 91,
                SmallChange = 1,
                ViewportSize = 10
            };

            ViewportScrollbarUpdater.TryExtendFromArrowSmallIncrement(scrollBar, absoluteLimit: 100)
                .Should()
                .BeFalse();

            scrollBar.Maximum.Should().Be(91);
            scrollBar.Value.Should().Be(91);
        });
    }
}
