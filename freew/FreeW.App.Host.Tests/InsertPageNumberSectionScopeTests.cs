using FreeW.App.Host.Editing;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for freew-page-numbering F1: a PAGE field inserted at "Current Position" in body
/// text (Insert &gt; Header &amp; Footer &gt; Page Number &gt; Current Position -&gt;
/// <see cref="DocumentView.InsertPageNumberAtCaret"/>) must resolve its initial cached text -- and every
/// re-render's fallback text -- against the SECTION the caret is actually in, not unconditionally
/// <see cref="TextDocument.Page"/> (the document's final section). Before this fix, a field inserted in
/// an earlier section (e.g. LowerRoman front matter restarting at 1) showed the final section's format/
/// start-at instead (e.g. "1" instead of "i"), and that wrong value was what got committed back into the
/// model by <c>CommitToModel</c>'s FieldMarker case -- so it also survived a save/reopen round-trip. Runs
/// on STA (WPF FlowDocument/caret), matching <see cref="PageSetupFamilySeedSectionScopeTests"/>'s sibling
/// coverage for the read side of the same <see cref="PageSettingsSectionResolver"/> choke point.
/// </summary>
public sealed class InsertPageNumberSectionScopeTests
{
    // Three top-level paragraphs: [0] an ordinary section-0 paragraph (where the caret sits -- NOT the
    // section-ending paragraph itself, so InsertBlockAfter's new block unambiguously lands back inside
    // section 0), [1] the paragraph that actually ends section 0 (carries the SectionBreak), [2] the
    // final section's body paragraph.
    private static DocumentView ThreeParagraphTwoSectionView(out PageSettings section0Page)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        section0Page = new PageSettings
        {
            PageNumberFormat = PageNumberFormat.LowerRoman,
            PageNumberStartAt = 1,
        };
        doc.Blocks.Add(new Paragraph("Front matter, page A."));
        doc.Blocks.Add(new Paragraph("Front matter, page B (ends section).")
        {
            SectionBreak = new Section(section0Page, SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("Body section (final)."));
        doc.Page.PageNumberFormat = PageNumberFormat.Decimal;
        doc.Page.PageNumberStartAt = 1;

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static Run InsertedPageNumberRun(DocumentView view, int precedingBlockIndex)
    {
        var newParagraph = view.Model.Blocks[precedingBlockIndex + 1].Should().BeOfType<Paragraph>().Subject;
        return newParagraph.Runs.Should().ContainSingle().Subject;
    }

    [StaFact]
    public void InsertPageNumberAtCaret_WithCaretInFirstSection_UsesFirstSectionFormatNotFinal()
    {
        var view = ThreeParagraphTwoSectionView(out _);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().ElementAt(0);
        view.CaretPosition = firstParagraph.ContentStart;

        view.InsertPageNumberAtCaret();

        var run = InsertedPageNumberRun(view, precedingBlockIndex: 0);
        run.FieldKind.Should().Be(RunFieldKind.PageNumber);
        run.Text.Should().Be("i", "section 0 is LowerRoman starting at 1 -- not the final section's Decimal/1");
    }

    /// <summary>
    /// Sibling no-regression case: a field inserted with the caret already in the (final) section must
    /// keep resolving from <see cref="TextDocument.Page"/> exactly as before this fix -- the overwhelming
    /// majority (single-section documents, and any caret at/after the last section break).
    /// </summary>
    [StaFact]
    public void InsertPageNumberAtCaret_WithCaretInFinalSection_UsesDocumentPageSettings()
    {
        var view = ThreeParagraphTwoSectionView(out _);
        var finalParagraph = view.Document.Blocks.OfType<WpfParagraph>().ElementAt(2);
        view.CaretPosition = finalParagraph.ContentStart;

        view.InsertPageNumberAtCaret();

        var run = InsertedPageNumberRun(view, precedingBlockIndex: 2);
        run.FieldKind.Should().Be(RunFieldKind.PageNumber);
        run.Text.Should().Be("1", "the final section is Decimal starting at 1");
    }

    /// <summary>
    /// The wrong value isn't just an insertion-time glitch: every re-render recomputed a body PAGE
    /// field's fallback text from <see cref="TextDocument.Page"/> unconditionally (see
    /// <c>DocumentView.ResolveFieldText</c>), so it got baked back into the model again on the very next
    /// <c>CommitToModel</c> even if the initial insert had (somehow) been correct. Reloading the SAME
    /// model into a fresh <see cref="DocumentView"/> (forcing a full re-render) must still show "i" for
    /// the section-0 field, proving the render path -- not just the one-shot insert helper -- is
    /// section-aware.
    /// </summary>
    [StaFact]
    public void PageNumberField_InFirstSection_KeepsFirstSectionFormatAcrossReRender()
    {
        var view = ThreeParagraphTwoSectionView(out _);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().ElementAt(0);
        view.CaretPosition = firstParagraph.ContentStart;
        view.InsertPageNumberAtCaret();
        var model = view.Model;

        var reRendered = new DocumentView();
        reRendered.LoadModel(model);
        reRendered.CommitToModel();

        var run = reRendered.Model.Blocks[1].Should().BeOfType<Paragraph>().Subject.Runs.Should().ContainSingle().Subject;
        run.FieldKind.Should().Be(RunFieldKind.PageNumber);
        run.Text.Should().Be("i", "a re-render must not overwrite the field back to the final section's value");
    }
}
