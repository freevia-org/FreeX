using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA coverage for <see cref="DocumentView.SetParagraphTabStops"/> (the Tabs dialog's apply path): load a
/// model, select paragraph(s), set/clear their custom tab stops through the (reversible) command bus, and
/// assert the model's <see cref="ParagraphFormatting.TabStops"/> reflects the change so it round-trips via
/// the existing w:tabs writer. These need STA + a Dispatcher for the RichTextBox/FlowDocument, so they run
/// as <c>[StaFact]</c>.
/// </summary>
public sealed class TabStopApplyTests
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
    public void SetParagraphTabStops_AppliesPositionAlignmentLeader_ToSelectedParagraphs()
    {
        var view = ViewWith("one", "two");
        SelectAllParagraphs(view);

        var stops = new[]
        {
            new TabStop(72, TabStopAlignment.Left, TabLeader.Dots),
            new TabStop(216, TabStopAlignment.Right, TabLeader.Dashes),
            new TabStop(324, TabStopAlignment.Decimal, TabLeader.Underline)
        };
        view.SetParagraphTabStops(stops);

        foreach (var paragraph in view.Model.Blocks.OfType<Paragraph>())
            paragraph.Formatting.TabStops.Should().Equal(stops);
    }

    [StaFact]
    public void SetParagraphTabStops_EmptyList_ClearsAllStops()
    {
        var view = ViewWith("only");
        SelectAllParagraphs(view);
        view.SetParagraphTabStops([new TabStop(144, TabStopAlignment.Center)]);

        view.Model.Blocks.OfType<Paragraph>().Single().Formatting.TabStops.Should().HaveCount(1);

        // Clear All (the dialog passes an empty list) removes every custom stop.
        SelectAllParagraphs(view);
        view.SetParagraphTabStops([]);

        view.Model.Blocks.OfType<Paragraph>().Single().Formatting.TabStops.Should().BeEmpty();
    }

    [StaFact]
    public void SetParagraphTabStops_IsReversible_ViaUndo()
    {
        var view = ViewWith("para");
        SelectAllParagraphs(view);
        view.SetParagraphTabStops([new TabStop(108, TabStopAlignment.Right, TabLeader.Dots)]);

        view.Commands.Undo();

        view.Model.Blocks.OfType<Paragraph>().Single().Formatting.TabStops.Should().BeEmpty();
    }
}
