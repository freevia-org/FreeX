using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class AccessibilityReportDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Clean_report_matches_WPF_window_and_copy_contract()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new AccessibilityReportDialog(new AccessibilityReport([]));

            dialog.Title.Should().Be("Accessibility Checker");
            dialog.Width.Should().Be(460);
            dialog.MaxHeight.Should().Be(560);
            dialog.SizeToContent.Should().Be(SizeToContent.Height);
            dialog.CanResize.Should().BeFalse();

            var texts = dialog.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
            texts.Should().Contain("No accessibility issues found.");
            dialog.GetLogicalDescendants().OfType<ScrollViewer>().Should().BeEmpty();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Populated_report_matches_WPF_severity_groups_and_scroll_contract()
    {
        await Session.Dispatch(() =>
        {
            var report = new AccessibilityReport([
                new AccessibilityIssue(AccessibilityRule.MissingImageAltText, AccessibilitySeverity.Error, "Image has no alternative text.", 0),
                new AccessibilityIssue(AccessibilityRule.HeadingOrderGap, AccessibilitySeverity.Warning, "Heading levels skip from 1 to 3.", 1),
                new AccessibilityIssue(AccessibilityRule.MissingDocumentTitle, AccessibilitySeverity.Tip, "Document title is missing.", -1),
            ]);
            var dialog = new AccessibilityReportDialog(report);

            var texts = dialog.GetLogicalDescendants().OfType<TextBlock>().ToArray();
            texts.Select(text => text.Text).Should().Contain([
                "1 error(s), 1 warning(s), 1 tip(s).",
                "Errors (1)",
                "Warnings (1)",
                "Tips (1)",
                "\u2022  Image has no alternative text.",
                "\u2022  Heading levels skip from 1 to 3.",
                "\u2022  Document title is missing.",
            ]);

            var scroll = dialog.GetLogicalDescendants().OfType<ScrollViewer>().Single();
            scroll.MaxHeight.Should().Be(420);
            dialog.GetLogicalDescendants().OfType<Button>().Single().MinWidth.Should().Be(84);
            var headings = texts.Where(text => text.Text is "Errors (1)" or "Warnings (1)" or "Tips (1)").ToArray();
            headings.Should().HaveCount(3);
            ((ISolidColorBrush)headings[0].Foreground!).Color.Should().Be(Color.FromRgb(0xC0, 0x00, 0x00));
            ((ISolidColorBrush)headings[1].Foreground!).Color.Should().Be(Color.FromRgb(0xB8, 0x6A, 0x00));
            ((ISolidColorBrush)headings[2].Foreground!).Color.Should().Be(Color.FromRgb(0x40, 0x40, 0x40));
        }, CancellationToken.None);
    }
}
