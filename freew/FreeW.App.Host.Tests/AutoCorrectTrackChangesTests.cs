using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Covers freew-autocorrect F1: AutoCorrect/AutoFormat as-you-type corrections (<c>TryAutoCorrect</c>) must
/// be recorded as tracked changes when Track Changes is on, exactly like ordinary typed text
/// (<c>TryApplyBodyTextInput</c>/<c>DocumentBodyTextInput</c>). Drives real keystrokes through
/// <see cref="DocumentView.SimulateTypeText"/> (the same path <c>OnPreviewTextInput</c> uses) and reads back
/// the committed model, mirroring the empirical probe that confirmed the defect.
/// </summary>
public sealed class AutoCorrectTrackChangesTests
{
    private static DocumentView NewEditor()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph());
        var view = new DocumentView { AutoFormatOptions = AutoFormatOptions.Default };
        view.LoadModel(doc);
        view.CaretPosition = view.Document.ContentStart.GetInsertionPosition(LogicalDirection.Forward)
            ?? view.Document.ContentStart;
        return view;
    }

    [StaFact]
    public void TrackChanges_MarksSmartQuoteAutoCorrectionAsInsertedRevision()
    {
        var view = NewEditor();
        view.RevisionAuthor = "Ada Reviewer";
        view.TrackChangesEnabled = true;

        // A lone straight quote at the paragraph start: AutoFormat replaces it in place with an opening
        // curly quote (DeleteBefore = 0, Insert = "“") -- the same single-character correction the
        // finding's probe used.
        view.SimulateTypeText("\"");
        view.CommitToModel();

        var paragraph = (Paragraph)view.Model.Blocks[0];
        var run = paragraph.Runs.Single(r => r.Text.Length > 0);
        run.Text.Should().Be("“");
        run.Revision.Should().Be(RevisionKind.Inserted,
            "the AutoFormat correction was typed while Track Changes was on, so it must be marked exactly like an ordinary typed character");
        run.RevisionAuthor.Should().Be("Ada Reviewer");
    }

    [StaFact]
    public void TrackChanges_RejectAllRemovesTheAutoCorrectedText()
    {
        var view = NewEditor();
        view.RevisionAuthor = "Ada Reviewer";
        view.TrackChangesEnabled = true;

        view.SimulateTypeText("\"");
        view.CommitToModel();
        ((Paragraph)view.Model.Blocks[0]).Runs.Single(r => r.Text.Length > 0).Text.Should().Be("“");

        // Review > Reject All Changes must discard text typed while Track Changes was on -- including the
        // AutoCorrect/AutoFormat substitution, not just the raw keystrokes either side of it.
        view.RejectAllRevisions();

        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.PlainText.Should().BeEmpty(
            "the smart-quote correction was a pure tracked insertion with nothing else typed, so rejecting all changes must leave the paragraph empty");
    }

    [StaFact]
    public void TrackChangesDisabled_AutoCorrectionStaysUntracked()
    {
        // Sibling/no-regression case: with Track Changes off, the correction must keep behaving exactly as
        // before -- applied, but carrying no revision mark.
        var view = NewEditor();
        view.TrackChangesEnabled = false;

        view.SimulateTypeText("\"");
        view.CommitToModel();

        var paragraph = (Paragraph)view.Model.Blocks[0];
        var run = paragraph.Runs.Single(r => r.Text.Length > 0);
        run.Text.Should().Be("“");
        run.Revision.Should().Be(RevisionKind.None);
    }
}
