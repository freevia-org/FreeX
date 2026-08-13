using System.Diagnostics;
using FluentAssertions;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Comments;

public sealed class CommentNavigationPlannerTests
{
    [Fact]
    public void OrderedComments_SortsByRowThenColumn()
    {
        var sheetId = SheetId.New();
        var comments = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 4, 1)] = "C",
            [new(sheetId, 2, 3)] = "B",
            [new(sheetId, 2, 1)] = "A"
        };

        CommentNavigationPlanner.OrderedCommentAddresses(comments)
            .Should()
            .Equal(new CellAddress(sheetId, 2, 1), new CellAddress(sheetId, 2, 3), new CellAddress(sheetId, 4, 1));
    }

    [Fact]
    public void OrderedComments_IncludesThreadedComments()
    {
        var sheetId = SheetId.New();
        var comments = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 4, 1)] = "Note"
        };
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [new(sheetId, 2, 1)] = new("Thread"),
            [new(sheetId, 3, 2)] = new("Discussion")
        };

        CommentNavigationPlanner.OrderedCommentAddresses(comments, threadedComments)
            .Should()
            .Equal(new CellAddress(sheetId, 2, 1), new CellAddress(sheetId, 3, 2), new CellAddress(sheetId, 4, 1));
    }

    [Fact]
    public void OrderedThreadedComments_SortsByRowThenColumnWithoutNotes()
    {
        var sheetId = SheetId.New();
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [new(sheetId, 4, 1)] = new("Later"),
            [new(sheetId, 2, 3)] = new("Middle"),
            [new(sheetId, 2, 1)] = new("First")
        };

        CommentNavigationPlanner.OrderedThreadedCommentAddresses(threadedComments)
            .Should()
            .Equal(new CellAddress(sheetId, 2, 1), new CellAddress(sheetId, 2, 3), new CellAddress(sheetId, 4, 1));
    }

    [Fact]
    public void OrderedNotes_SortsByRowThenColumnWithoutThreadedComments()
    {
        var sheetId = SheetId.New();
        var notes = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 4, 1)] = "Later",
            [new(sheetId, 2, 3)] = "Middle",
            [new(sheetId, 2, 1)] = "First"
        };

        CommentNavigationPlanner.OrderedNoteAddresses(notes)
            .Should()
            .Equal(new CellAddress(sheetId, 2, 1), new CellAddress(sheetId, 2, 3), new CellAddress(sheetId, 4, 1));
    }

    [Fact]
    public void CreateThreadedCommentRows_SortsAddressesAndFormatsThreadText()
    {
        var sheetId = SheetId.New();
        var firstAddress = new CellAddress(sheetId, 1, 1);
        var laterAddress = new CellAddress(sheetId, 3, 2);
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [laterAddress] = new("Later", "Quinn"),
            [firstAddress] = new("First")
            {
                Replies = [new CommentReply("Reply", "Reviewer")],
                IsResolved = true,
            },
        };

        CommentNavigationPlanner.CreateThreadedCommentRows(threadedComments)
            .Should()
            .Equal(
                new CommentListRowPlan(firstAddress, "A1", "FreeX: First | Reviewer: Reply | Resolved"),
                new CommentListRowPlan(laterAddress, "B3", "Quinn: Later"));
    }

    [Fact]
    public void CreateNoteRows_SortsAddressesAndKeepsPlainText()
    {
        var sheetId = SheetId.New();
        var firstAddress = new CellAddress(sheetId, 1, 1);
        var laterAddress = new CellAddress(sheetId, 3, 2);
        var notes = new Dictionary<CellAddress, string>
        {
            [laterAddress] = "Later note",
            [firstAddress] = "First note",
        };

        CommentNavigationPlanner.CreateNoteRows(notes)
            .Should()
            .Equal(
                new CommentListRowPlan(firstAddress, "A1", "First note"),
                new CommentListRowPlan(laterAddress, "B3", "Later note"));
    }

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
        var source = ReadPlannerSource();

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
        // DefaultTests runs many projects concurrently; keep the guard tight enough to catch a
        // linear-search regression without failing on scheduler contention from unrelated lanes.
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2_000);
    }

    [Fact]
    public void FormatCommentList_UsesA1AddressesInSortedOrder()
    {
        var sheetId = SheetId.New();
        var comments = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 3, 2)] = "Later",
            [new(sheetId, 1, 1)] = "First"
        };

        CommentNavigationPlanner.FormatCommentList(comments)
            .Should()
            .Be(string.Join(Environment.NewLine, "A1: First", "B3: Later"));
    }

    [Fact]
    public void FormatCommentList_IncludesThreadedComments()
    {
        var sheetId = SheetId.New();
        var comments = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 3, 2)] = "Later note"
        };
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [new(sheetId, 1, 1)] = new("First thread")
        };

        CommentNavigationPlanner.FormatCommentList(comments, threadedComments)
            .Should()
            .Be(string.Join(Environment.NewLine, "A1: FreeX: First thread", "B3: Later note"));
    }

    [Fact]
    public void FormatThreadedCommentList_ExcludesNotes()
    {
        var sheetId = SheetId.New();
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [new(sheetId, 1, 1)] = new("First thread"),
            [new(sheetId, 3, 2)] = new("Later thread", "Quinn")
        };

        CommentNavigationPlanner.FormatThreadedCommentList(threadedComments)
            .Should()
            .Be(string.Join(Environment.NewLine, "A1: FreeX: First thread", "B3: Quinn: Later thread"));
    }

    [Fact]
    public void FormatNoteList_ExcludesThreadedComments()
    {
        var sheetId = SheetId.New();
        var notes = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 3, 2)] = "Later note",
            [new(sheetId, 1, 1)] = "First note"
        };

        CommentNavigationPlanner.FormatNoteList(notes)
            .Should()
            .Be(string.Join(Environment.NewLine, "A1: First note", "B3: Later note"));
    }

    [Fact]
    public void FormatCommentList_IncludesThreadedAuthorsRepliesAndResolvedState()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 1, 1);
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [address] = new("Please review total", "Quinn")
            {
                Replies =
                [
                    new CommentReply("Updated", "Codex"),
                    new CommentReply("Looks good", "Quinn")
                ],
                IsResolved = true
            }
        };

        CommentNavigationPlanner.FormatCommentList(new Dictionary<CellAddress, string>(), threadedComments)
            .Should()
            .Be("A1: Quinn: Please review total | Codex: Updated | Quinn: Looks good | Resolved");
    }

    [Fact]
    public void FormatCommentList_IncludesThreadedCreatedTimestampsWhenAvailable()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 1, 1);
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [address] = new("Please review total", "Quinn")
            {
                CreatedAtUtc = new DateTimeOffset(2026, 5, 31, 8, 0, 0, TimeSpan.Zero),
                Replies =
                [
                    new CommentReply("Updated", "Codex")
                    {
                        CreatedAtUtc = new DateTimeOffset(2026, 5, 31, 8, 5, 0, TimeSpan.Zero)
                    }
                ]
            }
        };

        CommentNavigationPlanner.FormatCommentList(new Dictionary<CellAddress, string>(), threadedComments)
            .Should()
            .Be("A1: Quinn (2026-05-31 08:00 UTC): Please review total | Codex (2026-05-31 08:05 UTC): Updated");
    }

    [Fact]
    public void FormatCommentList_ShowsNoteAndThreadWhenCellHasBoth()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 2, 2);
        var comments = new Dictionary<CellAddress, string>
        {
            [address] = "Local note"
        };
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [address] = new("Threaded reply", "Codex")
        };

        CommentNavigationPlanner.FormatCommentList(comments, threadedComments)
            .Should()
            .Be(string.Join(Environment.NewLine, "B2: Note: Local note", "B2: Threaded: Codex: Threaded reply"));
    }

    [Fact]
    public void GetDefaultCommentText_ReturnsExistingCommentForSelectedCell()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 2, 2);
        var comments = new Dictionary<CellAddress, string>
        {
            [address] = "Existing note"
        };

        CommentNavigationPlanner.GetDefaultCommentText(comments, address)
            .Should()
            .Be("Existing note");
        CommentNavigationPlanner.GetDefaultCommentText(comments, new CellAddress(sheetId, 3, 3))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void FormatCellCommentPreview_ShowsNotesAndThreadedCommentsForHoveredCell()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 2, 2);
        var comments = new Dictionary<CellAddress, string>
        {
            [address] = "Local note"
        };
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [address] = new("Please review total", "Quinn")
            {
                Replies = [new CommentReply("Updated", "Codex")]
            }
        };

        CommentNavigationPlanner.FormatCellCommentPreview(comments, threadedComments, address)
            .Should()
            .Be(string.Join(Environment.NewLine, "Note: Local note", "Quinn: Please review total | Codex: Updated"));
        CommentNavigationPlanner.FormatCellCommentPreview(comments, threadedComments, new CellAddress(sheetId, 3, 3))
            .Should()
            .BeNull();
    }

    [BenchmarkFact]
    public void Benchmark_FormatCellCommentPreview_NoCommentCells_ReportsTiming()
    {
        var sheetId = SheetId.New();
        var comments = new Dictionary<CellAddress, string>();
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>();
        const int iterations = 200_000;

        for (var index = 0; index < 1_000; index++)
        {
            var address = new CellAddress(sheetId, (uint)(index + 1), 1);
            CommentNavigationPlanner.FormatCellCommentPreview(comments, threadedComments, address)
                .Should()
                .BeNull();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        string? preview = null;
        for (var index = 0; index < iterations; index++)
        {
            var address = new CellAddress(sheetId, (uint)((index % 10_000) + 1), 1);
            preview = CommentNavigationPlanner.FormatCellCommentPreview(comments, threadedComments, address);
        }

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Console.WriteLine(
            $"PERF COMMENT_PREVIEW_EMPTY steps={iterations} total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes:N0}");

        preview.Should().BeNull();
    }

    private static string ReadPlannerSource()
    {
        var path = Path.Combine(
            RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation", "Comments"),
            "CommentNavigationPlanner.cs");
        return File.ReadAllText(path);
    }
}
