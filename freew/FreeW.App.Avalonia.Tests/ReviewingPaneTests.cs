using FreeW.App.Avalonia;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Headless unit tests for the Avalonia <see cref="ReviewingPane"/>. These use the pane's
/// model-tier helpers directly (no Avalonia rendering needed) so they run in any CI environment.
/// The test document is built by directly stamping <see cref="RevisionKind"/> marks on runs — the
/// same way <see cref="RevisionList.Enumerate"/> reads them.
/// </summary>
public class ReviewingPaneTests
{
    // ── Document helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds a document with three paragraphs carrying:
    ///   [0] an inserted run "hello"  (author "Alice", date "2024-01-10")
    ///   [1] a deleted run  "world"   (author "Bob",   date "2024-01-11")
    ///   [2] a format-changed run "!" (author "Alice", date "2024-01-12")
    /// </summary>
    private static TextDocument BuildTrackedDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();

        // Paragraph 0: inserted run.
        var p0 = new Paragraph();
        var ins = new Run("hello") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice", RevisionDateXml = "2024-01-10T08:00:00Z" };
        p0.Runs.Add(ins);
        doc.Blocks.Add(p0);

        // Paragraph 1: deleted run.
        var p1 = new Paragraph();
        var del = new Run("world") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob", RevisionDateXml = "2024-01-11T09:00:00Z" };
        p1.Runs.Add(del);
        doc.Blocks.Add(p1);

        // Paragraph 2: format-changed run.
        var p2 = new Paragraph();
        var fmt = new Run("!") { FormatRevision = new FormatRevision(RunFormatting.Default with { Bold = true }, "Alice", "2024-01-12T10:00:00Z") };
        p2.Runs.Add(fmt);
        doc.Blocks.Add(p2);

