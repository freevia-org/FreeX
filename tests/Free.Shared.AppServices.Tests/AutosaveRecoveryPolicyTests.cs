namespace Free.Shared.AppServices.Tests;

public sealed class AutosaveRecoveryPolicyTests
{
    [Fact]
    public void OrderNewestFirst_UsesSidecarTimestampAndTreatsInvalidAsOldest()
    {
        var invalid = Candidate("Invalid", null);
        var older = Candidate("Older", "2026-08-15T08:00:00Z");
        var newer = Candidate("Newer", "2026-08-15T09:00:00Z");

        AutosaveRecoveryPolicy.OrderNewestFirst([older, invalid, newer])
            .Should().Equal(newer, older, invalid);
        AutosaveRecoveryPolicy.SelectLatest([older, newer]).Should().BeSameAs(newer);
    }

    [Fact]
    public void SelectLatest_DenseCandidates_PreservesFirstCandidateWhenNewestTimestampsTie()
    {
        var firstNewest = Candidate("First newest", "2026-08-31T20:00:00Z");
        var tiedNewest = Candidate("Tied newest", "2026-08-31T20:00:00Z");
        var candidates = Enumerable.Range(0, 1024)
            .Select(index => Candidate($"Older {index}", $"2026-08-30T{index % 20:00}:00:00Z"))
            .Prepend(firstNewest)
            .Append(tiedNewest);

        AutosaveRecoveryPolicy.SelectLatest(candidates).Should().BeSameAs(firstNewest);
    }

    [Fact]
    public void SelectLatest_SourceGuardKeepsLinearSelectionWithoutSortingOrMaterializing()
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "shared", "Free.Shared.AppServices", "AutosaveRecoveryPolicy.cs");
        var start = source.IndexOf("    public static AutosaveRecoveryCandidate? SelectLatest(", StringComparison.Ordinal);
        var end = source.IndexOf("    public static IReadOnlyList<AutosaveRecoveryCandidate> OrderNewestFirst(", StringComparison.Ordinal);
        var method = source[start..end];

        method.Should().Contain("foreach (var candidate in candidates)")
            .And.Contain("timestamp > latestTimestamp")
            .And.NotContain("OrderNewestFirst(")
            .And.NotContain(".ToList(")
            .And.NotContain("OrderBy");
    }

    [Fact]
    public void ResolveDisplayName_UsesCallerProvidedFallback()
    {
        AutosaveRecoveryPolicy.ResolveDisplayName(Candidate(" ", null), "an item")
            .Should().Be("an item");
    }

    [Theory]
    [InlineData(false, false, AutosaveRecoveryDisposition.Keep)]
    [InlineData(false, true, AutosaveRecoveryDisposition.Keep)]
    [InlineData(true, false, AutosaveRecoveryDisposition.Quarantine)]
    [InlineData(true, true, AutosaveRecoveryDisposition.Delete)]
    public void ResolveDisposition_MapsRecoveryOutcome(
        bool accepted,
        bool recovered,
        AutosaveRecoveryDisposition expected)
    {
        AutosaveRecoveryPolicy.ResolveDisposition(accepted, recovered).Should().Be(expected);
    }

    private static AutosaveRecoveryCandidate Candidate(string displayName, string? timestampUtc) =>
        new(
            displayName + ".snapshot",
            displayName + ".json",
            new AutosaveSidecar
            {
                DisplayName = displayName,
                TimestampUtc = timestampUtc,
            });
}
