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
    public void PackageAuthoredParagraphDefaults_DoNotForceWpfLineHeight()
    {
        var doc = TextDocument.CreateEmpty();
        doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
        // A package carrying an authoritative w:docDefaults/w:pPrDefault opts out of Word's
        // application default, so the host keeps the font's natural single-line box. Every other
        // document -- imported without those defaults, or authored here -- takes Word's cadence,
        // which the sibling test below covers.
        doc.UseWordApplicationDefaultLineSpacing = false;
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
        // A package that serialized its own w:rPrDefault supplies the run defaults, so only the
        // paragraph-level application default applies and the run-metric calibration does not.
        doc.UseWordApplicationDefaultRunFormatting = false;
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("body text")
        {
            Formatting = ParagraphFormatting.Default,
        });

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpf = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        var ratio = new System.Windows.Media.FontFamily("Calibri").LineSpacing;
        var expected = ParagraphFormatting.Default.LineSpacing * ratio * 11 * PxPerPoint;
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

    [StaFact]
    public void ImportedExplicitDocumentDefaultMultiple_AppliesWordLineHeight()
    {
        var doc = TextDocument.CreateEmpty();
        doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
        doc.DefaultParagraph = ParagraphFormatting.Default with
        {
            LineSpacing = 1.15,
            LineRule = LineSpacingRule.Multiple,
            LineSpacingIsSet = true,
        };
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("body text"));

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpf = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        var ratio = new System.Windows.Media.FontFamily("Calibri").LineSpacing;
        Assert.Equal(1.15 * ratio * 11 * PxPerPoint, wpf.LineHeight, 1);
    }

    [StaFact]
    public void ImportedApplicationRunDefault_AppliesMeasuredLineHeight()
    {
        var doc = TextDocument.CreateEmpty();
        doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 12 };
        doc.UseWordApplicationDefaultLineSpacing = true;
        doc.UseWordApplicationDefaultRunFormatting = true;
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("body text"));

        var view = new DocumentView();
        view.LoadModel(doc);

        var wpf = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        var ratio = new System.Windows.Media.FontFamily("Calibri").LineSpacing;
        var expected = ParagraphFormatting.Default.LineSpacing * ratio * 1.00975 * 12 * PxPerPoint;
        Assert.Equal(expected, wpf.LineHeight, 1);
    }

    [StaFact]
    public void MultipleRule_ScalesTheParagraphsOwnFontLine_NotTheDocumentDefault()
    {
        // stress008: Times New Roman body text in a Calibri-default document. Word's 1.15 lines are
        // 1.15 x the Times line (~1.15 em), not x Calibri's ~1.22 em, and a 16pt run's multiple scales
        // its 16pt line. The multiple must still read back exactly on commit.
        var doc = TextDocument.CreateEmpty();
        doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 11 };
        doc.DefaultParagraph = ParagraphFormatting.Default with
        {
            LineSpacing = 1.15,
            LineRule = LineSpacingRule.Multiple,
            LineSpacingIsSet = true,
        };
        doc.Blocks.Clear();
        var body = new Paragraph("Times body");
        body.Runs[0].Formatting = RunFormatting.Default with { FontFamily = "Times New Roman" };
        // A lone larger space run (seen in stress008) must not stretch every line of the paragraph.
        body.Runs.Add(new Run(" ", RunFormatting.Default with { FontFamily = "Times New Roman", FontSizePt = 14 }));
        body.Runs.Add(new Run("continues", RunFormatting.Default with { FontFamily = "Times New Roman" }));
        doc.Blocks.Add(body);
        var title = new Paragraph("Big title");
        title.Runs[0].Formatting = RunFormatting.Default with { FontFamily = "Times New Roman", FontSizePt = 16 };
        doc.Blocks.Add(title);
        var empty = new Paragraph(string.Empty)
        {
            MarkFormatting = RunFormatting.Default with { FontFamily = "Times New Roman", FontSizePt = 16 }
        };
        doc.Blocks.Add(empty);

        var view = new DocumentView();
        view.LoadModel(doc);

        var paragraphs = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        var times = new System.Windows.Media.FontFamily("Times New Roman").LineSpacing;
        Assert.Equal(1.15 * times * 11 * PxPerPoint, paragraphs[0].LineHeight, 1);
        Assert.Equal(1.15 * times * 16 * PxPerPoint, paragraphs[1].LineHeight, 1);
        Assert.Equal(1.15 * times * 16 * PxPerPoint, paragraphs[2].LineHeight, 1);

        view.CommitToModel();
        var committed = view.Model.Blocks.OfType<Paragraph>().ToList();
        Assert.Equal(1.15, committed[0].Formatting.LineSpacing, 6);
        Assert.Equal(1.15, committed[1].Formatting.LineSpacing, 6);
    }

    [StaTheory]
    [InlineData("Heading1", 3.0)]
    [InlineData("Title", 4.5)]
    public void ImportedApplicationRunDefault_AddsStyleSpecificBodyClearance(
        string styleId,
        double expectedExtraPoints)
    {
        static double BottomMargin(string id, bool usesApplicationDefault)
        {
            var doc = TextDocument.CreateEmpty();
            doc.DefaultRun = doc.DefaultRun with { FontFamily = "Calibri", FontSizePt = 12 };
            doc.UseWordApplicationDefaultLineSpacing = true;
            doc.UseWordApplicationDefaultRunFormatting = usesApplicationDefault;
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph("heading") { StyleId = id });

            var view = new DocumentView();
            view.LoadModel(doc);
            return view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First().Margin.Bottom;
        }

        var explicitDefaultMargin = BottomMargin(styleId, usesApplicationDefault: false);
        var applicationDefaultMargin = BottomMargin(styleId, usesApplicationDefault: true);

        Assert.Equal(expectedExtraPoints * PxPerPoint,
            applicationDefaultMargin - explicitDefaultMargin, 1);
    }
}
