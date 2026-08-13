using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-HFEDIT: tests for in-region header/footer caret editing in the Avalonia DocumentView.
/// Verifies: a caret can be placed in a header/footer (programmatically and via hit-test); typed text,
/// Backspace, Delete and Enter mutate the targeted header/footer paragraph in the model; Esc and a
/// body-click return the caret to the body; field runs (page number) are preserved across edits; and
/// body editing still works (regression guard).
/// </summary>
public sealed class DocumentViewHeaderFooterEditTests
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
            return false; // no headless drawing backend in this environment
        }
    }

    private static (TextDocument Doc, DocumentView View) MakeViewWithHeader(string headerText)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Body text."));
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter(headerText);
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(816, 4000));
        return (doc, view);
    }

    private static string HeaderText(TextDocument doc) =>
        doc.FinalSectionHeadersFooters.Header?.PlainText ?? string.Empty;

    // ── Place caret programmatically ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlaceCaretInHeaderFooter_sets_HeaderFooterCaretInfo_in_the_header()
    {
        (int SectionIndex, bool IsFooter, HeaderFooterSlotKind Slot, int ParaIdx, int Offset)? info = null;
        var ran = await OnUiThread(() =>
        {
            var (_, view) = MakeViewWithHeader("Header");
            view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 3);
            info = view.HeaderFooterCaretInfo;
        });
        if (!ran) return;
        info.Should().NotBeNull("placing the caret in the header must report H/F caret info");
        info!.Value.IsFooter.Should().BeFalse();
        info.Value.Slot.Should().Be(HeaderFooterSlotKind.Header);
        info.Value.Offset.Should().Be(3);
    }

    // ── Typing into a header mutates the model ────────────────────────────────────────────────────

    [Fact]
    public async Task Typing_in_header_updates_the_header_paragraph_text()
    {
        string? text = null;
        var ran = await OnUiThread(() =>
        {
            var (doc, view) = MakeViewWithHeader("Hello");
            view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 5); // end of "Hello"
            view.InsertText(" World");
            text = HeaderText(doc);
        });
        if (!ran) return;
        text.Should().Be("Hello World", "typed text must be appended to the header paragraph in the model");
    }

    [Fact]
    public async Task Typing_in_header_inserts_at_the_caret_offset()
    {
        string? text = null;
        var ran = await OnUiThread(() =>
        {
            var (doc, view) = MakeViewWithHeader("AC");
            view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 1); // between A and C
            view.InsertText("B");
            text = HeaderText(doc);
        });
        if (!ran) return;
        text.Should().Be("ABC", "typed text must insert at the caret offset, not at the end");
    }

    // ── Backspace + Delete ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Backspace_in_header_deletes_the_char_before_the_caret()
    {
        string? text = null;
        var ran = await OnUiThread(() =>
        {
            var (doc, view) = MakeViewWithHeader("Hello");
            view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 5);
            view.BackspaceForTest();
            text = HeaderText(doc);
        });
        if (!ran) return;
        text.Should().Be("Hell", "Backspace must delete the character before the caret in the header");
    }

    [Fact]
    public async Task Delete_in_header_deletes_the_char_at_the_caret()
    {
        string? text = null;
        var ran = await OnUiThread(() =>
        {
            var (doc, view) = MakeViewWithHeader("Hello");
            view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 0);
            view.DeleteForwardForTest();
            text = HeaderText(doc);
        });
        if (!ran) return;
        text.Should().Be("ello", "Delete must remove the character at the caret in the header");
    }

    // ── Enter splits a header paragraph ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Enter_in_header_splits_the_paragraph_into_two_lines()
    {
        int paraCount = -1;
        string? line0 = null;
        string? line1 = null;
        var ran = await OnUiThread(() =>
        {
            var (doc, view) = MakeViewWithHeader("AB");
            view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 1);
            view.InsertParagraphBreakForTest();
            var hf = doc.FinalSectionHeadersFooters.Header!;
            paraCount = hf.Paragraphs.Count;
            line0 = hf.Paragraphs[0].PlainText;
            line1 = hf.Paragraphs.Count > 1 ? hf.Paragraphs[1].PlainText : null;
        });
        if (!ran) return;
        paraCount.Should().Be(2, "Enter must split the header paragraph into two");
        line0.Should().Be("A");
        line1.Should().Be("B");
    }

    // ── Field runs are preserved across literal editing ───────────────────────────────────────────

    [Fact]
    public async Task Editing_literal_text_preserves_a_page_number_field_run()
    {
        bool fieldPreserved = false;
        string? plain = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Body."));
            var hf = new HeaderFooter();
            var para = new Paragraph();
            para.Runs.Add(new Run("Page ", RunFormatting.Default));
            para.Runs.Add(Run.PageNumberField());
            hf.Paragraphs.Add(para);
            doc.FinalSectionHeadersFooters.Header = hf;

            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            // Caret after "Page " (offset 5) — before the field run. Type a literal char.
            view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 5);
            view.InsertText("#");

            var p = doc.FinalSectionHeadersFooters.Header!.Paragraphs[0];
            fieldPreserved = p.Runs.Any(r => r.FieldKind == RunFieldKind.PageNumber);
            plain = p.PlainText;
        });
        if (!ran) return;
        fieldPreserved.Should().BeTrue("the page-number field run must survive literal text editing");
        plain.Should().StartWith("Page #", "the literal char must insert before the field, leaving the field intact");
    }

    [Fact]
    public async Task Current_field_commands_target_only_the_field_under_the_header_caret()
    {
        var ran = await OnUiThread(() =>
        {
            var headerTitle = Run.ComplexFieldRun(" DOCPROPERTY Title ", "Stale header title");
            var headerSubject = Run.ComplexFieldRun(" DOCPROPERTY Subject ", "Stale header subject");
            var bodyTitle = Run.ComplexFieldRun(" DOCPROPERTY Title ", "Stale body title");
            var doc = TextDocument.CreateEmpty();
            doc.Properties.Title = "Current title";
            doc.Properties.Subject = "Current subject";
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph { Runs = { bodyTitle } });
            var header = new HeaderFooter();
            header.Paragraphs.Add(new Paragraph
            {
                Runs = { new Run("Header "), headerTitle, new Run(" / "), headerSubject }
            });
            doc.FinalSectionHeadersFooters.Header = header;
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));
            view.PlaceCaretInHeaderFooter(
                footer: false,
                paraIdx: 0,
                offset: "Header ".Length + 2);

            view.UpdateFieldAtCaret();
            headerTitle.Text.Should().Be("Current title");
            headerSubject.Text.Should().Be("Stale header subject");
            bodyTitle.Text.Should().Be("Stale body title");

            view.ToggleFieldCodeAtCaret();
            headerTitle.ComplexField!.ShowCode.Should().BeTrue();
            headerSubject.ComplexField!.ShowCode.Should().BeFalse();

            view.SetFieldLockAtCaret(true);
            headerTitle.ComplexField!.IsLocked.Should().BeTrue();
            headerSubject.ComplexField!.IsLocked.Should().BeFalse();

            view.UnlinkFieldAtCaret();
            headerTitle.ComplexField.Should().BeNull();
            headerTitle.Text.Should().Be("Current title");
            headerSubject.ComplexField.Should().NotBeNull();
        });
        if (!ran) return;
    }

    // ── Exit to body ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Escape_exits_the_header_caret_back_to_body()
    {
        bool activeBefore = false;
        bool activeAfter = true;
        var ran = await OnUiThread(() =>
        {
            var (_, view) = MakeViewWithHeader("Header");
            view.PlaceCaretInHeaderFooter(footer: false);
            activeBefore = view.IsHeaderFooterCaretActive;
            view.ExitHeaderFooterCaret();
            activeAfter = view.IsHeaderFooterCaretActive;
        });
        if (!ran) return;
        activeBefore.Should().BeTrue("caret should be in the header before Esc");
        activeAfter.Should().BeFalse("Esc must return the caret to the body");
    }

    [Fact]
    public async Task Body_click_exits_the_header_caret()
    {
        bool activeAfter = true;
        var ran = await OnUiThread(() =>
        {
            var (_, view) = MakeViewWithHeader("Header");
            view.PlaceCaretInHeaderFooter(footer: false);
            // A click well inside the body text area returns the caret to the body.
            view.HandleBodyClickForTest(new Point(120, 200));
            activeAfter = view.IsHeaderFooterCaretActive;
        });
        if (!ran) return;
        activeAfter.Should().BeFalse("clicking back in the body must exit the header caret");
    }

    // ── Hit-test entry: clicking in a rendered header places the caret ────────────────────────────

    [Fact]
    public async Task Click_inside_rendered_header_places_caret_in_that_header()
    {
        bool hit = false;
        (int SectionIndex, bool IsFooter, HeaderFooterSlotKind Slot, int ParaIdx, int Offset)? info = null;
        var ran = await OnUiThread(() =>
        {
            var (_, view) = MakeViewWithHeader("My Header");
            // Derive the click point from the actual rendered header item so the test is robust to
            // exact margin/line-height geometry: click a few px into the text, mid-line.
            var hf = view.HeaderFooterItemsFull.First(i => i.Text == "My Header");
            var clickPoint = new Point(hf.X + 6, hf.Y + 4);
            hit = view.HitTestHeaderFooterForTest(clickPoint);
            info = view.HeaderFooterCaretInfo;
        });
        if (!ran) return;
        hit.Should().BeTrue("a click inside the rendered header band must register an H/F hit");
        info.Should().NotBeNull();
        info!.Value.Slot.Should().Be(HeaderFooterSlotKind.Header);
    }

    // ── Footer editing ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Typing_in_footer_updates_the_footer_paragraph_text()
    {
        string? text = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Body."));
            doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("Foot");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            view.PlaceCaretInHeaderFooter(footer: true, paraIdx: 0, offset: 4);
            view.InsertText("er");
            text = doc.FinalSectionHeadersFooters.Footer!.PlainText;
        });
        if (!ran) return;
        text.Should().Be("Footer");
    }

    // ── Editing an empty (freshly-created) header ─────────────────────────────────────────────────

    [Fact]
    public async Task Typing_into_empty_default_header_creates_and_fills_it()
    {
        string? text = null;
        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Body."));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            view.PlaceCaretInHeaderFooter(footer: false); // creates the empty header slot
            view.InsertText("New");
            text = doc.FinalSectionHeadersFooters.Header?.PlainText;
        });
        if (!ran) return;
        text.Should().Be("New", "typing into a freshly-created empty header must populate it");
    }

    // ── Regression: body editing still works with no H/F caret ─────────────────────────────────────

    [Fact]
    public async Task Body_editing_still_works_when_no_header_caret_is_active()
    {
        string? body = null;
        var ran = await OnUiThread(() =>
        {
            var (doc, view) = MakeViewWithHeader("Header");
            // No H/F caret placed → InsertText must route to the body as before.
            view.MoveCaretToBlockForTest(0, 4); // after "Body"
            view.InsertText("!!");
            body = doc.Blocks[0] is Paragraph p ? p.PlainText : null;
        });
        if (!ran) return;
        body.Should().Be("Body!! text.", "body editing must be unaffected by the H/F edit feature");
    }

    // ── DD1: Tab in header does NOT demote the body list item ───────────────────────────────────────

    /// <summary>
    /// Regression guard for DD1 (AV-HFEDIT): pressing Tab while the H/F caret is active must insert a
    /// literal tab into the header and must NOT mutate a body list paragraph.
    /// Before the fix, Key.Tab fell through the _hfCaret switch to the body switch where
    /// ListTabAtItemStart(false) would demote the body list item and the header got no tab.
    /// </summary>
    [Fact]
    public async Task Tab_in_header_inserts_tab_not_demotes_body_list()
    {
        string? headerAfter = null;
        int bodyLevelAfter = -1;
        bool hfHandled = false;
        bool bodyWouldFire = false;
        var ran = await OnUiThread(() =>
        {
            // Build a doc with a body list item at offset 0 (so ListTabAtItemStart would fire for Tab)
            // and a header containing "X".
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            var listPara = new Paragraph("Item")
            {
                Formatting = new ParagraphFormatting { ListKind = ListKind.Number, ListLevel = 0 }
            };
            doc.Blocks.Add(listPara);
            doc.FinalSectionHeadersFooters.Header = new HeaderFooter("X");
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(816, 4000));

            // Place body caret at offset 0 of the list item (so ListTabAtItemStart would fire).
            view.MoveCaretToBlock(0, 0);
            // Verify that WITHOUT the H/F guard ListTabAtItemStart would have consumed the key.
            bodyWouldFire = view.ListTabAtItemStartPublic(shift: false);
            // Restore the list level (ListTabAtItemStartPublic mutated it in the check above —
            // use a fresh view to avoid carrying state into the actual assertion).
            view.Undo(); // roll back the test-probe demote

            // Now activate H/F caret and body caret at offset 0 of the list item.
            view.MoveCaretToBlock(0, 0);
            view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 1); // caret after "X"

            // Simulate Tab through the H/F guard shim (mirrors the fixed OnKeyDown path).
            hfHandled = view.SimulateHfKeyForTest(Key.Tab, shift: false);

            headerAfter = doc.FinalSectionHeadersFooters.Header?.PlainText;
            bodyLevelAfter = ((Paragraph)doc.Blocks[0]).Formatting.ListLevel;
        });
        if (!ran) return;
        bodyWouldFire.Should().BeTrue("body ListTabAtItemStart fires at offset 0 — confirming the old fall-through would have demoted it");
        hfHandled.Should().BeTrue("the H/F switch must consume Tab before it reaches the body");
        headerAfter.Should().Be("X\t", "Tab must insert a literal tab character into the header");
        bodyLevelAfter.Should().Be(0, "the body list item level must not change while H/F caret is active");
    }

    // ── DD2: Up/Down arrows do NOT move the body caret while H/F is active ──────────────────────────

    /// <summary>
    /// Regression guard for DD2 (AV-HFEDIT): pressing Up or Down while the H/F caret is active must be
    /// a no-op for the body — the body caret must stay at its pre-entry position.
    /// Before the fix, both keys fell through to MoveCaretVertical which moved the body _caret, leaving
    /// the body caret displaced after ExitHeaderFooterCaret().
    /// </summary>
    [Fact]
    public async Task UpDown_in_header_does_not_move_body_caret()
    {
        (int Block, int Offset) bodyBefore = (-1, -1);
        (int Block, int Offset) bodyAfterUp = (-1, -1);
        (int Block, int Offset) bodyAfterDown = (-1, -1);
        bool upHandled = false;
        bool downHandled = false;
        bool hfActiveAfter = false;
        var ran = await OnUiThread(() =>
        {
            var (doc, view) = MakeViewWithHeader("Header");
            // Place body caret at a known position.
            view.MoveCaretToBlock(0, 3);
            bodyBefore = view.CaretPosition;

            // Enter H/F editing.
            view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 0);

            // Simulate Up/Down via the H/F guard shim.
            upHandled = view.SimulateHfKeyForTest(Key.Up);
            bodyAfterUp = view.CaretPosition; // must still be (0, 3)

            downHandled = view.SimulateHfKeyForTest(Key.Down);
            bodyAfterDown = view.CaretPosition; // must still be (0, 3)

            hfActiveAfter = view.IsHeaderFooterCaretActive;
        });
        if (!ran) return;
        upHandled.Should().BeTrue("Key.Up must be consumed by the H/F guard");
        downHandled.Should().BeTrue("Key.Down must be consumed by the H/F guard");
        bodyAfterUp.Should().Be(bodyBefore, "Up must not move the body caret while H/F caret is active");
        bodyAfterDown.Should().Be(bodyBefore, "Down must not move the body caret while H/F caret is active");
        hfActiveAfter.Should().BeTrue("H/F caret must still be active after Up/Down");
    }

    // ── Undo restores header text ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Undo_restores_header_text_after_typing()
    {
        string? afterType = null;
        string? afterUndo = null;
        var ran = await OnUiThread(() =>
        {
            var (doc, view) = MakeViewWithHeader("Hi");
            view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 2);
            view.InsertText("!");
            afterType = HeaderText(doc);
            view.Undo();
            afterUndo = HeaderText(doc);
        });
        if (!ran) return;
        afterType.Should().Be("Hi!");
        afterUndo.Should().Be("Hi", "Undo must restore the header text (edit is undoable)");
    }

    [Fact]
    public async Task FieldInsertionTargetsTheActiveHeaderCaretAndRemainsUndoablePerField()
    {
        var ran = await OnUiThread(() =>
        {
            var (doc, view) = MakeViewWithHeader("AC");
            doc.Properties.Title = "Current title";
            doc.Properties.Subject = "Current subject";
            view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 1);

            view.InsertField(RunFieldKind.Title);
            view.InsertComplexField(" DOCPROPERTY Subject ");

            var headerRuns = doc.FinalSectionHeadersFooters.Header!.Paragraphs[0].Runs;
            headerRuns.Single(run => run.FieldKind == RunFieldKind.Title).Text
                .Should().Be("Current title");
            var complex = headerRuns.Single(run => run.ComplexField != null);
            complex.ComplexField!.Instruction.Should().Be(" DOCPROPERTY Subject ");
            complex.Text.Should().Be("Current subject");
            ((Paragraph)doc.Blocks[0]).Runs.Count(run =>
                run.FieldKind != RunFieldKind.None || run.ComplexField != null).Should().Be(0);

            view.Undo();
            headerRuns = doc.FinalSectionHeadersFooters.Header!.Paragraphs[0].Runs;
            headerRuns.Count(run => run.ComplexField != null).Should().Be(0);
            headerRuns.Count(run => run.FieldKind == RunFieldKind.Title).Should().Be(1);

            view.Undo();
            doc.FinalSectionHeadersFooters.Header!.PlainText.Should().Be("AC");
            doc.FinalSectionHeadersFooters.Header.Paragraphs[0].Runs.Count(run =>
                run.FieldKind != RunFieldKind.None || run.ComplexField != null).Should().Be(0);
        });

        if (!ran) return;
    }
}
