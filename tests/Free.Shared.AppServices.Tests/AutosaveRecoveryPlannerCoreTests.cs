namespace Free.Shared.AppServices.Tests;

public sealed class AutosaveRecoveryPlannerCoreTests
{
    [Fact]
    public void PlanLatest_OrdersCandidatesAndAppliesCallerFallback()
    {
        var older = Candidate("older", "2026-08-22T08:00:00Z", "Older");
        var newer = Candidate("newer", "2026-08-22T09:00:00Z", " ");

        var plan = AutosaveRecoveryPlannerCore.PlanLatest(
            new[] { older, newer },
            "an app-specific item",
            static (candidate, displayName) => new TestPlan(candidate, displayName));

        plan.Should().NotBeNull();
        plan!.Candidate.Should().BeSameAs(newer);
        plan.DisplayName.Should().Be("an app-specific item");
    }

    [Theory]
    [InlineData(false, false, AutosaveRecoveryDisposition.Keep)]
    [InlineData(true, false, AutosaveRecoveryDisposition.Quarantine)]
    [InlineData(true, true, AutosaveRecoveryDisposition.Delete)]
    public void Complete_DelegatesDispositionForAnyFacadePlan(
        bool accepted,
        bool recovered,
        AutosaveRecoveryDisposition expected)
    {
        var plan = new TestPlan(Candidate("candidate", null, "Draft"), "Draft");

        AutosaveRecoveryPlannerCore.Complete(plan, accepted, recovered).Should().Be(expected);
    }

    [Fact]
    public void TextResolver_UsesCommonKeysAndCallerDefaults()
    {
        var resolver = new AutosaveRecoveryTextResolver(
            new AutosaveRecoveryTextDefaults(
                "Product - Recover",
                "Restore",
                "Later",
                "Nothing found.",
                "Could not recover. {0}"));

        resolver.RequiredResourceKeys.Should().Equal(
            "Autosave_Recovery_Title",
            "Autosave_Recovery_Recover_Button",
            "Autosave_Recovery_Skip_Button",
            "Autosave_Recovery_None_Message",
            "Autosave_Recovery_Failure_Message_Format");
        resolver.Resolve().Should().Be(new AutosaveRecoveryTextValues(
            "Product - Recover",
            "Restore",
            "Later",
            "Nothing found.",
            "Could not recover. {0}"));
    }

    private static AutosaveRecoveryCandidate Candidate(
        string id,
        string? timestamp,
        string displayName) =>
        new(
            id + ".fxl",
            id + ".sidecar.json",
            new AutosaveSidecar
            {
                SnapshotId = id,
                TimestampUtc = timestamp,
                DisplayName = displayName,
            });

    private sealed record TestPlan(
        AutosaveRecoveryCandidate Candidate,
        string DisplayName) : IAutosaveRecoveryPlan;
}
