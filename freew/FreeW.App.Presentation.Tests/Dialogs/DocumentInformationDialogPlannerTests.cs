using System.Globalization;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentInformationDialogPlannerTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void Compact_statistics_plan_preserves_renderer_row_order_and_number_formatting()
    {
        var plan = StatisticsDialogPlanner.Build(Statistics(), StatisticsDialogDepth.Compact, EnUs);

        plan.Title.Should().Be("Word Count");
        plan.Rows.Select(row => row.Label).Should().Equal(
            "Words",
            "Characters (no spaces)",
            "Characters (with spaces)",
            "Paragraphs",
            "Lines");
        plan.Rows.Select(row => row.Value).Should().Equal("1,234", "4,000", "5,000", "30", "40");
    }

    [Fact]
    public void Detailed_statistics_plan_formats_extended_metrics_and_section_boundary()
    {
        var plan = StatisticsDialogPlanner.Build(Statistics(), StatisticsDialogDepth.Detailed, EnUs);

        plan.Rows.Select(row => row.Label).Should().Equal(
            "Words",
            "Characters (with spaces)",
            "Characters (no spaces)",
            "Paragraphs",
            "Lines",
            "Sentences",
            "Reading time",
            "Words per sentence",
            "Readability (Flesch)");
        plan.Rows.Single(row => row.Key == "reading-time")
            .Should().Be(new StatisticsDialogRow("reading-time", "Reading time", "7 minutes", true));
        plan.Rows.Single(row => row.Key == "words-per-sentence").Value.Should().Be("12.3");
        plan.Rows.Single(row => row.Key == "readability").Value.Should().Be("65.5 \u2014 plain English");
    }

    [Fact]
    public void Accessibility_plan_projects_clean_summary_without_groups()
    {
        var plan = AccessibilityReportDialogPlanner.Build(new AccessibilityReport([]));

        plan.Title.Should().Be("Accessibility Checker");
        plan.Summary.Should().Be("No accessibility issues found.");
        plan.IsClean.Should().BeTrue();
    }

    [Fact]
    public void Accessibility_plan_groups_in_severity_order_and_builds_shared_lines()
    {
        var report = new AccessibilityReport([
            new AccessibilityIssue(AccessibilityRule.MissingDocumentTitle, AccessibilitySeverity.Tip, "Add a title.", -1),
            new AccessibilityIssue(AccessibilityRule.HeadingOrderGap, AccessibilitySeverity.Warning, "Fix heading order.", 1),
            new AccessibilityIssue(AccessibilityRule.MissingImageAltText, AccessibilitySeverity.Error, "Add alt text.", 2),
        ]);

        var plan = AccessibilityReportDialogPlanner.Build(report);

        plan.Summary.Should().Be("1 error(s), 1 warning(s), 1 tip(s).");
        plan.Groups.Select(group => group.Severity).Should().Equal(
            AccessibilitySeverity.Error,
            AccessibilitySeverity.Warning,
            AccessibilitySeverity.Tip);
        plan.Groups.Select(group => group.Heading).Should().Equal("Errors (1)", "Warnings (1)", "Tips (1)");
        plan.Groups.Select(group => group.AccentHex).Should().Equal("#C00000", "#B86A00", "#404040");
        plan.Groups.SelectMany(group => group.IssueLines).Should().Equal(
            "\u2022  Add alt text.",
            "\u2022  Fix heading order.",
            "\u2022  Add a title.");
    }

    private static DocumentStatistics Statistics() =>
        new(
            Words: 1234,
            CharactersWithSpaces: 5000,
            CharactersWithoutSpaces: 4000,
            Paragraphs: 30,
            Sentences: 100,
            Syllables: 1500,
            ReadingTimeMinutes: 7,
            AverageWordsPerSentence: 12.34,
            FleschReadingEase: 65.5)
        {
            Lines = 40
        };
}
