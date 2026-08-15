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
