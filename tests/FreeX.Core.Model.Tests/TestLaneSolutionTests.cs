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
            "tests/FreeX.App.Avalonia.CaptureTests/FreeX.App.Avalonia.CaptureTests.csproj",
            "tests/FreeX.App.Avalonia.CaptureTests/FreeX.App.Avalonia.CaptureTests.Batch2.csproj",
            "tests/FreeX.App.Avalonia.CaptureTests/FreeX.App.Avalonia.CaptureTests.Batch3.csproj",
            "tests/FreeX.App.Avalonia.CaptureTests/FreeX.App.Avalonia.CaptureTests.Batch4.csproj",
            "tests/FreeX.App.Avalonia.CaptureTests/FreeX.App.Avalonia.CaptureTests.Batch5.csproj",
            "tests/FreeX.App.Avalonia.CaptureTests/FreeX.App.Avalonia.CaptureTests.Batch6.csproj",
            "tests/FreeX.App.Avalonia.CaptureTests/FreeX.App.Avalonia.CaptureTests.Batch7.csproj",
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
            "tests/Free.Shared.AppServices.Tests/Free.Shared.AppServices.Tests.csproj",
            "tests/Free.Shared.Pdf.Tests/Free.Shared.Pdf.Tests.csproj",
            "tests/Free.Shared.Ribbon.Tests/Free.Shared.Ribbon.Tests.csproj",
            "tests/Free.Shared.Shell.Avalonia.Tests/Free.Shared.Shell.Avalonia.Tests.csproj",
            "tests/Free.Shared.Shell.Wpf.Tests/Free.Shared.Shell.Wpf.Tests.csproj",
            "tests/Free.Shared.Theme.Tests/Free.Shared.Theme.Tests.csproj",
            "freep/FreeP.Ribbon.Definitions.Tests/FreeP.Ribbon.Definitions.Tests.csproj",
            "freep/FreeP.App.Host.Tests/FreeP.App.Host.Tests.csproj",
            "freep/FreeP.App.Localization.Tests/FreeP.App.Localization.Tests.csproj",
            "freep/FreeP.App.Presentation.Tests/FreeP.App.Presentation.Tests.csproj",
            "freep/FreeP.App.Recording.Tests/FreeP.App.Recording.Tests.csproj",
            "freep/FreeP.App.Avalonia.Tests/FreeP.App.Avalonia.Tests.csproj",
            "freep/FreeP.App.Rendering.Avalonia.Tests/FreeP.App.Rendering.Avalonia.Tests.csproj"
        });

        uiLaneProjects.Should().BeEquivalentTo(new[]
        {
            "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.Batch1.csproj",
            "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.Batch2.csproj",
            "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.Batch3.csproj",
            "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.Batch4.csproj",
            "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.Batch5.csproj",
            "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.Batch6.csproj",
            "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.Batch7.csproj",
            "tests/FreeX.App.UI.Tests/FreeX.App.UI.Tests.csproj",
            "tests/Free.Shared.Ribbon.Wpf.Tests/Free.Shared.Ribbon.Wpf.Tests.csproj",
            "tests/Free.Shared.Shell.Wpf.Tests/Free.Shared.Shell.Wpf.Tests.csproj"
        });
    }

    // R125: the two pinned lists above are a deliberate "must update me on purpose" guard for the
    // *shape* of each lane, but neither one ever asked whether a tests/-directory project is
    // reachable from EITHER automatic lane at all. Free.Shared.Shell.Avalonia.Tests.csproj (built
    // by FreeX.slnx, referenced by neither FreeX.DefaultTests.slnx nor FreeX.UiTests.slnx) and
    // Free.Shared.Ribbon.Wpf.Tests.csproj (referenced only by the manual-only FreeX.RibbonTests.slnx)
    // both compiled cleanly and both were silently never executed by ci.yml. This test derives the
    // "should run automatically" set from FreeX.slnx itself (not a hard-coded list) so a future
    // tests/-directory project that isn't wired into either automatic lane fails loudly instead of
    // accruing unnoticed, the same way R118 does this for freep/ test projects against FreeP.slnx.
    [Fact]
    public void R125_EveryFreeXTestsDirectoryTestProjectRunsInAnAutomaticLane()
    {
        var freeXSolutionProjects = ReadSolutionProjects(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "FreeX.slnx"));
        var testsDirectoryTestProjects = freeXSolutionProjects
            .Where(path => path.StartsWith("tests/", StringComparison.Ordinal)
                && path.EndsWith(".Tests.csproj", StringComparison.Ordinal))
            .ToArray();

        testsDirectoryTestProjects.Should().NotBeEmpty("FreeX.slnx should register at least its known tests/ test projects");

        var defaultLaneProjects = ReadSolutionProjects(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "FreeX.DefaultTests.slnx"));
        var uiLaneProjects = ReadSolutionProjects(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "FreeX.UiTests.slnx"));
        var automaticLaneProjects = defaultLaneProjects.Concat(uiLaneProjects).ToHashSet(StringComparer.Ordinal);

        const string canonicalHostProject = "tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.csproj";
        var hostBatchProjects = Enumerable.Range(1, 7)
            .Select(batch => $"tests/FreeX.App.Host.Tests/FreeX.App.Host.Tests.Batch{batch}.csproj")
            .ToArray();
        if (hostBatchProjects.All(automaticLaneProjects.Contains))
            automaticLaneProjects.Add(canonicalHostProject);

        var missing = testsDirectoryTestProjects.Where(path => !automaticLaneProjects.Contains(path)).ToArray();

        missing.Should().BeEmpty(
            "every tests/-directory test project registered in FreeX.slnx must run under an automatic lane " +
            "(FreeX.DefaultTests.slnx or FreeX.UiTests.slnx, both wired into ci.yml) -- FreeX.RibbonTests.slnx " +
            "is documented as a manual-only focused lane (docs/ribbon-ui-test-lane.md) and does not count");
    }

    [Fact]
    public void R118_DefaultTestLane_IncludesEveryFreePTestProjectRegisteredInFreePSolution()
    {
        // FreeX.DefaultTests.slnx is the only automatically-triggered test lane (ci.yml runs it on
        // every push/PR); FreeP.slnx itself is wired only into the manual-only freep-ci.yml workflow.
        // This must derive the expected set from FreeP.slnx (not hard-code it) so that a future FreeP
        // test project silently regresses this contract instead of accruing unnoticed for rounds, the
        // way FreeP.App.Localization.Tests and FreeP.App.Recording.Tests previously did.
        var defaultLaneProjects = ReadSolutionProjects(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "FreeX.DefaultTests.slnx"));
        var freePSolutionProjects = ReadSolutionProjects(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "FreeP.slnx"));

        var freePTestProjects = freePSolutionProjects
            .Where(path => path.StartsWith("freep/", StringComparison.Ordinal)
                && path.EndsWith(".Tests.csproj", StringComparison.Ordinal))
            .ToArray();

        freePTestProjects.Should().NotBeEmpty("FreeP.slnx should register at least its known test projects");
        defaultLaneProjects.Should().Contain(freePTestProjects,
            "every FreeP test project registered in FreeP.slnx must also run under the " +
            "automatically-triggered FreeX.DefaultTests.slnx gate; freep-ci.yml (FreeP.slnx) is manual-only " +
            "and never runs on push/PR, so a project missing here is never exercised automatically");
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
