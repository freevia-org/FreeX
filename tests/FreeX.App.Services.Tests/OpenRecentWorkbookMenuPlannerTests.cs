using System.Diagnostics;
using System.Threading;
using FluentAssertions;
using Free.Shared.Shell;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class OpenRecentWorkbookMenuPlannerTests
{
    [Fact]
    public void Create_WithRawSlowExistenceProbe_BlocksTheCallingThreadForTheFullProbeDuration()
    {
        // R152-shared-recent-files-F1 (fail-before shape): this is exactly what the Avalonia native
        // Open-Recent menu rebuild used to do -- hand OpenRecentWorkbookMenuPlanner.Create a raw
        // Func<string, bool> straight off the filesystem (File.Exists), with no cache in front of
        // it. A slow/unreachable UNC probe blocks the calling (UI) thread for its full duration,
        // once per recent entry. Simulated here with a short controlled delay released from a
        // background thread -- if Create() returned before that delay elapsed, the probe could not
        // have run on (and blocked) the calling thread.
        var releaseProbe = new ManualResetEventSlim(false);
        var releaseDelay = TimeSpan.FromMilliseconds(300);
        // A dedicated Thread (not Task.Run) so a saturated test-run threadpool can never delay the
        // release past the probe's own guard timeout below.
        var releaser = new Thread(() =>
        {
            Thread.Sleep(releaseDelay);
            releaseProbe.Set();
        })
        { IsBackground = true };
        releaser.Start();

        var stopwatch = Stopwatch.StartNew();
        var plan = OpenRecentWorkbookMenuPlanner.Create(
            [Entry("Work/Report.xlsx", DateTimeOffset.UtcNow)],
            fileExists: _ =>
            {
                releaseProbe.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
                    "the background release must fire well inside the 5s guard timeout");
                return true;
            },
            canOpenWorkbook: _ => true);
        stopwatch.Stop();

        stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo((long)releaseDelay.TotalMilliseconds - 50,
            "Create's LINQ pipeline calls fileExists synchronously on the calling thread, so it cannot " +
            "return before a probe that only unblocks after releaseDelay has elapsed");
        plan.ItemCount.Should().Be(1);
    }

    [Fact]
    public void Create_WithRecentFilePathExistenceCache_NeverBlocksOnAnUnresolvedProbe()
    {
        // The fix: wrap the same slow probe in RecentFilePathExistenceCache (as
        // MainWindow.CreateNativeOpenRecentMenu now does) so Create's fileExists delegate is the
        // cache's non-blocking Exists, not the raw probe. The first call for an unresolved path
        // returns the optimistic "exists" default immediately -- Create must complete near-instantly
        // even though the underlying probe is still sitting there un-released, proving the calling
        // thread was never blocked on it.
        var probeReleased = new ManualResetEventSlim(false);
        var cache = new RecentFilePathExistenceCache(probe: path =>
        {
            probeReleased.Wait(TimeSpan.FromSeconds(5)); // would deadlock the test if Create blocked on this
            return true;
        });

        var stopwatch = Stopwatch.StartNew();
        var plan = OpenRecentWorkbookMenuPlanner.Create(
            [Entry("Work/Report.xlsx", DateTimeOffset.UtcNow)],
            fileExists: cache.Exists,
            canOpenWorkbook: _ => true);
        stopwatch.Stop();

        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000,
            "the cache's optimistic default must let Create return immediately, without waiting for the " +
            "still-unreleased background probe");
        plan.ItemCount.Should().Be(1, "an unresolved path is optimistically shown, never hidden pre-probe");

        probeReleased.Set(); // let the background probe finish so it doesn't outlive the test
    }

    [Fact]
    public void Create_FiltersBlankMissingAndUnsupportedPaths()
    {
        var now = new DateTimeOffset(2026, 6, 8, 9, 0, 0, TimeSpan.Zero);
        var existingSupported = Path.Combine("Work", "Budget.xlsx");
        var missingSupported = Path.Combine("Work", "Missing.xlsx");
        var unsupported = Path.Combine("Work", "Budget.txt");

        var plan = OpenRecentWorkbookMenuPlanner.Create(
            [
                Entry(" ", now.AddMinutes(4)),
                Entry(missingSupported, now.AddMinutes(3)),
                Entry(unsupported, now.AddMinutes(2)),
                Entry(existingSupported, now.AddMinutes(1))
            ],
            fileExists: path => path == existingSupported || path == unsupported,
            canOpenWorkbook: path => string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase));

        plan.ItemCount.Should().Be(1);
        plan.Items.Should().ContainSingle()
            .Which.Path.Should().Be(existingSupported);
    }

    [Fact]
    public void Create_SortsNewestFirstAndLimitsToTenItems()
    {
        var now = new DateTimeOffset(2026, 6, 8, 10, 0, 0, TimeSpan.Zero);
        var entries = Enumerable.Range(0, 12)
            .Select(index => Entry(Path.Combine("Work", $"Book{index}.fxl"), now.AddMinutes(index)))
            .ToArray();

        var plan = OpenRecentWorkbookMenuPlanner.Create(
            entries,
            fileExists: _ => true,
            canOpenWorkbook: _ => true);

        plan.ItemCount.Should().Be(10);
        plan.Items.Select(item => Path.GetFileName(item.Path))
            .Should()
            .Equal(
                "Book11.fxl",
                "Book10.fxl",
                "Book9.fxl",
                "Book8.fxl",
                "Book7.fxl",
                "Book6.fxl",
                "Book5.fxl",
                "Book4.fxl",
                "Book3.fxl",
                "Book2.fxl");
    }

    [Fact]
    public void Create_FormatsNativeMenuHeadersFromFileAndDirectory()
    {
        var path = Path.Combine("Users", "quinn", "Documents", "Budget.fxl");

        var plan = OpenRecentWorkbookMenuPlanner.Create(
            [Entry(path, DateTimeOffset.UtcNow)],
            fileExists: _ => true,
            canOpenWorkbook: _ => true);

        plan.Items.Should().ContainSingle()
            .Which.Header.Should().Be($"Budget.fxl - {Path.Combine("Users", "quinn", "Documents")}");
    }

    [Fact]
    public void Create_UsesResolvedOpenPathForExistenceHeaderAndDuplicates()
    {
        var now = new DateTimeOffset(2026, 6, 8, 10, 30, 0, TimeSpan.Zero);
        var normalizedPath = "/Users/quinn/Work/Budget 2026.fxl";
        var newerIdentity = new WorkbookFileAccessIdentity(
            normalizedPath,
            "macos-security-scoped-bookmark",
            "newer-token");
        var olderIdentity = new WorkbookFileAccessIdentity(
            normalizedPath,
            "macos-security-scoped-bookmark",
            "older-token");

        var plan = OpenRecentWorkbookMenuPlanner.Create(
            [
                Entry("file:///Users/quinn/Work/Budget%202026.fxl", now.AddMinutes(1), newerIdentity),
                Entry(normalizedPath, now, olderIdentity)
            ],
            fileExists: path => path == normalizedPath,
            resolveOpenWorkbookPath: path => LocalFilePath.TryNormalize(path, out var normalized) ? normalized : null);

        plan.Items.Should().ContainSingle();
        plan.Items[0].Path.Should().Be(normalizedPath);
        plan.Items[0].Header.Should().Be($"Budget 2026.fxl - {Path.GetDirectoryName(normalizedPath)}");
        plan.Items[0].LastOpened.Should().Be(now.AddMinutes(1));
        var plannedIdentity = plan.Items[0].FileAccessIdentity;
        plannedIdentity.Should().NotBeNull();
        plannedIdentity!.LocalPath.Should().Be(normalizedPath);
        plannedIdentity.BookmarkPayload.Should().Be("newer-token");
    }

    [Fact]
    public void FormatHeader_UsesRawPathWhenNoFileNameCanBeDerived()
    {
        OpenRecentWorkbookMenuPlanner.FormatHeader("")
            .Should()
            .Be("");
    }

    private static RecentFileEntry Entry(
        string path,
        DateTimeOffset lastOpened,
        WorkbookFileAccessIdentity? fileAccessIdentity = null) =>
        new()
        {
            Path = path,
            LastOpened = lastOpened,
            FileAccessIdentity = fileAccessIdentity,
        };
}
