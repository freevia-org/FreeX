using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Services.Tests;

public sealed class OpenRecentWorkbookMenuPlannerTests
{
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
