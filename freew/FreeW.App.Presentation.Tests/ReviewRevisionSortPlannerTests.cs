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
    public void Options_are_the_single_ordered_menu_catalog()
    {
        ReviewRevisionSortPlanner.Options
            .Select(option => (option.Order, option.Label))
            .Should().Equal(
                (ReviewRevisionSortOrder.Sequence, "By Sequence"),
                (ReviewRevisionSortOrder.Author, "By Author"),
                (ReviewRevisionSortOrder.Kind, "By Type"),
                (ReviewRevisionSortOrder.Date, "By Date"));

        foreach (var option in ReviewRevisionSortPlanner.Options)
        {
            ReviewRevisionSortPlanner.IndexOf(option.Order)
                .Should().Be(Array.IndexOf(ReviewRevisionSortPlanner.Options.ToArray(), option));
        }
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

    [Fact]
    public void Sort_preserves_WPF_null_empty_and_single_entry_contracts()
    {
        var nullDate = Entry(0, RevisionEntryKind.Insertion, "Alice", "missing", null);
        var dated = Entry(1, RevisionEntryKind.Insertion, "Bob", "dated", "2026-06-19T10:00:00Z");

        ReviewRevisionSortPlanner.Sort([dated, nullDate], ReviewRevisionSortOrder.Date)
            .Should().Equal(nullDate, dated);
        ReviewRevisionSortPlanner.Sort([], ReviewRevisionSortOrder.Author).Should().BeEmpty();
        ReviewRevisionSortPlanner.Sort([dated], ReviewRevisionSortOrder.Author)
            .Should().ContainSingle().Which.Should().BeSameAs(dated);
    }

    [Fact]
    public void IndexOf_rejects_unknown_order()
    {
        FluentActions.Invoking(() => ReviewRevisionSortPlanner.IndexOf((ReviewRevisionSortOrder)99))
            .Should().Throw<ArgumentOutOfRangeException>();
    }
}
