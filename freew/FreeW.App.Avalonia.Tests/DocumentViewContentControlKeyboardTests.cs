using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-CCEDIT: keyboard text editing INSIDE a content control. The Avalonia DocumentView is a fully
/// custom editor with no native-editor fallback, and its body-text paths reject any paragraph that
/// holds a control run — so without a dedicated path a Plain-Text / Rich-Text field could not be typed
/// into at all, least of all under "Filling in Forms" protection, whose whole purpose is to let the
/// user fill exactly those fields in. These are the Avalonia counterpart of the WPF host's
/// content-control keyboard-lock tests.
/// </summary>
public sealed class DocumentViewContentControlKeyboardTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private const string Prefix = "Name: ";   // offsets 0..6
    private const string Suffix = " (staff)"; // offsets 9..17
    private const int ControlStart = 6;       // "Bob" occupies 6..9
    private const int ControlEnd = 9;

    [Fact]
    public void Typing_inside_a_plain_text_control_edits_only_that_control_run()
    {
        var (view, paragraph) = BuildView(Run.PlainTextControl("Bob"));

        view.MoveCaretToBlockForTest(0, ControlEnd);
        view.InsertText("by");

        paragraph.Runs[0].Text.Should().Be(Prefix);
        paragraph.Runs[1].Text.Should().Be("Bobby");
        paragraph.Runs[1].Control!.Kind.Should().Be(ContentControlKind.PlainText);
        paragraph.Runs[2].Text.Should().Be(Suffix);
        view.CaretOffsetForTest.Should().Be(ControlEnd + 2);

        // Typing at the control's start edits the control, not the body text before it.
        view.MoveCaretToBlockForTest(0, ControlStart);
        view.InsertText("X");
        paragraph.Runs[0].Text.Should().Be(Prefix);
        paragraph.Runs[1].Text.Should().Be("XBobby");
    }

    [Fact]
    public void Typing_inside_a_rich_text_control_preserves_the_runs_other_marks()
    {
        var control = Run.RichTextControl("Bob", tag: "Applicant");
        control.HyperlinkUrl = "https://example.test/";
        control.HyperlinkTooltip = "preserved";
        control.CommentId = 4;
        control.Formatting = control.Formatting with { Bold = true };
        var (view, paragraph) = BuildView(control);

        view.MoveCaretToBlockForTest(0, ControlEnd);
        view.InsertText("!");

        paragraph.Runs[1].Text.Should().Be("Bob!");
        paragraph.Runs[1].Control!.Kind.Should().Be(ContentControlKind.RichText);
        paragraph.Runs[1].Control!.Tag.Should().Be("Applicant");
        paragraph.Runs[1].HyperlinkUrl.Should().Be("https://example.test/");
        paragraph.Runs[1].HyperlinkTooltip.Should().Be("preserved");
        paragraph.Runs[1].CommentId.Should().Be(4);
        paragraph.Runs[1].Formatting.Bold.Should().BeTrue();
    }

    [Fact]
    public void FillingForms_protection_still_lets_the_user_type_and_delete_in_a_form_field()
    {
        var (view, paragraph) = BuildView(Run.PlainTextControl("Bob"));
        view.SetProtection(ProtectionMode.FillingForms);
        view.IsEditingLocked.Should().BeTrue("Filling in Forms locks body editing");

        view.MoveCaretToBlockForTest(0, ControlEnd);
        view.SimulateTextInputForTest("by");
        paragraph.Runs[1].Text.Should().Be("Bobby");

        view.BackspaceForTest();
        paragraph.Runs[1].Text.Should().Be("Bobb");

        view.MoveCaretToBlockForTest(0, ControlStart + 1);
        view.DeleteForwardForTest();
        paragraph.Runs[1].Text.Should().Be("Bbb");

        // The edits are form-field mutations, so history stays available under forms protection.
        view.CanUndo.Should().BeTrue();
        view.Undo();
        paragraph.Runs[1].Text.Should().Be("Bobb");
        view.Redo();
        paragraph.Runs[1].Text.Should().Be("Bbb");

        // ...and the body text around the field is untouched throughout.
        paragraph.Runs[0].Text.Should().Be(Prefix);
        paragraph.Runs[2].Text.Should().Be(Suffix);
    }

    [Fact]
    public void FillingForms_protection_still_blocks_typing_in_the_body_text_around_the_field()
    {
        var (view, paragraph) = BuildView(Run.PlainTextControl("Bob"));
        view.SetProtection(ProtectionMode.FillingForms);

        view.MoveCaretToBlockForTest(0, 2);
        view.InsertText("Z");
        view.SimulateTextInputForTest("Z");
        view.BackspaceForTest();

        paragraph.Runs[0].Text.Should().Be(Prefix);
        paragraph.Runs[1].Text.Should().Be("Bob");
        paragraph.Runs[2].Text.Should().Be(Suffix);
    }

    [Theory]
    [InlineData(ProtectionMode.ReadOnly)]
    [InlineData(ProtectionMode.CommentsOnly)]
    public void Protection_modes_that_block_form_fields_block_typing_in_the_control(ProtectionMode mode)
    {
        var (view, paragraph) = BuildView(Run.PlainTextControl("Bob"));
        view.SetProtection(mode);

        view.MoveCaretToBlockForTest(0, ControlEnd);
        view.InsertText("!");
        view.SimulateTextInputForTest("!");
        view.BackspaceForTest();
        view.DeleteForwardForTest();

        paragraph.Runs[1].Text.Should().Be("Bob");
    }

    [Fact]
    public void MarkedAsFinal_blocks_typing_in_the_control()
    {
        var (view, paragraph) = BuildView(Run.PlainTextControl("Bob"));
        view.SetMarkedAsFinal(true);

        view.MoveCaretToBlockForTest(0, ControlEnd);
        view.InsertText("!");
        paragraph.Runs[1].Text.Should().Be("Bob");

        view.SetMarkedAsFinal(false);
        view.InsertText("!");
        paragraph.Runs[1].Text.Should().Be("Bob!");
    }

    [Theory]
    [InlineData(ContentControlLockMode.NotSpecified, true)]
    [InlineData(ContentControlLockMode.Unlocked, true)]
    [InlineData(ContentControlLockMode.ControlLocked, true)]
    [InlineData(ContentControlLockMode.ContentLocked, false)]
    [InlineData(ContentControlLockMode.ControlAndContentLocked, false)]
    public void Typing_honors_the_controls_own_content_lock(ContentControlLockMode lockMode, bool editable)
    {
        var control = Run.PlainTextControl("Bob");
        control.Control = control.Control! with { LockMode = lockMode };
        var (view, paragraph) = BuildView(control);

        view.MoveCaretToBlockForTest(0, ControlEnd);
        view.InsertText("!");

        paragraph.Runs[1].Text.Should().Be(editable ? "Bob!" : "Bob");
    }

    [Fact]
    public void Controls_that_own_their_text_ignore_typing()
    {
        // A check box, date picker and drop-down list carry generated text (glyph / formatted date /
        // picked item); Word only changes those through the control's own interaction.
        AssertIgnoresTyping(Run.CheckBoxControl(@checked: false));
        AssertIgnoresTyping(Run.DatePickerControl("2026-01-01", dateFormat: "yyyy-MM-dd"));
        AssertIgnoresTyping(Run.DropDownListControl(
            [new ContentControlListItem("Red", "R"), new ContentControlListItem("Green", "G")]));
    }

    [Fact]
    public void A_combo_box_accepts_typed_text()
    {
        var combo = Run.ComboBoxControl(
            [new ContentControlListItem("Red", "R")],
            selectedText: "Red");
        var (view, paragraph) = BuildView(combo);

        view.MoveCaretToBlockForTest(0, ControlStart + 3);
        view.InsertText("dish");

        paragraph.Runs[1].Text.Should().Be("Reddish");
        paragraph.Runs[1].Control!.Items.Should().HaveCount(1, "the control's item list survives typing");
    }

    [Fact]
    public void Deleting_never_reaches_past_the_controls_own_boundaries()
    {
        var (view, paragraph) = BuildView(Run.PlainTextControl("Bob"));
        view.SetProtection(ProtectionMode.FillingForms);

        view.MoveCaretToBlockForTest(0, ControlStart);
        view.BackspaceForTest();
        paragraph.Runs[0].Text.Should().Be(Prefix, "Backspace at the field start must not eat body text");

        view.MoveCaretToBlockForTest(0, ControlEnd);
        view.DeleteForwardForTest();
        paragraph.Runs[2].Text.Should().Be(Suffix, "Delete at the field end must not eat body text");
        paragraph.Runs[1].Text.Should().Be("Bob");
        paragraph.Runs.Should().HaveCount(3);
    }

    [Fact]
    public void A_selection_inside_the_control_is_replaced_but_one_that_spills_out_is_refused()
    {
        var (view, paragraph) = BuildView(Run.PlainTextControl("Bob"));
        view.SetProtection(ProtectionMode.FillingForms);

        view.SetBodySelectionForTest(0, ControlStart + 1, 0, ControlEnd);
        view.InsertText("ill");
        paragraph.Runs[1].Text.Should().Be("Bill");
        view.CaretOffsetForTest.Should().Be(ControlStart + 4);

        // A selection that starts in the surrounding body text is not a form-field edit.
        view.SetBodySelectionForTest(0, ControlStart - 2, 0, ControlEnd);
        view.InsertText("nope");
        paragraph.Runs[0].Text.Should().Be(Prefix);
        paragraph.Runs[1].Text.Should().Be("Bill");
    }

    [Fact]
    public void Arrow_keys_walk_the_caret_through_a_content_control_paragraph()
    {
        var (view, _) = BuildView(Run.PlainTextControl("Bob"));
        view.SetProtection(ProtectionMode.FillingForms);

        view.MoveCaretToBlockForTest(0, ControlStart);
        view.MoveCaretHorizontalForTest(+1);
        view.CaretOffsetForTest.Should().Be(ControlStart + 1);

        view.MoveCaretHorizontalForTest(-1);
        view.CaretOffsetForTest.Should().Be(ControlStart);

        // Typing after the arrow keys lands where the caret is, inside the field.
        view.MoveCaretHorizontalForTest(+2);
        view.InsertText("-");
        view.Document.Blocks.OfType<Paragraph>().Single().Runs[1].Text.Should().Be("Bo-b");
    }

    // Placing a caret in a cell forces a table layout, which needs the headless font manager.
    [Fact]
    public async Task A_content_control_in_a_table_cell_is_typable_under_forms_protection() =>
        await Session.Dispatch(TypeInTableCellContentControl, CancellationToken.None);

    private static void TypeInTableCellContentControl()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Clear();
        var cellParagraph = new Paragraph();
        cellParagraph.Runs.Add(Run.PlainTextControl("Bob"));
        cell.Paragraphs.Add(cellParagraph);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadDocument(document);
        view.SetProtection(ProtectionMode.FillingForms);

        view.PlaceCaretInCell(0, 0, 0, 0, 3);
        view.InsertText("by");
        cellParagraph.Runs[0].Text.Should().Be("Bobby");
        cellParagraph.Runs[0].Control!.Kind.Should().Be(ContentControlKind.PlainText);

        view.BackspaceForTest();
        cellParagraph.Runs[0].Text.Should().Be("Bobb");

        view.CanUndo.Should().BeTrue();
        view.Undo();
        cellParagraph.Runs[0].Text.Should().Be("Bobby");
    }

    [Fact]
    public void Tab_moves_between_fields_under_forms_protection_instead_of_typing_a_tab()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var first = new Paragraph();
        first.Runs.Add(new Run("A: "));
        first.Runs.Add(Run.PlainTextControl("one", tag: "One"));
        var second = new Paragraph();
        second.Runs.Add(new Run("B: "));
        second.Runs.Add(Run.PlainTextControl("two", tag: "Two"));
        document.Blocks.Add(first);
        document.Blocks.Add(second);

        var view = new DocumentView();
        view.LoadDocument(document);
        view.SetProtection(ProtectionMode.FillingForms);

        view.MoveCaretToBlockForTest(0, 0);
        view.SimulateKeyForTest(Key.Tab);
        // The whole field is selected, ready to be typed over.
        view.CaretBlockForTest.Should().Be(0);
        view.CaretOffsetForTest.Should().Be(6);
        view.SelectedText.Should().Be("one");

        view.SimulateKeyForTest(Key.Tab);
        view.CaretBlockForTest.Should().Be(1);
        view.SelectedText.Should().Be("two");

        // Wrapping around at the last field, and back again with Shift+Tab.
        view.SimulateKeyForTest(Key.Tab);
        view.CaretBlockForTest.Should().Be(0);
        view.SimulateKeyForTest(Key.Tab, shift: true);
        view.CaretBlockForTest.Should().Be(1);

        view.PlainText.Should().Be("A: one\nB: two", "Tab must not type a literal tab into a field");
    }

    /// <summary>
    /// K3: a placeholder-showing field (w:showingPlcHdr) stops showing placeholder text the moment the
    /// user actually edits its own text -- typing over it or deleting from it -- matching Word, which
    /// drops the flag on any edit even if the result is empty. Before the fix, ApplyContentControlTextEdit
    /// carried caret.Control (and the source runs cloned from the field) through unchanged, so a
    /// freshly-typed-into field still reported itself as showing placeholder text.
    /// </summary>
    [Fact]
    public void Typing_into_a_placeholder_showing_field_clears_the_placeholder_flag()
    {
        var control = PlaceholderControl("Click to enter text");
        var (view, paragraph) = BuildView(control);

        view.MoveCaretToBlockForTest(0, ControlStart + control.Text.Length);
        view.InsertText("!");

        paragraph.Runs.Should().HaveCount(3,
            "the edit must not split the field's own run into two (SetRuns groups by Control reference)");
        paragraph.Runs[1].Text.Should().Be("Click to enter text!");
        paragraph.Runs[1].Control!.WordMetadata!.ShowingPlaceholder.Should().BeFalse(
            "typing real text into a placeholder-showing field must clear w:showingPlcHdr");
    }

    [Fact]
    public void Backspacing_a_placeholder_showing_field_also_clears_the_placeholder_flag()
    {
        var control = PlaceholderControl("Click to enter text");
        var (view, paragraph) = BuildView(control);

        view.MoveCaretToBlockForTest(0, ControlStart + control.Text.Length);
        view.BackspaceForTest();

        paragraph.Runs[1].Control!.WordMetadata!.ShowingPlaceholder.Should().BeFalse(
            "deleting inside a placeholder-showing field also counts as editing it, per Word");
    }

    /// <summary>
    /// Sibling no-regression coverage: typing in the BODY TEXT around an untouched placeholder-showing
    /// field must leave its flag alone. SetRuns rebuilds every run of the whole paragraph (including the
    /// field's) on every body edit, so a fix that cleared the flag unconditionally there -- rather than in
    /// ApplyContentControlTextEdit, which only runs for an edit to the field's OWN text -- would wipe the
    /// flag out from edits that never touched the field at all.
    /// </summary>
    [Fact]
    public void Typing_in_body_text_elsewhere_leaves_an_untouched_placeholder_field_alone()
    {
        var control = PlaceholderControl("Click to enter text");
        var (view, paragraph) = BuildView(control);

        view.MoveCaretToBlockForTest(0, 0);
        view.InsertText("Z");

        paragraph.Runs.Should().HaveCount(3, "typing elsewhere must not split or merge the field's own run");
        paragraph.Runs[0].Text.Should().Be("Z" + Prefix);
        paragraph.Runs[1].Text.Should().Be("Click to enter text");
        paragraph.Runs[1].Control!.WordMetadata!.ShowingPlaceholder.Should().BeTrue(
            "editing text elsewhere in the paragraph must not clear an untouched field's placeholder flag");
    }

    /// <summary>
    /// F3: real Word selects the entire placeholder run the instant a plain-text/rich-text content
    /// control showing w:showingPlcHdr is entered by CLICK, just as it already does for Tab (see
    /// Tab_moves_between_fields_under_forms_protection_instead_of_typing_a_tab). Before the fix,
    /// TryActivateContentControl only special-cased CheckBox/DatePicker/DropDownList/ComboBox and fell
    /// through to ordinary click-to-position-caret for PlainText/RichText, so a click mid-placeholder left
    /// an unselected caret there and the next keystroke spliced into the middle of the placeholder wording.
    /// </summary>
    // Measuring/hit-testing forces a text layout, which needs the headless font manager (see
    // A_content_control_in_a_table_cell_is_typable_under_forms_protection above).
    [Fact]
    public async Task Clicking_into_a_placeholder_showing_field_selects_the_whole_placeholder() =>
        await Session.Dispatch(ClickPlaceholderFieldSelectsItWhole, CancellationToken.None);

    private static void ClickPlaceholderFieldSelectsItWhole()
    {
        var control = PlaceholderControl("Click to enter text");
        var (view, paragraph) = BuildView(control);
        view.Measure(new Size(816, 4000));

        // Click strictly inside the placeholder wording, not at either edge.
        view.MoveCaretToBlockForTest(0, ControlStart + 5);
        var clickPoint = view.CaretRectForTest!.Value.Center;

        view.ActivateContentControlAtForTest(clickPoint).Should().BeTrue();
        view.SelectedText.Should().Be("Click to enter text",
            "clicking a placeholder-showing field must select its whole run, like Tab already does, so " +
            "the first keystroke replaces it instead of splicing into the middle of the placeholder wording");

        // And the practical consequence: typing now REPLACES the placeholder instead of merging with it.
        view.InsertText("Bobby");
        paragraph.Runs[1].Text.Should().Be("Bobby");
    }

    /// <summary>
    /// Sibling no-regression coverage for F3: a field that is NOT showing its placeholder (i.e. already
    /// filled in) must keep ordinary click-to-position-caret -- select-all on every click into a filled
    /// field would make it impossible to position the caret for a normal in-place edit, which is worse
    /// than the bug being fixed. Real Word only select-alls a placeholder run, not filled content.
    /// </summary>
    [Fact]
    public async Task Clicking_into_an_already_filled_field_does_not_select_it_all() =>
        await Session.Dispatch(ClickFilledFieldDoesNotSelectItAll, CancellationToken.None);

    private static void ClickFilledFieldDoesNotSelectItAll()
    {
        var control = Run.PlainTextControl("Click to enter text"); // no WordMetadata -> not a placeholder
        var (view, _) = BuildView(control);
        view.Measure(new Size(816, 4000));

        view.MoveCaretToBlockForTest(0, ControlStart + 5);
        var clickPoint = view.CaretRectForTest!.Value.Center;

        view.ActivateContentControlAtForTest(clickPoint).Should().BeFalse(
            "a filled plain-text field falls through to ordinary click-to-position-caret");
        view.SelectedText.Should().BeEmpty("clicking a filled field must not select its whole run");
    }

    private static Run PlaceholderControl(string text) => new(text)
    {
        Control = new ContentControl(
            ContentControlKind.PlainText,
            WordMetadata: new ContentControlWordMetadata(ShowingPlaceholder: true))
    };

    private static void AssertIgnoresTyping(Run control)
    {
        var original = control.Text;
        var (view, paragraph) = BuildView(control);

        view.MoveCaretToBlockForTest(0, ControlStart + original.Length);
        view.InsertText("!");
        view.BackspaceForTest();

        paragraph.Runs[1].Text.Should().Be(original);
    }

    private static (DocumentView View, Paragraph Paragraph) BuildView(Run control)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(Prefix));
        paragraph.Runs.Add(control);
        paragraph.Runs.Add(new Run(Suffix));

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadDocument(document);
        return (view, paragraph);
    }
}
