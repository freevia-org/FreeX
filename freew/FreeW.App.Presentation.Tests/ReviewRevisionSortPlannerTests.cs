using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewRevisionSortPlannerTests
{
    private static RevisionEntry Entry(
        int blockIndex,
        RevisionEntryKind kind,
        string author,
        string text,
        string? dateXml)
    {
        var paragraph = new Paragraph();
        var run = new Run(text);
        paragraph.Runs.Add(run);
        return new RevisionEntry(blockIndex, kind, author, dateXml, text, paragraph, run);
    }

    [Fact]
    public void Sort_orders_are_stable_and_leave_sequence_untouched()
    {
        var entries = new[]
        {
            Entry(3, RevisionEntryKind.Insertion, "Carol", "c", "2026-03-01"),
            Entry(1, RevisionEntryKind.Deletion, "Alice", "a", null),
            Entry(2, RevisionEntryKind.Insertion, "Alice", "b", "2026-02-01"),
        };

        ReviewRevisionSortPlanner.Sort(entries, ReviewRevisionSortOrder.Sequence).Should().BeSameAs(entries);
        ReviewRevisionSortPlanner.Sort(entries, ReviewRevisionSortOrder.Author)
            .Select(entry => entry.Text).Should().Equal("a", "b", "c");
        ReviewRevisionSortPlanner.Sort(entries, ReviewRevisionSortOrder.Kind)
            .Select(entry => entry.Kind).Should().Equal(
                RevisionEntryKind.Insertion, RevisionEntryKind.Insertion, RevisionEntryKind.Deletion);
        ReviewRevisionSortPlanner.Sort(entries, ReviewRevisionSortOrder.Date)
            .Select(entry => entry.Text).Should().Equal("a", "b", "c");
    }
}
