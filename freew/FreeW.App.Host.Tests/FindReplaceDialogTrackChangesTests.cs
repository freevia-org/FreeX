using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Find &amp; Replace must route replacements through the same tracked-edit path ordinary typing uses
/// (<see cref="DocumentView.InsertText"/>) instead of assigning <c>Selection.Text</c> directly, so
/// Track Changes records every Replace/Replace All edit as a revision and Restrict Editing's
/// TrackChangesOnly protection (which permits body edits but requires them to be tracked) is honoured
/// rather than silently bypassed. See freew/FreeW.App.Host/FindReplaceDialog.cs Replace()/ReplaceAll().
/// </summary>
public sealed class FindReplaceDialogTrackChangesTests
{
    private static DocumentView BuildView(string text)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static DocumentView BuildProtectedView(string text, ProtectionMode mode)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(text));
        doc.Protection = new ProtectionSettings(mode);

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static Paragraph ParagraphOf(DocumentView view)
    {
        view.CommitToModel();
        return (Paragraph)view.Model.Blocks[0];
    }

    [StaFact]
    public void Replace_WithTrackChangesOn_RecordsInsertedAndDeletedRevisionsInsteadOfSilentRewrite()
    {
        var view = BuildView("Hello cat world");
        view.RevisionAuthor = "Ada Reviewer";
        view.TrackChangesEnabled = true;
        // Select "cat" (offsets 6..9) directly, bypassing the dialog's own Find so the test isolates
        // the Replace path itself.
        view.SetSelectionRangeForTest(0, 6, 0, 9);

        var dialog = new FindReplaceDialog(null!, view, FindReplaceDialogOpenMode.Replace);
        try
        {
            dialog.Show();
            dialog.SetFindTextForTest("cat");
            dialog.SetReplaceTextForTest("dog");

            dialog.ReplaceForTest();

            var paragraph = ParagraphOf(view);
            // Track Changes keeps the original "cat" in the document (struck through) right after the
            // newly inserted "dog" -- it is not silently overwritten.
            paragraph.PlainText.Should().Be("Hello dogcat world");
            var inserted = paragraph.Runs.Single(r => r.Text == "dog");
            inserted.Revision.Should().Be(RevisionKind.Inserted);
            inserted.RevisionAuthor.Should().Be("Ada Reviewer");
            var deleted = paragraph.Runs.Single(r => r.Text == "cat");
            deleted.Revision.Should().Be(RevisionKind.Deleted);
        }
        finally
        {
            dialog.Close();
        }
    }

    [StaFact]
    public void Replace_WithTrackChangesOff_StillRewritesTextDirectlyNoRegression()
    {
        var view = BuildView("Hello cat world");
        view.TrackChangesEnabled.Should().BeFalse();
        view.SetSelectionRangeForTest(0, 6, 0, 9);

        var dialog = new FindReplaceDialog(null!, view, FindReplaceDialogOpenMode.Replace);
        try
        {
            dialog.Show();
            dialog.SetFindTextForTest("cat");
            dialog.SetReplaceTextForTest("dog");

            dialog.ReplaceForTest();

            var paragraph = ParagraphOf(view);
            paragraph.PlainText.Should().Be("Hello dog world");
            paragraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
        }
        finally
        {
            dialog.Close();
        }
    }

    [StaFact]
    public void ReplaceAll_WithTrackChangesOn_RecordsARevisionPairForEveryOccurrence()
    {
        var view = BuildView("cat cat cat");
        view.RevisionAuthor = "Ada Reviewer";
        view.TrackChangesEnabled = true;

        var dialog = new FindReplaceDialog(null!, view, FindReplaceDialogOpenMode.Replace);
        try
        {
            dialog.Show();
            dialog.SetFindTextForTest("cat");
            dialog.SetReplaceTextForTest("dog");

            dialog.ReplaceAllForTest();

            var paragraph = ParagraphOf(view);
            paragraph.Runs.Count(r => r.Text == "dog" && r.Revision == RevisionKind.Inserted).Should().Be(3);
            paragraph.Runs.Count(r => r.Text == "cat" && r.Revision == RevisionKind.Deleted).Should().Be(3);
            dialog.StatusForTest.Should().Contain("3");
        }
        finally
        {
            dialog.Close();
        }
    }

    [StaFact]
    public void ReplaceAll_WithTrackChangesOff_RewritesAllOccurrencesNoRegression()
    {
        var view = BuildView("cat cat cat");

        var dialog = new FindReplaceDialog(null!, view, FindReplaceDialogOpenMode.Replace);
        try
        {
            dialog.Show();
            dialog.SetFindTextForTest("cat");
            dialog.SetReplaceTextForTest("dog");

            dialog.ReplaceAllForTest();

            var paragraph = ParagraphOf(view);
            paragraph.PlainText.Should().Be("dog dog dog");
            paragraph.Runs.Should().OnlyContain(r => r.Revision == RevisionKind.None);
        }
        finally
        {
            dialog.Close();
        }
    }

    /// <summary>
    /// The scenario the finding actually named: a document PROTECTED for tracked changes only. Unlike
    /// ReadOnly it stays editable, so Replace All proceeds -- and precisely because it proceeds, every
    /// replacement has to be recorded as a revision, or the protection has been silently defeated
    /// rather than merely bypassed. The document is loaded already protected (ApplyProtection, which
    /// LoadModel runs, is what turns tracking on for this mode), matching how a real restricted .docx
    /// arrives, and nothing here sets TrackChangesEnabled by hand -- the protection alone must force it.
    /// The other tests here toggle TrackChangesEnabled directly, so none of them would notice if the
    /// protection-driven path stopped forcing tracking.
    /// </summary>
    [StaFact]
    public void ReplaceAll_WhenTrackChangesOnlyProtected_StillRecordsEveryReplacementAsARevision()
    {
        var view = BuildProtectedView("cat cat cat", ProtectionMode.TrackChangesOnly);
        view.RevisionAuthor = "Ada Reviewer";
        view.IsReadOnly.Should().BeFalse("TrackChangesOnly permits body edits -- it only requires them to be tracked");
        view.TrackChangesEnabled.Should().BeTrue("the protection mode alone must force tracking on, with no explicit opt-in");

        var dialog = new FindReplaceDialog(null!, view, FindReplaceDialogOpenMode.Replace);
        try
        {
            dialog.Show();
            dialog.SetFindTextForTest("cat");
            dialog.SetReplaceTextForTest("dog");

            dialog.ReplaceAllForTest();

            var paragraph = ParagraphOf(view);
            paragraph.Runs.Count(r => r.Text == "dog" && r.Revision == RevisionKind.Inserted).Should().Be(3);
            paragraph.Runs.Count(r => r.Text == "cat" && r.Revision == RevisionKind.Deleted).Should().Be(3);
        }
        finally
        {
            dialog.Close();
        }
    }

    [StaFact]
    public void ReplaceAll_WhenReadOnlyProtected_MakesNoChangeAndReportsZero()
    {
        var view = BuildView("cat cat cat");
        view.SetProtection(ProtectionMode.ReadOnly);
        view.IsReadOnly.Should().BeTrue();

        var dialog = new FindReplaceDialog(null!, view, FindReplaceDialogOpenMode.Replace);
        try
        {
            dialog.Show();
            dialog.SetFindTextForTest("cat");
            dialog.SetReplaceTextForTest("dog");

            dialog.ReplaceAllForTest();

            var paragraph = ParagraphOf(view);
            paragraph.PlainText.Should().Be("cat cat cat");
            // Zero replacements happened (blocked by protection), so the status reads the same as a
            // genuine no-match -- it must not claim any occurrence was replaced.
            dialog.StatusForTest.Should().NotContain("Replaced");
        }
        finally
        {
            dialog.Close();
        }
    }
}
