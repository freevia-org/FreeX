using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class FreeWMediaDialogParityEvidenceGeneratorTests
{
    [Fact]
    public void Check_PassesAgainstTheCommittedEvidenceOnTheRealRepositoryTree()
    {
        var repositoryRoot = WorkspaceFileLocator.FindWorkspaceRoot();

        var result = PowerShellScriptRunner.RunToolScript(
            "Generate-FreeWMediaDialogParityEvidence.ps1",
            repositoryRoot,
            "-Check");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.CombinedOutput.Should().NotContain("is stale for route");
        result.Output.Should().Contain("routes;");
        result.Output.Should().Contain("wired;");
    }

    [Fact]
    public void AllOtherRoutes_RemainWiredAndUnchangedAfterRefreshingTheStaleRoute()
    {
        // Sibling/no-regression guard: refreshing the stale image-adjust hash must not have
        // dropped, unwired, or renamed any of the other 13 routes in the same inventory.
        var jsonPath = WorkspaceFileLocator.Find("docs", "parity", "freew-media-dialog-parity-inventory.json");
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = document.RootElement;

        root.GetProperty("routeCount").GetInt32().Should().Be(14);
        root.GetProperty("wiredCount").GetInt32().Should().Be(14);
        root.GetProperty("shellFollowUpCount").GetInt32().Should().Be(0);

        var expectedRouteIds = new[]
        {
            "image-adjust", "image-border", "image-crop", "image-position", "image-size",
            "image-alt-text", "image-table-conversion", "insert-chart", "chart-title",
            "chart-axis-titles", "chart-size", "insert-smartart", "smartart-edit", "icon-picker"
        };

        var routes = root.GetProperty("routes").EnumerateArray().ToList();
        routes.Should().HaveCount(14);

        foreach (var expectedId in expectedRouteIds)
        {
            var route = routes.Single(r => r.GetProperty("id").GetString() == expectedId);
            route.GetProperty("status").GetString().Should().Be("implemented-and-wired");
            route.GetProperty("shellWired").GetBoolean().Should().BeTrue();
            route.GetProperty("wpfPresent").GetBoolean().Should().BeTrue();
            route.GetProperty("avaloniaPresent").GetBoolean().Should().BeTrue();
        }
    }
}
