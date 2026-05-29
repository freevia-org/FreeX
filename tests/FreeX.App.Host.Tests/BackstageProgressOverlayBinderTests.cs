using System.Windows;
using System.Windows.Controls;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class BackstageProgressOverlayBinderTests
{
    [Fact]
    public void ShowOverlay_UpdatesTextClampsProgressAndShowsOverlay()
    {
        StaTestRunner.Run(() =>
        {
            var overlay = new Grid { Visibility = Visibility.Collapsed };
            var title = new TextBlock();
            var detail = new TextBlock();
            var progress = new ProgressBar { Minimum = 1, Maximum = 99 };

            BackstageProgressOverlayBinder.ShowOverlay(
                overlay,
                title,
                detail,
                progress,
                "Opening workbook",
                "Loading file (parsing)",
                150);

            title.Text.Should().Be("Opening workbook");
            detail.Text.Should().Be("Loading file (parsing)");
            progress.IsIndeterminate.Should().BeFalse();
            progress.Value.Should().Be(99);
            overlay.Visibility.Should().Be(Visibility.Visible);
        });
    }

    [Fact]
    public void ShowOverlay_MakesProgressIndeterminateWhenPercentMissing()
    {
        StaTestRunner.Run(() =>
        {
            var progress = new ProgressBar { Minimum = 0, Maximum = 100 };

            BackstageProgressOverlayBinder.ShowOverlay(
                new Grid(),
                new TextBlock(),
                new TextBlock(),
                progress,
                "Opening workbook",
                "Loading file",
                percent: null);

            progress.IsIndeterminate.Should().BeTrue();
        });
    }

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
