using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;

namespace FreeP.App.Host.Tests;

public sealed class WpfRichTextClipboardAdapterTests
{
    [StaFact]
    public void BuildDataObject_PublishesFreePAndNativeRichFormats()
    {
        var source = Body();
        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(source, 12));
        box.SelectAll();
        var selection = new InCanvasEditorTextSelection(0, InCanvasTextEditPlanner.ExtractPlainText(source).Length);
        var payload = InCanvasRichClipboardPlanner.Capture(source, selection);

        var data = WpfRichTextClipboardAdapter.BuildDataObject(box, payload);

        data.GetDataPresent(PresentationClipboardFormats.RichText, autoConvert: false).Should().BeTrue();
        data.GetDataPresent(DataFormats.UnicodeText, autoConvert: false).Should().BeTrue();
        data.GetDataPresent(DataFormats.Rtf, autoConvert: false).Should().BeTrue();
        data.GetDataPresent(DataFormats.XamlPackage, autoConvert: false).Should().BeTrue();
        data.GetData(PresentationClipboardFormats.RichText, autoConvert: false)
            .Should().BeOfType<System.IO.MemoryStream>();
    }

    [StaFact]
    public void TryPasteDataObject_RestoresRichPayloadIntoWpfDocument()
    {
        var source = Body();
        var sourceBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(source, 12));
        sourceBox.SelectAll();
        var payload = InCanvasRichClipboardPlanner.Capture(
            source,
            new InCanvasEditorTextSelection(0, InCanvasTextEditPlanner.ExtractPlainText(source).Length));
        var data = WpfRichTextClipboardAdapter.BuildDataObject(sourceBox, payload);

        var target = InCanvasRichClipboardPayload.FromPlainText("replace me").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();

        WpfRichTextClipboardAdapter.TryPasteDataObject(targetBox, target, data, out var updated)
            .Should().BeTrue();

        updated.Should().NotBeNull();
        InCanvasTextEditPlanner.ExtractPlainText(updated!).Should().Be(payload.PlainText);
        updated!.Paragraphs[0].Runs.Should().Contain(run => run.Bold);
        updated.Paragraphs[0].Runs.Should().Contain(run => run.Text == "\n");
        updated.Paragraphs[1].BulletKind.Should().Be(BulletKind.Char);
    }

    private static TextBody Body()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Auto,
            Runs =
            {
                new Run { Text = "Bold", Bold = true, BoldSet = true },
                new Run { Text = "\n", Italic = true, ItalicSet = true },
                new Run { Text = "line" },
            },
        });
        body.Paragraphs.Add(new Paragraph
        {
            BulletKind = BulletKind.Char,
            BulletChar = "-",
            Runs = { new Run { Text = "second" } },
        });
        return body;
    }
}
