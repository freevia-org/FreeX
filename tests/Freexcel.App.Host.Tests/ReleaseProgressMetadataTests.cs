using System.IO;
using System.Text.Json;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

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

        var distributionPlan = File.ReadAllText(WorkspaceFileLocator.Find("docs", "TEST_DISTRIBUTION_PLAN.md"));
        distributionPlan.Should().Contain(completionText);
        distributionPlan.Should().Contain(documentedStream);
    }

    [Fact]
    public void ReleaseProgressCompletionBands_MatchTesterReleaseWorkflow()
    {
        var workflow = File.ReadAllText(WorkspaceFileLocator.Find(".github", "workflows", "tester-release.yml"));

        workflow.Should().Contain("if ($overallCompletion -ge 99) { $minor = 9 }");
        workflow.Should().Contain("elseif ($overallCompletion -ge 96) { $minor = 8 }");
        workflow.Should().Contain("elseif ($overallCompletion -ge 93) { $minor = 7 }");
        workflow.Should().Contain("elseif ($overallCompletion -ge 90) { $minor = 6 }");
        workflow.Should().Contain("else { $minor = 5 }");
    }

    private static ReleaseProgress LoadProgress()
    {
        var path = WorkspaceFileLocator.Find("release", "progress.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ReleaseProgress>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Release progress metadata is empty.");
    }

    private static int MapCompletionToMinor(int overallCompletion)
    {
        if (overallCompletion >= 99)
            return 9;
        if (overallCompletion >= 96)
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
