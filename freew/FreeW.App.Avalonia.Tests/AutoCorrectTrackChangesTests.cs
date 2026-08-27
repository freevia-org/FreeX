using System.Linq;
using System.Threading;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Avalonia counterpart of <c>FreeW.App.Host.Tests.AutoCorrectTrackChangesTests</c>.
///
/// <para>
/// <see cref="R158_AutoCorrectTracksAsRevisionTests"/> already pins the all-tracked case: everything in
/// the paragraph typed with tracking on, so Reject All must empty it. This class covers the mixed case
/// that one cannot distinguish -- ordinary text typed BEFORE Track Changes was switched on, then a
/// correction typed after. Here the correction must be the ONLY tracked span: a fix that marked too much
/// (tagging the whole neighbouring run, which is how the WPF shell failed) would turn pre-existing text
/// into a tracked insertion, and Reject All would eat text the author never changed.
/// </para>
///
/// <para>
/// The smart-quote correction is used deliberately: it is the one AutoFormat outcome with
/// <c>DeleteBefore == 0</c>, so the assertions are about the inserted correction alone and are not
/// entangled with how a tracked shell should record the characters a correction deletes.
/// </para>
/// </summary>
public sealed class AutoCorrectTrackChangesTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task AutoCorrection_after_untracked_text_is_the_only_tracked_span()
    {
        string? text = null;
        (string Text, RevisionKind Revision, string? Author)[]? runs = null;

        await Session.Dispatch(() =>
        {
            var view = NewEditor();
            view.RevisionAuthor = "Ada Reviewer";

            // Typed before tracking is switched on -- like text loaded from a file, or written before the
            // reviewer enabled the feature. It carries no revision mark and must keep none.
            view.SimulateTextInputForTest("Hello ");

            view.ToggleTrackChanges();
            view.SimulateTextInputForTest("\"");

            text = view.Document.PlainText;
            runs = view.Document.Blocks
                .OfType<Paragraph>()
                .SelectMany(paragraph => paragraph.Runs)
                .Where(run => run.Text.Length > 0)
                .Select(run => (run.Text, run.Revision, run.RevisionAuthor))
                .ToArray();
        }, CancellationToken.None);

        text.Should().Be("Hello “", "the correction itself must still happen");

        runs.Should().NotBeNull();
        string.Concat(runs!.Where(run => run.Revision == RevisionKind.None).Select(run => run.Text))
            .Should().Be("Hello ",
                "text typed before Track Changes was switched on must not become tracked just because "
                + "the correction landed next to it");

        var tracked = runs.Where(run => run.Revision == RevisionKind.Inserted).ToArray();
        tracked.Should().HaveCount(1);
        tracked[0].Text.Should().Be("“",
            "the AutoFormat substitution is text the user never typed, so with tracking on it is a "
            + "reviewable insertion like any other edit");
        tracked[0].Author.Should().Be("Ada Reviewer");
    }

    [Fact]
    public async Task RejectAll_after_untracked_text_removes_only_the_autocorrection()
    {
        string? afterReject = null;

        await Session.Dispatch(() =>
        {
            var view = NewEditor();
            view.RevisionAuthor = "Ada Reviewer";
            view.SimulateTextInputForTest("Hello ");
            view.ToggleTrackChanges();
            view.SimulateTextInputForTest("\"");
            view.Document.PlainText.Should().Be("Hello “");

            view.RejectAllRevisions();
            afterReject = view.Document.PlainText;
        }, CancellationToken.None);

        // The user-visible symptom of the defect: with the correction untracked, Reject All left
        // "Hello “" behind -- a change nobody approved surviving review with no trace it was made.
        afterReject.Should().Be("Hello ",
            "Reject All must discard the tracked correction and nothing else");
    }

    [Fact]
    public async Task AutoCorrection_after_untracked_text_stays_untracked_when_tracking_is_off()
    {
        // No-regression sibling: the same gesture with tracking never enabled must record no revision, so
        // the fix cannot be satisfied by marking corrections unconditionally.
        RevisionKind[]? revisions = null;
        string? text = null;

        await Session.Dispatch(() =>
        {
            var view = NewEditor();
            view.SimulateTextInputForTest("Hello \"");

            text = view.Document.PlainText;
            revisions = view.Document.Blocks
                .OfType<Paragraph>()
                .SelectMany(paragraph => paragraph.Runs)
                .Select(run => run.Revision)
                .ToArray();
        }, CancellationToken.None);

        text.Should().Be("Hello “");
        revisions.Should().NotBeNull();
        revisions!.Should().OnlyContain(revision => revision == RevisionKind.None);
    }

    [Fact]
    public async Task AutoHyperlink_recognized_with_tracking_on_is_recorded_as_an_insertion()
    {
        // The hyperlink outcome writes twice: the replacement cells, then a follow-up pass that stamps
        // LinkInfo onto them. That second write must preserve the revision marks the first put there, or
        // the recognized link would be an untracked edit that survives Reject All.
        string? afterReject = null;
        (string Text, RevisionKind Revision, string? Url)[]? runs = null;

        await Session.Dispatch(() =>
        {
            var view = NewEditor();
            view.RevisionAuthor = "Ada Reviewer";
            view.AutoCorrectOptions = AutoCorrectOptions.AllOff;
            view.ToggleTrackChanges();
            view.SimulateTextInputForTest("http://example.com ");

            runs = view.Document.Blocks
                .OfType<Paragraph>()
                .SelectMany(paragraph => paragraph.Runs)
                .Where(run => run.Text.Length > 0)
                .Select(run => (run.Text, run.Revision, run.HyperlinkUrl))
                .ToArray();

            view.RejectAllRevisions();
            afterReject = view.Document.PlainText;
        }, CancellationToken.None);

        runs.Should().NotBeNull();
        var linked = runs!.Where(run => run.Url is not null).ToArray();
        linked.Should().NotBeEmpty("the URL must still be recognized as a hyperlink");
        linked.Should().OnlyContain(run => run.Revision == RevisionKind.Inserted,
            "stamping the link onto the corrected cells must not drop the revision mark the correction "
            + "carried");

        afterReject.Should().BeEmpty(
            "everything here was typed or inserted with tracking on, so Reject All must leave the "
            + "paragraph empty");
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
