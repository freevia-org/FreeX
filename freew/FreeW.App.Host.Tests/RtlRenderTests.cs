using System.Linq;
using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Verifies the editor maps right-to-left direction (<see cref="ParagraphFormatting.Rtl"/> /
/// <see cref="RunFormatting.Rtl"/>) to WPF <see cref="FlowDirection.RightToLeft"/>, and recovers it on
/// commit. Runs on STA because it builds the real WPF editing surface.
/// </summary>
public sealed class RtlRenderTests
{
    private static TextDocument RtlDoc()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var p = new Paragraph { Formatting = ParagraphFormatting.Default with { Rtl = true } };
        p.Runs.Add(new Run("שלום", new RunFormatting { Rtl = true }));
        doc.Blocks.Add(p);
        return doc;
    }

    [StaFact]
    public void RtlParagraph_RendersRightToLeft()
    {
        var view = new DocumentView();
        view.LoadModel(RtlDoc());

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        Assert.Equal(FlowDirection.RightToLeft, paragraph.FlowDirection);
    }

    [StaFact]
    public void RtlParagraph_SurvivesCommit()
    {
        var view = new DocumentView();
        view.LoadModel(RtlDoc());
        view.CommitToModel();

        var paragraph = view.Model.Blocks.OfType<Paragraph>().First();
        Assert.True(paragraph.Formatting.Rtl);
        Assert.True(paragraph.Runs[0].Formatting.Rtl);
    }
}
