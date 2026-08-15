using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Shell;

public sealed class WorkbookWindowRegistryDedupSourceGuardTests
{
    [Fact]
    public void WpfAndAvaloniaRegistries_DelegatePortablePolicyToTheSharedCore()
    {
        var srcRoot = RepositoryFileLocator.FindDirectory("src");
        var wpfSource = File.ReadAllText(Path.Combine(
            srcRoot,
            "FreeX.App.Host",
            "WorkbookWindowRegistry.cs"));
        var avaloniaSource = File.ReadAllText(Path.Combine(
            srcRoot,
            "FreeX.App.Avalonia",
            "AvaloniaWorkbookWindowRegistry.cs"));
        var avaloniaWindowManagement = File.ReadAllText(Path.Combine(
            srcRoot,
            "FreeX.App.Avalonia",
            "MainWindow.WindowManagement.cs"));
        var avaloniaUnhideDialog = File.ReadAllText(Path.Combine(
            srcRoot,
            "FreeX.App.Avalonia",
            "MainWindow.MissingParityDialogs.cs"));
        var avaloniaMainWindow = File.ReadAllText(Path.Combine(
            srcRoot,
            "FreeX.App.Avalonia",
            "MainWindow.cs"));
        var repositoryRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var avaloniaParityCapture = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "tools",
            "FreeX.ParityCapture.Avalonia",
            "Capture",
            "MainWindow.ParityCapture.cs"));

        wpfSource.Should().Contain("WorkbookWindowRegistryCore<IWorkbookWindow>");
        avaloniaSource.Should().Contain("WorkbookWindowRegistryCore<MainWindow>");
        wpfSource.Should().Contain("_core.PlanVisibleArrangement(");
        avaloniaSource.Should().Contain("_core.PlanVisibleArrangement(");
        avaloniaWindowManagement.Should().Contain("WindowRegistry.PlanVisibleArrangement(");
        avaloniaWindowManagement.Should().NotContain("foreach (var hidden in HiddenWindows.ToArray())");
        avaloniaWindowManagement.Should().NotContain("HiddenWindows.Clear();");
        avaloniaWindowManagement.Should().NotContain("List<Window> HiddenWindows");
        avaloniaWindowManagement.Should().NotContain("AllTopLevelWindows.Count(static w => w.IsVisible)");
        avaloniaWindowManagement.Should().Contain("WindowRegistry.Hide(this)");
        avaloniaWindowManagement.Should().Contain("SideBySideCoordinator.DisableFor(this)");
        wpfSource.Should().NotContain("HashSet<IWorkbookWindow> _hidden");
        wpfSource.Should().Contain("_core.Hide(window)");
        wpfSource.Should().Contain("_core.Unhide(window)");
        wpfSource.Should().Contain("_core.HiddenWindows");
        avaloniaSource.Should().Contain("_core.Hide(window)");
        avaloniaSource.Should().Contain("_core.Unhide(window)");
        avaloniaSource.Should().Contain("_core.HiddenWindows");
        avaloniaUnhideDialog.Should().Contain("WindowRegistry.HiddenWindows");
        avaloniaUnhideDialog.Should().Contain("WindowRegistry.Unhide(selected)");
        avaloniaUnhideDialog.Should().Contain("window.WindowMenuDisplayName");
        avaloniaMainWindow.Should().Contain("[\"Hide\"] = () => new RibbonCommandState(IsEnabled: WindowRegistry.CanHide(this))");
        avaloniaMainWindow.Should().Contain("[\"Unhide\"] = () => new RibbonCommandState(IsEnabled: WindowRegistry.HiddenWindows.Count > 0)");
        avaloniaParityCapture.Should().Contain("WindowRegistry.Hide(hidden)");
        avaloniaParityCapture.Should().NotContain("HiddenWindows.Add(");

        foreach (var rendererSource in new[] { wpfSource, avaloniaSource })
        {
            rendererSource.Should().NotContain("Dictionary<WorkbookId");
            rendererSource.Should().NotContain("FormatWindowTitleSuffix(");
            rendererSource.Should().NotContain("NextWindowIndex(");
            rendererSource.Should().NotContain("PreviousWindowIndex(");
        }
    }
}
