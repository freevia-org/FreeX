using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

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

    [StaFact]
    public void CreatePayload_PreservesModeledEffectsLostByFlowDocument()
    {
        var shadowColor = new ThemeAwareColor(SrgbColor.FromRgb(0x203040), 0x9A);
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run
                {
                    Text = "effect",
                    TextFill = new ShapeFill.Gradient(
                        new ThemeAwareColor(SrgbColor.FromRgb(0x102030)),
                        new ThemeAwareColor(SrgbColor.FromRgb(0xD0E0F0)),
                        angleDegrees: 22.0),
                    TextOutline = new ShapeOutline.Visible(
                        new ThemeAwareColor(SrgbColor.FromRgb(0x506070)),
                        widthPt: 1.75),
                    TextShadow = new RunTextShadow
                    {
                        Color = shadowColor,
                        Alpha = 0x67,
                        BlurPt = 3.25,
                        DistPt = 2.0,
                        DirDeg = 135.0,
                    },
                    TextReflection = new RunTextReflection
                    {
                        Alpha = 0x55,
                        ScaleY = -0.5,
                        EndPos = 0.7,
                    },
                    TextGlow = new RunTextGlow
                    {
                        Color = new ThemeAwareColor(SrgbColor.FromRgb(0xF0B000)),
                        Alpha = 0x80,
                        RadiusPt = 4.0,
                    },
                    TextSoftEdge = new RunTextSoftEdge { RadiusPt = 1.5 },
                },
            },
        });
        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(source, 12));
        box.SelectAll();

        var payload = WpfRichTextClipboardAdapter.CreatePayload(box, source);

        payload.Should().NotBeNull();
        var run = payload!.Body.Paragraphs.Single().Runs.Single();
        run.TextFill.Should().BeOfType<ShapeFill.Gradient>();
        run.TextOutline.Should().BeOfType<ShapeOutline.Visible>();
        run.TextShadow.Should().BeEquivalentTo(source.Paragraphs[0].Runs[0].TextShadow);
        run.TextReflection.Should().BeEquivalentTo(source.Paragraphs[0].Runs[0].TextReflection);
        run.TextGlow.Should().BeEquivalentTo(source.Paragraphs[0].Runs[0].TextGlow);
        run.TextSoftEdge.Should().BeEquivalentTo(source.Paragraphs[0].Runs[0].TextSoftEdge);

        var data = WpfRichTextClipboardAdapter.BuildDataObject(box, payload);
        var target = InCanvasRichClipboardPayload.FromPlainText("target").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();

        WpfRichTextClipboardAdapter.TryPasteDataObject(
            targetBox,
            target,
            data,
            out var updated).Should().BeTrue();
        updated.Should().NotBeNull();
        updated!.Paragraphs.Single().Runs.Single().TextShadow.Should()
            .BeEquivalentTo(source.Paragraphs[0].Runs[0].TextShadow);
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
