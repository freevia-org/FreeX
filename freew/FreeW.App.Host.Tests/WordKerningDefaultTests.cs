using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using Xunit;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun = System.Windows.Documents.Run;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Word applies pair kerning only where <c>w:kern</c> requests it (font size at or above the threshold);
/// WPF kerns by default, which made FreeW lines narrower than Word's and moved line breaks. These tests
/// pin the Word rule on the rendered surface.
/// </summary>
public sealed class WordKerningDefaultTests
{
    [Theory]
    [InlineData(null, 11.0, false)]   // no w:kern -> off (Word default)
    [InlineData(0.0, 11.0, false)]    // w:kern="0" -> explicitly off
    [InlineData(1.0, 11.0, true)]     // modern Normal.dotm docDefaults (w:kern val=2 -> 1pt)
    [InlineData(11.0, 11.0, true)]    // at the threshold
    [InlineData(14.0, 11.0, false)]   // below the threshold
    [InlineData(14.0, null, false)]   // falls back to the default size (11pt)
    [InlineData(8.0, null, true)]
    public void KerningAppliesOnlyAtOrAboveTheDocumentThreshold(double? kernMinPt, double? sizePt, bool expected)
    {
        var formatting = RunFormatting.Default with { KerningMinSizePt = kernMinPt, FontSizePt = sizePt };

        RunKerningPlanner.IsKerningEnabled(formatting, defaultFontSizePt: 11).Should().Be(expected);
    }

    [StaFact]
    public void RenderedRuns_KernOnlyWhereTheDocumentRequestsIt_AndTheModelIsUntouched()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new FreeW.Core.Model.Paragraph();
        paragraph.Runs.Add(new FreeW.Core.Model.Run("AVATAR To ", RunFormatting.Default));
        paragraph.Runs.Add(new FreeW.Core.Model.Run("Wave ",
            RunFormatting.Default with { FontSizePt = 12, KerningMinSizePt = 8 }));
        paragraph.Runs.Add(new FreeW.Core.Model.Run("Tomorrow",
            RunFormatting.Default with { FontSizePt = 12, KerningMinSizePt = 14 }));
        doc.Blocks.Add(paragraph);

        var view = new DocumentView();
        view.LoadModel(doc);

        Typography.GetKerning(view.Document).Should().BeFalse("the editor surface defaults to Word's no-kerning");
        var wpfParagraph = view.Document.Blocks.OfType<WpfParagraph>().Single();
        Typography.GetKerning(wpfParagraph).Should().BeFalse();
        var runs = wpfParagraph.Inlines.OfType<WpfRun>().Where(run => run.Text.Length > 0).ToList();
        runs.Select(run => run.Text).Should().Equal("AVATAR To ", "Wave ", "Tomorrow");
        runs.Select(Typography.GetKerning).Should().Equal(false, true, false);

        // A block re-hosted in another FlowDocument (page boxes / print) keeps the decision.
        view.Document.Blocks.Remove(wpfParagraph);
        var rehosted = new FlowDocument(wpfParagraph);
        rehosted.Blocks.OfType<WpfParagraph>().Single().Inlines.OfType<WpfRun>()
            .Where(run => run.Text.Length > 0).Select(Typography.GetKerning).Should().Equal(false, true, false);
    }

    [StaFact]
    public void DocumentDefaultKerning_IsInheritedByPlainRuns_AndSurvivesCommit()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.DefaultRun = doc.DefaultRun with { KerningMinSizePt = 1 };
        doc.Blocks.Add(new FreeW.Core.Model.Paragraph("AVATAR"));

        var view = new DocumentView();
        view.LoadModel(doc);

        view.Document.Blocks.OfType<WpfParagraph>().Single().Inlines.OfType<WpfRun>()
            .Single(run => run.Text == "AVATAR")
            .Invoking(run => Typography.GetKerning(run).Should().BeTrue())
            .Should().NotThrow();

        view.CommitToModel();
        view.Model.DefaultRun.KerningMinSizePt.Should().Be(1);
        // Render-only: the inherited threshold is not baked into the run's direct formatting.
        view.Model.Blocks.OfType<FreeW.Core.Model.Paragraph>().Single().Runs.Single()
            .Formatting.KerningMinSizePt.Should().BeNull();
    }
}
