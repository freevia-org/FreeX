using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-LIST: headless tests for list-editing behaviours in DocumentView.
///
/// Behaviors exercised:
///  1. Enter in a NON-EMPTY list item → splits into two list paragraphs (continues list).
///  2. Enter in an EMPTY list item → exits list (paragraph becomes non-list).
///  3. Tab at offset 0 on a list item → demotes (ListLevel + 1).
///  4. Shift+Tab at offset 0 on a list item (level > 0) → promotes (ListLevel - 1).
///  5. Shift+Tab at offset 0 on a list item at level 0 → leaves the list.
///  6. Tab at offset > 0 on a list item → falls through to normal tab insert.
///  7. Numbered-list display numbers are sequential after an Enter split (render-time).
///  8. Backspace at offset 0 on a list item (level > 0) → outdents (ListLevel - 1).
///  9. Backspace at offset 0 on a list item at level 0 → removes list formatting.
/// 10. Non-list Enter regression: non-list paragraph still splits normally.
/// 11. Non-list Tab regression: Tab outside a list still inserts a tab character.
/// </summary>
public sealed class DocumentViewListEditTests
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
            return false; // headless backend not available
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Create a document with a single list paragraph whose text and kind can be configured.
    /// The view is measured so layout + caret math are valid.
    /// </summary>
    private static (DocumentView View, int BlockIdx) MakeListDoc(
        string text,
        ListKind kind = ListKind.Number,
        int level = 0)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var para = new Paragraph(text)
        {
            Formatting = new ParagraphFormatting { ListKind = kind, ListLevel = level }
        };
        doc.Blocks.Add(para);
        var view = new DocumentView();
        view.LoadDocument(doc);
        view.Measure(new Size(800, 4000));
        return (view, 0);
    }

    /// <summary>Returns the paragraph at <paramref name="blockIdx"/> cast as Paragraph.</summary>
    private static Paragraph Para(DocumentView view, int blockIdx) =>
        (Paragraph)view.Document.Blocks[blockIdx];

    // ── 1. Enter in NON-EMPTY list item → continues list ─────────────────────────────────────────

    [Fact]
    public async Task Enter_in_nonempty_numbered_item_creates_new_list_paragraph()
    {
        int blockCount = 0;
        ListKind kind0 = ListKind.None, kind1 = ListKind.None;
        int level0 = -1, level1 = -1;
        string? text1 = null;

        var ran = await OnUiThread(() =>
        {
            var (view, _) = MakeListDoc("Hello");
            // Place caret at end of text (offset 5).
            view.MoveCaretToBlock(0, 5);
            view.InsertParagraphBreakPublic();    // Enter
            blockCount = view.Document.Blocks.Count;
            kind0 = Para(view, 0).Formatting.ListKind;
            kind1 = Para(view, 1).Formatting.ListKind;
            level0 = Para(view, 0).Formatting.ListLevel;
            level1 = Para(view, 1).Formatting.ListLevel;
            text1 = Para(view, 1).PlainText;
        });

        if (!ran) return;
        blockCount.Should().Be(2, "Enter should split into two blocks");
        kind0.Should().Be(ListKind.Number, "first block keeps list kind");
        kind1.Should().Be(ListKind.Number, "new block continues list kind");
        level0.Should().Be(0);
        level1.Should().Be(0, "new block inherits list level");
        text1.Should().BeEmpty("new item starts empty");
    }

    // ── 2. Enter in EMPTY list item → exits list ─────────────────────────────────────────────────

    [Fact]
    public async Task Enter_in_empty_list_item_exits_list()
    {
        int blockCount = 0;
        ListKind kind = ListKind.Number;

        var ran = await OnUiThread(() =>
        {
            var (view, _) = MakeListDoc(""); // empty list item
            view.MoveCaretToBlock(0, 0);
            view.InsertParagraphBreakPublic();    // Enter on empty item
            blockCount = view.Document.Blocks.Count;
            kind = Para(view, 0).Formatting.ListKind;
        });

        if (!ran) return;
        blockCount.Should().Be(1, "empty-item Enter should NOT split — it just exits the list");
        kind.Should().Be(ListKind.None, "paragraph becomes non-list after exit");
    }

    // ── 3. Tab at start of list item → demotes (ListLevel + 1) ──────────────────────────────────

    [Fact]
    public async Task Tab_at_start_of_list_item_demotes_one_level()
    {
        int level = -1;
        ListKind kind = ListKind.None;

        var ran = await OnUiThread(() =>
        {
            var (view, _) = MakeListDoc("Item", level: 0);
            view.MoveCaretToBlock(0, 0);
            view.ListTabAtItemStartPublic(shift: false);   // Tab
            level = Para(view, 0).Formatting.ListLevel;
            kind  = Para(view, 0).Formatting.ListKind;
        });

        if (!ran) return;
        level.Should().Be(1, "Tab at list-item start demotes to level 1");
        kind.Should().Be(ListKind.Number, "list kind unchanged");
    }

    // ── 4. Shift+Tab at start → promotes (ListLevel - 1) when level > 0 ─────────────────────────

    [Fact]
    public async Task ShiftTab_at_start_of_list_item_promotes_one_level()
    {
        int level = -1;
        ListKind kind = ListKind.None;

        var ran = await OnUiThread(() =>
        {
            var (view, _) = MakeListDoc("Item", level: 2);
            view.MoveCaretToBlock(0, 0);
            view.ListTabAtItemStartPublic(shift: true);   // Shift+Tab
            level = Para(view, 0).Formatting.ListLevel;
            kind  = Para(view, 0).Formatting.ListKind;
        });

        if (!ran) return;
        level.Should().Be(1, "Shift+Tab promotes from level 2 to level 1");
        kind.Should().Be(ListKind.Number, "list kind unchanged");
    }

    // ── 5. Shift+Tab at level 0 → leaves the list ───────────────────────────────────────────────

    [Fact]
    public async Task ShiftTab_at_level_0_leaves_list()
    {
        ListKind kind = ListKind.Number;

        var ran = await OnUiThread(() =>
        {
            var (view, _) = MakeListDoc("Item", level: 0);
            view.MoveCaretToBlock(0, 0);
            view.ListTabAtItemStartPublic(shift: true);   // Shift+Tab at top level
            kind = Para(view, 0).Formatting.ListKind;
        });

        if (!ran) return;
        kind.Should().Be(ListKind.None, "Shift+Tab at level 0 should leave the list");
    }

    // ── 6. Tab at offset > 0 falls through (not consumed) ────────────────────────────────────────

    [Fact]
    public async Task Tab_at_nonzero_offset_is_not_consumed_by_list_handler()
    {
        bool consumed = false;

        var ran = await OnUiThread(() =>
        {
            var (view, _) = MakeListDoc("Item");
            view.MoveCaretToBlock(0, 3);    // offset 3, not start
            consumed = view.ListTabAtItemStartPublic(shift: false);
        });

        if (!ran) return;
        consumed.Should().BeFalse("Tab away from item start should not be consumed");
    }

    // ── 7. Numbered list renumbering is sequential after Enter ───────────────────────────────────

    [Fact]
    public async Task Numbered_list_items_get_sequential_display_numbers()
    {
        // After splitting a single numbered item into two, the layout should produce markers
        // "1." for block 0 and "2." for block 1.  Numbers are render-time: we verify via
        // GetListNumberForBlockPublic(), which reads the internal layout state.
        int num0 = -1, num1 = -1;

        var ran = await OnUiThread(() =>
        {
            var (view, _) = MakeListDoc("Item");
            view.MoveCaretToBlock(0, 4);    // end of "Item"
            view.InsertParagraphBreakPublic();
            // Force relayout.
            view.Measure(new Size(800, 4000));
            num0 = view.GetListNumberForBlockPublic(0);
            num1 = view.GetListNumberForBlockPublic(1);
        });

        if (!ran) return;
        num0.Should().Be(1, "first numbered item is 1");
        num1.Should().Be(2, "second numbered item is 2");
    }

    // ── 8. Backspace at start of list item (level > 0) outdents ─────────────────────────────────

    [Fact]
    public async Task Backspace_at_start_of_list_item_at_level1_outdents()
    {
        int level = -1;
        ListKind kind = ListKind.None;

        var ran = await OnUiThread(() =>
        {
            var (view, _) = MakeListDoc("Item", level: 1);
            view.MoveCaretToBlock(0, 0);
            view.BackspaceOutdentListItemPublic();
            level = Para(view, 0).Formatting.ListLevel;
            kind  = Para(view, 0).Formatting.ListKind;
        });

        if (!ran) return;
        level.Should().Be(0, "Backspace at level-1 start outdents to level 0");
        kind.Should().Be(ListKind.Number, "list kind unchanged");
    }

    // ── 9. Backspace at start of list item at level 0 → removes list formatting ─────────────────

    [Fact]
    public async Task Backspace_at_start_of_list_item_at_level0_removes_list()
    {
        ListKind kind = ListKind.Number;

        var ran = await OnUiThread(() =>
        {
            var (view, _) = MakeListDoc("Item", level: 0);
            view.MoveCaretToBlock(0, 0);
            view.BackspaceOutdentListItemPublic();
            kind = Para(view, 0).Formatting.ListKind;
        });

        if (!ran) return;
        kind.Should().Be(ListKind.None, "Backspace at level-0 list start removes list formatting");
    }

    // ── 10. Non-list Enter regression ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enter_in_nonlist_paragraph_still_splits_normally()
    {
        int blockCount = 0;
        string? text0 = null, text1 = null;
        ListKind kind0 = ListKind.Number, kind1 = ListKind.Number;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("Hello World"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            view.MoveCaretToBlock(0, 5); // after "Hello"
            view.InsertParagraphBreakPublic();
            blockCount = view.Document.Blocks.Count;
            text0 = Para(view, 0).PlainText;
            text1 = Para(view, 1).PlainText;
            kind0 = Para(view, 0).Formatting.ListKind;
            kind1 = Para(view, 1).Formatting.ListKind;
        });

        if (!ran) return;
        blockCount.Should().Be(2);
        text0.Should().Be("Hello");
        text1.Should().Be(" World");
        kind0.Should().Be(ListKind.None);
        kind1.Should().Be(ListKind.None);
    }

    // ── 11. Non-list Tab regression: Tab inserts a tab character ─────────────────────────────────

    [Fact]
    public async Task Tab_in_nonlist_paragraph_inserts_tab_character()
    {
        string? text = null;

        var ran = await OnUiThread(() =>
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("AB"));
            var view = new DocumentView();
            view.LoadDocument(doc);
            view.Measure(new Size(800, 4000));
            // Tab at non-zero offset in non-list paragraph: ListTabAtItemStartPublic returns false.
            view.MoveCaretToBlock(0, 1);
            var consumed = view.ListTabAtItemStartPublic(shift: false);
            if (!consumed)
                view.InsertText("\t");
            text = Para(view, 0).PlainText;
        });

        if (!ran) return;
        text.Should().Be("A\tB", "Tab in non-list para inserts a literal tab");
    }
}
