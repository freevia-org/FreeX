using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Accessibility;

public sealed class AccessibilityCheckerDialogPlannerSourceGuardTests
{
    [Fact]
    public void AccessibilityCheckerDialogPlanner_IsSingleSharedPresentationImplementation()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        File.Exists(Path.Combine(presentationRoot, "Accessibility", "AccessibilityCheckerDialogPlanner.cs"))
            .Should()
            .BeTrue("Accessibility Checker dialog planning should live in the shared Presentation layer");

        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Host", "AccessibilityCheckerDialogPlanner.cs"))
            .Should()
            .BeFalse("WPF should use the shared planner instead of carrying a renderer-local copy");
        File.Exists(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "AccessibilityCheckerDialogPlanner.cs"))
            .Should()
            .BeFalse("Avalonia should use the shared planner instead of carrying a renderer-local copy");
    }

    [Fact]
    public void WpfAndAvaloniaAccessibilityCheckerRenderers_DelegatePlanningToSharedPlanner()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var hostDialogSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "AccessibilityCheckerDialog.cs"));
        var hostReviewSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"));
        var avaloniaSource = File.ReadAllText(Path.Combine(repoRoot, "src", "FreeX.App.Avalonia", "MainWindow.AccessibilityChecker.cs"));

        hostDialogSource.Should().Contain("AccessibilityCheckerDialogPlanner.Create(issues, UiText.Get)");
        hostDialogSource.Should().Contain("AccessibilityCheckerDialogPlanner.CreateSelection(");
        hostDialogSource.Should().NotContain("AccessibilityInspectionResult.Build(");
        hostDialogSource.Should().NotContain("AccessibilityIssueFormatter.Format(");
        hostDialogSource.Should().NotContain("LocalizedFallbackTextResolver");
        hostDialogSource.Should().NotContain("GetNavigationTarget(");

        hostReviewSource.Should().Contain("AccessibilityCheckerDialogPlanner.GetNavigationTarget(dialog.Result!.Issue)");
        hostReviewSource.Should().NotContain("AccessibilityCheckerDialog.GetNavigationTarget(dialog.Result!.Issue)");

        avaloniaSource.Should().Contain("AccessibilityCheckerDialogPlanner.Create(issues, UiText.Get)");
        avaloniaSource.Should().Contain("AccessibilityCheckerDialogPlanner.CreateSelection(");
        avaloniaSource.Should().Contain("AccessibilityCheckerDialogPlanner.GetNavigationTarget(selectedIssue)");
        avaloniaSource.Should().NotContain("AccessibilityInspectionResult.Build(");
        avaloniaSource.Should().NotContain("ShellLoc_AccessibilityChecker");
        avaloniaSource.Should().NotContain("AcText(");
        avaloniaSource.Should().NotContain("SeverityHeader(");
        avaloniaSource.Should().NotContain("SelectedDescriptor(");
    }
}
