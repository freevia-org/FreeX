using Free.Shared.AppServices;
using FreeW.App.Host;

namespace FreeW.App.Host.Tests;

public sealed class AutosaveRecoveryCandidatePlannerTests
{
    [Fact]
    public void SelectLatest_UsesSidecarTimestamp()
    {
        var older = Candidate("Older", "2026-06-22T08:00:00Z");
        var newer = Candidate("Newer", "2026-06-22T09:00:00Z");

        AutosaveRecoveryCandidatePlanner.SelectLatest([older, newer]).Should().BeSameAs(newer);
    }

    [Fact]
    public void SelectLatest_TreatsMissingTimestampAsOldest()
    {
        var missing = Candidate("Missing", timestampUtc: null);
        var dated = Candidate("Dated", "2026-06-22T08:00:00Z");

        AutosaveRecoveryCandidatePlanner.SelectLatest([missing, dated]).Should().BeSameAs(dated);
    }

    [Fact]
    public void DisplayName_FallsBackWhenSidecarNameIsBlank()
    {
        AutosaveRecoveryCandidatePlanner.DisplayName(Candidate(" ", "2026-06-22T08:00:00Z"))
            .Should().Be("a document");
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
