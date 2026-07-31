using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class TestLaneSolutionTests
{
    [Fact]
    public void DefaultTestLane_ExcludesUiTestProjects()
    {
        var defaultLaneProjects = ReadSolutionProjects(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "FreeX.DefaultTests.slnx"));
        var uiLaneProjects = ReadSolutionProjects(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "FreeX.UiTests.slnx"));

        defaultLaneProjects.Should().BeEquivalentTo(new[]
        {
            "tests/FreeX.App.Avalonia.Tests/FreeX.App.Avalonia.Tests.csproj",
            "tests/FreeX.App.Host.Logic.Tests/FreeX.App.Host.Logic.Tests.csproj",
            "tests/FreeX.App.Localization.Tests/FreeX.App.Localization.Tests.csproj",
            "tests/FreeX.App.Presentation.Tests/FreeX.App.Presentation.Tests.csproj",
            "tests/FreeX.App.Services.Tests/FreeX.App.Services.Tests.csproj",
            "tests/FreeX.Core.Calc.Tests/FreeX.Core.Calc.Tests.csproj",
            "tests/FreeX.Core.Formula.Tests/FreeX.Core.Formula.Tests.csproj",
            "tests/FreeX.Core.IO.Tests/FreeX.Core.IO.Tests.csproj",
            "tests/FreeX.Core.Model.Tests/FreeX.Core.Model.Tests.csproj",
            "tests/FreeX.Fixtures/FreeX.Fixtures.csproj",
            "tests/FreeX.Integration.Tests/FreeX.Integration.Tests.csproj",
            "tests/FreeX.ParityCompare.Tests/FreeX.ParityCompare.Tests.csproj",
            "tests/Free.Shared.Pdf.Tests/Free.Shared.Pdf.Tests.csproj",
            "tests/Free.Shared.Ribbon.Tests/Free.Shared.Ribbon.Tests.csproj",
            "tests/Free.Shared.Theme.Tests/Free.Shared.Theme.Tests.csproj",
            "freep/FreeP.Ribbon.Definitions.Tests/FreeP.Ribbon.Definitions.Tests.csproj",
            "freep/FreeP.App.Host.Tests/FreeP.App.Host.Tests.csproj",
            "freep/FreeP.App.Presentation.Tests/FreeP.App.Presentation.Tests.csproj",
            "freep/FreeP.App.Avalonia.Tests/FreeP.App.Avalonia.Tests.csproj",
            "freep/FreeP.App.Rendering.Avalonia.Tests/FreeP.App.Rendering.Avalonia.Tests.csproj"
        });

        uiLaneProjects.Should().BeEquivalentTo(new[]
        {
            "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.csproj",
            "tests/FreeX.App.UI.Tests/FreeX.App.UI.Tests.csproj"
        });
    }

    [Fact]
    public void RibbonTestLane_ContainsOnlyRibbonUiTestProjects()
    {
        var ribbonLaneProjects = ReadSolutionProjects(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "FreeX.RibbonTests.slnx"));

        ribbonLaneProjects.Should().BeEquivalentTo(new[]
        {
            "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.csproj",
            "tests/Free.Shared.Ribbon.Wpf.Tests/Free.Shared.Ribbon.Wpf.Tests.csproj"
        }, "the ribbon lane is a focused view over the host UI tests, run with --filter Category=RibbonUiLane");
    }

    [Fact]
    public void DefaultAgentVerification_DocumentsNonUiTestLane()
    {
        var agents = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("AGENTS.md");
        var readme = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("README.md");
        var plan = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot("docs", "release/test-distribution.md");

        agents.Should().Contain("default agent verification path");
        agents.Should().Contain("dotnet test FreeX.DefaultTests.slnx");
        agents.Should().Contain("Do not run `dotnet test FreeX.slnx` or `dotnet test FreeX.UiTests.slnx` as routine/default verification.");
        readme.Should().Contain("tests only the non-UI lane");
        readme.Should().Contain("dotnet test FreeX.DefaultTests.slnx");
        readme.Should().Contain("Run the UI lane separately only");
        plan.Should().Contain("Default agent verification does not run the UI lane");
        plan.Should().Contain("does not use `dotnet test FreeX.slnx`");
    }

    private static string[] ReadSolutionProjects(string solutionPath)
    {
        var document = XDocument.Load(solutionPath);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
