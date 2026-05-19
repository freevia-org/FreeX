using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class AccessibilityIssueFormatterTests
{
    [Fact]
    public void Format_ReturnsSheetQualifiedIssueLines()
    {
        var sheetId = SheetId.New();
        var issues = new[]
        {
            new AccessibilityIssue(AccessibilityIssueKind.MergedCells, sheetId, "Sheet1", "A1:B1", "Merged cells."),
            new AccessibilityIssue(AccessibilityIssueKind.MissingAltText, sheetId, "Sheet2", "C3", "Picture missing alt text.")
        };

        AccessibilityIssueFormatter.Format(issues)
            .Should()
            .Be(string.Join(Environment.NewLine,
                "Sheet1!A1:B1: Merged cells.",
                "Sheet2!C3: Picture missing alt text."));
    }

    [Fact]
    public void Format_TruncatesAfterTwentyIssues()
    {
        var sheetId = SheetId.New();
        var issues = Enumerable.Range(1, 22)
            .Select(index => new AccessibilityIssue(
                AccessibilityIssueKind.MissingAltText,
                sheetId,
                "Sheet1",
                $"A{index}",
                "Missing alt text."))
            .ToList();

        var formatted = AccessibilityIssueFormatter.Format(issues);

        formatted.Should().Contain("Sheet1!A20: Missing alt text.");
        formatted.Should().NotContain("Sheet1!A21: Missing alt text.");
        formatted.Should().EndWith("...and 2 more.");
    }
}
