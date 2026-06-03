using System.Diagnostics;
using System.IO;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class CommentNavigationPlannerTests
{
    [Fact]
    public void NextComment_WrapsForwardAndBackward()
    {
        var sheetId = SheetId.New();
        var comments = new[]
        {
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 4, 1)
        };

        CommentNavigationPlanner.FindNext(comments, new CellAddress(sheetId, 2, 1), previous: false)
            .Should()
            .Be(new CellAddress(sheetId, 4, 1));
        CommentNavigationPlanner.FindNext(comments, new CellAddress(sheetId, 4, 1), previous: false)
            .Should()
            .Be(new CellAddress(sheetId, 1, 1));
        CommentNavigationPlanner.FindNext(comments, new CellAddress(sheetId, 1, 1), previous: true)
            .Should()
            .Be(new CellAddress(sheetId, 4, 1));
    }

    [Fact]
    public void NextComment_UsesIndexedLookupForLargeOrderedLists()
    {
        var sheetId = SheetId.New();
        var comments = Enumerable.Range(1, 100_000)
            .Select(index => new CellAddress(sheetId, (uint)index, 1))
            .ToArray();
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "CommentNavigationPlanner.cs"));

        source.Should().Contain("FindFirstAfter");
        source.Should().NotContain("FirstOrDefault(address => address.Row > current.Row");
        source.Should().NotContain("LastOrDefault(address => address.Row < current.Row");

        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 10_000; index++)
        {
            var row = (uint)((index * 37) % comments.Length + 1);
            var current = new CellAddress(sheetId, row, 1);
            CommentNavigationPlanner.FindNext(comments, current, previous: false)
                .Should()
                .Be(row == 100_000 ? comments[0] : new CellAddress(sheetId, row + 1, 1));
            CommentNavigationPlanner.FindNext(comments, current, previous: true)
                .Should()
                .Be(row == 1 ? comments[^1] : new CellAddress(sheetId, row - 1, 1));
        }

        stopwatch.Stop();
        Console.WriteLine($"Comment navigation indexed lookup: {stopwatch.ElapsedMilliseconds}ms for 20000 lookups");
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(500);
    }
}
