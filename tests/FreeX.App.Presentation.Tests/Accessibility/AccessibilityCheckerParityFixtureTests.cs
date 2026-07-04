using FluentAssertions;
using FreeX.App.Presentation.Accessibility;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Accessibility;

public sealed class AccessibilityCheckerParityFixtureTests
{
    [Fact]
    public void CreateDialogIssues_BuildsCanonicalIssueModelForDialogCapture()
    {
        var sheetId = SheetId.New();

        var issues = AccessibilityCheckerParityFixture.CreateDialogIssues(sheetId);

        issues.Should().HaveCount(2);
        issues.Select(issue => issue.Kind).Should().Equal(
            AccessibilityIssueKind.DefaultWorksheetName,
            AccessibilityIssueKind.MissingAltText);
        issues.Should().OnlyContain(issue => issue.SheetId == sheetId);
        issues.Should().OnlyContain(issue => issue.SheetName == AccessibilityCheckerParityFixture.SheetName);
        issues[0].Location.Should().Be(AccessibilityCheckerParityFixture.SheetName);
        issues[1].Location.Should().Be(AccessibilityCheckerParityFixture.ChartName);

        var plan = AccessibilityCheckerDialogPlanner.Create(issues, key => key);
        plan.State.Should().Be(AccessibilityCheckerDialogState.Issues);
        plan.Sections.Sum(section => section.IssueCount).Should().Be(issues.Count);
    }
}
