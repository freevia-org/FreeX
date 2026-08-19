using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for Insert &gt; Break &gt; Section Break (any <see cref="SectionBreakKind"/>): the
/// newly inserted section-break paragraph must inherit the <see cref="PageSettings"/> of the section the
/// caret is actually in (resolved the same way page-setup mutations resolve it, via
/// <see cref="PageSettingsSectionResolver"/>), not unconditionally the document's final section. See
/// <see cref="PageSetupSectionScopeTests"/> for the sibling family covering Layout ribbon mutations
/// through the same choke point. Runs on STA (WPF FlowDocument).
/// </summary>
public sealed class InsertSectionBreakSectionScopeTests
{
    private static DocumentView TwoSectionView(out PageSettings section0Page)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        section0Page = new PageSettings
        {
            MarginLeftPt = 111,
            WidthPt = 1008,
            HeightPt = 612,
            Landscape = true,
            ColumnCount = 2
        };
        doc.Blocks.Add(new Paragraph("Section one body.")
        {
            SectionBreak = new Section(section0Page, SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("Section two (final) body."));
        doc.Page.MarginLeftPt = 222;
        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 792;
        doc.Page.Landscape = false;
        doc.Page.ColumnCount = 1;

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void InsertSectionBreak_WithCaretInFirstSection_NewSectionInheritsFirstSectionPageSettings()
    {
        var view = TwoSectionView(out var section0Page);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().First();
        view.CaretPosition = firstParagraph.ContentStart;

        view.InsertSectionBreak(SectionBreakKind.NextPage);

        // The new section-break paragraph was inserted right after the caret's own block (index 0).
        var inserted = view.Model.Blocks
            .OfType<Paragraph>()
            .Skip(1)
            .First(p => p.SectionBreak is not null);

        inserted.SectionBreak.Should().NotBeNull();
        var insertedPage = inserted.SectionBreak!.Page;

        insertedPage.MarginLeftPt.Should().Be(section0Page.MarginLeftPt);
        insertedPage.WidthPt.Should().Be(section0Page.WidthPt);
        insertedPage.HeightPt.Should().Be(section0Page.HeightPt);
        insertedPage.Landscape.Should().Be(section0Page.Landscape);
        insertedPage.ColumnCount.Should().Be(section0Page.ColumnCount);

        // Must NOT have silently copied the document's final-section (Page) settings instead.
        insertedPage.Landscape.Should().NotBe(view.Model.Page.Landscape);
        insertedPage.WidthPt.Should().NotBe(view.Model.Page.WidthPt);
    }

    [StaFact]
    public void InsertSectionBreak_WithCaretInFinalSection_NewSectionInheritsFinalSectionPageSettings()
    {
        // Sibling no-regression case: caret in the (already-final) section must keep inheriting the
        // document's final-section page settings, exactly as before this fix.
        var view = TwoSectionView(out _);
        var secondParagraph = view.Document.Blocks.OfType<WpfParagraph>().Last();
        view.CaretPosition = secondParagraph.ContentStart;

        view.InsertSectionBreak(SectionBreakKind.NextPage);

        var inserted = view.Model.Blocks
            .OfType<Paragraph>()
            .Skip(2)
            .First(p => p.SectionBreak is not null);

        var insertedPage = inserted.SectionBreak!.Page;
        insertedPage.MarginLeftPt.Should().Be(view.Model.Page.MarginLeftPt);
        insertedPage.WidthPt.Should().Be(view.Model.Page.WidthPt);
        insertedPage.HeightPt.Should().Be(view.Model.Page.HeightPt);
        insertedPage.Landscape.Should().Be(view.Model.Page.Landscape);
        insertedPage.ColumnCount.Should().Be(view.Model.Page.ColumnCount);
    }

    [StaFact]
    public void InsertSectionBreak_SingleSectionDocument_StillInheritsDocumentPageSettings()
    {
        // Sibling no-regression case: a plain single-section document (the overwhelmingly common case,
        // and what DocumentBlockInsertionMutationPlannerTests.Section_break_inherits_document_page_settings
        // pins) must keep inheriting document.Page exactly as before this fix.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Only section."));
        doc.Page.MarginLeftPt = 99;
        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 792;

        var view = new DocumentView();
        view.LoadModel(doc);
        view.CaretPosition = view.Document.Blocks.OfType<WpfParagraph>().Single().ContentStart;

        view.InsertSectionBreak(SectionBreakKind.NextPage);

        var inserted = view.Model.Blocks
            .OfType<Paragraph>()
            .First(p => p.SectionBreak is not null);

        var insertedPage = inserted.SectionBreak!.Page;
        insertedPage.MarginLeftPt.Should().Be(99);
        insertedPage.WidthPt.Should().Be(612);
        insertedPage.HeightPt.Should().Be(792);
    }
}
