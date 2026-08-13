using FreeP.App.Compositor;

namespace FreeP.App.Avalonia.Tests;

public sealed class ReadingOrderPaneVisualParitySourceTests
{
    [Fact]
    public void Shared_geometry_matches_the_Wpf_authority()
    {
        PresentationReadingOrderPaneVisualMetrics.PaneWidth.Should().Be(320);
        PresentationReadingOrderPaneVisualMetrics.MoveEarlierButtonWidth.Should().Be(94);
        PresentationReadingOrderPaneVisualMetrics.MoveLaterButtonWidth.Should().Be(84);
        PresentationReadingOrderPaneVisualMetrics.ActionButtonHeight.Should().Be(27);
        PresentationReadingOrderPaneVisualMetrics.CardPadding.Should().Be(10);
        PresentationReadingOrderPaneVisualMetrics.CardBottomMargin.Should().Be(10);
    }

    [Fact]
    public void Avalonia_reading_order_cards_pin_the_gutter_and_authority_spacing()
    {
        var repo = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(repo, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var wpfSource = File.ReadAllText(Path.Combine(repo, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var methodStart = source.IndexOf(
            "private Control BuildReadingOrderItemCard",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private static void SetTextIfChanged",
            methodStart,
            StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..methodEnd];

        source.Should().Contain("itemsScroll.SetValue(ScrollViewer.AllowAutoHideProperty, false)");
        method.Should().Contain("Spacing = 2");
        method.Should().Contain("item.AltTextDisplayText");
        source.Should().Contain("Height = PresentationReadingOrderPaneVisualMetrics.ActionButtonHeight");
        source.Should().Contain("Width = PresentationReadingOrderPaneVisualMetrics.MoveEarlierButtonWidth");
        wpfSource.Should().Contain("MinWidth = PresentationReadingOrderPaneVisualMetrics.MoveEarlierButtonWidth");
        wpfSource.Should().Contain("Width = PresentationReadingOrderPaneVisualMetrics.PaneWidth");
    }

    [Fact]
    public void Checked_in_reading_order_target_retains_the_Wpf_authority_dimensions()
    {
        var repo = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var report = File.ReadAllText(Path.Combine(
            repo,
            "docs",
            "parity",
            "freep-dialog-pane-visual-evidence",
            "report.md"));

        report.Should().Contain("review.reading-order-pane.seeded");
        report.Should().Contain("320x578/320x578");
    }
}
