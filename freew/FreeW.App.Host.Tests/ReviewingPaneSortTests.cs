using System.Collections.Generic;
using System.Linq;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Pure (non-STA) tests for the Reviewing Pane sort comparator
/// (<see cref="ReviewRevisionSortPlanner"/>). Sort operates on <see cref="RevisionEntry"/> values and
/// is independent of any WPF surface.
/// </summary>
public sealed class ReviewingPaneSortTests
{
    private static Paragraph MakeParagraph() => new Paragraph();
    private static Run MakeRun(string text) => new Run(text);

    private static RevisionEntry Entry(
        int blockIndex,
        RevisionEntryKind kind,
        string? author,
        string? dateXml,
        string text = "x")
    {
        var para = MakeParagraph();
        var run = MakeRun(text);
        return new RevisionEntry(blockIndex, kind, author, dateXml, text, para, run);
    }

    private static IReadOnlyList<RevisionEntry> MakeEntries() =>
    [
        Entry(2, RevisionEntryKind.Deletion,   "Carol", "2026-06-20T09:00:00Z"),
        Entry(0, RevisionEntryKind.Insertion,  "Alice", "2026-06-19T10:00:00Z"),
        Entry(1, RevisionEntryKind.Formatting, "Bob",   "2026-06-19T11:00:00Z"),
        Entry(3, RevisionEntryKind.Insertion,  "Alice", "2026-06-21T08:00:00Z"),
    ];

    [Fact]
    public void Sort_Sequence_ReturnsSameOrder()
    {
        var entries = MakeEntries();
        var sorted = ReviewRevisionSortPlanner.Sort(entries, ReviewRevisionSortOrder.Sequence);
        Assert.Same(entries, sorted); // no-copy for Sequence
        Assert.Equal([2, 0, 1, 3], sorted.Select(e => e.BlockIndex).ToArray());
    }

    [Fact]
    public void Sort_Author_OrdersAlphabeticallyThenByBlock()
    {
        var sorted = ReviewRevisionSortPlanner.Sort(MakeEntries(), ReviewRevisionSortOrder.Author);
        // Alice(0), Alice(3), Bob(1), Carol(2)
        sorted.Select(e => e.Author).Should().Equal("Alice", "Alice", "Bob", "Carol");
        // Within same author, stable by block index
        Assert.Equal([0, 3], sorted.Where(e => e.Author == "Alice").Select(e => e.BlockIndex).ToArray());
    }

    [Fact]
    public void Sort_Kind_OrdersByKindEnumThenBlock()
    {
        // Enum order: Insertion=0, Deletion=1, Formatting=2
        var sorted = ReviewRevisionSortPlanner.Sort(MakeEntries(), ReviewRevisionSortOrder.Kind);
        Assert.Equal(
            [RevisionEntryKind.Insertion, RevisionEntryKind.Insertion, RevisionEntryKind.Deletion, RevisionEntryKind.Formatting],
            sorted.Select(e => e.Kind).ToArray());
        // Insertions are at block 0 and 3 → block 0 first
        Assert.Equal([0, 3], sorted.Where(e => e.Kind == RevisionEntryKind.Insertion).Select(e => e.BlockIndex).ToArray());
    }

    [Fact]
    public void Sort_Date_OrdersChronologicallyThenByBlock()
    {
        var sorted = ReviewRevisionSortPlanner.Sort(MakeEntries(), ReviewRevisionSortOrder.Date);
        // Lexicographic ISO-8601 sort (which equals chronological for zero-padded dates)
        sorted.Select(e => e.DateXml).Should().Equal(
            "2026-06-19T10:00:00Z", "2026-06-19T11:00:00Z", "2026-06-20T09:00:00Z", "2026-06-21T08:00:00Z");
    }

    [Fact]
    public void Sort_NullDateXml_SortsFirst()
    {
        var entries = new List<RevisionEntry>
        {
            Entry(1, RevisionEntryKind.Insertion, "Bob",  "2026-06-19T10:00:00Z"),
            Entry(0, RevisionEntryKind.Insertion, "Alice", null),
        };
        var sorted = ReviewRevisionSortPlanner.Sort(entries, ReviewRevisionSortOrder.Date);
        Assert.Null(sorted[0].DateXml); // null sorts before any real date string
    }

    [Fact]
    public void Sort_EmptyList_ReturnsEmpty()
    {
        var result = ReviewRevisionSortPlanner.Sort([], ReviewRevisionSortOrder.Author);
        Assert.Empty(result);
    }

    [Fact]
    public void Sort_SingleEntry_ReturnsSingleEntry()
    {
        var entry = Entry(0, RevisionEntryKind.Insertion, "Alice", "2026-01-01T00:00:00Z");
        var result = ReviewRevisionSortPlanner.Sort([entry], ReviewRevisionSortOrder.Author);
        Assert.Single(result);
        Assert.Same(entry, result[0]);
    }
}
