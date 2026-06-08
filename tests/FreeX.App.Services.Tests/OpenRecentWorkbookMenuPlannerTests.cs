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
    public void FormatHeader_UsesRawPathWhenNoFileNameCanBeDerived()
    {
        OpenRecentWorkbookMenuPlanner.FormatHeader("")
            .Should()
            .Be("");
    }

    private static RecentFileEntry Entry(string path, DateTimeOffset lastOpened) =>
        new()
        {
            Path = path,
            LastOpened = lastOpened,
        };
}
