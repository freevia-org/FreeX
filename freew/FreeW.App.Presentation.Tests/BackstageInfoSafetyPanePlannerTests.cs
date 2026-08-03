using FreeW.App.Presentation.Backstage;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class BackstageInfoSafetyPanePlannerTests
{
    [Fact]
    public void Build_ReturnsWordStyleProtectAndInspectGroups()
    {
        var groups = BackstageInfoSafetyPanePlanner.Build();

        groups.Should().HaveCount(2);
        groups[0].Heading.Should().Be("Protect Document");
        groups[0].Actions.Should().Contain(action =>
            action.Kind == BackstageInfoSafetyActionKind.MarkAsFinal &&
            action.Label == "Mark as Final" &&
            action.Description.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        groups[0].Actions.Should().Contain(action =>
            action.Kind == BackstageInfoSafetyActionKind.RestrictEditing &&
            action.Label == "Restrict Editing" &&
            action.Description.Contains("Limit editing", StringComparison.Ordinal));

        groups[1].Heading.Should().Be("Inspect Document");
        groups[1].Actions.Should().Contain(action =>
            action.Kind == BackstageInfoSafetyActionKind.InspectDocument &&
            action.Label == "Inspect Document" &&
            action.Description.Contains("comments", StringComparison.OrdinalIgnoreCase));
        groups[1].Actions.Should().Contain(action =>
            action.Kind == BackstageInfoSafetyActionKind.CheckAccessibility &&
            action.Label == "Check Accessibility" &&
            action.Description.Contains("accessibility", StringComparison.OrdinalIgnoreCase));

        groups.SelectMany(group => group.Actions)
            .Select(action => action.Kind)
            .Should().Equal(
                BackstageInfoSafetyActionKind.MarkAsFinal,
                BackstageInfoSafetyActionKind.RestrictEditing,
                BackstageInfoSafetyActionKind.InspectDocument,
                BackstageInfoSafetyActionKind.CheckAccessibility);
    }

    [Fact]
    public void Build_WithDocumentState_ReportsCurrentProtectionInspectionAndAccessibilityStatus()
    {
        var document = new TextDocument
        {
            MarkedAsFinal = true,
            Protection = new ProtectionSettings(ProtectionMode.ReadOnly)
            {
                PasswordHash = "hash",
                PasswordSalt = "salt",
            },
        };
        document.Properties.Title = "Safety plan";
        document.Blocks.Add(new Paragraph("Body text without headings"));

        var groups = BackstageInfoSafetyPanePlanner.Build(document);
        var actions = groups.SelectMany(group => group.Actions).ToArray();

        actions.Single(action => action.Kind == BackstageInfoSafetyActionKind.MarkAsFinal)
            .Should().Match<BackstageInfoSafetyAction>(action =>
                action.Label == "Edit Anyway" &&
                action.Description.Contains("Clear the final marker", StringComparison.Ordinal));
        actions.Single(action => action.Kind == BackstageInfoSafetyActionKind.RestrictEditing)
            .Description.Should().Contain("Current restriction: Read only. Password protection is configured.");
        actions.Single(action => action.Kind == BackstageInfoSafetyActionKind.InspectDocument)
            .Description.Should().Contain("1 metadata item");
        actions.Single(action => action.Kind == BackstageInfoSafetyActionKind.CheckAccessibility)
            .Description.Should().Contain("Current scan:");
    }

    [Fact]
    public void Build_WithCleanDocument_ReportsNoCurrentSafetyIssues()
    {
        var state = new BackstageInfoSafetyDocumentState(
            IsMarkedAsFinal: false,
            ProtectionMode: ProtectionMode.None,
            ProtectionHasPassword: false,
            InspectionItemCount: 0,
            AccessibilityIssueCount: 0,
            AccessibilityErrorCount: 0,
            AccessibilityWarningCount: 0,
            AccessibilityTipCount: 0);

        var actions = BackstageInfoSafetyPanePlanner.Build(state)
            .SelectMany(group => group.Actions)
            .ToArray();

        actions.Single(action => action.Kind == BackstageInfoSafetyActionKind.RestrictEditing)
            .Description.Should().StartWith("No editing restrictions are active.");
        actions.Single(action => action.Kind == BackstageInfoSafetyActionKind.InspectDocument)
            .Description.Should().Be("No comments, revisions, document properties, or bookmarks are currently detected.");
        actions.Single(action => action.Kind == BackstageInfoSafetyActionKind.CheckAccessibility)
            .Description.Should().Be("No accessibility issues are currently detected.");
    }
}
