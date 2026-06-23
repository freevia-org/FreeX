using FreeW.App.Presentation.Backstage;

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
    }
}
