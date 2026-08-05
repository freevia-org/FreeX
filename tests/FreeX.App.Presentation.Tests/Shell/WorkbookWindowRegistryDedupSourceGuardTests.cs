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

        wpfSource.Should().Contain("WorkbookWindowRegistryCore<IWorkbookWindow>");
        avaloniaSource.Should().Contain("WorkbookWindowRegistryCore<MainWindow>");

        foreach (var rendererSource in new[] { wpfSource, avaloniaSource })
        {
            rendererSource.Should().NotContain("Dictionary<WorkbookId");
            rendererSource.Should().NotContain("FormatWindowTitleSuffix(");
            rendererSource.Should().NotContain("NextWindowIndex(");
            rendererSource.Should().NotContain("PreviousWindowIndex(");
        }
    }
}
