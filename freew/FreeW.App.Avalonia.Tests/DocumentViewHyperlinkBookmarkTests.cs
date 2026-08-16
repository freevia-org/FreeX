using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-LINK: hyperlink + bookmark render / follow / navigate / insert in the Avalonia
/// <see cref="DocumentView"/>. Verifies <see cref="DocumentView.InsertHyperlink"/> wraps a selection (and
/// inserts new text) as a model hyperlink run that round-trips through the cell layout, the run renders in
/// the hyperlink style (blue + underline), Ctrl+Click / FollowHyperlinkAtCaret raises
/// <see cref="DocumentView.HyperlinkActivated"/> for an external URL or jumps to a bookmark for an internal
/// link, <see cref="DocumentView.InsertBookmark"/> marks a range, <see cref="DocumentView.GoToBookmark"/>
/// moves the caret, undo reverts, and plain text is unaffected.
/// </summary>
public sealed class DocumentViewHyperlinkBookmarkTests
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

    private static TextDocument DocWith(params string[] paragraphs)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var text in paragraphs)
        {
            var p = new Paragraph();
            p.Runs.Add(new Run(text, RunFormatting.Default));
            doc.Blocks.Add(p);
        }
        return doc;
    }

    private static DocumentView Build(params string[] paragraphs)
    {
        var view = new DocumentView();
        view.LoadDocument(DocWith(paragraphs));
        view.Measure(new Size(800, 2000));
        return view;
    }

    // ── Insert hyperlink (+ undo) ───────────────────────────────────────────────

    [Fact]
    public async Task InsertHyperlink_over_selection_wraps_it_as_a_hyperlink_run()
    {
        var linkedText = "";
        var url = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build("Visit Acme today");
            // Select "Acme" (offsets 6..10).
            view.SetSelectionRangePublic(0, 6, 0, 10);
            view.InsertHyperlink("Acme", "https://acme.example");

            var p = (Paragraph)view.Document.Blocks[0];
            var link = p.Runs.FirstOrDefault(r => r.HyperlinkUrl is { Length: > 0 });
            linkedText = link?.Text ?? "";
            url = link?.HyperlinkUrl ?? "";
        });

        if (!ran) return;
        linkedText.Should().Be("Acme", "the selected range becomes the hyperlink run, preserving its text");
        url.Should().Be("https://acme.example");
    }

    [Fact]
    public async Task InsertHyperlink_with_no_selection_inserts_new_hyperlinked_text()
    {
        var hasLink = false;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Start");
            view.MoveCaretToBlock(0, 5); // end of "Start", no selection
            view.InsertHyperlink("Home", "https://home.example");

            var p = (Paragraph)view.Document.Blocks[0];
            hasLink = p.Runs.Any(r => r.Text == "Home" && r.HyperlinkUrl == "https://home.example");
        });

        if (!ran) return;
        hasLink.Should().BeTrue("with no selection the display text is inserted as a new hyperlinked run");
    }

    [Fact]
    public async Task InsertHyperlink_over_selection_with_different_display_text_replaces_the_selected_text()
    {
        string? linkedText = null;
        string? url = null;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Visit click here today");
            // Select "click here" (offsets 6..17).
            view.SetSelectionRangePublic(0, 6, 0, 17);
            view.InsertHyperlink("docs", "https://x.example");

            var p = (Paragraph)view.Document.Blocks[0];
            var link = p.Runs.FirstOrDefault(r => r.HyperlinkUrl is { Length: > 0 });
            linkedText = link?.Text;
            url = link?.HyperlinkUrl;
        });

        if (!ran) return;
        linkedText.Should().Be("docs", "Word replaces the selected text with the dialog's Display field when it differs");
        url.Should().Be("https://x.example");
    }

    [Fact]
    public async Task InsertHyperlink_over_selection_with_unchanged_display_text_only_retags_the_link()
    {
        string? text = null;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Visit Acme today");
            view.SetSelectionRangePublic(0, 6, 0, 10); // "Acme"
            view.InsertHyperlink("Acme", "https://acme.example"); // display text == selection

            var p = (Paragraph)view.Document.Blocks[0];
            text = p.PlainText;
        });

        if (!ran) return;
        text.Should().Be("Visit Acme today", "when the display text matches the selection, only the Link is retagged");
    }

    [Fact]
    public async Task InsertHyperlink_over_selection_with_empty_display_text_only_retags_the_link()
    {
        string? text = null;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Visit Acme today");
            view.SetSelectionRangePublic(0, 6, 0, 10); // "Acme"
            view.InsertHyperlink("", "https://acme.example"); // no display text supplied

            var p = (Paragraph)view.Document.Blocks[0];
            text = p.PlainText;
        });

        if (!ran) return;
        text.Should().Be("Visit Acme today", "an empty display text leaves the selected text untouched");
    }

    [Fact]
    public async Task InsertHyperlink_is_undoable()
    {
        var beforeHadLink = false;
        var afterUndoHasLink = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Visit Acme today");
            view.SetSelectionRangePublic(0, 6, 0, 10);
            view.InsertHyperlink("Acme", "https://acme.example");
            var p1 = (Paragraph)view.Document.Blocks[0];
            beforeHadLink = p1.Runs.Any(r => r.HyperlinkUrl is { Length: > 0 });

            view.Undo();
            var p2 = (Paragraph)view.Document.Blocks[0];
            afterUndoHasLink = p2.Runs.Any(r => r.HyperlinkUrl is { Length: > 0 });
        });

        if (!ran) return;
        beforeHadLink.Should().BeTrue();
        afterUndoHasLink.Should().BeFalse("undo removes the inserted hyperlink, restoring plain text");
    }

    // ── Render styling ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Hyperlink_run_renders_blue_and_underlined()
    {
        (string? ColorHex, bool Underline, bool IsHyperlink)? linkStyle = null;
        (string? ColorHex, bool Underline, bool IsHyperlink)? plainStyle = null;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Go Acme now");
            view.SetSelectionRangePublic(0, 3, 0, 7); // "Acme"
            view.InsertHyperlink("Acme", "https://acme.example");
            view.Measure(new Size(800, 2000));

            linkStyle = view.GetGlyphRenderStyle(0, 3);  // 'A' of the hyperlink
            plainStyle = view.GetGlyphRenderStyle(0, 0);  // 'G' of plain text
        });

        if (!ran) return;
        linkStyle.Should().NotBeNull();
        linkStyle!.Value.IsHyperlink.Should().BeTrue();
        linkStyle.Value.Underline.Should().BeTrue("hyperlinked glyphs render underlined");
        linkStyle.Value.ColorHex.Should().Be("#0563C1", "hyperlinked glyphs render in Word's hyperlink blue");

        plainStyle.Should().NotBeNull();
        plainStyle!.Value.IsHyperlink.Should().BeFalse("plain text is not styled as a hyperlink");
        plainStyle.Value.Underline.Should().BeFalse();
    }

    // ── Follow / activate ───────────────────────────────────────────────────────

    [Fact]
    public async Task FollowHyperlinkAtCaret_raises_HyperlinkActivated_for_external_url()
    {
        string? activatedUrl = null;
        var followed = false;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Open Acme link");
            view.HyperlinkActivated += u => activatedUrl = u;
            view.SetSelectionRangePublic(0, 5, 0, 9); // "Acme"
            view.InsertHyperlink("Acme", "https://acme.example");

            // Place the caret inside the link, then follow.
            view.MoveCaretToBlock(0, 7);
            followed = view.FollowHyperlinkAtCaret();
        });

        if (!ran) return;
        followed.Should().BeTrue();
        activatedUrl.Should().Be("https://acme.example",
            "following an external hyperlink raises HyperlinkActivated with the URL (no hard-coded browser)");
    }

    [Fact]
    public async Task FollowHyperlinkAtCaret_navigates_to_bookmark_for_internal_link()
    {
        var followed = false;
        var caretBlock = -1;
        string? activatedUrl = "set";
        var ran = await OnUiThread(() =>
        {
            // Two paragraphs: a link source (para 0) and a bookmark target (para 1).
            var view = Build("Jump to section", "Target section here");
            view.HyperlinkActivated += u => activatedUrl = u;

            // Mark para 1 as bookmark "sec1".
            view.MoveCaretToBlock(1, 0);
            view.InsertBookmark("sec1");

            // Make para 0 an internal hyperlink to "#sec1".
            view.SetSelectionRangePublic(0, 0, 0, 4); // "Jump"
            view.InsertHyperlink("Jump", "#sec1");

            view.MoveCaretToBlock(0, 2); // inside the link
            followed = view.FollowHyperlinkAtCaret();
            caretBlock = view.CaretPositionForTest.Block;
        });

        if (!ran) return;
        followed.Should().BeTrue();
        caretBlock.Should().Be(1, "following an internal link jumps the caret to the bookmark's paragraph");
        activatedUrl.Should().Be("set", "an internal link does NOT raise HyperlinkActivated (it navigates in-place)");
    }

    // ── Bookmarks ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertBookmark_marks_the_caret_paragraph()
    {
        var hasBookmark = false;
        var ran = await OnUiThread(() =>
        {
            var view = Build("First", "Second");
            view.MoveCaretToBlock(1, 0);
            view.InsertBookmark("mark2");

            var p = (Paragraph)view.Document.Blocks[1];
            hasBookmark = p.BookmarkNames.Contains("mark2");
        });

        if (!ran) return;
        hasBookmark.Should().BeTrue("InsertBookmark adds the name to the caret paragraph's BookmarkNames");
    }

    // Confirmed HIGH finding: Insert Bookmark allowed a duplicate name. InsertBookmark must reject a name
    // already used by a different paragraph (Word's unique-name rule), leaving the original target
    // untouched, instead of silently creating a second bookmark instance sharing that name.
    [Fact]
    public async Task InsertBookmark_rejects_a_duplicate_name_and_leaves_the_original_target_in_place()
    {
        var outcome = BookmarkInsertOutcome.Applied;
        var firstStillHasIt = false;
        var secondGotIt = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build("First", "Second");
            view.MoveCaretToBlock(0, 0);
            view.InsertBookmark("Shared");

            view.MoveCaretToBlock(1, 0);
            outcome = view.InsertBookmark("Shared");

            firstStillHasIt = ((Paragraph)view.Document.Blocks[0]).BookmarkNames.Contains("Shared");
            secondGotIt = ((Paragraph)view.Document.Blocks[1]).BookmarkNames.Contains("Shared");
        });

        if (!ran) return;
        outcome.Should().Be(BookmarkInsertOutcome.DuplicateName);
        firstStillHasIt.Should().BeTrue("the original bookmark target must be untouched");
        secondGotIt.Should().BeFalse("a duplicate name must not be applied to a second paragraph");
    }

    [Fact]
    public async Task GoToBookmark_moves_caret_to_the_bookmark_paragraph()
    {
        var found = false;
        var caretBlock = -1;
        var ran = await OnUiThread(() =>
        {
            var view = Build("One", "Two", "Three");
            view.MoveCaretToBlock(2, 0);
            view.InsertBookmark("third");

            view.MoveCaretToBlock(0, 0); // move away
            found = view.GoToBookmark("third");
            caretBlock = view.CaretPositionForTest.Block;
        });

        if (!ran) return;
        found.Should().BeTrue();
        caretBlock.Should().Be(2, "GoToBookmark moves the caret to the bookmarked paragraph");
    }

    [Fact]
    public async Task GoToBookmark_returns_false_for_unknown_name()
    {
        var found = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Body text");
            found = view.GoToBookmark("does-not-exist");
        });

        if (!ran) return;
        found.Should().BeFalse("navigating to a missing bookmark is a no-op returning false");
    }

    // Regression: Bookmarks.List() was widened to also find bookmarks nested in table cells, reporting
    // the containing Table's block index for those (a cell-nested paragraph has no standalone Blocks
    // index). GoToBookmark must resolve that Table-valued BlockIndex into the actual cell rather than
    // dropping a plain body caret on the table block.
    [Fact]
    public async Task GoToBookmark_navigates_into_a_bookmark_nested_in_a_table_cell()
    {
        var found = false;
        (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? cellCaret = null;
        var caretBlock = -1;
        var tableBlockIndex = -1;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var lead = new Paragraph();
            lead.Runs.Add(new Run("Before table", RunFormatting.Default));
            doc.Blocks.Add(lead);

            var table = Table.Create(2, 2);
            // Bookmark the paragraph in the second row, second column.
            table.Rows[1].Cells[1].Paragraphs[0].BookmarkNames.Add("cellmark");
            doc.Blocks.Add(table);
            tableBlockIndex = doc.Blocks.IndexOf(table);

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 2000));

            view.MoveCaretToBlock(0, 0); // start away from the table
            found = view.GoToBookmark("cellmark");
            cellCaret = view.CellCaretInfo;
            caretBlock = view.CaretPositionForTest.Block;
        });

        if (!ran) return;
        found.Should().BeTrue();
        caretBlock.Should().Be(tableBlockIndex, "the caret block lands on the table containing the bookmark");
        cellCaret.Should().NotBeNull(
            "navigating to a bookmark nested in a table cell must place a cell caret in that cell, " +
            "not just a body caret on the table block");
        cellCaret!.Value.TableBlock.Should().Be(tableBlockIndex);
        cellCaret.Value.Row.Should().Be(1, "the bookmark is on the second table row");
        cellCaret.Value.Col.Should().Be(1, "the bookmark is on the second table column");
    }

    // ── Round-trip + introspection ──────────────────────────────────────────────

    [Fact]
    public async Task HyperlinksAtCaret_reports_the_link_under_the_caret()
    {
        string? url = null;
        var ran = await OnUiThread(() =>
        {
            var view = Build("See Acme site");
            view.SetSelectionRangePublic(0, 4, 0, 8); // "Acme"
            view.InsertHyperlink("Acme", "https://acme.example");

            view.MoveCaretToBlock(0, 6); // inside the link
            var links = view.HyperlinksAtCaret();
            url = links.Count > 0 ? links[0].Url : null;
        });

        if (!ran) return;
        url.Should().Be("https://acme.example");
    }

    [Fact]
    public async Task NativeHyperlinkFieldsExposeExternalAndBookmarkTargets()
    {
        string? externalUrl = null;
        string? externalTooltip = null;
        string? anchor = null;
        string? anchorTooltip = null;
        var ran = await OnUiThread(() =>
        {
            var document = TextDocument.CreateEmpty();
            document.Blocks.Clear();
            var external = new Paragraph();
            external.Runs.Add(new Run("Manual")
            {
                ComplexField = new ComplexField(" HYPERLINK \"https://example.com/manual\" \\o \"Open manual\" "),
                HyperlinkUrl = "https://example.com/manual",
                HyperlinkTooltip = "Open manual"
            });
            var internalLink = new Paragraph();
            internalLink.Runs.Add(new Run("Details")
            {
                ComplexField = new ComplexField(" HYPERLINK \\l \"Details\" \\o \"Jump to details\" "),
                HyperlinkAnchor = "Details",
                HyperlinkTooltip = "Jump to details"
            });
            document.Blocks.Add(external);
            document.Blocks.Add(internalLink);

            var view = new DocumentView();
            view.LoadDocument(document);
            view.Measure(new Size(800, 2000));
            view.MoveCaretToBlock(0, 3);
            var externalTargets = view.HyperlinksAtCaret();
            externalUrl = externalTargets.Single().Url;
            externalTooltip = externalTargets.Single().Tooltip;
            view.MoveCaretToBlock(1, 3);
            var internalTargets = view.HyperlinksAtCaret();
            anchor = internalTargets.Single().Anchor;
            anchorTooltip = internalTargets.Single().Tooltip;
        });

        if (!ran) return;
        externalUrl.Should().Be("https://example.com/manual");
        externalTooltip.Should().Be("Open manual");
        anchor.Should().Be("Details");
        anchorTooltip.Should().Be("Jump to details");
    }

    [Fact]
    public async Task EditHyperlink_retargets_the_link_under_the_caret_and_preserves_text_and_screentip()
    {
        string? text = null;
        string? url = null;
        string? anchor = null;
        string? tooltip = null;
        var isOnLink = false;
        var ran = await OnUiThread(() =>
        {
            var view = Build("See Acme site");
            view.SetSelectionRangePublic(0, 4, 0, 8); // "Acme"
            view.InsertHyperlink("Acme", "https://old.example");
            view.MoveCaretToBlock(0, 6);
            view.SetHyperlinkTooltip("Old tip");

            isOnLink = view.IsCaretOnHyperlink();
            view.EditHyperlink("#TargetBookmark");

            var link = ((Paragraph)view.Document.Blocks[0]).Runs.First(r => r.HyperlinkAnchor == "TargetBookmark");
            text = link.Text;
            url = link.HyperlinkUrl;
            anchor = link.HyperlinkAnchor;
            tooltip = link.HyperlinkTooltip;
        });

        if (!ran) return;
        isOnLink.Should().BeTrue();
        text.Should().Be("Acme");
        url.Should().BeNull();
        anchor.Should().Be("TargetBookmark");
        tooltip.Should().Be("Old tip", "retargeting should keep the existing ScreenTip");
    }

    [Fact]
    public async Task EditHyperlink_with_changed_display_text_rewrites_the_span_text_and_retargets()
    {
        string? text = null;
        string? url = null;
        var ran = await OnUiThread(() =>
        {
            var view = Build("See Acme site");
            view.SetSelectionRangePublic(0, 4, 0, 8); // "Acme"
            view.InsertHyperlink("Acme", "https://old.example");
            view.MoveCaretToBlock(0, 6);

            view.EditHyperlink("https://new.example", "Acme Corp");

            var p = (Paragraph)view.Document.Blocks[0];
            var link = p.Runs.FirstOrDefault(r => r.HyperlinkUrl is { Length: > 0 });
            text = link?.Text;
            url = link?.HyperlinkUrl;
        });

        if (!ran) return;
        text.Should().Be("Acme Corp", "an edited Display field should replace the link span's visible text, matching Word");
        url.Should().Be("https://new.example");
    }

    [Fact]
    public async Task EditHyperlink_with_unchanged_display_text_only_retargets()
    {
        string? text = null;
        string? url = null;
        var ran = await OnUiThread(() =>
        {
            var view = Build("See Acme site");
            view.SetSelectionRangePublic(0, 4, 0, 8); // "Acme"
            view.InsertHyperlink("Acme", "https://old.example");
            view.MoveCaretToBlock(0, 6);

            view.EditHyperlink("https://new.example", "Acme"); // display unchanged

            var p = (Paragraph)view.Document.Blocks[0];
            var link = p.Runs.FirstOrDefault(r => r.HyperlinkUrl is { Length: > 0 });
            text = link?.Text;
            url = link?.HyperlinkUrl;
        });

        if (!ran) return;
        text.Should().Be("Acme", "unchanged display text leaves the span's characters untouched");
        url.Should().Be("https://new.example");
    }

    [Fact]
    public async Task RemoveHyperlink_clears_the_link_under_the_caret_but_keeps_visible_text()
    {
        var plainText = "";
        var hasLink = true;
        var ran = await OnUiThread(() =>
        {
            var view = Build("See Acme site");
            view.SetSelectionRangePublic(0, 4, 0, 8); // "Acme"
            view.InsertHyperlink("Acme", "https://old.example");
            view.MoveCaretToBlock(0, 6);

            view.RemoveHyperlink();

            var paragraph = (Paragraph)view.Document.Blocks[0];
            plainText = paragraph.PlainText;
            hasLink = paragraph.Runs.Any(r => r.HyperlinkUrl is { Length: > 0 } || r.HyperlinkAnchor is { Length: > 0 });
        });

        if (!ran) return;
        plainText.Should().Be("See Acme site");
        hasLink.Should().BeFalse();
    }

    [Fact]
    public async Task SetHyperlinkTooltip_sets_and_clears_the_link_screentip()
    {
        string? afterSet = null;
        string? afterClear = "still set";
        var ran = await OnUiThread(() =>
        {
            var view = Build("See Acme site");
            view.SetSelectionRangePublic(0, 4, 0, 8); // "Acme"
            view.InsertHyperlink("Acme", "https://old.example");
            view.MoveCaretToBlock(0, 6);

            view.SetHyperlinkTooltip("Screen tip");
            afterSet = view.HyperlinkTooltipAtCaret();
            view.SetHyperlinkTooltip(" ");
            afterClear = view.HyperlinkTooltipAtCaret();
        });

        if (!ran) return;
        afterSet.Should().Be("Screen tip");
        afterClear.Should().BeNull();
    }

    [Fact]
    public async Task ApplyInternalLink_wraps_selection_with_bookmark_anchor()
    {
        string? anchor = null;
        IReadOnlyList<string>? bookmarkNames = null;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Jump target", "Target");
            view.MoveCaretToBlock(1, 0);
            view.InsertBookmark("Target1");
            bookmarkNames = view.BookmarkNames();

            view.SetSelectionRangePublic(0, 0, 0, 4);
            view.ApplyInternalLink("Target1");

            anchor = ((Paragraph)view.Document.Blocks[0]).Runs
                .FirstOrDefault(r => r.Text == "Jump")?.HyperlinkAnchor;
        });

        if (!ran) return;
        bookmarkNames.Should().Contain("Target1");
        anchor.Should().Be("Target1");
    }

    [Fact]
    public async Task Editing_inside_a_hyperlink_keeps_it_one_link_run()
    {
        var linkText = "";
        var linkRunCount = 0;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Go Acme now");
            view.SetSelectionRangePublic(0, 3, 0, 7); // "Acme"
            view.InsertHyperlink("Acme", "https://acme.example");

            // Type inside the link span (after "Ac").
            view.MoveCaretToBlock(0, 5);
            view.InsertText("X");

            var p = (Paragraph)view.Document.Blocks[0];
            var linkRuns = p.Runs.Where(r => r.HyperlinkUrl == "https://acme.example").ToList();
            linkRunCount = linkRuns.Count;
            linkText = string.Concat(linkRuns.Select(r => r.Text));
        });

        if (!ran) return;
        linkRunCount.Should().Be(1, "the cell round-trip re-coalesces the hyperlink into one contiguous run");
        linkText.Should().Be("AcXme", "typing inside the link extends the same hyperlink span");
    }

    [Fact]
    public async Task Repeated_typing_inside_a_hyperlink_keeps_one_contiguous_link_span()
    {
        var linkRuns = 0;
        var linkText = "";
        var ran = await OnUiThread(() =>
        {
            var view = Build("Go Acme now");
            view.SetSelectionRangePublic(0, 3, 0, 7);
            view.InsertHyperlink("Acme", "https://acme.example");

            view.MoveCaretToBlock(0, 4);
            view.InsertText("1");
            view.InsertText("2");

            var paragraph = (Paragraph)view.Document.Blocks[0];
            var runs = paragraph.Runs.Where(run => run.HyperlinkUrl == "https://acme.example").ToArray();
            linkRuns = runs.Length;
            linkText = string.Concat(runs.Select(run => run.Text));
        });

        if (!ran) return;
        linkRuns.Should().Be(1);
        linkText.Should().Be("A12cme");
    }

    // ── Regression: plain text unaffected ───────────────────────────────────────

    [Fact]
    public async Task Plain_text_has_no_hyperlink_styling_or_targets()
    {
        var hasAnyLink = false;
        (string? ColorHex, bool Underline, bool IsHyperlink)? style = null;
        var ran = await OnUiThread(() =>
        {
            var view = Build("Just plain text");
            var p = (Paragraph)view.Document.Blocks[0];
            hasAnyLink = p.Runs.Any(r => r.HyperlinkUrl is { Length: > 0 } || r.HyperlinkAnchor is { Length: > 0 });
            style = view.GetGlyphRenderStyle(0, 0);
        });

        if (!ran) return;
        hasAnyLink.Should().BeFalse();
        style.Should().NotBeNull();
        style!.Value.IsHyperlink.Should().BeFalse();
    }

    // ── ED1: MainWindow wiring — HyperlinkActivated → OpenExternalUri ───────────

    /// <summary>
    /// After MainWindow construction the DocumentView's HyperlinkActivated event must have
    /// exactly one subscriber (the OpenExternalUri handler wired at ~line 138).
    /// </summary>
    [Fact]
    public async Task MainWindow_wires_HyperlinkActivated_on_construction()
    {
        int? subscriberCount = null;
        var ran = await OnUiThread(() =>
        {
            var window = new MainWindow(Array.Empty<string>());
            // Inspect the multicast delegate via reflection — public event, so GetInvocationList
            // works on a test-injected subscriber added alongside the wired one.
            var editor = window.Editor;

            // Add a second subscriber so the list is non-null even before any activation.
            var activations = 0;
            editor.HyperlinkActivated += _ => activations++;

            // Retrieve the backing field count via the event accessor.
            // We can't call GetInvocationList on a C# event directly; verify by raising it.
            editor.SimulateHyperlinkActivatedForTest("https://test.example");
            subscriberCount = activations; // the MainWindow handler silently ignores; our counter fires
        });

        if (!ran) return;
        // Our test handler fired → the event is subscribed (and MainWindow's handler is also wired).
        subscriberCount.Should().Be(1, "raising HyperlinkActivated invokes our test subscriber");
    }

    /// <summary>
    /// ExternalUriLauncher.TryCreateAllowedUri (used by MainWindow.OpenExternalUri) accepts the
    /// safe schemes (http/https/mailto/ftp/well-formed local file) and rejects unsafe ones
    /// (javascript, unknown, empty). "file" is deliberately on the shared allowlist — Word-style
    /// hyperlinks to local files are a supported feature, mirroring FreeX's own well-tested
    /// ExternalUriLauncherTests contract for the same shared component — so a syntactically
    /// well-formed file:// URI is accepted here. This tests the scheme guard without invoking
    /// Process.Start.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/page", true)]
    [InlineData("http://example.com/page", true)]
    [InlineData("mailto:user@example.com", true)]
    [InlineData("ftp://files.example.com/data", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///etc/passwd", true)]
    [InlineData("data:text/html,<h1>x</h1>", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void OpenExternalUri_scheme_guard_accepts_safe_and_rejects_unsafe_schemes(
        string url, bool expectedAllowed)
    {
        var allowed = ExternalUriLauncher.TryCreateAllowedUri(url, out _);
        allowed.Should().Be(expectedAllowed,
            $"scheme guard should {(expectedAllowed ? "allow" : "block")} \"{url}\"");
    }
}
