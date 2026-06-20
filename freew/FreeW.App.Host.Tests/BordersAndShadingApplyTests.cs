using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA coverage for the Borders and Shading dialog's apply paths on <see cref="DocumentView"/>:
/// <see cref="DocumentView.SetParagraphBorder"/> / <see cref="DocumentView.SetParagraphShading"/> (the
/// undo/redo bus). Beyond applying, these assert the model-only fields — the border line style / per-edge
/// flags and the shading pattern, which have no WPF Border slot — survive a render + commit cycle (they ride
/// on the paragraph Tag). Needs STA + a Dispatcher for the RichTextBox/FlowDocument, so they run as
/// <c>[StaFact]</c>.
/// </summary>
public sealed class BordersAndShadingApplyTests
{
    private static DocumentView ViewWith(params string[] texts)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var text in texts)
            doc.Blocks.Add(new Paragraph(text));
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static void SelectAllParagraphs(DocumentView view)
    {
        var paragraphs = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        view.Selection.Select(paragraphs[0].ContentStart, paragraphs[^1].ContentEnd);
    }

    [StaFact]
    public void SetParagraphBorder_AppliesStyleColourWidthAndEdges()
    {
        var view = ViewWith("one", "two");
        SelectAllParagraphs(view);

        view.SetParagraphBorder(new ParagraphBorder("#00B050", 2.25)
        {
            LineStyle = BorderLineStyle.Dashed,
            Top = true,
            Left = false,
            Bottom = true,
            Right = false,
        });

        foreach (var paragraph in view.Model.Blocks.OfType<Paragraph>())
        {
            var border = paragraph.Formatting.Border;
            border.Should().NotBeNull();
            border!.ColorHex.Should().Be("#00B050");
            border.WidthPt.Should().BeApproximately(2.25, 0.001);
            border.LineStyle.Should().Be(BorderLineStyle.Dashed);
            border.Top.Should().BeTrue();
            border.Left.Should().BeFalse();
            border.Bottom.Should().BeTrue();
            border.Right.Should().BeFalse();
        }
    }

    [StaFact]
    public void SetParagraphBorder_StyleAndEdges_SurviveRenderAndCommit()
    {
        var view = ViewWith("para");
        SelectAllParagraphs(view);
        view.SetParagraphBorder(new ParagraphBorder("#FF0000", 1.5)
        {
            LineStyle = BorderLineStyle.Double,
            Right = false,
        });

        // Re-render from the model (stamps the Tag), then commit the rendered document back into the model.
        // The model-only border fields must come back unchanged via the Tag, not be flattened by the WPF Border.
        view.LoadModel(view.Model);
        view.CommitToModel();

        var border = view.Model.Blocks.OfType<Paragraph>().Single().Formatting.Border;
        border.Should().NotBeNull();
        border!.LineStyle.Should().Be(BorderLineStyle.Double);
        border.Right.Should().BeFalse();
        border.Top.Should().BeTrue();
    }

    [StaFact]
    public void SetParagraphShading_AppliesColourAndPattern_AndSurvivesCommit()
    {
        var view = ViewWith("shaded");
        SelectAllParagraphs(view);
        view.SetParagraphShading("#DDDDDD", ShadingPattern.Pct25);

        view.LoadModel(view.Model);
        view.CommitToModel();

        var formatting = view.Model.Blocks.OfType<Paragraph>().Single().Formatting;
        formatting.ShadingColorHex.Should().Be("#DDDDDD");
        formatting.ShadingPattern.Should().Be(ShadingPattern.Pct25);
    }

    [StaFact]
    public void SetParagraphShading_NullColour_ClearsShading()
    {
        var view = ViewWith("p");
        SelectAllParagraphs(view);
        view.SetParagraphShading("#FFFF00", ShadingPattern.Solid);
        view.Model.Blocks.OfType<Paragraph>().Single().Formatting.ShadingColorHex.Should().Be("#FFFF00");

        SelectAllParagraphs(view);
        view.SetParagraphShading(null, ShadingPattern.Clear);

        var formatting = view.Model.Blocks.OfType<Paragraph>().Single().Formatting;
        formatting.ShadingColorHex.Should().BeNull();
        formatting.ShadingPattern.Should().Be(ShadingPattern.Clear);
    }

    [StaFact]
    public void SetParagraphBorder_IsReversible_ViaUndo()
    {
        var view = ViewWith("para");
        SelectAllParagraphs(view);
        view.SetParagraphBorder(new ParagraphBorder("#000000", 1.0) { LineStyle = BorderLineStyle.Dotted });

        view.Commands.Undo();

        view.Model.Blocks.OfType<Paragraph>().Single().Formatting.Border.Should().BeNull();
    }
}
