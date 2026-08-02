using System.IO;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AccessibilityCheckerSourceTests
{
    [Fact]
    public void AccessibilityCheckerDialog_UsesSharedPresentationPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.AccessibilityChecker.cs"));

        source.Should().Contain("private async Task ShowAccessibilityCheckerDialogAsync()");
        source.Should().Contain("private async Task ShowAccessibilityCheckerCleanDialogAsync(AccessibilityCheckerDialogPlan plan)");
        source.Should().Contain("private async Task ShowAccessibilityCheckerIssuesDialogAsync(AccessibilityCheckerDialogPlan plan)");
        source.Should().Contain("AccessibilityCheckerDialogPlanner.Create(issues, UiText.Get)");
        source.Should().Contain("AccessibilityCheckerDialogPlanner.CreateSelection(");
        source.Should().Contain("AccessibilityCheckerDialogPlanner.GetNavigationTarget(selectedIssue)");
        source.Should().Contain("new TreeView");
        source.Should().Contain("ApplyAutomation(resultsTree, plan.IssueListAutomation);");
        source.Should().Contain("ApplyAction(goToButton, plan.GoToAction);");
        source.Should().Contain("ApplyAction(closeButton, plan.CloseAction);");
        source.Should().Contain("private static void ApplyAutomation(StyledElement target, AccessibilityCheckerAutomationSpec automation)");
        source.Should().Contain("AutomationProperties.SetAutomationId(target, automation.AutomationId);");
    }

    [Fact]
    public void AccessibilityCheckerDialog_DoesNotOwnGroupingLocalizationOrNavigationParsing()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.AccessibilityChecker.cs"));

        source.Should().NotContain("AccessibilityInspectionResult.Build(");
        source.Should().NotContain("ShellLoc_AccessibilityChecker");
        source.Should().NotContain("AcText(");
        source.Should().NotContain("SeverityHeader(");
        source.Should().NotContain("SelectedDescriptor(");
        source.Should().NotContain("_session.GoToAccessibilityIssue(selectedIssue)");
        source.Should().NotContain("ReviewWorkflowPlanner.GetAccessibilityNavigationTarget(");
    }

    [Fact]
    public void AccessibilityCheckerDialog_IsWiredToMenuAndParityCapture()
    {
        var mainSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var parityCaptureSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));

        mainSource.Should().Contain("_checkAccessibilityMenuItem.Click += async (_, _) => await ShowAccessibilityCheckerDialogAsync();");
        mainSource.Should().Contain("[\"review.checkAccessibility\"] = () => _ = ShowAccessibilityCheckerDialogAsync(),");
        parityCaptureSource.Should().Contain("(\"dialog.AccessibilityChecker\", () => ShowAccessibilityCheckerParityDialogAsync()),");
    }

    [Fact]
    public void AccessibilityCheckerDialog_UsesSharedWpfFootprintAndAvaloniaChrome()
    {
        var avaloniaSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.AccessibilityChecker.cs"));
        var wpfSource = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "AccessibilityCheckerDialog.cs"));
        var captureSource = File.ReadAllText(RepoFile("src", "FreeX.App.Host", "ParityCapture.cs"));

        avaloniaSource.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog);");
        avaloniaSource.Should().Contain("AvaloniaCompactDialogChrome.WindowsStyle");
        avaloniaSource.Should().Contain("AccessibilityCheckerDialogMetrics.ResultsTreeHeight");
        avaloniaSource.Should().Contain("Class(\":selected\")");
        avaloniaSource.Should().Contain("AccessibilityCheckerDialogMetrics.ActionButtonWidth");
        wpfSource.Should().Contain("Width = AccessibilityCheckerDialogMetrics.Width;");
        wpfSource.Should().Contain("Height = AccessibilityCheckerDialogMetrics.Height;");
        captureSource.Should().Contain("AccessibilityCheckerDialogMetrics.ButtonDividerTop");
        captureSource.Should().Contain("AccessibilityCheckerDialogMetrics.ActionButtonSpacing");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
