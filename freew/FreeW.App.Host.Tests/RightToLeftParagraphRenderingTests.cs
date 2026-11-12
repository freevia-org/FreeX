using System.Linq;
using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;
using ModelParagraph = FreeW.Core.Model.Paragraph;
using ModelRun = FreeW.Core.Model.Run;
using ModelTextAlignment = FreeW.Core.Model.TextAlignment;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Right-to-left paragraph layout: an explicit w:jc="left" overrides a justified bidi style, Arabic
/// text takes the complex-script face/size, a tab-positioned justified line renders start-aligned,
/// and none of those render-only projections is baked into the model on commit. Runs on STA.
/// </summary>
public sealed class RightToLeftParagraphRenderingTests
{
    private const double PxPerPoint = 96.0 / 72.0;
    private const string Arabic = "مقدمة";

    private static TextDocument BuildDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.UseWordApplicationDefaultLineSpacing = false;
        doc.DefaultRun = doc.DefaultRun with
        {
            FontFamily = "Times New Roman",
            ComplexScriptFontFamily = "Arial",
            FontSizePt = 10,
        };
        doc.Styles["Normal"] = new DocumentStyle
        {
            Id = "Normal",
            Name = "Normal",
            IsDefault = true,
            Run = new RunFormatting { ComplexScriptFontSizePt = 15 },
            Paragraph = ParagraphFormatting.Default with { Alignment = ModelTextAlignment.Justify, Rtl = true }
        };
        doc.Blocks.Clear();

        var explicitLeft = new ModelParagraph
        {
            StyleId = "Normal",
            Formatting = ParagraphFormatting.Default with { AlignmentIsSet = true }
        };
        explicitLeft.Runs.Add(new ModelRun(Arabic));
        explicitLeft.Runs.Add(new ModelRun("Latin"));
        doc.Blocks.Add(explicitLeft);

        var tabbed = new ModelParagraph
        {
            StyleId = "Normal",
            Formatting = ParagraphFormatting.Default with
            {
                TabStops = [new TabStop(300, TabStopAlignment.Right, TabLeader.Dots)]
            }
        };
        tabbed.Runs.Add(new ModelRun("\t" + Arabic + "\t"));
        doc.Blocks.Add(tabbed);
        return doc;
    }

    [StaFact]
    public void BidiStyleParagraphs_RenderWithComplexScriptRunsAndCommitUnchanged()
    {
        var view = new DocumentView();
        view.LoadModel(BuildDocument());

        var paragraphs = view.Document.Blocks.OfType<WpfParagraph>().ToList();
        Assert.Equal(FlowDirection.RightToLeft, paragraphs[0].FlowDirection);
        Assert.Equal(System.Windows.TextAlignment.Left, paragraphs[0].TextAlignment);
        var runs = paragraphs[0].Inlines.OfType<WpfRun>().Where(run => run.Text.Length > 0).ToList();
        Assert.Equal("Arial", runs[0].FontFamily.Source);
        Assert.Equal(15 * PxPerPoint, runs[0].FontSize, 3);
        Assert.Equal("Times New Roman", runs[1].FontFamily.Source);
        Assert.Equal(10 * PxPerPoint, runs[1].FontSize, 3);
        // Tab-positioned lines are never justified.
        Assert.Equal(System.Windows.TextAlignment.Left, paragraphs[1].TextAlignment);

        view.CommitToModel();
        var committed = view.Model.Blocks.OfType<ModelParagraph>().ToList();
        Assert.True(committed[0].Formatting.AlignmentIsSet);
        Assert.Equal(ModelTextAlignment.Left, committed[0].Formatting.Alignment);
        Assert.Equal(ModelTextAlignment.Justify, committed[1].Formatting.Alignment);
        var arabicRun = committed[0].Runs.First(run => run.Text == Arabic);
        Assert.Null(arabicRun.Formatting.FontFamily);
        Assert.Null(arabicRun.Formatting.FontSizePt);
    }
}
