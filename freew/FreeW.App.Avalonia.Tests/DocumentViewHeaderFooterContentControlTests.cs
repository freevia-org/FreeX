using System;
using System.Linq;
using Avalonia.Controls;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-CCEDIT: a header/footer story is edited through its own atom round-trip, which knew about field
/// runs but not content controls — so the first keystroke anywhere in a header flattened a w:sdt in it
/// into ordinary text, and no lock was consulted at all. Word puts document-property controls (Title,
/// Author) in headers routinely, so this is the same data-loss and lock story as the body.
/// </summary>
public sealed class DocumentViewHeaderFooterContentControlTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Editing_the_header_around_a_field_keeps_the_field() =>
        await Session.Dispatch(
            () =>
            {
                var (document, view) = MakeViewWithHeaderControl(ContentControlLockMode.NotSpecified);

                // Type at the very start of the header, well away from the field.
                view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 0);
                view.InsertText("X");

                HeaderText(document).Should().Be("XTitle: Report");
                HeaderFields(document).Should().ContainSingle();
                HeaderFields(document)[0].Text.Should().Be("Report");
                HeaderFields(document)[0].Control!.Tag.Should().Be("DocTitle");
            },
            CancellationToken.None);

    [Fact]
    public async Task Typing_inside_a_header_field_extends_that_field() =>
        await Session.Dispatch(
            () =>
            {
                var (document, view) = MakeViewWithHeaderControl(ContentControlLockMode.NotSpecified);

                // "Title: Report" — the field occupies offsets 7..13; type inside it.
                view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 13);
                view.InsertText("s");

                HeaderText(document).Should().Be("Title: Reports");
                HeaderFields(document).Should().ContainSingle(
                    "typed characters join the field instead of splitting it into two w:sdt's");
                HeaderFields(document)[0].Text.Should().Be("Reports");
            },
            CancellationToken.None);

    [Fact]
    public async Task A_locked_header_field_refuses_typing_and_deletion() =>
        await Session.Dispatch(
            () =>
            {
                var (document, view) = MakeViewWithHeaderControl(ContentControlLockMode.ContentLocked);

                view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 10);
                view.InsertText("X");
                view.BackspaceForTest();
                view.DeleteForwardForTest();

                HeaderText(document).Should().Be("Title: Report");
                HeaderFields(document)[0].Text.Should().Be("Report");

                // The body text around it stays editable.
                view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 0);
                view.InsertText("X");
                HeaderText(document).Should().Be("XTitle: Report");
            },
            CancellationToken.None);

    [Fact]
    public async Task A_delete_locked_header_field_survives_a_backspace_that_would_empty_it() =>
        await Session.Dispatch(
            () =>
            {
                var (document, view) = MakeViewWithHeaderControl(ContentControlLockMode.ControlLocked);

                // Delete the field's characters one at a time from its end; the last one would remove the
                // run — and with it the w:sdt Word's sdtLocked protects.
                for (var index = 0; index < 6; index++)
                {
                    view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 13 - index);
                    view.BackspaceForTest();
                }

                HeaderFields(document).Should().ContainSingle("sdtLocked forbids removing the control");
                HeaderFields(document)[0].Text.Should().Be("R", "its text is editable, only its removal is not");
            },
            CancellationToken.None);

    [Fact]
    public async Task Typing_at_a_locked_header_fields_end_boundary_is_refused() =>
        await Session.Dispatch(
            () =>
            {
                var (document, view) = MakeViewWithHeaderControl(ContentControlLockMode.ContentLocked);

                // The field ends the paragraph, so its end boundary is also the line end: typed text
                // would be inherited INTO the field, which its content lock forbids.
                view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 13);
                view.InsertText("!");

                HeaderText(document).Should().Be("Title: Report");
                HeaderFields(document)[0].Text.Should().Be("Report");
            },
            CancellationToken.None);

    /// <summary>
    /// F2: the header/footer counterpart of DocumentViewContentControlKeyboardTests'
    /// Typing_into_a_placeholder_showing_field_clears_the_placeholder_flag -- HfInsertText must clear
    /// ContentControlWordMetadata.ShowingPlaceholder the moment real text is typed into a
    /// placeholder-showing header/footer field, exactly like the body's ApplyContentControlTextEdit
    /// already does, so a saved .docx does not keep w:showingPlcHdr on a field the user just filled in.
    /// </summary>
    [Fact]
    public async Task Typing_into_a_placeholder_showing_header_field_clears_the_placeholder_flag() =>
        await Session.Dispatch(
            () =>
            {
                var (document, view) = MakeViewWithHeaderControl(
                    ContentControlLockMode.NotSpecified,
                    PlaceholderHeaderControl());

                // "Title: Click to enter text" -- the field starts at offset 7; type at its end.
                view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 7 + "Click to enter text".Length);
                view.InsertText("!");

                var field = HeaderFields(document).Should().ContainSingle().Subject;
                field.Text.Should().Be("Click to enter text!");
                field.Control!.WordMetadata!.ShowingPlaceholder.Should().BeFalse(
                    "typing real text into a placeholder-showing header field must clear w:showingPlcHdr, matching the body fix");
            },
            CancellationToken.None);

    /// <summary>
    /// Sibling no-regression coverage for F2: typing in the header text AROUND an untouched
    /// placeholder-showing field must leave its flag alone -- HfSetAtoms rebuilds every run of the whole
    /// paragraph (including the field's) on every header edit, so a fix that cleared the flag
    /// unconditionally there, rather than only when the edit targets the field's own text, would wipe the
    /// flag out from edits that never touched the field at all.
    /// </summary>
    [Fact]
    public async Task Typing_in_header_text_elsewhere_leaves_an_untouched_placeholder_field_alone() =>
        await Session.Dispatch(
            () =>
            {
                var (document, view) = MakeViewWithHeaderControl(
                    ContentControlLockMode.NotSpecified,
                    PlaceholderHeaderControl());

                view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 0);
                view.InsertText("X");

                var field = HeaderFields(document).Should().ContainSingle().Subject;
                field.Text.Should().Be("Click to enter text");
                field.Control!.WordMetadata!.ShowingPlaceholder.Should().BeTrue(
                    "editing text elsewhere in the header must not clear an untouched field's placeholder flag");
            },
            CancellationToken.None);

    private static Run PlaceholderHeaderControl()
    {
        var control = Run.PlainTextControl("Click to enter text", tag: "DocTitle");
        control.Control = control.Control! with
        {
            WordMetadata = new ContentControlWordMetadata(ShowingPlaceholder: true)
        };
        return control;
    }

    [Fact]
    public async Task Hovering_a_header_field_shows_its_description() =>
        await Session.Dispatch(
            () =>
            {
                var (_, view) = MakeViewWithHeaderControl(ContentControlLockMode.NotSpecified);
                view.Measure(new Size(816, 4000));

                var region = view.ContentControlHeaderFooterRegionForTest();
                region.Should().NotBeNull("the header band must expose the field it renders");

                view.ContentControlHoverTipForTest(region!.Value.Center)
                    .Should().Be("Plain-text content control");
            },
            CancellationToken.None);


    /// <summary>
    /// AV-CCEDIT: a header field could be TYPED into but never OPERATED — every click gesture resolved
    /// its target through the body/table-cell hit test, so a check box in a header would not toggle, a
    /// list offered no choices and a date field no calendar. Word puts exactly these controls in headers.
    /// </summary>
    [Fact]
    public async Task A_check_box_in_a_header_toggles_when_it_is_activated() =>
        await Session.Dispatch(
            () =>
            {
                var (document, view) = MakeViewWithHeaderControl(
                    ContentControlLockMode.NotSpecified,
                    Run.CheckBoxControl(@checked: false, tag: "Approved"));

                view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 7);
                view.ActivateHfContentControlForTest().Should().BeTrue();

                var field = HeaderFields(document).Should().ContainSingle().Subject;
                field.Control!.Checked.Should().BeTrue();
                field.Text.Should().Be(FreeW.Core.Model.ContentControl.CheckedGlyph);
                field.Control.Tag.Should().Be("Approved", "the field must survive the atom round-trip");
            },
            CancellationToken.None);

    [Fact]
    public async Task A_locked_check_box_in_a_header_refuses_to_toggle() =>
        await Session.Dispatch(
            () =>
            {
                var (document, view) = MakeViewWithHeaderControl(
                    ContentControlLockMode.ContentLocked,
                    Run.CheckBoxControl(@checked: false, tag: "Approved"));

                view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 7);
                view.ActivateHfContentControlForTest().Should().BeFalse();

                HeaderFields(document).Should().ContainSingle().Which.Control!.Checked.Should().BeFalse();
            },
            CancellationToken.None);

    [Fact]
    public async Task A_date_field_in_a_header_opens_a_calendar_and_commits_the_picked_date() =>
        await Session.Dispatch(
            () =>
            {
                var (document, view) = MakeViewWithHeaderControl(
                    ContentControlLockMode.NotSpecified,
                    Run.DatePickerControl("2026-07-04", tag: "Signed", dateFormat: "yyyy-MM-dd"));

                view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 7);
                view.ActivateHfContentControlForTest().Should().BeTrue();

                var calendar = view.ActiveContentControlCalendarForTest
                    .Should().NotBeNull().And.Subject.As<Flyout>()
                    .Content.Should().BeOfType<StackPanel>().Subject
                    .Children.OfType<global::Avalonia.Controls.Calendar>().Single();
                calendar.SelectedDate.Should().Be(new DateTime(2026, 7, 4));

                calendar.SelectedDate = new DateTime(1999, 12, 31);

                HeaderText(document).Should().Be("Title: 1999-12-31");
                HeaderFields(document).Should().ContainSingle()
                    .Which.Control!.Kind.Should().Be(ContentControlKind.DatePicker);
            },
            CancellationToken.None);

    [Fact]
    public async Task A_drop_down_in_a_header_offers_its_items_and_the_pick_lands_in_the_header() =>
        await Session.Dispatch(
            () =>
            {
                var (document, view) = MakeViewWithHeaderControl(
                    ContentControlLockMode.NotSpecified,
                    Run.DropDownListControl(
                        [new ContentControlListItem("Red", "R"), new ContentControlListItem("Green", "G")],
                        tag: "Colour"));

                view.PlaceCaretInHeaderFooter(footer: false, paraIdx: 0, offset: 7);
                view.ActivateHfContentControlForTest().Should().BeTrue();

                var menu = view.ActiveContextMenuForTests.Should().NotBeNull().And.Subject.As<ContextMenu>();
                var green = menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Green");
                green.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

                HeaderText(document).Should().Be("Title: Green");
                HeaderFields(document).Should().ContainSingle().Which.Control!.Tag.Should().Be("Colour");
            },
            CancellationToken.None);
    private static (TextDocument Document, DocumentView View) MakeViewWithHeaderControl(
        ContentControlLockMode lockMode,
        Run? field = null)
    {
        var control = field ?? Run.PlainTextControl("Report", tag: "DocTitle");
        control.Control = control.Control! with { LockMode = lockMode };
        var header = new HeaderFooter();
        header.Paragraphs.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Title: "));
        paragraph.Runs.Add(control);
        header.Paragraphs.Add(paragraph);

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Body text."));
        document.FinalSectionHeadersFooters.Header = header;

        var view = new DocumentView();
        view.LoadDocument(document);
        view.Measure(new Size(816, 4000));
        return (document, view);
    }

    private static string HeaderText(TextDocument document) =>
        document.FinalSectionHeadersFooters.Header?.PlainText ?? string.Empty;

    private static List<Run> HeaderFields(TextDocument document) =>
        (document.FinalSectionHeadersFooters.Header?.Paragraphs ?? [])
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.Control is not null)
            .ToList();
}
