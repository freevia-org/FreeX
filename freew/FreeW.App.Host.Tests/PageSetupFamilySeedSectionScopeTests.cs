using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Regression coverage for the page-setup-family ribbon dialogs (Page Setup, Columns, Line Number
/// Options, Borders and Shading's page-border field) seeding themselves from the section the caret is
/// actually in, via <see cref="DocumentView.CurrentSectionPageSettings"/>, instead of unconditionally
/// <see cref="TextDocument.Page"/> (the document's final section). Before this fix, every one of those
/// dialogs was seeded from <c>editor.Model.Page</c> directly — the caret's section only came into play on
/// the apply side (<see cref="DocumentView.ApplyPageSettings"/>), so opening the dialog while the caret sat
/// in a non-final section showed the wrong (final-section) numbers and clicking OK/Apply silently
/// overwrote the caret's real section with them. See <see cref="PageSetupSectionScopeTests"/> (Avalonia)
/// and <see cref="InsertSectionBreakSectionScopeTests"/> for the sibling families covering the apply side
/// and section-break inheritance through the same <see cref="PageSettingsSectionResolver"/> choke point.
/// Runs on STA (WPF FlowDocument/caret).
/// </summary>
public sealed class PageSetupFamilySeedSectionScopeTests
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
            ColumnCount = 2,
            LineNumberStartAt = 5,
            LineNumberCountBy = 2,
            LineNumberMode = LineNumberMode.Continuous,
            PageBorder = new PageBorder("#AA0000", 1.0)
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
        doc.Page.LineNumberStartAt = 1;
        doc.Page.LineNumberCountBy = 1;
        doc.Page.LineNumberMode = LineNumberMode.None;
        doc.Page.PageBorder = null;

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void CurrentSectionPageSettings_WithCaretInFirstSection_ReturnsFirstSectionNotFinal()
    {
        var view = TwoSectionView(out var section0Page);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().First();
        view.CaretPosition = firstParagraph.ContentStart;

        var seeded = view.CurrentSectionPageSettings();

        // Must be the actual first-section PageSettings instance (what the page-setup dialogs should now
        // seed from), not a copy and not the document's final section.
        seeded.Should().BeSameAs(section0Page);
        seeded.MarginLeftPt.Should().Be(111);
        seeded.WidthPt.Should().Be(1008);
        seeded.HeightPt.Should().Be(612);
        seeded.Landscape.Should().BeTrue();
        seeded.ColumnCount.Should().Be(2);
        seeded.LineNumberStartAt.Should().Be(5);
        seeded.LineNumberCountBy.Should().Be(2);
        seeded.LineNumberMode.Should().Be(LineNumberMode.Continuous);
        seeded.PageBorder.Should().NotBeNull();
        seeded.PageBorder!.ColorHex.Should().Be("#AA0000");

        // Must NOT have silently returned the document's final-section (Page) settings instead — this is
        // the exact defect: every page-setup-family dialog always seeded from here regardless of caret.
        seeded.Should().NotBeSameAs(view.Model.Page);
        seeded.Landscape.Should().NotBe(view.Model.Page.Landscape);
        seeded.WidthPt.Should().NotBe(view.Model.Page.WidthPt);
        seeded.MarginLeftPt.Should().NotBe(view.Model.Page.MarginLeftPt);
    }

    [StaFact]
    public void CurrentSectionPageSettings_WithCaretInFirstSection_MatchesWhatApplyPageSettingsWillWriteTo()
    {
        // The seed and the write must agree on the same section, otherwise the dialog looks internally
        // consistent (it read *some* PageSettings) but silently clobbers a different section on OK/Apply.
        var view = TwoSectionView(out var section0Page);
        var firstParagraph = view.Document.Blocks.OfType<WpfParagraph>().First();
        view.CaretPosition = firstParagraph.ContentStart;

        var seeded = view.CurrentSectionPageSettings();
        view.ApplyPageSettings(page => page.MarginLeftPt = 999);

        seeded.MarginLeftPt.Should().Be(999); // the very instance the dialog seeded from was the one mutated
        view.Model.Page.MarginLeftPt.Should().Be(222); // final section untouched
    }

    [StaFact]
    public void CurrentSectionPageSettings_WithCaretInFinalSection_ReturnsFinalSectionSettings()
    {
        // Sibling no-regression case: caret already in the (final) section must keep seeding from
        // TextDocument.Page exactly as before this fix — this is the overwhelmingly common single-section
        // and caret-at-end cases.
        var view = TwoSectionView(out _);
        var secondParagraph = view.Document.Blocks.OfType<WpfParagraph>().Last();
        view.CaretPosition = secondParagraph.ContentStart;

        var seeded = view.CurrentSectionPageSettings();

        seeded.Should().BeSameAs(view.Model.Page);
        seeded.MarginLeftPt.Should().Be(222);
        seeded.WidthPt.Should().Be(612);
        seeded.HeightPt.Should().Be(792);
        seeded.Landscape.Should().BeFalse();
        seeded.ColumnCount.Should().Be(1);
    }

    [StaFact]
    public void CurrentSectionPageSettings_SingleSectionDocument_StillReturnsDocumentPageSettings()
    {
        // Sibling no-regression case: a plain single-section document (the default, and what every
        // existing page-setup test constructs) must keep seeding from TextDocument.Page exactly as before.
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Only section."));
        doc.Page.MarginLeftPt = 99;
        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 792;

        var view = new DocumentView();
        view.LoadModel(doc);
        view.CaretPosition = view.Document.Blocks.OfType<WpfParagraph>().Single().ContentStart;

        var seeded = view.CurrentSectionPageSettings();

        seeded.Should().BeSameAs(view.Model.Page);
        seeded.MarginLeftPt.Should().Be(99);
        seeded.WidthPt.Should().Be(612);
        seeded.HeightPt.Should().Be(792);
    }
}
