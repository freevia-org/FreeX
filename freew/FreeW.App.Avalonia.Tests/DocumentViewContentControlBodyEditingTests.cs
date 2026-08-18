using FreeW.App.Avalonia.Editing;
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
