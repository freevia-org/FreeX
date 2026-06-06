using System.IO;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class TestLaneSolutionTests
{
    [Fact]
    public void DefaultTestLane_ExcludesUiTestProjects()
    {
        var defaultLaneProjects = ReadSolutionProjects(WorkspaceFileLocator.FindFromWorkspaceRoot(
            "FreeX.DefaultTests.slnx"));
        var uiLaneProjects = ReadSolutionProjects(WorkspaceFileLocator.FindFromWorkspaceRoot(
            "FreeX.UiTests.slnx"));

        defaultLaneProjects.Should().BeEquivalentTo(new[]
        {
            "tests/FreeX.App.Services.Tests/FreeX.App.Services.Tests.csproj",
            "tests/FreeX.Core.Calc.Tests/FreeX.Core.Calc.Tests.csproj",
            "tests/FreeX.Core.Formula.Tests/FreeX.Core.Formula.Tests.csproj",
            "tests/FreeX.Core.IO.Tests/FreeX.Core.IO.Tests.csproj",
            "tests/FreeX.Core.Model.Tests/FreeX.Core.Model.Tests.csproj",
            "tests/FreeX.Fixtures/FreeX.Fixtures.csproj",
            "tests/FreeX.Integration.Tests/FreeX.Integration.Tests.csproj"
        });

        uiLaneProjects.Should().BeEquivalentTo(new[]
        {
            "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.csproj",
            "tests/FreeX.App.UI.Tests/FreeX.App.UI.Tests.csproj"
        });
    }

    [Fact]
    public void DefaultAgentVerification_DocumentsNonUiTestLane()
    {
        var agents = File.ReadAllText(WorkspaceFileLocator.FindFromWorkspaceRoot("AGENTS.md"));
        var readme = File.ReadAllText(WorkspaceFileLocator.FindFromWorkspaceRoot("README.md"));
        var plan = File.ReadAllText(WorkspaceFileLocator.FindFromWorkspaceRoot(
            "docs", "release/test-distribution.md"));

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
