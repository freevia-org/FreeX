using FluentAssertions;
using FreeX.App.Presentation.Accessibility;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Accessibility;

public sealed class AccessibilityCheckerDialogPlannerTests
{
    [Fact]
    public void Create_BuildsCleanStateMessageActionsAndAutomationMetadata()
    {
        var plan = AccessibilityCheckerDialogPlanner.Create([], Text);

        plan.State.Should().Be(AccessibilityCheckerDialogState.Clean);
        plan.Title.Should().Be("Accessibility Checker");
        plan.CleanMessage.Should().Be("No accessibility issues found.");
        plan.StatusText.Should().Contain("No accessibility issues found");
        plan.Sections.Should().BeEmpty();
        plan.TreeNodes.Should().BeEmpty();
        plan.InitialItem.Should().BeNull();
        plan.ResultAutomation.Should().Be(new AccessibilityCheckerAutomationSpec(
            "Accessibility checker result",
            "AccessibilityCheckerResultText",
            "Summarizes the workbook accessibility check when no issues are found."));
        plan.GoToAction.Should().Match<AccessibilityCheckerActionSpec>(action =>
            action.Text == "Go To" &&
            action.IsDefault &&
            !action.IsCancel &&
            action.Automation.AutomationId == "AccessibilityCheckerGoToButton");
        plan.CloseAction.Should().Match<AccessibilityCheckerActionSpec>(action =>
            action.Text == "_Close" &&
            !action.IsDefault &&
            action.IsCancel &&
            action.Automation.AutomationId == "AccessibilityCheckerCloseButton");
    }

    [Fact]
    public void Create_BuildsLocalizedIssueSectionsAndSelectionGuidance()
    {
        var sheetId = SheetId.New();
        var issues = new[]
        {
            new AccessibilityIssue(
                AccessibilityIssueKind.LowContrastCellText,
                sheetId,
                "Sheet1",
                "B2",
                "Cell text has low contrast against its fill color."),
            new AccessibilityIssue(
                AccessibilityIssueKind.MergedCells,
                sheetId,
                "Sheet1",
                "A1:B1",
                "Merged cells can make worksheet navigation harder.")
        };
        var localizedText = new Dictionary<string, string>
        {
            ["AccessibilityChecker_SectionWarnings"] = "Localized warnings",
            ["AccessibilityChecker_GroupLowContrast"] = "Localized contrast",
            ["AccessibilityChecker_WhyLowContrast"] = "Localized why contrast",
            ["AccessibilityChecker_HowLowContrast"] = "Localized how contrast",
        };

        var plan = AccessibilityCheckerDialogPlanner.Create(
            issues,
            key => localizedText.TryGetValue(key, out var value) ? value : key);

        plan.State.Should().Be(AccessibilityCheckerDialogState.Issues);
        plan.Sections.Should().ContainSingle();
        var section = plan.Sections[0];
        section.Header.Should().Be("Localized warnings");
        section.IssueCount.Should().Be(2);
        section.Groups.Select(group => group.Label).Should().Contain("Localized contrast");

        var group = section.Groups.Single(group => group.Label == "Localized contrast");
        var item = group.Items.Should().ContainSingle().Subject;
        item.ObjectLabel.Should().Be("Sheet1!B2");
        item.Description.Should().Be("Sheet1!B2: Cell text has low contrast against its fill color.");

        var sectionNode = plan.TreeNodes.Should().ContainSingle().Subject;
        sectionNode.Should().Match<AccessibilityCheckerTreeNodePlan>(node =>
            node.Kind == AccessibilityCheckerTreeNodeKind.Section &&
            node.Header == "Localized warnings (2)" &&
            node.IsExpanded &&
            !node.IsInitialSelection &&
            node.Group == null &&
            node.Item == null);
        sectionNode.Children.Select(node => node.Header).Should().Contain("Localized contrast (1)");
        var initialNode = sectionNode.Children
            .SelectMany(node => node.Children)
            .Single(node => node.IsInitialSelection);
        initialNode.Kind.Should().Be(AccessibilityCheckerTreeNodeKind.Item);
        initialNode.Item.Should().BeSameAs(plan.InitialItem);
        plan.InitialItem.Should().BeSameAs(
            plan.Sections.SelectMany(value => value.Groups).SelectMany(value => value.Items).First());

        AccessibilityCheckerDialogPlanner.CreateSelection(item, null, plan)
            .Should()
            .Be(new AccessibilityCheckerSelectionPlan(
                HasAdditionalInformation: true,
                CanNavigate: true,
                "Localized why contrast",
                "Localized how contrast",
                "Sheet1!B2: Cell text has low contrast against its fill color."));

        AccessibilityCheckerDialogPlanner.CreateSelection(null, group, plan)
            .Should()
            .Be(new AccessibilityCheckerSelectionPlan(
                HasAdditionalInformation: true,
                CanNavigate: true,
                "Localized why contrast",
                "Localized how contrast",
                "Localized contrast"));
    }

    [Fact]
    public void CreateMessage_FormatsAndTruncatesIssueSummary()
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

        var message = AccessibilityCheckerDialogPlanner.CreateMessage(issues, Text);

        message.Should().Contain("Sheet1!A20: Missing alt text.");
        message.Should().NotContain("Sheet1!A21: Missing alt text.");
        message.Should().EndWith("...and 2 more.");
    }

    [Fact]
    public void GetNavigationTarget_UsesFirstCellInRangeOrFallsBackToSheetStart()
    {
        var sheetId = SheetId.New();

        AccessibilityCheckerDialogPlanner.GetNavigationTarget(new AccessibilityIssue(
                AccessibilityIssueKind.ChartMissingTitle,
                sheetId,
                "Sheet1",
                "C3:E8",
                "Chart is missing a title."))
            .Should()
            .Be(new CellAddress(sheetId, 3, 3));

        AccessibilityCheckerDialogPlanner.GetNavigationTarget(new AccessibilityIssue(
                AccessibilityIssueKind.DefaultWorksheetName,
                sheetId,
                "Sheet1",
                "Sheet1",
                "Worksheet tab names should describe their contents."))
            .Should()
            .Be(new CellAddress(sheetId, 1, 1));
    }

    private static string Text(string key) => key;
}
