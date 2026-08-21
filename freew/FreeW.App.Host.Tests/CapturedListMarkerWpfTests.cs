using System.Linq;
using System.Windows;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using FluentAssertions;
using Xunit;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// U2 remediation (round 161): the WPF host — the primary Windows shell — rendered every Bullet
/// paragraph with WPF's native Disc <see cref="TextMarkerStyle"/> and every Number paragraph with native
/// Decimal, because <c>ToMarkerStyle</c> switched on <see cref="ListKind"/> alone and discarded the
/// captured <see cref="ParagraphFormatting.ListMarkerText"/>/<see cref="ParagraphFormatting.ListNumberFormat"/>
/// for anything that wasn't <see cref="ListKind.MultiLevel"/>. So a document whose real numbering.xml used
/// a dash bullet or a lowerLetter list still showed a round bullet / Arabic decimal in the primary shell,
/// even though the model captured the real marker correctly and the Avalonia renderer already drew it.
/// <para>
/// The MultiLevel path already prepends real computed marker text as a leading run instead of using a
/// native marker style (see <c>PrependMultiLevelMarker</c> in <see cref="DocumentView"/>); this fix follows
/// that same precedent for Bullet/Number paragraphs that carry a non-default captured marker, while an
/// ordinary FreeW-authored list (no captured marker data) keeps rendering via the native marker unchanged
/// — see the no-regression tests below.
/// </para>
/// </summary>
public sealed class CapturedListMarkerWpfTests
{
    private static DocumentView ViewWith(TextDocument doc)
    {
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static TextDocument DocOf(params Paragraph[] paragraphs)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.AddRange(paragraphs);
        return doc;
    }

    // --- The core defect: a captured non-default marker must actually render -----------------------

    [StaFact]
    public void BulletList_WithCapturedDashGlyph_RendersDashMarkerNotNativeDisc()
    {
        var doc = DocOf(new Paragraph("Item")
        {
            Formatting = ParagraphFormatting.Default with
            {
                ListKind = ListKind.Bullet,
                ListMarkerText = "-",
            }
        });

        var view = ViewWith(doc);

        var list = view.Document.Blocks.OfType<List>().Single();
        list.MarkerStyle.Should().Be(TextMarkerStyle.None,
            "a captured non-default bullet glyph must suppress WPF's native Disc marker");
        var paragraph = list.ListItems.Single().Blocks.OfType<WpfParagraph>().Single();
        var markerRun = paragraph.Inlines.OfType<WpfRun>().First();
        markerRun.Text.Should().Be("- ", "the real captured glyph must render, not a round bullet");
    }

    [StaFact]
    public void NumberList_WithCapturedLowerLetterFormat_RendersLetterMarkerNotNativeDecimal()
    {
        var doc = DocOf(new Paragraph("Item")
        {
            Formatting = ParagraphFormatting.Default with
            {
                ListKind = ListKind.Number,
                ListNumberFormat = ListNumberFormat.LowerLetter,
            }
        });

        var view = ViewWith(doc);

        var list = view.Document.Blocks.OfType<List>().Single();
        list.MarkerStyle.Should().Be(TextMarkerStyle.None,
            "a captured non-default numFmt must suppress WPF's native Decimal marker");
        var paragraph = list.ListItems.Single().Blocks.OfType<WpfParagraph>().Single();
        var markerRun = paragraph.Inlines.OfType<WpfRun>().First();
        markerRun.Text.Should().Be("a. ", "the real captured numFmt must render, not Arabic decimal");
    }

    [StaFact]
    public void NumberList_WithCapturedCustomLvlTextPattern_RendersCapturedPattern()
    {
        var doc = DocOf(new Paragraph("Item")
        {
            Formatting = ParagraphFormatting.Default with
            {
                ListKind = ListKind.Number,
                ListMarkerText = "(%1)",
            }
        });

        var view = ViewWith(doc);

        var list = view.Document.Blocks.OfType<List>().Single();
        list.MarkerStyle.Should().Be(TextMarkerStyle.None,
            "a captured lvlText pattern must suppress WPF's native Decimal marker");
        var paragraph = list.ListItems.Single().Blocks.OfType<WpfParagraph>().Single();
        var markerRun = paragraph.Inlines.OfType<WpfRun>().First();
        markerRun.Text.Should().Be("(1) ", "the real captured pattern must render, not the classic \"1.\" shape");
    }

    // --- Sibling no-regression: an ordinary FreeW-authored list keeps the native marker unchanged ---

    [StaFact]
    public void BulletList_WithNoCapturedMarker_StillRendersNativeDiscUnchanged()
    {
        var doc = DocOf(new Paragraph("Item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
        });

        var view = ViewWith(doc);

        var list = view.Document.Blocks.OfType<List>().Single();
        list.MarkerStyle.Should().Be(TextMarkerStyle.Disc,
            "an ordinary bullet with no captured marker data must not change rendering");
        var paragraph = list.ListItems.Single().Blocks.OfType<WpfParagraph>().Single();
        paragraph.Inlines.OfType<WpfRun>().First().Text.Should().Be("Item",
            "no synthetic marker run should be prepended for the default case");
    }

    [StaFact]
    public void NumberList_WithNoCapturedMarker_StillRendersNativeDecimalUnchanged()
    {
        var doc = DocOf(new Paragraph("Item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number }
        });

        var view = ViewWith(doc);

        var list = view.Document.Blocks.OfType<List>().Single();
        list.MarkerStyle.Should().Be(TextMarkerStyle.Decimal,
            "an ordinary numbered list with no captured marker data must not change rendering");
        list.StartIndex.Should().Be(1);
        var paragraph = list.ListItems.Single().Blocks.OfType<WpfParagraph>().Single();
        paragraph.Inlines.OfType<WpfRun>().First().Text.Should().Be("Item",
            "no synthetic marker run should be prepended for the default case");
    }
}
