using System.Globalization;
using System.IO;
using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R82-services-autosave-recovery-5-3: the startup recovery prompt must
/// surface the autosave timestamp (Excel's Document Recovery pane always shows "last autosaved at
/// HH:MM" next to each recovered file) instead of leaving the user to guess how fresh/stale an
/// offered snapshot is. The shared recovery-offer planner produces that display string, reusing the
/// candidate processor's parse-with-fallback-to-file-mtime logic so it always matches what
/// deduplication and ordering compute internally.
/// </summary>
public sealed class R82_StartupRecoveryTimestampDisplayTests
{
    /// <summary>Self-contained temp directory helper (avoids relying on another test project's internal type).</summary>
    private sealed class RecoveryTempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

        public RecoveryTempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    [Fact]
    public void FormatRecoveryTimestampForDisplay_UsesSidecarTimestamp_FormattedInLocalTime()
    {
        // The sidecar stores the autosave time as UTC ISO-8601 (see AutosaveSnapshotCoordinator's
        // "TimestampUtc = DateTimeOffset.UtcNow.ToString("O")"). Before this fix, that value never
        // reached the user-facing prompt at all; this verifies it is now surfaced, converted to
        // local time and formatted with the current UI culture.
        var timestampUtc = new DateTimeOffset(2026, 7, 20, 12, 34, 56, TimeSpan.Zero);
        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = @"C:\Users\alice\Budget.fxl",
            DisplayName = "Budget",
            TimestampUtc = timestampUtc.ToString("O"),
            SnapshotId = "recovery-1-w0"
        };
        var candidate = new AutosaveRecoveryCandidate(
            @"C:\nonexistent\recovery-1-w0.fxl", @"C:\nonexistent\recovery-1-w0.sidecar.json", sidecar);

        var display = AutosaveRecoveryOfferPlanner.FormatTimestamp(candidate, CultureInfo.CurrentCulture);

        display.Should().Be(timestampUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
    }

    [Fact]
    public void FormatRecoveryTimestampForDisplay_FallsBackToSnapshotFileMtime_WhenSidecarTimestampMissing()
    {
        // No-regression sibling: when the sidecar's TimestampUtc is missing/unparseable (e.g. a
        // corrupt or legacy sidecar), the DISPLAYED timestamp must still fall back to the
        // snapshot's on-disk last-write time — the same fallback GetCandidateTimestamp already
        // provided for dedup/ordering — rather than showing nothing or throwing.
        using var temp = new RecoveryTempDirectory();
        var snapshotPath = System.IO.Path.Combine(temp.Path, "recovery-2-w0.fxl");
        File.WriteAllText(snapshotPath, "placeholder");
        var mtimeUtc = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(snapshotPath, mtimeUtc);

        var sidecar = new AutosaveSidecar
        {
            OriginalFilePath = @"C:\Users\alice\Budget.fxl",
            DisplayName = "Budget",
            TimestampUtc = null,
            SnapshotId = "recovery-2-w0"
        };
        var candidate = new AutosaveRecoveryCandidate(
            snapshotPath, snapshotPath + ".sidecar.json", sidecar);

        var display = AutosaveRecoveryOfferPlanner.FormatTimestamp(candidate, CultureInfo.CurrentCulture);

        var expected = new DateTimeOffset(mtimeUtc, TimeSpan.Zero).ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        display.Should().Be(expected);
    }
}
