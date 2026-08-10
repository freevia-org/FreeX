using System.Linq;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class LinkedStyleApplicationTests
{
    [StaFact]
    public void ApplyNamedStyle_SelectedText_UsesLinkedCharacterStyleAndSingleUndo()
    {
        var view = CreateView("Linked text");
        Select(view, 0, 6);

        view.ApplyNamedStyle("Heading1");

        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.StyleId.Should().BeNull();
        string.Concat(paragraph.Runs.Where(run => run.Formatting.Bold).Select(run => run.Text))
            .Should().Be("Linked");
        paragraph.Runs.Where(run => run.Formatting.Bold).Should()
            .OnlyContain(run => run.Formatting.ColorHex == "#2F5496");

        view.Commands.Undo().Should().BeTrue();
        ((Paragraph)view.Model.Blocks[0]).Runs.Should().OnlyContain(run => !run.Formatting.Bold);
    }

    [StaFact]
    public void ApplyNamedStyle_CollapsedCaret_AppliesParagraphSide()
    {
        var view = CreateView("Linked text");
        view.MoveCaretToBlockForTest(0, 3);

        view.ApplyNamedStyle("Heading1");

        ((Paragraph)view.Model.Blocks[0]).StyleId.Should().Be("Heading1");
        view.Commands.Undo().Should().BeTrue();
        ((Paragraph)view.Model.Blocks[0]).StyleId.Should().BeNull();
    }

    [StaFact]
    public void CommitStylePreview_RestoresOriginalSelectionBeforeApplyingLinkedStyle()
    {
        var view = CreateView("Linked text");
        Select(view, 0, 6);

        view.PreviewParagraphStyle("Heading1");
        view.CommitStylePreview("Heading1");

        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.StyleId.Should().BeNull();
        string.Concat(paragraph.Runs.Where(run => run.Formatting.Bold).Select(run => run.Text))
            .Should().Be("Linked");
    }

    [StaFact]
    public void EndStylePreview_RestoresOriginalSelectionForSubsequentApply()
    {
        var view = CreateView("Linked text");
        Select(view, 0, 6);

        view.PreviewParagraphStyle("Heading1");
        view.EndStylePreview();
        view.ApplyNamedStyle("Heading1");

        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.StyleId.Should().BeNull();
        string.Concat(paragraph.Runs.Where(run => run.Formatting.Bold).Select(run => run.Text))
            .Should().Be("Linked");
    }

    [StaFact]
    public void ApplyNamedStyle_WholeParagraphSelection_UsesLinkedCharacterStyle()
    {
        var view = CreateView("Whole paragraph");
        Select(view, 0, "Whole paragraph".Length);

        view.ApplyNamedStyle("Heading1");

        var paragraph = (Paragraph)view.Model.Blocks[0];
        paragraph.StyleId.Should().BeNull();
        paragraph.Runs.Should().OnlyContain(run => run.Formatting.Bold);
    }

    [StaFact]
    public void ApplyNamedStyle_InvalidLinkedTarget_KeepsParagraphApplication()
    {
        var view = CreateView("Linked text", validCharacterLink: false);
        Select(view, 0, 6);

        view.ApplyNamedStyle("Heading1");

        ((Paragraph)view.Model.Blocks[0]).StyleId.Should().Be("Heading1");
    }

    private static DocumentView CreateView(string text, bool validCharacterLink = true)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph(text));
        document.Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "Heading 1",
            Type = StyleType.Paragraph,
            LinkedStyleId = validCharacterLink ? "Heading1Char" : "Normal",
        };
        document.Styles["Heading1Char"] = new DocumentStyle
        {
            Id = "Heading1Char",
            Name = "Heading 1 Char",
            Type = StyleType.Character,
            LinkedStyleId = "Heading1",
            Run = RunFormatting.Default with { Bold = true, ColorHex = "#2F5496" },
        };

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    private static void Select(DocumentView view, int startOffset, int endOffset)
    {
        var paragraph = view.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        view.Selection.Select(
            TextPointerAt(paragraph, startOffset),
            TextPointerAt(paragraph, endOffset));
    }

    private static System.Windows.Documents.TextPointer TextPointerAt(
        System.Windows.Documents.Paragraph paragraph,
        int offset)
    {
        var run = paragraph.Inlines.OfType<System.Windows.Documents.Run>().First();
        return run.ContentStart.GetPositionAtOffset(offset)
            ?? throw new InvalidOperationException("Unable to resolve test text offset.");
    }
}
