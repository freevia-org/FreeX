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

    private static TextDocument TabbedDocument(string text, params TabStop[] stops)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new FreeW.Core.Model.Paragraph
        {
            Formatting = ParagraphFormatting.Default with { TabStops = stops }
        };
        paragraph.Runs.Add(new FreeW.Core.Model.Run(text));
        doc.Blocks.Add(paragraph);
        return doc;
    }

    private static TextDocument TabbedDocumentWithRuns(IEnumerable<FreeW.Core.Model.Run> runs, params TabStop[] stops)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var paragraph = new FreeW.Core.Model.Paragraph
        {
            Formatting = ParagraphFormatting.Default with { TabStops = stops }
        };
        foreach (var run in runs)
            paragraph.Runs.Add(run);
        doc.Blocks.Add(paragraph);
        return doc;
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

    [StaFact]
    public void ApplyPageSettings_UpdatesDefaultTabStopInterval()
    {
        var view = ViewWith("para");

        view.ApplyPageSettings(page => page.DefaultTabStopPt = 42);

        view.Model.Page.DefaultTabStopPt.Should().Be(42);
    }

    [StaFact]
    public void TabsDialogApplyPath_PreservesCustomStops_WhenUpdatingDefaultInterval()
    {
        var view = ViewWith("para");
        SelectAllParagraphs(view);

        var stops = new[] { new TabStop(108, TabStopAlignment.Center, TabLeader.Dots) };
        view.SetParagraphTabStops(stops);
        view.ApplyPageSettings(page => page.DefaultTabStopPt = 42);

        view.Model.Page.DefaultTabStopPt.Should().Be(42);
        view.Model.Blocks.OfType<Paragraph>().Single().Formatting.TabStops.Should().Equal(stops);
    }

    [StaFact]
    public void BuildParagraph_WithLeaderTab_EmitsSharedPlannedSpacer()
    {
        var doc = TabbedDocument("Name\tValue", new TabStop(144, TabStopAlignment.Left, TabLeader.Dots));
        var view = new DocumentView();

        view.LoadModel(doc);

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        var plans = DocumentView.GetRenderedTabStopPlans(paragraph);
        plans.Should().ContainSingle();
        var plan = plans.Single();
        plan.StopPositionDip.Should().BeApproximately(192, 0.01);
        plan.SegmentStartDip.Should().BeApproximately(192, 0.01);
        plan.AdvanceDip.Should().BeGreaterThan(120);
        plan.Alignment.Should().Be(TabStopAlignment.Left);
        plan.Leader.Should().Be(TabLeader.Dots);
        plan.IsExplicit.Should().BeTrue();
    }

    [StaFact]
    public void BuildParagraph_WithRightTab_MeasuresFollowingSegmentAcrossRuns()
    {
        var stop = new TabStop(288, TabStopAlignment.Right);
        var view = new DocumentView();
        view.LoadModel(TabbedDocumentWithRuns(
            [new FreeW.Core.Model.Run("Name\t"), new FreeW.Core.Model.Run("Value")],
            stop));

        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        var plan = DocumentView.GetRenderedTabStopPlans(paragraph).Single();

        plan.Alignment.Should().Be(TabStopAlignment.Right);
        plan.StopPositionDip.Should().BeApproximately(384, 0.01);
        plan.SegmentStartDip.Should().BeLessThan(
            plan.StopPositionDip - 10,
            "the text after the tab can live in the next run and still participates in right-tab alignment");
    }

    [StaFact]
    public void BuildParagraph_WithDecimalTab_AlignsSeparatorRatherThanRightEdge()
    {
        var decimalView = new DocumentView();
        decimalView.LoadModel(TabbedDocument("Total\t123.45", new TabStop(216, TabStopAlignment.Decimal)));
        var rightView = new DocumentView();
        rightView.LoadModel(TabbedDocument("Total\t123.45", new TabStop(216, TabStopAlignment.Right)));

        var decimalParagraph = decimalView.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        var rightParagraph = rightView.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        var decimalPlan = DocumentView.GetRenderedTabStopPlans(decimalParagraph).Single();
        var rightPlan = DocumentView.GetRenderedTabStopPlans(rightParagraph).Single();

        decimalPlan.Alignment.Should().Be(TabStopAlignment.Decimal);
        decimalPlan.StopPositionDip.Should().BeApproximately(rightPlan.StopPositionDip, 0.01);
        decimalPlan.SegmentStartDip.Should().BeGreaterThan(
            rightPlan.SegmentStartDip + 5,
            "decimal tabs align the separator, not the segment's right edge");
    }

    [StaFact]
    public void BuildParagraph_WithRenderedTabSpacer_RoundTripsLiteralTabAndStops()
    {
        var stop = new TabStop(144, TabStopAlignment.Left, TabLeader.Dots);
        var view = new DocumentView();
        view.LoadModel(TabbedDocument("Name\tValue", stop));

        view.CommitToModel();

        var paragraph = view.Model.Blocks.OfType<Paragraph>().Single();
        paragraph.PlainText.Should().Be("Name\tValue");
        paragraph.Formatting.TabStops.Should().Equal(stop);
    }
}