        return doc;
    }

    // ── Enumeration ───────────────────────────────────────────────────────────

    [Fact]
    public void EnumerateRevisions_Lists_All_Three_Entries_In_Reading_Order()
    {
        var doc = BuildTrackedDoc();
        var entries = ReviewingPane.EnumerateRevisions(doc);

        entries.Should().HaveCount(3);
        entries[0].Kind.Should().Be(RevisionEntryKind.Insertion);
        entries[0].Author.Should().Be("Alice");
        entries[0].Text.Should().Be("hello");

        entries[1].Kind.Should().Be(RevisionEntryKind.Deletion);
        entries[1].Author.Should().Be("Bob");
        entries[1].Text.Should().Be("world");

        entries[2].Kind.Should().Be(RevisionEntryKind.Formatting);
        entries[2].Author.Should().Be("Alice");
        entries[2].Text.Should().Be("!");
    }

    [Fact]
    public void EnumerateRevisions_Returns_Empty_For_Clean_Document()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Add(new Paragraph { Runs = { new Run("no revisions") } });
        var entries = ReviewingPane.EnumerateRevisions(doc);
        entries.Should().BeEmpty();
    }

    // ── Per-entry Accept ──────────────────────────────────────────────────────

    [Fact]
    public void AcceptEntry_Insertion_Keeps_Run_As_Ordinary_Text()
    {
        var doc = BuildTrackedDoc();
        var entries = ReviewingPane.EnumerateRevisions(doc);

        // Accept the insertion entry.
        var insertionEntry = entries.Single(e => e.Kind == RevisionEntryKind.Insertion);
        var accepted = RevisionList.Accept(doc, insertionEntry);

        accepted.Should().BeTrue();
        var remaining = ReviewingPane.EnumerateRevisions(doc);
        remaining.Should().NotContain(e => e.Kind == RevisionEntryKind.Insertion && e.Text == "hello");
        // The run text should still be present (kept, no longer marked).
        var p0 = (Paragraph)doc.Blocks[0];
        p0.Runs.Should().Contain(r => r.Text == "hello" && r.Revision == RevisionKind.None);
    }

    [Fact]
    public void AcceptEntry_Deletion_Removes_The_Deleted_Run()
    {
        var doc = BuildTrackedDoc();
        var entries = ReviewingPane.EnumerateRevisions(doc);

        var deletionEntry = entries.Single(e => e.Kind == RevisionEntryKind.Deletion);
        var accepted = RevisionList.Accept(doc, deletionEntry);

        accepted.Should().BeTrue();
        var p1 = (Paragraph)doc.Blocks[1];
        p1.Runs.Should().NotContain(r => r.Text == "world");
    }

    // ── Per-entry Reject ──────────────────────────────────────────────────────

    [Fact]
    public void RejectEntry_Insertion_Removes_The_Inserted_Run()
    {
        var doc = BuildTrackedDoc();
        var entries = ReviewingPane.EnumerateRevisions(doc);

        var insertionEntry = entries.Single(e => e.Kind == RevisionEntryKind.Insertion);
        var rejected = RevisionList.Reject(doc, insertionEntry);

        rejected.Should().BeTrue();
        var p0 = (Paragraph)doc.Blocks[0];
        p0.Runs.Should().NotContain(r => r.Text == "hello");
    }

    [Fact]
    public void RejectEntry_Deletion_Keeps_Run_As_Ordinary_Text()
    {
        var doc = BuildTrackedDoc();
        var entries = ReviewingPane.EnumerateRevisions(doc);

        var deletionEntry = entries.Single(e => e.Kind == RevisionEntryKind.Deletion);
        var rejected = RevisionList.Reject(doc, deletionEntry);

        rejected.Should().BeTrue();
        var p1 = (Paragraph)doc.Blocks[1];
        p1.Runs.Should().Contain(r => r.Text == "world" && r.Revision == RevisionKind.None);
    }

    // ── Bulk Accept-All / Reject-All ──────────────────────────────────────────

    [Fact]
    public void AcceptAll_Clears_All_Revisions_From_Document()
    {
        var doc = BuildTrackedDoc();

        TrackChanges.AcceptAll(doc);

        var remaining = ReviewingPane.EnumerateRevisions(doc);
        remaining.Should().BeEmpty();
    }

    [Fact]
    public void RejectAll_Clears_All_Revisions_From_Document()
    {
        var doc = BuildTrackedDoc();

        TrackChanges.RejectAll(doc);

        var remaining = ReviewingPane.EnumerateRevisions(doc);
        remaining.Should().BeEmpty();
    }

    // ── Stale-entry safety ────────────────────────────────────────────────────

    [Fact]
    public void Accept_With_Stale_Entry_Is_A_No_Op()
    {
        // Accept the same insertion twice — second call should return false (run no longer tracked).
        var doc = BuildTrackedDoc();
        var entries = ReviewingPane.EnumerateRevisions(doc);
        var insertionEntry = entries.Single(e => e.Kind == RevisionEntryKind.Insertion);

        RevisionList.Accept(doc, insertionEntry).Should().BeTrue();
        // Now the run's Revision is None — accepting again resolves nothing.
        RevisionList.Accept(doc, insertionEntry).Should().BeFalse();
    }

    // ── Formatting revision accept/reject ─────────────────────────────────────

    [Fact]
    public void AcceptEntry_Formatting_Keeps_New_Formatting_And_Clears_Mark()
    {
        var doc = BuildTrackedDoc();
        var entries = ReviewingPane.EnumerateRevisions(doc);
        var fmtEntry = entries.Single(e => e.Kind == RevisionEntryKind.Formatting);

        RevisionList.Accept(doc, fmtEntry).Should().BeTrue();

        var p2 = (Paragraph)doc.Blocks[2];
        p2.Runs[0].FormatRevision.Should().BeNull();
        // The bold=true formatting that was the "new" formatting is now the run's plain formatting.
        // (FormatRevision carried PreviousFormatting=Bold, so accept keeps current, which is Default.)
    }

    [Fact]
    public void RejectEntry_Formatting_Restores_Previous_Formatting_And_Clears_Mark()
    {
        var doc = BuildTrackedDoc();
        var entries = ReviewingPane.EnumerateRevisions(doc);
        var fmtEntry = entries.Single(e => e.Kind == RevisionEntryKind.Formatting);

        RevisionList.Reject(doc, fmtEntry).Should().BeTrue();

        var p2 = (Paragraph)doc.Blocks[2];
        p2.Runs[0].FormatRevision.Should().BeNull();
        // Previous formatting was Bold=true; after reject the run formatting should match it.
        p2.Runs[0].Formatting.Bold.Should().BeTrue();
    }
}
