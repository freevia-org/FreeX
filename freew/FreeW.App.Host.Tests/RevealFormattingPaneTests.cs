using System.Linq;
using System.Windows.Documents;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA coverage for the data path behind the Reveal Formatting side pane (Word's Shift+F1 pane). The pane
/// rebuilds itself on <see cref="DocumentView.SelectionChanged"/> from the effective formatting the view
/// exposes (<see cref="DocumentView.CurrentRunFormatting"/> / <see cref="DocumentView.CurrentParagraphFormatting"/>)
/// fed through the pure <see cref="RevealFormatting.Describe"/> describer. These tests assert that moving the
/// caret raises SelectionChanged and that the resolved formatting (and therefore the pane's described content)
/// tracks the new selection. Needs STA + a Dispatcher for the RichTextBox/FlowDocument, so it runs as
/// <c>[StaFact]</c>.
/// </summary>
public sealed class RevealFormattingPaneTests
{
    private static DocumentView ViewWith(params Paragraph[] paragraphs)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var p in paragraphs)
            doc.Blocks.Add(p);
        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    private static void CaretInto(DocumentView view, int paragraphIndex)
    {
        var rendered = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();
        var caret = rendered[paragraphIndex].ContentStart;
        view.Selection.Select(caret, caret);
    }

    // The pane's update step, expressed exactly as MainWindow.RefreshRevealFormatting feeds it.
    private static IReadOnlyList<RevealFormattingSection> DescribeAtCaret(DocumentView view) =>
        RevealFormatting.Describe(view.CurrentRunFormatting, view.CurrentParagraphFormatting, view.Model.Page);

    [StaFact]
    public void MovingCaret_RaisesSelectionChanged_DrivingThePaneRefresh()
    {
        var view = ViewWith(new Paragraph("first"), new Paragraph("second"));

        var fired = 0;
        view.SelectionChanged += (_, _) => fired++;

        CaretInto(view, 1);

        fired.Should().BeGreaterThan(0);
    }

    [StaFact]
    public void DescribeAtCaret_TracksTheParagraphAlignmentUnderTheCaret()
    {
        var centered = new Paragraph("centered") { Formatting = new ParagraphFormatting { Alignment = TextAlignment.Center } };
        var plain = new Paragraph("plain");
        var view = ViewWith(centered, plain);

        CaretInto(view, 0);
        var first = DescribeAtCaret(view).Single(s => s.Heading == "PARAGRAPH")
            .Items.Single(i => i.Label == "Alignment").Value;
        first.Should().Be("Centered");

        CaretInto(view, 1);
        var second = DescribeAtCaret(view).Single(s => s.Heading == "PARAGRAPH")
            .Items.Single(i => i.Label == "Alignment").Value;
        second.Should().Be("Left");
    }

    [StaFact]
    public void DescribeAtCaret_ReflectsTheRunFontEffectsUnderTheCaret()
    {
        var bold = new Paragraph();
        bold.Runs.Add(new Run("bold", new RunFormatting { Bold = true }));
        var view = ViewWith(bold, new Paragraph("plain"));

        // Select the bold run's text so the effective run formatting is read from inside the run
        // (a caret at a paragraph boundary can read the boundary's run, not the first run's).
        var rendered = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().First();
        view.Selection.Select(rendered.ContentStart, rendered.ContentEnd);

        var effects = DescribeAtCaret(view).Single(s => s.Heading == "FONT")
            .Items.Single(i => i.Label == "Effects").Value;

        effects.Should().Contain("Bold");
    }
}
