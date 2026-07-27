namespace FreeP.App.Avalonia.Tests;

public sealed class ReadingOrderPaneVisualParitySourceTests
{
    [Fact]
    public void Avalonia_compensation_constants_match_the_measured_variant()
    {
        var repo = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(repo, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private const double ReadingOrderScrollbarGutter = 16;");
        source.Should().Contain("private const double ReadingOrderActionButtonMinHeight = 27;");
    }

    [Fact]
    public void Avalonia_reading_order_cards_pin_the_gutter_and_authority_spacing()
    {
        var repo = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(repo, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));
        var methodStart = source.IndexOf(
            "private Control BuildReadingOrderItemCard",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf(
            "private static string BuildReadingOrderAltTextLine",
            methodStart,
            StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var method = source[methodStart..methodEnd];

        source.Should().Contain(
            "Margin = new Thickness(0, 0, ReadingOrderScrollbarGutter, 0)");
        method.Should().Contain("Spacing = 2");
        source.Should().Contain(
            "MinHeight = ReadingOrderActionButtonMinHeight");
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
        report.Should().Contain("changed 18.60 %");
    }
}
