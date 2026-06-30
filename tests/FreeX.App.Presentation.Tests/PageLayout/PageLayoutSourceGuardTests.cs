using FluentAssertions;
using FreeX.App.Presentation.Tests;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageLayoutSourceGuardTests
{
    [Fact]
    public void PageLayoutPresentationPlanners_DoNotReferencePlatformUiAssemblies()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "PageLayout");

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            var source = File.ReadAllText(file);

            source.Should().NotContain("System.Windows");
            source.Should().NotContain("Avalonia");
            source.Should().NotContain("FreeX.App.Host");
            source.Should().NotContain("FreeX.App.Avalonia");
        }
    }

    [Fact]
    public void WpfPrintRenderer_DelegatesWorksheetPrintPlanningToPresentation()
    {
        var directory = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Host");
        var source = File.ReadAllText(Path.Combine(directory, "PrintRenderer.cs"));

        source.Should().Contain("WorksheetPrintRenderPlanner.TryBuild(");
        source.Should().NotContain("sheet.PrintAreas");
        source.Should().NotContain("sheet.GetUsedRange()");
        source.Should().NotContain("PagePaginationPlanner.BuildPlan(");
        source.Should().NotContain("PrintPageGridPlanner.Build(");
        source.Should().NotContain("WorksheetPageLayout.GetPageSizeInches(");
    }
}
