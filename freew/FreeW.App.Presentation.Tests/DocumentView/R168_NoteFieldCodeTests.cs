using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests.DocumentView;

/// <summary>
/// r168 finding meta-F1: Toggle Field Codes (Shift+F9/Alt+F9) never reached footnote/endnote content in
/// either shell because <see cref="DocumentNoteRegionPlanner.ResolveVisiblePlainText"/> read
/// <see cref="Run.Text"/> raw and never consulted <see cref="Run.FieldCodeVisible"/>/<see cref="Run.FieldKind"/>
/// at all -- unlike the body run-display path, the table-cell wrap path, and
/// <see cref="HeaderFooterVisualPlanner"/>, which round 167 wired up. These tests assert the RENDERED note
/// text produced by the public footnote/endnote region builders (what a host actually paints), not the
/// model flag alone, per the round-168 directive.
/// </summary>
public sealed class R168_NoteFieldCodeTests
{
    [Fact]
    public void BuildFootnoteRegion_SimpleFieldWithCodeVisible_RendersFieldCode()
    {
        var document = BuildDocumentWithOneFootnoteReference();
        var footnote = new Footnote(1);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Page "));
        paragraph.Runs.Add(new Run("1") { FieldKind = RunFieldKind.PageNumber, FieldCodeVisible = true });
        footnote.Content.Add(paragraph);
        document.Footnotes[1] = footnote;

        var plan = DocumentNoteRegionPlanner.BuildFootnoteRegion(
            document, [1], pageNumber: 1, contentWidthDip: 400);

        plan.Rows.Should().ContainSingle();
        plan.Rows[0].Text.Should().Be("Page { PAGE }",
            "Shift+F9 must flip a simple field inside a footnote to its code, exactly as it already does " +
            "in the body/table/header-footer (round 167)");
    }

    [Fact]
    public void BuildFootnoteRegion_SimpleFieldWithCodeHidden_RendersResolvedResult()
    {
        // Sibling no-regression: the untouched (code-hidden) case must keep showing the cached/live
        // result text exactly as before this fix -- the fix must not affect fields that are NOT toggled.
        var document = BuildDocumentWithOneFootnoteReference();
        var footnote = new Footnote(1);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Page "));
        paragraph.Runs.Add(new Run("1") { FieldKind = RunFieldKind.PageNumber, FieldCodeVisible = false });
        footnote.Content.Add(paragraph);
        document.Footnotes[1] = footnote;

        var plan = DocumentNoteRegionPlanner.BuildFootnoteRegion(
            document, [1], pageNumber: 1, contentWidthDip: 400);

        plan.Rows.Should().ContainSingle();
        plan.Rows[0].Text.Should().Be("Page 1");
    }

    [Fact]
    public void BuildEndnoteRegion_SimpleFieldWithCodeVisible_RendersFieldCode()
    {
        // Sibling: endnotes share the exact same ResolveVisiblePlainText/ResolvePlainText path as
        // footnotes (DocumentNoteRegionPlanner.cs ResolvePlainText), so the fix must cover both.
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var bodyParagraph = new Paragraph();
        bodyParagraph.Runs.Add(new Run("see"));
        bodyParagraph.Runs.Add(Run.EndnoteReference(1));
        document.Blocks.Add(bodyParagraph);

        var endnote = new Endnote(1);
        var notePara = new Paragraph();
        notePara.Runs.Add(new Run("Written on "));
        notePara.Runs.Add(new Run("1/1/2026") { FieldKind = RunFieldKind.Date, FieldCodeVisible = true });
        endnote.Content.Add(notePara);
        document.Endnotes[1] = endnote;

        var plan = DocumentNoteRegionPlanner.BuildEndnoteRegion(
            document, [1], pageNumber: 1, contentWidthDip: 400, isSyntheticPage: false);

        plan.Rows.Should().ContainSingle();
        plan.Rows[0].Text.Should().Be("Written on { DATE }");
    }

    [Fact]
    public void ResolveVisiblePlainText_OrdinaryTextRun_IsUnaffected()
    {
        // Sibling no-regression at the lower-level helper: a plain (non-field) run, which always has
        // FieldCodeVisible == false, must keep rendering exactly as before.
        var document = TextDocument.CreateEmpty();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("just plain text"));

        var text = DocumentNoteRegionPlanner.ResolveVisiblePlainText(document, [paragraph]);

        text.Should().Be("just plain text");
    }

    private static TextDocument BuildDocumentWithOneFootnoteReference()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("text"));
        paragraph.Runs.Add(Run.FootnoteReference(1));
        document.Blocks.Add(paragraph);
        return document;
    }
}
