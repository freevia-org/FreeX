using System.Windows;
using System.Windows.Controls;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class BackstageProgressOverlayBinderTests
{
    [Fact]
    public void ShowStatusPanel_FormatsStatusTextAndShowsPanel()
    {
        StaTestRunner.Run(() =>
        {
            var panel = new StackPanel { Visibility = Visibility.Collapsed };
            var status = new TextBlock();
            var progress = new ProgressBar { Minimum = 0, Maximum = 100 };

            BackstageProgressOverlayBinder.ShowStatusPanel(
                panel,
                status,
                progress,
                "Saving workbook",
                "Saving file (writing)",
                -10);

            status.Text.Should().Be("Saving workbook: Saving file (writing)");
            progress.Value.Should().Be(0);
            panel.Visibility.Should().Be(Visibility.Visible);
        });
    }

    [Fact]
    public void ShowStatusPanel_OmitsTitlePrefixWhenTitleIsEmpty()
    {
        StaTestRunner.Run(() =>
        {
            var panel = new StackPanel { Visibility = Visibility.Collapsed };
            var status = new TextBlock();
            var progress = new ProgressBar { Minimum = 0, Maximum = 100 };

            BackstageProgressOverlayBinder.ShowStatusPanel(
                panel,
                status,
                progress,
                title: string.Empty,
                "Book1.xlsx — Loading file (parsing)",
                42);

            status.Text.Should().Be("Book1.xlsx — Loading file (parsing)");
            progress.Value.Should().Be(42);
            panel.Visibility.Should().Be(Visibility.Visible);
        });
    }

    [Fact]
    public void Hide_CollapsesElementAndAllowsNull()
    {
        StaTestRunner.Run(() =>
        {
            var element = new Grid { Visibility = Visibility.Visible };

            BackstageProgressOverlayBinder.Hide(element);
            BackstageProgressOverlayBinder.Hide(null);

            element.Visibility.Should().Be(Visibility.Collapsed);
        });
    }
}
