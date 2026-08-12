using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for the Layout/Page Setup ribbon family (Orientation, Margins, Columns, Page
/// Setup dialog, Watermark, Page Border, Line Numbers, Hyphenation, default tab stop, Different First
/// Page/Odd-Even, header/footer distance, Page Number Format, ...) always writing to the document's
/// FINAL section instead of the section containing the caret. All of these route through
/// <see cref="DocumentView.ApplyPageSettings"/>, so this exercises that single choke point directly
/// with a two-section document and a caret in the non-final section. Runs on STA (WPF FlowDocument).
/// </summary>
public sealed class PageSetupSectionScopeTests
{
    private static DocumentView TwoSectionView(out PageSettings section0Page)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        section0Page = new PageSettings { MarginLeftPt = 111 };
        doc.Blocks.Add(new Paragraph("Section one body.")
        {
            SectionBreak = new Section(section0Page, SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("Section two (final) body."));
        doc.Page.MarginLeftPt = 222;

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void ApplyPageSettings_WithCaretInFirstSection_MutatesFirstSectionNotFinal()
    {
        var view = TwoSectionView(out _);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().First();
        view.CaretPosition = firstParagraph.ContentStart;

        view.ApplyPageSettings(page => page.MarginLeftPt = 555);

        view.Model.Sections[0].Page.MarginLeftPt.Should().Be(555);
        view.Model.Page.MarginLeftPt.Should().Be(222); // final section untouched
    }

    [StaFact]
    public void ApplyPageSettings_WithCaretInFinalSection_MutatesFinalSectionNotFirst()
    {
        var view = TwoSectionView(out _);
        var secondParagraph = view.Document.Blocks.OfType<WpfParagraph>().Last();
        view.CaretPosition = secondParagraph.ContentStart;

        view.ApplyPageSettings(page => page.MarginLeftPt = 777);

        view.Model.Page.MarginLeftPt.Should().Be(777);
        view.Model.Sections[0].Page.MarginLeftPt.Should().Be(111); // first section untouched
    }

    [StaFact]
    public void ApplyPageSettings_SingleSectionDocument_StillTargetsFinalSection()
    {
        // Sibling no-regression case: a plain single-section document (the overwhelmingly common case)
        // must keep writing to the final/only section exactly as before this fix.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Only section."));
        doc.Page.MarginLeftPt = 99;

        var view = new DocumentView();
        view.LoadModel(doc);
        view.CaretPosition = view.Document.Blocks.OfType<WpfParagraph>().Single().ContentStart;

        view.ApplyPageSettings(page => page.MarginLeftPt = 333);

        view.Model.Page.MarginLeftPt.Should().Be(333);
        view.Model.Sections.Should().HaveCount(1);
    }

    [StaFact]
    public void ApplyPageSettings_WithCaretInFirstSection_UndoRestoresOnlyFirstSection()
    {
        var view = TwoSectionView(out _);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().First();
        view.CaretPosition = firstParagraph.ContentStart;

        view.ApplyPageSettings(page => page.MarginLeftPt = 555);
        view.Undo();

        view.Model.Sections[0].Page.MarginLeftPt.Should().Be(111);
        view.Model.Page.MarginLeftPt.Should().Be(222);
    }
}
