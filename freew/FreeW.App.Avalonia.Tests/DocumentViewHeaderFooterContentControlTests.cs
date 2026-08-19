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

    private static (TextDocument Document, DocumentView View) MakeViewWithHeaderControl(
        ContentControlLockMode lockMode)
    {
        var control = Run.PlainTextControl("Report", tag: "DocTitle");
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
