using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class MacOsLaunchSmokeReportKeyDriftGuardTests
{
    private static readonly Regex SourceReportKeyPattern = new(
        "\\$\"(?<key>(?:native|toolbar)_[a-z0-9_]+)=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WorkflowGrepReportKeyPattern = new(
        "grep -q \"(?<key>(?:native|toolbar)_[a-z0-9_]+)=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReadinessReportKeyMarkerPattern = new(
        "\"(?<key>(?:native|toolbar)_[a-z0-9_]+)=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void MacOsLaunchSmoke_NativeAndToolbarReportKeysMatchWorkflowGrepsAndReadinessMarkers()
    {
        var sourceReportKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs")),
            SourceReportKeyPattern);
        var workflowGrepKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find(".github", "workflows", "macos-app.yml")),
            WorkflowGrepReportKeyPattern);
        var readinessMarkerKeys = ExtractDistinctKeys(
            File.ReadAllText(RepositoryFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1")),
            ReadinessReportKeyMarkerPattern);

        sourceReportKeys.Should().NotBeEmpty("MacOsLaunchSmoke should emit native_ and toolbar_ report keys");
        workflowGrepKeys.Should().NotBeEmpty("the macOS workflow should grep native_ and toolbar_ report keys");
        readinessMarkerKeys.Should().NotBeEmpty("the readiness preflight should track native_ and toolbar_ report keys");

        FindSetDrift(sourceReportKeys, workflowGrepKeys, "MacOsLaunchSmoke report", "macOS workflow grep")
            .Should()
            .BeEmpty("the macOS workflow grep contract should match the native_ and toolbar_ report keys emitted by MacOsLaunchSmoke");

        FindMissingKeys(readinessMarkerKeys, sourceReportKeys, "readiness report marker", "MacOsLaunchSmoke report")
            .Should()
            .BeEmpty("readiness report markers should stay backed by MacOsLaunchSmoke native_ and toolbar_ report keys");
    }

    private static string[] ExtractDistinctKeys(string text, Regex pattern) =>
        pattern.Matches(text)
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

    private static string[] FindSetDrift(
        IReadOnlyCollection<string> expectedKeys,
        IReadOnlyCollection<string> actualKeys,
        string expectedLabel,
        string actualLabel)
    {
        var missing = expectedKeys
            .Where(key => !actualKeys.Contains(key))
            .Select(key => $"{actualLabel} is missing '{key}' from {expectedLabel}");
        var unexpected = actualKeys
            .Where(key => !expectedKeys.Contains(key))
            .Select(key => $"{actualLabel} has unexpected '{key}' not found in {expectedLabel}");

        return missing.Concat(unexpected).ToArray();
    }

    private static string[] FindMissingKeys(
        IReadOnlyCollection<string> expectedKeys,
        IReadOnlyCollection<string> actualKeys,
        string expectedLabel,
        string actualLabel) =>
        expectedKeys
            .Where(key => !actualKeys.Contains(key))
            .Select(key => $"{actualLabel} is missing '{key}' from {expectedLabel}")
            .ToArray();
}
