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

        wpfSource.Should().Contain("WorkbookWindowRegistryCore<IWorkbookWindow>");
        avaloniaSource.Should().Contain("WorkbookWindowRegistryCore<MainWindow>");
        wpfSource.Should().Contain("_core.PlanVisibleArrangement(");
        avaloniaSource.Should().Contain("_core.PlanVisibleArrangement(");
        avaloniaWindowManagement.Should().Contain("WindowRegistry.PlanVisibleArrangement(");
        avaloniaWindowManagement.Should().NotContain("foreach (var hidden in HiddenWindows.ToArray())");
        avaloniaWindowManagement.Should().NotContain("HiddenWindows.Clear();");

        foreach (var rendererSource in new[] { wpfSource, avaloniaSource })
        {
            rendererSource.Should().NotContain("Dictionary<WorkbookId");
            rendererSource.Should().NotContain("FormatWindowTitleSuffix(");
            rendererSource.Should().NotContain("NextWindowIndex(");
            rendererSource.Should().NotContain("PreviousWindowIndex(");
        }
    }
}
