using System.IO;
using System.Threading;
using Avalonia.Headless;
using System.IO.Compression;
using System.Xml.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// AV-CCEDIT: the body text AROUND a content control is ordinary editable text, as in Word. That
/// requires the field to survive the renderer's ParaCells → SetRuns round-trip (a <see cref="Cell"/>
/// carries the control, and runs re-segment on a control boundary), and it requires the structural
/// gestures — Enter, paragraph merge, selection delete — to keep one w:sdt one w:sdt.
/// </summary>
public sealed class DocumentViewContentControlBodyEditingTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    private const string Prefix = "Name: ";   // offsets 0..6
    private const string Suffix = " (staff)"; // offsets 9..17
    private const int ControlStart = 6;       // "Bob" occupies 6..9
    private const int ControlEnd = 9;

    [Fact]
    public void Typing_before_and_after_a_field_edits_the_body_text_and_keeps_the_field()
    {
        var (view, paragraph) = BuildView();

        view.MoveCaretToBlockForTest(0, 0);
        view.InsertText("Dr. ");
        view.PlainText.Should().Be("Dr. Name: Bob (staff)");

        view.MoveCaretToBlockForTest(0, view.PlainText.Length);
        view.InsertText("!");
        view.PlainText.Should().Be("Dr. Name: Bob (staff)!");

        Field(paragraph).Text.Should().Be("Bob");
        Field(paragraph).Control!.Kind.Should().Be(ContentControlKind.PlainText);
        Field(paragraph).Control!.Tag.Should().Be("Applicant");
    }

    [Fact]
    public void Backspace_and_Delete_remove_body_characters_and_keep_the_field()
    {
        var (view, paragraph) = BuildView();

        view.MoveCaretToBlockForTest(0, 3);
        view.BackspaceForTest();
        view.PlainText.Should().Be("Nae: Bob (staff)");

        // Delete at the field's end boundary removes the character AFTER the field, not inside it.
        view.MoveCaretToBlockForTest(0, ControlEnd - 1);
        view.DeleteForwardForTest();
        view.PlainText.Should().Be("Nae: Bob(staff)");
        Field(paragraph).Text.Should().Be("Bob");

        view.CanUndo.Should().BeTrue();
        view.Undo();
        view.Undo();
        view.PlainText.Should().Be("Name: Bob (staff)");
        Fields(paragraph).Should().ContainSingle();
    }

    [Fact]
    public void An_edit_elsewhere_in_the_paragraph_keeps_the_field_a_separate_run()
    {
        var (view, paragraph) = BuildView();

        view.MoveCaretToBlockForTest(0, 0);
        view.InsertText("X");

        var fields = Fields(paragraph);
        fields.Should().ContainSingle();
        fields[0].Text.Should().Be("Bob");
        paragraph.Runs.Where(run => run.Control is null).Select(run => run.Text)
            .Should().NotContain(text => text.Contains("Bob"), "the field's text must not leak into a body run");
    }

    [Fact]
    public void Two_identically_configured_adjacent_fields_stay_two_fields_across_an_edit()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.PlainTextControl("A", tag: "Same"));
        paragraph.Runs.Add(Run.PlainTextControl("A", tag: "Same"));
        paragraph.Runs.Add(new Run(" tail"));
        var view = LoadParagraph(paragraph);

        view.MoveCaretToBlockForTest(0, paragraph.PlainText.Length);
        view.InsertText("!");

        var fields = Fields(paragraph);
        fields.Should().HaveCount(2, "record-equal controls are still two separate w:sdt's");
        ReferenceEquals(fields[0].Control, fields[1].Control).Should().BeFalse();
    }

    [Fact]
    public void An_emptied_field_survives_a_later_edit_elsewhere_in_the_paragraph()
    {
        var (view, paragraph) = BuildView();

        // Clear the field's own content (a selection covering exactly the field).
        view.SetBodySelectionForTest(0, ControlStart, 0, ControlEnd);
        view.BackspaceForTest();
        Field(paragraph).Text.Should().BeEmpty();
        view.PlainText.Should().Be("Name:  (staff)");

        view.MoveCaretToBlockForTest(0, 0);
        view.InsertText("X");

        Fields(paragraph).Should().ContainSingle("an empty field contributes no cells and must be preserved positionally");
        view.PlainText.Should().Be("XName:  (staff)");
    }

    [Fact]
    public void A_selection_that_spans_out_of_the_field_deletes_the_field_with_the_text()
    {
        var (view, paragraph) = BuildView();

        view.SetBodySelectionForTest(0, ControlStart - 1, 0, ControlEnd + 1);
        view.BackspaceForTest();

        view.PlainText.Should().Be("Name:(staff)");
        Fields(paragraph).Should().BeEmpty("Word deletes a field that is fully inside the deleted range");
    }

    [Fact]
    public void Formatting_a_range_that_covers_the_field_keeps_the_field()
    {
        var (view, paragraph) = BuildView();

        view.SetBodySelectionForTest(0, 0, 0, paragraph.PlainText.Length);
        view.ToggleBold();

        var fields = Fields(paragraph);
        fields.Should().ContainSingle();
        fields[0].Text.Should().Be("Bob");
        fields[0].Formatting.Bold.Should().BeTrue();
        paragraph.Runs.All(run => run.Formatting.Bold == true).Should().BeTrue();
    }

    [Fact]
    public void Enter_inside_the_field_is_ignored_but_at_its_boundary_splits_the_paragraph()
    {
        var (view, paragraph) = BuildView();

        view.MoveCaretToBlockForTest(0, ControlStart + 1);
        view.InsertParagraphBreakForTest();
        view.Document.Blocks.Should().HaveCount(1, "a break inside a field would duplicate the w:sdt");
        Fields(paragraph).Should().ContainSingle();
        Field(paragraph).Text.Should().Be("Bob");

        view.MoveCaretToBlockForTest(0, ControlEnd);
        view.InsertParagraphBreakForTest();
        view.Document.Blocks.Should().HaveCount(2);
        var first = (Paragraph)view.Document.Blocks[0];
        var second = (Paragraph)view.Document.Blocks[1];
        Fields(first).Should().ContainSingle();
        Field(first).Text.Should().Be("Bob");
        second.PlainText.Should().Be(Suffix);
        Fields(second).Should().BeEmpty();
    }

    [Fact]
    public void Merging_paragraphs_over_a_backspace_keeps_the_field()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var first = new Paragraph();
        first.Runs.Add(new Run("Head"));
        var second = new Paragraph();
        second.Runs.Add(Run.PlainTextControl("Bob", tag: "Applicant"));
        second.Runs.Add(new Run(" tail"));
        document.Blocks.Add(first);
        document.Blocks.Add(second);
        var view = new DocumentView();
        view.LoadDocument(document);

        view.MoveCaretToBlockForTest(1, 0);
        view.BackspaceForTest();

        view.Document.Blocks.Should().HaveCount(1);
        var merged = (Paragraph)view.Document.Blocks[0];
        merged.PlainText.Should().Be("HeadBob tail");
        Fields(merged).Should().ContainSingle();
        Field(merged).Text.Should().Be("Bob");
    }

    [Fact]
    public void Tracked_changes_record_body_edits_around_the_field()
    {
        var (view, paragraph) = BuildView();
        view.ToggleTrackChanges().Should().BeTrue();

        view.MoveCaretToBlockForTest(0, 0);
        view.InsertText("X");
        view.MoveCaretToBlockForTest(0, paragraph.PlainText.Length);
        view.BackspaceForTest();

        paragraph.Runs.Where(run => run.Revision == RevisionKind.Inserted).Select(run => run.Text)
            .Should().Equal("X");
        // A tracked delete strikes the character through instead of removing it.
        paragraph.Runs.Where(run => run.Revision == RevisionKind.Deleted).Select(run => run.Text)
            .Should().Equal([")"]);
        Fields(paragraph).Should().ContainSingle();
        Field(paragraph).Text.Should().Be("Bob");
    }

    [Fact]
    public void Tracked_changes_record_edits_inside_the_field_without_splitting_it()
    {
        var (view, paragraph) = BuildView();
        view.ToggleTrackChanges();

        view.MoveCaretToBlockForTest(0, ControlEnd);
        view.InsertText("by");

        var fields = Fields(paragraph);
        fields.Should().HaveCount(2, "the insertion is its own run inside the same field");
        fields.Select(run => run.Text).Should().Equal("Bob", "by");
        fields.Select(run => run.Revision).Should().Equal(RevisionKind.None, RevisionKind.Inserted);
        fields.Select(run => run.Control).Distinct().Should().ContainSingle(
            "one control instance keeps the runs inside a single w:sdt on save");

        view.MoveCaretToBlockForTest(0, ControlStart + 1);
        view.DeleteForwardForTest();
        var struck = Fields(paragraph).Where(run => run.Revision == RevisionKind.Deleted).ToList();
        struck.Select(run => run.Text).Should().Equal("o");
        Fields(paragraph).Select(run => run.Control).Distinct().Should().ContainSingle();
        view.PlainText.Should().Be("Name: Bobby (staff)", "struck text stays visible until the change is accepted");
    }

    [Fact]
    public void Autocorrect_does_not_reach_into_a_field()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Body "));
        paragraph.Runs.Add(Run.PlainTextControl("teh", tag: "Applicant"));
        var view = LoadParagraph(paragraph);

        // Typing the trigger space inside the field must not rewrite "teh" via the autocorrect path,
        // which rebuilds cells without the control.
        view.MoveCaretToBlockForTest(0, paragraph.PlainText.Length);
        view.SimulateTextInputForTest(" ");

        Fields(paragraph).Should().ContainSingle();
        Field(paragraph).Text.Should().Be("teh ");
        view.PlainText.Should().Be("Body teh ");
    }

    [Fact]
    public void A_content_locked_field_freezes_its_paragraphs_text()
    {
        var control = Run.PlainTextControl("Bob", tag: "Applicant");
        control.Control = control.Control! with { LockMode = ContentControlLockMode.ContentLocked };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(Prefix));
        paragraph.Runs.Add(control);
        paragraph.Runs.Add(new Run(Suffix));
        var view = LoadParagraph(paragraph);

        // Typing, deleting and case changes around the field are all refused while its content is locked
        // — the range gestures address characters, and rewriting the locked ones is what the lock forbids.
        view.MoveCaretToBlockForTest(0, 0);
        view.InsertText("X");
        view.MoveCaretToBlockForTest(0, 3);
        view.BackspaceForTest();
        view.DeleteForwardForTest();
        view.SetBodySelectionForTest(0, 0, 0, paragraph.PlainText.Length);
        view.BackspaceForTest();

        view.PlainText.Should().Be("Name: Bob (staff)");
        Fields(paragraph).Should().ContainSingle();
        Field(paragraph).Text.Should().Be("Bob");
    }

    [Fact]
    public void A_delete_locked_field_survives_a_selection_that_would_remove_it()
    {
        var control = Run.PlainTextControl("Bob", tag: "Applicant");
        // Word's sdtLocked: the control may not be deleted, but its text is still editable.
        control.Control = control.Control! with { LockMode = ContentControlLockMode.ControlLocked };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(Prefix));
        paragraph.Runs.Add(control);
        paragraph.Runs.Add(new Run(Suffix));
        var view = LoadParagraph(paragraph);

        // A selection that covers the whole field would delete it → the gesture is declined outright.
        view.SetBodySelectionForTest(0, ControlStart - 2, 0, ControlEnd + 2);
        view.BackspaceForTest();
        view.PlainText.Should().Be("Name: Bob (staff)");
        Fields(paragraph).Should().ContainSingle();

        // Its text stays editable: a partial selection, and typing, both still work.
        view.SetBodySelectionForTest(0, ControlStart + 1, 0, ControlEnd);
        view.BackspaceForTest();
        Field(paragraph).Text.Should().Be("B");

        view.MoveCaretToBlockForTest(0, ControlStart + 1);
        view.InsertText("en");
        Field(paragraph).Text.Should().Be("Ben");

        // ...and body text away from the field deletes as usual.
        view.SetBodySelectionForTest(0, 0, 0, 2);
        view.BackspaceForTest();
        view.PlainText.Should().Be("me: Ben (staff)");
    }

    [Fact]
    public void A_delete_locked_body_region_survives_a_paragraph_merge()
    {
        var region = new BlockContentControl(
            BlockContentControlKind.Group,
            Tag: "Locked",
            LockMode: ContentControlLockMode.ControlLocked);
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var first = new Paragraph();
        first.Runs.Add(new Run("Head"));
        var second = new Paragraph { BlockContentControl = region };
        second.Runs.Add(new Run("Region"));
        document.Blocks.Add(first);
        document.Blocks.Add(second);
        var view = new DocumentView();
        view.LoadDocument(document);

        // Backspace at the region's start would merge it away with the paragraph that carries it.
        view.MoveCaretToBlockForTest(1, 0);
        view.BackspaceForTest();
        view.Document.Blocks.Should().HaveCount(2);
        ((Paragraph)view.Document.Blocks[1]).BlockContentControl.Should().BeSameAs(region);

        // A selection spanning into it is declined for the same reason.
        view.SetBodySelectionForTest(0, 2, 1, 3);
        view.BackspaceForTest();
        view.Document.Blocks.Should().HaveCount(2);
        view.PlainText.Should().Be("Head\nRegion");
    }

    [Fact]
    public void Splitting_a_paragraph_keeps_both_halves_inside_their_body_region()
    {
        var region = new BlockContentControl(BlockContentControlKind.Group, Tag: "Region");
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph { BlockContentControl = region };
        paragraph.Runs.Add(new Run("HeadTail"));
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);

        view.MoveCaretToBlockForTest(0, 4);
        view.InsertParagraphBreakForTest();

        view.Document.Blocks.Should().HaveCount(2);
        view.Document.Blocks.Should().OnlyContain(block => ReferenceEquals(block.BlockContentControl, region),
            "consecutive blocks sharing the instance re-emit as the one w:sdt they came from");
    }

    [Fact]
    public void A_selection_reaching_a_locked_field_deletes_nothing()
    {
        var control = Run.PlainTextControl("Bob", tag: "Applicant");
        control.Control = control.Control! with { LockMode = ContentControlLockMode.ControlAndContentLocked };
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(Prefix));
        paragraph.Runs.Add(control);
        paragraph.Runs.Add(new Run(Suffix));
        var view = LoadParagraph(paragraph);

        view.SetBodySelectionForTest(0, ControlStart - 2, 0, ControlStart + 1);
        view.BackspaceForTest();

        view.PlainText.Should().Be("Name: Bob (staff)");
        Field(paragraph).Text.Should().Be("Bob");
    }

    // Placing a caret in a cell forces a table layout, which needs the headless font manager.
    [Fact]
    public async Task Body_text_around_a_field_in_a_table_cell_is_editable_too() =>
        await Session.Dispatch(EditBodyTextAroundFieldInTableCell, CancellationToken.None);

    private static void EditBodyTextAroundFieldInTableCell()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var table = new Table();
        var row = new TableRow();
        var cell = new TableCell();
        cell.Paragraphs.Clear();
        var cellParagraph = new Paragraph();
        cellParagraph.Runs.Add(new Run("Name: "));
        cellParagraph.Runs.Add(Run.PlainTextControl("Bob", tag: "Applicant"));
        cell.Paragraphs.Add(cellParagraph);
        row.Cells.Add(cell);
        table.Rows.Add(row);
        document.Blocks.Add(table);

        var view = new DocumentView();
        view.LoadDocument(document);

        view.PlaceCaretInCell(0, 0, 0, 0, 0);
        view.InsertText("Dr. ");
        cellParagraph.PlainText.Should().Be("Dr. Name: Bob");

        view.PlaceCaretInCell(0, 0, 0, 0, 4);
        view.BackspaceForTest();
        cellParagraph.PlainText.Should().Be("Dr.Name: Bob");
        Fields(cellParagraph).Should().ContainSingle();
        Field(cellParagraph).Text.Should().Be("Bob");
    }

    [Fact]
    public void An_edited_paragraph_saves_the_field_as_one_content_control()
    {
        var (view, _) = BuildView();

        view.MoveCaretToBlockForTest(0, 0);
        view.InsertText("Dr. ");
        // The field moved right by the body insert; type at its end.
        view.MoveCaretToBlockForTest(0, ControlEnd + 4);
        view.InsertText("by");

        using var saved = new MemoryStream();
        DocxWriter.Write(view.Document, saved);
        var bytes = saved.ToArray();

        var sdts = ContentControls(bytes);
        sdts.Should().ContainSingle();
        string.Concat(sdts[0].Descendants(W + "t").Select(text => text.Value)).Should().Be("Bobby");

        var reopened = DocxReader.Read(new MemoryStream(bytes));
        reopened.PlainText.Should().Be("Dr. Name: Bobby (staff)");
        var reopenedFields = Fields(reopened.Paragraphs.Single());
        reopenedFields.Should().ContainSingle();
        reopenedFields[0].Text.Should().Be("Bobby");
        reopenedFields[0].Control!.Tag.Should().Be("Applicant");
    }

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static List<XElement> ContentControls(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry).Descendants(W + "sdt").ToList();
    }

    private static Run Field(Paragraph paragraph) => Fields(paragraph)[0];

    private static List<Run> Fields(Paragraph paragraph) =>
        paragraph.Runs.Where(run => run.Control is not null).ToList();

    private static (DocumentView View, Paragraph Paragraph) BuildView()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(Prefix));
        paragraph.Runs.Add(Run.PlainTextControl("Bob", tag: "Applicant"));
        paragraph.Runs.Add(new Run(Suffix));
        return (LoadParagraph(paragraph), paragraph);
    }

    private static DocumentView LoadParagraph(Paragraph paragraph)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(paragraph);
        var view = new DocumentView();
        view.LoadDocument(document);
        return view;
    }
}
