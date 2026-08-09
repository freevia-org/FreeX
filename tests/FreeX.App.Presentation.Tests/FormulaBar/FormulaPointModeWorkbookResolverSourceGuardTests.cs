using FluentAssertions;

namespace FreeX.App.Presentation.Tests.FormulaBar;

public sealed class FormulaPointModeWorkbookResolverSourceGuardTests
{
    [Fact]
    public void Hosts_DelegatePointModeLifecycleAndRoutingPolicyToPresentation()
    {
        var host = ReadHost("FreeX.App.Host");
        var avalonia = ReadHost("FreeX.App.Avalonia");

        foreach (var source in new[] { host, avalonia })
        {
            source.Should().Contain("_formulaRangeEditingSession.IsPointModeActive(");
            source.Should().Contain("_formulaRangeEditingSession.TryApplyPointModeSelection(");
            source.Should().Contain("_formulaRangeEditingSession.GetRoutedPointModeCommand(");
            source.Should().Contain("FormulaPointModeWorkbookResolver.TryCreateSelection(");
            source.Should().Contain("FormulaPointModeWorkbookResolver.TryRouteCommand(");
            source.Should().Contain("FormulaPointModeEditSelection selection");
            source.Should().NotContain("selection.Mode == FormulaPointModeSelectionMode");
            source.Should().NotContain("selection.WorkbookId ==");
            source.Should().NotContain("new FormulaPointModeSelection(");
            source.Should().NotContain("FormulaPointModeWorkbookResolver.TryRouteCommit(");
            source.Should().NotContain("FormulaPointModeWorkbookResolver.TryRouteCancel(");
            source.Should().NotContain("FormulaPointModeWorkbookResolver.TryRouteReferenceCycle(");
        }
    }

    [Fact]
    public void Resolver_RemainsRendererNeutral()
    {
        var formulaBarRoot = RepositoryFileLocator.FindDirectory(
            "src",
            "FreeX.App.Presentation",
            "FormulaBar");
        var source = File.ReadAllText(
            Path.Combine(formulaBarRoot, "FormulaPointModeWorkbookResolver.cs"));

        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia.");
        source.Should().NotContain("FreeX.App.Host");
        source.Should().NotContain("FreeX.App.Avalonia");
    }

    private static string ReadHost(string projectName)
    {
        var projectRoot = RepositoryFileLocator.FindDirectory("src", projectName);
        return File.ReadAllText(Path.Combine(projectRoot, "MainWindow.FormulaPointMode.cs"));
    }
}
