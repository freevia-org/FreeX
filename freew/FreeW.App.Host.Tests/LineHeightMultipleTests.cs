using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Word's "Multiple" line rule multiplies the font's natural line height (one line =
/// ascent+descent+gap), which WPF surfaces as FontFamily.LineSpacing — not the raw em. These tests pin
/// that DocumentView renders a multiple-spaced paragraph at multiple x naturalLineHeight x fontSize, so
/// FreeW's pagination density matches Word's. Runs on STA.
/// </summary>
public sealed class LineHeightMultipleTests
{
    private const double PxPerPoint = 96.0 / 72.0;

    [StaFact]
    public void MultipleRule_AppliesMultipleToNaturalLineHeight()
    {
        var doc = TextDocument.CreateEmpty();
        doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("body text")
        {
            Formatting = ParagraphFormatting.Default with
            {
                LineSpacing = 1.5,
                LineRule = LineSpacingRule.Multiple,
                LineSpacingIsSet = true,
            },
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpf = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        var ratio = new System.Windows.Media.FontFamily("Calibri").LineSpacing; // ~1.22
        var expected = 1.5 * ratio * 11 * PxPerPoint;
        Assert.Equal(expected, wpf.LineHeight, 1);
        // Regression guard: the old formula (multiple x em, no ratio) would be ~22% shorter.
        Assert.True(wpf.LineHeight > 1.5 * 11 * PxPerPoint + 1, "line height must include the natural-line ratio");
    }

    [StaFact]
    public void ImplicitDefaultMultiple_DoesNotForceWpfLineHeight()
    {
        var doc = TextDocument.CreateEmpty();
        doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("body text")
        {
            // This is the model's convenience default, not an explicit w:spacing/@w:line.
            Formatting = ParagraphFormatting.Default,
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpf = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        Assert.True(double.IsNaN(wpf.LineHeight));
    }

    [StaFact]
    public void ImportedImplicitDefaultMultiple_AppliesWordApplicationLineHeight()
    {
        var doc = TextDocument.CreateEmpty();
        doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
        doc.UseWordApplicationDefaultLineSpacing = true;
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("body text")
        {
            Formatting = ParagraphFormatting.Default,
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpf = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        var ratio = new System.Windows.Media.FontFamily("Calibri").LineSpacing;
        var expected = ParagraphFormatting.Default.LineSpacing * ratio * 1.10 * 11 * PxPerPoint;
        Assert.Equal(expected, wpf.LineHeight, 1);
    }

    [StaFact]
    public void ImportedExplicitSingle_DoesNotApplyWordApplicationCalibration()
    {
        var doc = TextDocument.CreateEmpty();
        doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
        doc.UseWordApplicationDefaultLineSpacing = true;
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("body text")
        {
            Formatting = ParagraphFormatting.Default with
            {
                LineSpacing = 1.0,
                LineSpacingIsSet = true,
            },
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpf = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        var ratio = new System.Windows.Media.FontFamily("Calibri").LineSpacing;
        Assert.Equal(ratio * 11 * PxPerPoint, wpf.LineHeight, 1);
    }
}
