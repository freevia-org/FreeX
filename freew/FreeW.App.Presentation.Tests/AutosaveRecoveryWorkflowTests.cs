using System.IO.Compression;
using Free.Shared.AppServices;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class AutosaveRecoveryWorkflowTests : IDisposable
{
    private readonly string _recoveryDirectory = Path.Combine(
        Path.GetTempPath(),
        "FreeWAutosaveRecoveryWorkflowTests_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PlanLatest_EnumeratesSelectsAndBuildsDisplayName()
    {
        CreateCandidate("older", "2026-08-05T08:00:00Z", "Older");
        var newer = CreateCandidate("newer", "2026-08-05T09:00:00Z", " ");

        var plan = AutosaveRecoveryPlanner.PlanLatest(
            new AutosaveSnapshotStore(_recoveryDirectory));

        plan.Should().NotBeNull();
        plan!.Candidate.SnapshotPath.Should().Be(newer.SnapshotPath);
        plan.DisplayName.Should().Be("a document");
    }

    [Fact]
    public void Complete_DeclinedRecoveryKeepsCandidate()
    {
        var candidate = CreateCandidate("declined", "2026-08-05T09:00:00Z", "Draft");
        var plan = new AutosaveRecoveryPlan(candidate, "Draft");

        var disposition = AutosaveRecoveryPlanner.Complete(
            plan,
            accepted: false,
            recovered: false);

        disposition.Should().Be(AutosaveRecoveryDisposition.Keep);
        File.Exists(candidate.SnapshotPath).Should().BeTrue();
        File.Exists(candidate.SidecarPath).Should().BeTrue();
    }

    [Fact]
    public void Complete_RecoveredCandidateDeletesSnapshotAndSidecar()
    {
        var candidate = CreateCandidate("recovered", "2026-08-05T09:00:00Z", "Draft");
        var plan = new AutosaveRecoveryPlan(candidate, "Draft");

        var disposition = AutosaveRecoveryPlanner.Complete(
            plan,
            accepted: true,
            recovered: true);

        disposition.Should().Be(AutosaveRecoveryDisposition.Delete);
        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        File.Exists(candidate.SidecarPath).Should().BeFalse();
    }

    [Fact]
    public void Complete_FailedRecoveryQuarantinesSnapshotAndSidecar()
    {
        var candidate = CreateCandidate("failed", "2026-08-05T09:00:00Z", "Draft");
        var plan = new AutosaveRecoveryPlan(candidate, "Draft");

        var disposition = AutosaveRecoveryPlanner.Complete(
            plan,
            accepted: true,
            recovered: false);

        disposition.Should().Be(AutosaveRecoveryDisposition.Quarantine);
        File.Exists(candidate.SnapshotPath).Should().BeFalse();
        File.Exists(candidate.SidecarPath).Should().BeFalse();

        var quarantineDirectory = Path.Combine(_recoveryDirectory, "Quarantine");
        Directory.GetFiles(quarantineDirectory).Should().HaveCount(2);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_recoveryDirectory, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }

    private AutosaveRecoveryCandidate CreateCandidate(
        string snapshotId,
        string timestampUtc,
        string displayName)
    {
        Directory.CreateDirectory(_recoveryDirectory);

        var snapshotPath = Path.Combine(_recoveryDirectory, snapshotId + ".fxl");
        using (var archive = ZipFile.Open(snapshotPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("[Content_Types].xml");
        }

        var sidecarPath = Path.Combine(_recoveryDirectory, snapshotId + ".sidecar.json");
        var sidecar = new AutosaveSidecar
        {
            DisplayName = displayName,
            SnapshotId = snapshotId,
            TimestampUtc = timestampUtc
        };
        File.WriteAllText(sidecarPath, AutosaveSnapshotStore.SerializeSidecar(sidecar));

        return new AutosaveRecoveryCandidate(snapshotPath, sidecarPath, sidecar);
    }
}
