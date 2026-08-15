using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Host.Tests;

public sealed class AutosaveRecoveryPlannerTests
{
    [Fact]
    public void SelectLatest_UsesSidecarTimestamp()
    {
        var older = Candidate("Older", "2026-06-22T08:00:00Z");
        var newer = Candidate("Newer", "2026-06-22T09:00:00Z");

        AutosaveRecoveryPolicy.SelectLatest([older, newer]).Should().BeSameAs(newer);
    }

    [Fact]
    public void SelectLatest_TreatsMissingTimestampAsOldest()
    {
        var missing = Candidate("Missing", timestampUtc: null);
        var dated = Candidate("Dated", "2026-06-22T08:00:00Z");

        AutosaveRecoveryPolicy.SelectLatest([missing, dated]).Should().BeSameAs(dated);
    }

    [Fact]
    public void DisplayName_FallsBackWhenSidecarNameIsBlank()
    {
        AutosaveRecoveryPolicy.ResolveDisplayName(
                Candidate(" ", "2026-06-22T08:00:00Z"),
                "a document")
            .Should().Be("a document");
    }

    [Theory]
    [InlineData(false, false, AutosaveRecoveryDisposition.Keep)]
    [InlineData(false, true, AutosaveRecoveryDisposition.Keep)]
    [InlineData(true, false, AutosaveRecoveryDisposition.Quarantine)]
    [InlineData(true, true, AutosaveRecoveryDisposition.Delete)]
    public void ResolveDisposition_UsesOneRecoveryLifecyclePolicy(
        bool accepted,
        bool recovered,
        AutosaveRecoveryDisposition expected)
    {
        AutosaveRecoveryPolicy.ResolveDisposition(accepted, recovered).Should().Be(expected);
    }

    private static AutosaveRecoveryCandidate Candidate(string displayName, string? timestampUtc) =>
        new(
            snapshotPath: displayName + ".docx",
            sidecarPath: displayName + ".json",
            new AutosaveSidecar
            {
                DisplayName = displayName,
                TimestampUtc = timestampUtc
            });
}
