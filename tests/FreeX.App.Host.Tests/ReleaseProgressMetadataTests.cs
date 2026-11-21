using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ReleaseProgressMetadataTests
{
    [Fact]
    public void ReleaseProgressMetadata_UsesSupportedTesterVersionContract()
    {
        var progress = LoadProgress();

        progress.Major.Should().BeGreaterThanOrEqualTo(0);
        progress.OverallCompletion.Should().BeInRange(0, 100);
        progress.ReleasePatchBase.Should().BeGreaterThanOrEqualTo(0);
        progress.ReleasePatchSource.Should().Be("github_run_number");
        progress.Channel.Should().Be("test");
    }

    [Fact]
    public void ReleaseProgressMetadata_MapsCurrentCompletionToDocumentedTesterStream()
    {
        var progress = LoadProgress();
        var minor = MapCompletionToMinor(progress.OverallCompletion);
        var documentedStream = $"v{progress.Major}.{minor}.<run>";
        var completionText = $"At {progress.OverallCompletion}% completion";

        var distributionPlan = WorkspaceFileLocator.ReadAllText("docs", "release/test-distribution.md");
        distributionPlan.Should().Contain(completionText);
        distributionPlan.Should().Contain(documentedStream);
    }

    [Fact]
    public void ReleaseProgressCompletionBands_MatchTesterReleaseReadinessTool()
    {
        // tester-release.yml was retired; the completion-to-minor bands now live in the readiness
        // preflight that validates release/progress.json before a release is dispatched.
        var tool = WorkspaceFileLocator.ReadAllText("tools", "Test-TesterReleaseReadiness.ps1");

        tool.Should().Contain("if ($OverallCompletion -ge 99) { return 9 }");
        tool.Should().Contain("if ($OverallCompletion -ge 95) { return 8 }");
        tool.Should().Contain("if ($OverallCompletion -ge 93) { return 7 }");
        tool.Should().Contain("if ($OverallCompletion -ge 90) { return 6 }");
        tool.Should().Contain("return 5");
    }

    private static ReleaseProgress LoadProgress()
    {
        var json = WorkspaceFileLocator.ReadAllText("release", "progress.json");
        return JsonSerializer.Deserialize<ReleaseProgress>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Release progress metadata is empty.");
    }

    private static int MapCompletionToMinor(int overallCompletion)
    {
        if (overallCompletion >= 99)
            return 9;
        if (overallCompletion >= 95)
            return 8;
        if (overallCompletion >= 93)
            return 7;
        if (overallCompletion >= 90)
            return 6;

        return 5;
    }

    private sealed record ReleaseProgress(
        int Major,
        int OverallCompletion,
        int ReleasePatchBase,
        string ReleasePatchSource,
        string Channel);
}
