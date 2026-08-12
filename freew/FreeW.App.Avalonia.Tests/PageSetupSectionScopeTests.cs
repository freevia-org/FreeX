using FreeW.App.Avalonia.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for the Layout/Page Setup ribbon family (Orientation, Margins, Columns, Page
/// Setup dialog, Watermark, Page Border, Page Color, Line Numbers, Hyphenation, default tab stop,
/// Different First Page/Odd-Even, header/footer distance, Page Number Format, ...) always writing to
/// the document's FINAL section instead of the section containing the caret. All of these route
/// through <see cref="DocumentView.ApplyPageSettings"/> (or, for Page Color/Border/Watermark, the
/// commands it shares the same section resolution with), so this exercises that choke point directly
/// with a two-section document and a caret in the non-final section.
/// </summary>
public sealed class PageSetupSectionScopeTests
{
    private static DocumentView TwoSectionView()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var section0Page = new PageSettings { MarginLeftPt = 111 };
        doc.Blocks.Add(new Paragraph("Section one body.")
        {
            SectionBreak = new Section(section0Page, SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("Section two (final) body."));
        doc.Page.MarginLeftPt = 222;

        var view = new DocumentView();
        view.LoadDocument(doc);
        return view;
    }

    [Fact]
    public void ApplyPageSettings_WithCaretInFirstSection_MutatesFirstSectionNotFinal()
    {
        var view = TwoSectionView();
        view.MoveCaretToBlockForTest(0, 0);

        view.ApplyPageSettings(page => page.MarginLeftPt = 555);

        view.Document.Sections[0].Page.MarginLeftPt.Should().Be(555);
        view.Document.Page.MarginLeftPt.Should().Be(222); // final section untouched
    }

    [Fact]
    public void ApplyPageSettings_WithCaretInFinalSection_MutatesFinalSectionNotFirst()
    {
        var view = TwoSectionView();
        view.MoveCaretToBlockForTest(1, 0);

        view.ApplyPageSettings(page => page.MarginLeftPt = 777);

        view.Document.Page.MarginLeftPt.Should().Be(777);
        view.Document.Sections[0].Page.MarginLeftPt.Should().Be(111); // first section untouched
    }

    [Fact]
    public void ApplyPageSettings_SingleSectionDocument_StillTargetsFinalSection()
    {
        // Sibling no-regression case: a plain single-section document (the overwhelmingly common case)
        // must keep writing to the final/only section exactly as before this fix.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Only section."));
        doc.Page.MarginLeftPt = 99;

        var view = new DocumentView();
        view.LoadDocument(doc);
        view.MoveCaretToBlockForTest(0, 0);

        view.ApplyPageSettings(page => page.MarginLeftPt = 333);

        view.Document.Page.MarginLeftPt.Should().Be(333);
        view.Document.Sections.Should().HaveCount(1);
    }

    [Fact]
    public void SetPageColor_WithCaretInFirstSection_MutatesFirstSectionNotFinal()
    {
        var view = TwoSectionView();
        view.MoveCaretToBlockForTest(0, 0);

        view.SetPageColor("#ABCDEF");

        view.Document.Sections[0].Page.BackgroundColorHex.Should().Be("#ABCDEF");
        view.Document.Page.BackgroundColorHex.Should().BeNull(); // final section untouched
    }

    [Fact]
    public void TogglePageBorder_WithCaretInFirstSection_TargetsFirstSectionBorderState()
    {
        var view = TwoSectionView();
        view.MoveCaretToBlockForTest(0, 0);

        // First section starts with no border, so the toggle must ADD one there — reading and writing
        // must agree on the same (caret) section, not read the final section's (also null) border by
        // coincidence and still land the write on the wrong section.
        view.TogglePageBorder("#112233", 2.0);

        view.Document.Sections[0].Page.PageBorder.Should().NotBeNull();
        view.Document.Sections[0].Page.PageBorder!.ColorHex.Should().Be("#112233");
        view.Document.Page.PageBorder.Should().BeNull(); // final section untouched
    }

    [Fact]
    public void ApplyPageSettings_WithCaretInFirstSection_UndoRestoresOnlyFirstSection()
    {
        var view = TwoSectionView();
        view.MoveCaretToBlockForTest(0, 0);

        view.ApplyPageSettings(page => page.MarginLeftPt = 555);
        view.Undo();

        view.Document.Sections[0].Page.MarginLeftPt.Should().Be(111);
        view.Document.Page.MarginLeftPt.Should().Be(222);
    }
}
