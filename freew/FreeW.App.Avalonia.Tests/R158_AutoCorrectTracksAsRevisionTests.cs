using System.Linq;
using System.Threading;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// r158 remediation for the Avalonia half of the AutoCorrect/Track Changes defect.
///
/// <para>
/// An AutoCorrect or AutoFormat replacement is text the user never typed. With Track Changes on it
/// has to be recorded as an insertion like any other edit; otherwise it is invisible to review and
/// survives Review &gt; Reject All Changes, so a document the author believes they reverted still
/// carries a change nobody approved.
/// </para>
///
/// <para>
/// The WPF shell was fixed for this in round 158 and that fixer disclosed, correctly, that this
/// shell still had it: the replacement cells were constructed with the Cell record's default
/// Revision (None) regardless of TrackChangesEnabled.
/// </para>
/// </summary>
public sealed class R158_AutoCorrectTracksAsRevisionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task AutoCorrection_typed_with_track_changes_on_is_recorded_as_an_insertion()
    {
        string? text = null;
        string? afterReject = null;

        await Session.Dispatch(() =>
        {
            var view = NewEditor();
            view.ToggleTrackChanges();
            view.SimulateTextInputForTest("I teh ");

            text = view.Document.PlainText;

            // Reject All is the behaviour that matters, and the only assertion that can tell the
            // two states apart. Asserting merely that SOME run is Inserted passes even when the
            // correction is untracked, because the characters the user really typed are tracked
            // insertions themselves -- an earlier draft of this test made exactly that mistake and
            // passed with the production fix reverted.
            view.RejectAllRevisions();
            afterReject = view.Document.PlainText;
        }, CancellationToken.None);

        text.Should().Be("I the ", "the correction itself must still happen");
        afterReject.Should().BeEmpty(
            "everything here was typed or inserted with tracking on, so Reject All must leave the "
            + "paragraph empty; if the correction was not recorded as a revision it survives review "
            + "with no trace it was ever made");
    }

    /// <summary>
    /// Sibling no-regression: with tracking OFF the same gesture must produce no revision at all,
    /// so the fix cannot be satisfied by marking everything unconditionally.
    /// </summary>
    [Fact]
    public async Task AutoCorrection_typed_with_track_changes_off_records_no_revision()
    {
        RevisionKind[]? revisions = null;
        string? text = null;

        await Session.Dispatch(() =>
        {
            var view = NewEditor();
            view.SimulateTextInputForTest("I teh ");

            text = view.Document.PlainText;
            revisions = view.Document.Blocks
                .OfType<Paragraph>()
                .SelectMany(paragraph => paragraph.Runs)
                .Select(run => run.Revision)
                .ToArray();
        }, CancellationToken.None);

        text.Should().Be("I the ");
        revisions.Should().NotBeNull();
        revisions!.Should().OnlyContain(
            revision => revision == RevisionKind.None,
            "tracking is off, so nothing here is a reviewable change");
    }

    private static DocumentView NewEditor()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
        var view = new DocumentView();
        view.LoadDocument(document);
        return view;
    }
}
