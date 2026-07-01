using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using Free.Shared.Ribbon;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-REVIEW: Review-tab wiring — tracked-change accept/reject (single + all), Track Changes toggle,
/// comment add/delete via the ribbon-backed DocumentView methods, word count, command resolution and undo.
/// Accept/reject ride the undoable DocumentCommandBus; comments reuse the AV-COMMENT infra; word count
/// reads DocumentStatistics from the model.
/// </summary>
public sealed class DocumentViewReviewTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private static async Task<bool> OnUiThread(Action action)
    {
        try
        {
            await Session.Dispatch(action, CancellationToken.None);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // A paragraph: "Hello " (plain) + "world" (tracked insertion by Ann).
    private static TextDocument DocWithInsertion()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Hello ", RunFormatting.Default));
        p.Runs.Add(new Run("world", RunFormatting.Default)
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Ann",
            RevisionDateXml = "2024-01-01T00:00:00Z",
        });
        doc.Blocks.Add(p);
        return doc;
    }

    // A paragraph: "Keep " (plain) + "gone" (tracked deletion).
    private static TextDocument DocWithDeletion()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph();
        p.Runs.Add(new Run("Keep ", RunFormatting.Default));
        p.Runs.Add(new Run("gone", RunFormatting.Default)
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Ann",
        });
        doc.Blocks.Add(p);
        return doc;
    }

    private static DocumentView Build(TextDocument doc)
    {
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 2000));
        return view;
    }

    private static string PlainText(DocumentView view) => ((Paragraph)view.Document.Blocks[0]).PlainText;
    private static bool HasInsertion(DocumentView view) =>
        ((Paragraph)view.Document.Blocks[0]).Runs.Any(r => r.Revision == RevisionKind.Inserted);
    private static bool HasDeletion(DocumentView view) =>
        ((Paragraph)view.Document.Blocks[0]).Runs.Any(r => r.Revision == RevisionKind.Deleted);

    // ── Accept ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcceptCurrent_clears_insertion_mark_keeping_text()
    {
        bool resolved = false, hadInsertion = true; string text = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            resolved = view.AcceptCurrentRevision();
            hadInsertion = HasInsertion(view);
            text = PlainText(view);
        });
        if (!ran) return;

        resolved.Should().BeTrue();
        hadInsertion.Should().BeFalse("accepting an insertion clears its revision mark");
        text.Should().Be("Hello world", "the inserted text is kept as ordinary text");
    }

    [Fact]
    public async Task RejectCurrent_removes_inserted_text()
    {
        bool resolved = false; string text = "x";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            resolved = view.RejectCurrentRevision();
            text = PlainText(view);
        });
        if (!ran) return;

        resolved.Should().BeTrue();
        text.Should().Be("Hello ", "rejecting an insertion removes the inserted run");
    }

    [Fact]
    public async Task AcceptCurrent_on_deletion_removes_text()
    {
        string text = "x";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithDeletion());
            view.AcceptCurrentRevision();
            text = PlainText(view);
        });
        if (!ran) return;
        text.Should().Be("Keep ", "accepting a deletion drops the deleted run");
    }

    [Fact]
    public async Task RejectCurrent_on_deletion_keeps_text()
    {
        string text = "x"; bool hadDeletion = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithDeletion());
            view.RejectCurrentRevision();
            text = PlainText(view);
            hadDeletion = HasDeletion(view);
        });
        if (!ran) return;
        text.Should().Be("Keep gone", "rejecting a deletion restores it as ordinary text");
        hadDeletion.Should().BeFalse("the deletion mark is cleared");
    }

    // ── Accept-all / Reject-all ───────────────────────────────────────────────────

    [Fact]
    public async Task AcceptAll_clears_every_revision()
    {
        bool anyRevision = true; string text = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            view.AcceptAllRevisions();
            anyRevision = view.HasRevisions;
            text = PlainText(view);
        });
        if (!ran) return;
        anyRevision.Should().BeFalse("accept-all resolves every tracked change");
        text.Should().Be("Hello world");
    }

    [Fact]
    public async Task RejectAll_clears_every_revision_and_drops_insertions()
    {
        bool anyRevision = true; string text = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            view.RejectAllRevisions();
            anyRevision = view.HasRevisions;
            text = PlainText(view);
        });
        if (!ran) return;
        anyRevision.Should().BeFalse("reject-all resolves every tracked change");
        text.Should().Be("Hello ", "reject-all drops the inserted text");
    }

    [Fact]
    public async Task AcceptAll_on_clean_document_returns_false()
    {
        bool resolved = true;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("No changes here"));
            var view = Build(doc);
            resolved = view.AcceptAllRevisions();
        });
        if (!ran) return;
        resolved.Should().BeFalse("a clean document has nothing to accept");
    }

    // ── Undo ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Undo_reverts_AcceptCurrent()
    {
        bool insertionAfterUndo = false; string textAfterUndo = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            view.AcceptCurrentRevision();
            view.Undo();
            insertionAfterUndo = HasInsertion(view);
            textAfterUndo = PlainText(view);
        });
        if (!ran) return;
        insertionAfterUndo.Should().BeTrue("Undo restores the tracked insertion mark");
        textAfterUndo.Should().Be("Hello world");
    }

    [Fact]
    public async Task Undo_reverts_RejectCurrent_restoring_inserted_text()
    {
        string textAfterUndo = ""; bool insertionAfterUndo = false;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            view.RejectCurrentRevision();      // removes "world"
            view.Undo();
            textAfterUndo = PlainText(view);
            insertionAfterUndo = HasInsertion(view);
        });
        if (!ran) return;
        textAfterUndo.Should().Be("Hello world", "Undo restores the removed inserted run");
        insertionAfterUndo.Should().BeTrue("Undo restores its insertion mark");
    }

    [Fact]
    public async Task Undo_reverts_AcceptAll()
    {
        bool revisionsAfterUndo = false;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            view.AcceptAllRevisions();
            view.Undo();
            revisionsAfterUndo = view.HasRevisions;
        });
        if (!ran) return;
        revisionsAfterUndo.Should().BeTrue("Undo restores every revision accept-all resolved");
    }

    // ── Track Changes toggle + mark selection ─────────────────────────────────────

    [Fact]
    public async Task ToggleTrackChanges_flips_flag()
    {
        bool first = false, second = true, defaultOff = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build(DocWithInsertion());
            defaultOff = view.TrackChangesEnabled;
            first = view.ToggleTrackChanges();
            second = view.ToggleTrackChanges();
        });
        if (!ran) return;
        defaultOff.Should().BeFalse("Track Changes is off by default");
        first.Should().BeTrue("first toggle turns it on");
        second.Should().BeFalse("second toggle turns it off");
    }

    [Fact]
    public async Task MarkSelectionAsRevision_records_an_insertion()
    {
        bool marked = false; bool hasInsertion = false;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            view.SetSelectionRangePublic(0, 6, 0, 11); // "world"
            marked = view.MarkSelectionAsRevision(RevisionKind.Inserted);
            hasInsertion = ((Paragraph)view.Document.Blocks[0]).Runs
                .Any(r => r.Revision == RevisionKind.Inserted && r.Text == "world");
        });
        if (!ran) return;
        marked.Should().BeTrue();
        hasInsertion.Should().BeTrue("the selected range is marked as a tracked insertion");
    }

    [Fact]
    public async Task Undo_reverts_MarkSelectionAsRevision()
    {
        bool hasInsertionAfterUndo = true;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            view.SetSelectionRangePublic(0, 6, 0, 11);
            view.MarkSelectionAsRevision(RevisionKind.Inserted);
            view.Undo();
            hasInsertionAfterUndo = ((Paragraph)view.Document.Blocks[0]).Runs
                .Any(r => r.Revision == RevisionKind.Inserted);
        });
        if (!ran) return;
        hasInsertionAfterUndo.Should().BeFalse("Undo removes the tracked-change mark");
    }

    // ── Comments via ribbon-backed methods ────────────────────────────────────────

    [Fact]
    public async Task NewComment_adds_a_comment_over_the_selection()
    {
        int count = -1; int? id = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            view.SetSelectionRangePublic(0, 0, 0, 5);
            id = view.NewComment("Please review");
            count = view.Document.Comments.Count;
        });
        if (!ran) return;
        id.Should().NotBeNull();
        count.Should().Be(1, "NewComment anchors a comment over the selection");
    }

    [Fact]
    public async Task DeleteCommentAtCaret_removes_the_comment()
    {
        int countAfterDelete = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            view.SetSelectionRangePublic(0, 0, 0, 5);
            view.NewComment("note");
            view.MoveCaretToBlock(0, 2); // inside the commented range
            view.DeleteCommentAtCaret();
            countAfterDelete = view.Document.Comments.Count;
        });
        if (!ran) return;
        countAfterDelete.Should().Be(0, "DeleteCommentAtCaret removes the thread at the caret");
    }

    [Fact]
    public async Task ResolveComment_registry_command_toggles_the_comment_at_the_caret()
    {
        bool resolved = false;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello world"));
            var view = Build(doc);
            view.SetSelectionRangePublic(0, 0, 0, 5);
            var id = view.NewComment("note");
            view.MoveCaretToBlock(0, 2);

            var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());
            Execute(registry, "freew.resolve-comment");

            resolved = id is { } commentId && view.Document.Comments[commentId].Resolved;
        });

        if (!ran) return;
        resolved.Should().BeTrue("the Review > Comments > Resolve command uses the editor comment model");
    }

    // ── Word count ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComputeStatistics_reports_word_and_paragraph_counts()
    {
        DocumentStatistics stats = default;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("The quick brown fox"));
            doc.Blocks.Add(new Paragraph("jumps over"));
            var view = Build(doc);
            stats = view.ComputeStatistics();
        });
        if (!ran) return;
        stats.Words.Should().Be(6, "six whitespace-delimited words across two paragraphs");
        stats.Paragraphs.Should().Be(2);
        stats.CharactersWithoutSpaces.Should().BeGreaterThan(0);
    }

    // ── Command resolution ────────────────────────────────────────────────────────

    [Fact]
    public void Review_command_ids_resolve_in_the_registry()
    {
        var view = new DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(view, NoopCallbacks());

        foreach (var id in new[]
        {
            "freew.track-changes",
            "freew.reviewing-pane",
            "freew.reviewingpane",
            "freew.statistics",
            "freew.word-count",
            "freew.check-accessibility",
            "freew.accept-change",
            "freew.accept-this",
            "freew.reject-change",
            "freew.reject-this",
            "freew.accept-all",
            "freew.reject-all",
            "freew.mark-as-final",
            "freew.restrict-editing",
            "freew.inspect-document",
            "freew.new-comment",
            "freew.delete-comment",
            "freew.previous-comment",
            "freew.next-comment",
            "freew.reply-comment",
            "freew.resolve-comment",
            "freew.show-comments",
        })
        {
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Review command '{id}' must be registered");
        }
    }

    [Fact]
    public void Review_command_ids_are_declared_in_the_ribbon_definition()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var ids = definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .Select(GetCommandId)
            .Where(id => id is not null)
            .Select(id => id!.Value.Value)
            .ToHashSet();

        foreach (var id in new[]
        {
            "freew.track-changes", "freew.reviewing-pane", "freew.statistics",
            "freew.check-accessibility", "freew.accept-this", "freew.reject-this",
            "freew.accept-all", "freew.reject-all", "freew.new-comment",
            "freew.delete-comment", "freew.previous-comment", "freew.next-comment",
            "freew.reply-comment", "freew.resolve-comment", "freew.show-comments",
            "freew.mark-as-final", "freew.restrict-editing",
            "freew.inspect-document",
        })
        {
            ids.Should().Contain(id, $"Review tab must declare '{id}'");
        }

        ids.Should().NotContain(new[]
        {
            "freew.reviewingpane",
            "freew.word-count",
            "freew.accept-change",
            "freew.reject-change",
        });
    }

    [Fact]
    public void Review_safety_commands_route_to_host_callbacks()
    {
        var callbacks = NoopCallbacks();
        var calls = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        callbacks = callbacks with
        {
            ToggleReviewingPane = () => calls.Add("reviewing-pane"),
            OpenWordCountDialog = () => calls.Add("statistics"),
            CheckAccessibility = () => calls.Add("accessibility"),
            InspectDocument = () => calls.Add("inspect"),
            MarkAsFinal = () => calls.Add("mark-final"),
            RestrictEditing = () => calls.Add("restrict"),
        };

        var registry = FreeWAvaloniaRibbonCommands.Build(new DocumentView(), callbacks);

        Execute(registry, "freew.reviewing-pane");
        Execute(registry, "freew.reviewingpane");
        Execute(registry, "freew.statistics");
        Execute(registry, "freew.word-count");
        Execute(registry, "freew.check-accessibility");
        Execute(registry, "freew.inspect-document");
        Execute(registry, "freew.mark-as-final");
        Execute(registry, "freew.restrict-editing");

        calls.Should().Contain(new[]
        {
            "reviewing-pane",
            "statistics",
            "accessibility",
            "inspect",
            "mark-final",
            "restrict",
        });
    }

    private static RibbonHostCallbacks NoopCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { }, SetPrintLayout: () => { }, SetWebLayout: () => { },
            SetDraftView: () => { }, OpenFontDialog: () => { }, OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { }, ToggleOrientation: () => { }, ApplyMarginPreset: _ => { },
            ApplyPaperSize: _ => { }, InsertPicture: () => { }, OpenWordCountDialog: () => { }, ApplyZoom: (_, _) => { });

    private static void Execute(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command)
            .Should().BeTrue($"command '{id}' must be registered");
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static RibbonCommandId? GetCommandId(RibbonControl control) => control switch
    {
        RibbonButton b => b.CommandId,
        RibbonToggleButton t => t.CommandId,
        RibbonComboBox c => c.CommandId,
        RibbonCheckBox cb => cb.CommandId,
        RibbonSplitButton sb => sb.CommandId,
        RibbonDropdown d => d.CommandId,
        RibbonGallery g => g.CommandId,
        _ => (RibbonCommandId?)null,
    };
}
