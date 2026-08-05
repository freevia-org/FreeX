using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class WpfRichTextClipboardAdapterTests
{
    [StaFact]
    public void BuildDataObject_NativeXamlPackagePublishesInlineImagePartAndLoads()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
        var source = new TextBody
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new Run { Text = "Before " },
                        new Run
                        {
                            Text = "\uFFFC",
                            InlineImage = new ImagePart { Bytes = png, ContentType = "image/png" },
                            InlineImageWidthEmu = 228_600,
                            InlineImageHeightEmu = 114_300,
                        },
                        new Run { Text = " After" },
                    },
                },
            },
        };
        var box = new RichTextBox
        {
            Document = TextBodyFlowDocumentConverter.ToFlowDocument(source, 12),
        };
        box.SelectAll();
        var payload = new InCanvasRichClipboardPayload(
            source,
            InCanvasTextEditPlanner.ExtractPlainText(source));

        var data = WpfRichTextClipboardAdapter.BuildDataObject(box, payload);
        var packageBytes = ((MemoryStream)data.GetData(
            DataFormats.XamlPackage,
            autoConvert: false)!).ToArray();
        using (var package = new ZipArchive(
                   new MemoryStream(packageBytes, writable: false),
                   ZipArchiveMode.Read))
        {
            package.Entries.Should().Contain(entry =>
                entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        }

        var document = new System.Windows.Documents.FlowDocument();
        using var stream = new MemoryStream(packageBytes, writable: false);
        new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd)
            .Load(stream, DataFormats.XamlPackage);

        var paragraph = document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        var inlines = paragraph.Inlines.ToArray();
        inlines.Select(inline => inline is System.Windows.Documents.InlineUIContainer ? "image" : "text")
            .Should().Equal("text", "image", "text");
        new System.Windows.Documents.TextRange(
                paragraph.ContentStart,
                ((System.Windows.Documents.InlineUIContainer)inlines[1]).ContentEnd)
            .Text.Should().Contain("Before ");
        ((System.Windows.Documents.InlineUIContainer)inlines[1]).Child
            .Should().BeOfType<Image>();
        new System.Windows.Documents.TextRange(
                ((System.Windows.Documents.InlineUIContainer)inlines[1]).ContentEnd,
                paragraph.ContentEnd)
            .Text.Should().Contain(" After");
    }

    [StaFact]
    public void InlineOleRun_RoundTripsThroughWpfFlowDocument()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run { Text = "Before" },
                new Run
                {
                    Text = "\uFFFC",
                    InlineOleObject = new InlineOleObjectInfo
                    {
                        EmbeddedBytes = [0x01, 0x02, 0x03],
                        FileName = "Embedded.docx",
                        ClassName = "Word.Document.12",
                    },
                },
                new Run { Text = "After" },
            },
        });

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(source);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(document, source);

        restored.Paragraphs.Single().Runs.Select(run => run.Text)
            .Should().Equal("Before", "\uFFFC", "After");
        var inlineOle = restored.Paragraphs.Single().Runs[1].InlineOleObject;
        inlineOle.Should().NotBeNull();
        inlineOle!.EmbeddedBytes
            .Should().Equal(0x01, 0x02, 0x03);
        inlineOle.FileName
            .Should().Be("Embedded.docx");
        inlineOle.ClassName
            .Should().Be("Word.Document.12");
    }

    [StaFact]
    public void TryPasteDataObject_PreservesInlineXamlImageInsideTextRunSequence()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
        var xaml = $"<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph><Run Text=\"Before\"/><Image Source=\"data:image/png;base64,{Convert.ToBase64String(png)}\" Width=\"24\" Height=\"12\"/><Run Text=\"After\"/></Paragraph></FlowDocument>";
        var target = InCanvasRichClipboardPayload.FromPlainText("replace me").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();
        var data = new DataObject();
        data.SetData(
            DataFormats.XamlPackage,
            new MemoryStream(CreateXamlPackage(xaml)),
            autoConvert: false);

        WpfRichTextClipboardAdapter.TryPasteDataObject(targetBox, target, data, out var updated)
            .Should().BeTrue();

        updated.Should().NotBeNull();
        updated!.Paragraphs.Single().Runs.Select(run => run.Text)
            .Should().Equal("Before", "\uFFFC", "After");
        var inline = updated.Paragraphs.Single().Runs[1];
        inline.InlineImage.Should().NotBeNull();
        inline.InlineImage!.Bytes.Should().NotBeEmpty();
        inline.InlineImageWidthEmu.Should().Be(228_600);
        inline.InlineImageHeightEmu.Should().Be(114_300);
    }

    [StaFact]
    public void TryPasteDataObject_UsesSharedXamlPackageBeforeRtfAndPlainText()
    {
        var target = InCanvasRichClipboardPayload.FromPlainText("replace me").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();
        var data = new DataObject();
        data.SetData(
            DataFormats.XamlPackage,
            new MemoryStream(CreateXamlPackage(
                "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph><Hyperlink NavigateUri=\"https://example.test/package\" ToolTip=\"Package link\"><Bold>Package</Bold></Hyperlink><Italic> text</Italic></Paragraph></FlowDocument>")),
            autoConvert: false);
        data.SetData(DataFormats.Rtf, Encoding.ASCII.GetBytes(@"{\rtf1\ansi\b ignored\b0}"));
        data.SetText("plain fallback");

        WpfRichTextClipboardAdapter.TryPasteDataObject(targetBox, target, data, out var updated)
            .Should().BeTrue();

        InCanvasTextEditPlanner.ExtractPlainText(updated!).Should().Be("Package text");
        updated!.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        updated.Paragraphs[0].Runs[0].Hyperlink!.Url.Should().Be("https://example.test/package");
        updated.Paragraphs[0].Runs[0].Hyperlink!.Tooltip.Should().Be("Package link");
        updated.Paragraphs[0].Runs[1].Italic.Should().BeTrue();
    }

    [StaFact]
    public void TryPasteDataObject_CustomPayloadPrecedesXamlPackageAndRtf()
    {
        var target = InCanvasRichClipboardPayload.FromPlainText("replace me").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();
        var custom = InCanvasRichClipboardPayload.FromPlainText("custom");
        var data = new DataObject();
        data.SetData(
            PresentationClipboardFormats.RichText,
            new MemoryStream(InCanvasRichClipboardPlanner.Serialize(custom)),
            autoConvert: false);
        data.SetData(
            DataFormats.XamlPackage,
            new MemoryStream(CreateXamlPackage(
                "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph><Bold>ignored package</Bold></Paragraph></FlowDocument>")),
            autoConvert: false);
        data.SetData(DataFormats.Rtf, Encoding.ASCII.GetBytes(@"{\rtf1\ansi ignored rtf}"));
        data.SetText("plain fallback");

        WpfRichTextClipboardAdapter.TryPasteDataObject(targetBox, target, data, out var updated)
            .Should().BeTrue();

        InCanvasTextEditPlanner.ExtractPlainText(updated!).Should().Be("custom");
    }

    [StaFact]
    public void TryPasteDataObject_UsesSharedXamlPackageListMarkers()
    {
        var target = InCanvasRichClipboardPayload.FromPlainText("replace me").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();
        var data = new DataObject();
        data.SetData(
            DataFormats.XamlPackage,
            new MemoryStream(CreateXamlPackage(
                """
                <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                              xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                              xmlns:sys="clr-namespace:System;assembly=mscorlib">
                      <FlowDocument.Resources>
                        <ResourceDictionary>
                          <SolidColorBrush x:Key="Accent" Color="#FF2F5597" />
                          <FontFamily x:Key="BodyFont">Aptos</FontFamily>
                          <sys:Double x:Key="BodySize">18</sys:Double>
                          <Style x:Key="ListBase">
                            <Setter Property="Foreground" Value="{StaticResource Accent}" />
                            <Setter Property="FontFamily" Value="{DynamicResource BodyFont}" />
                            <Setter Property="FontSize" Value="{StaticResource BodySize}" />
                          </Style>
                          <Style x:Key="ListText" BasedOn="{StaticResource ListBase}">
                            <Setter Property="FontWeight" Value="Bold" />
                          </Style>
                        </ResourceDictionary>
                      </FlowDocument.Resources>
                      <List MarkerStyle="UpperRoman">
                        <ListItem><Paragraph Style="{StaticResource ListText}">First</Paragraph></ListItem>
                        <ListItem><Paragraph>Second</Paragraph></ListItem>
                  </List>
                </FlowDocument>
                """)),
            autoConvert: false);

        WpfRichTextClipboardAdapter.TryPasteDataObject(targetBox, target, data, out var updated)
            .Should().BeTrue();

        updated!.Paragraphs.Should().HaveCount(2);
        updated.Paragraphs[0].BulletKind.Should().Be(BulletKind.Auto);
        updated.Paragraphs[0].AutoNumType.Should().Be(AutoNumType.RomanUcPeriod);
        updated.Paragraphs[0].Runs.Single().Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x2F5597));
        updated.Paragraphs[0].Runs.Single().FontFamily.Should().Be("Aptos");
        updated.Paragraphs[0].Runs.Single().FontSizePt.Should().Be(13.5);
        updated.Paragraphs[0].Runs.Single().Bold.Should().BeTrue();
        updated.Paragraphs[1].BulletKind.Should().Be(BulletKind.Auto);
    }

    [StaFact]
    public void ExternalRtfTable_UsesNativeWpfTableBlockAndTabRowTextProjection()
    {
        const string rtf = @"{\rtf1\ansi\trowd\cellx1440\cellx2880 A\cell B\cell\row\trowd\cellx1440\cellx2880 C\cell D\cell\row}";
        var document = new System.Windows.Documents.FlowDocument();
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes(rtf));
        new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd)
            .Load(stream, DataFormats.Rtf);

        document.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<System.Windows.Documents.Table>();
        new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd)
            .Text.Should().Be("A\tB\r\nC\tD\r\n");
    }

    [StaFact]
    public void TryPasteDataObject_UsesSharedRtfTableProjectionWhenCustomPayloadIsAbsent()
    {
        const string rtf = @"{\rtf1\ansi\trowd\cellx1440\cellx2880\b A\b0\cell\i B\i0\cell\row\trowd\cellx1440\cellx2880 C\cell D\cell\row}";
        var target = InCanvasRichClipboardPayload.FromPlainText("replace me").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();
        var data = new DataObject();
        data.SetData(DataFormats.Rtf,
            new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes(rtf)),
            autoConvert: false);
        data.SetText("plain fallback", TextDataFormat.UnicodeText);

        WpfRichTextClipboardAdapter.TryPasteDataObject(targetBox, target, data, out var updated)
            .Should().BeTrue();

        updated.Should().NotBeNull();
        InCanvasTextEditPlanner.ExtractPlainText(updated!)
            .Should().Be("A\tB\nC\tD");
        updated!.Paragraphs[0].Runs.Should().Contain(run => run.Text == "A" && run.Bold);
        updated.Paragraphs[0].Runs.Should().Contain(run => run.Text == "B" && run.Italic);
    }

    [StaFact]
    public void TryPasteDataObject_UsesPlainTextAfterMalformedRtf()
    {
        var target = InCanvasRichClipboardPayload.FromPlainText("replace me").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();
        var data = new DataObject();
        data.SetData(DataFormats.Rtf, "not an rtf payload", autoConvert: false);
        data.SetText("plain fallback", TextDataFormat.UnicodeText);

        WpfRichTextClipboardAdapter.TryPasteDataObject(targetBox, target, data, out var updated)
            .Should().BeTrue();

        InCanvasTextEditPlanner.ExtractPlainText(updated!).Should().Be("plain fallback");
    }

    [StaFact]
    public void TryPasteDataObject_PreservesRtfBaselineOffsets()
    {
        const string rtf =
            @"{\rtf1\ansi\deff0{\fonttbl{\f0 Calibri;}}\f0\fs24 H\super i\sub j\nosupersub k}";
        var target = InCanvasRichClipboardPayload.FromPlainText("replace me").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();
        var data = new DataObject();
        data.SetData(
            DataFormats.Rtf,
            new MemoryStream(Encoding.ASCII.GetBytes(rtf)),
            autoConvert: false);

        WpfRichTextClipboardAdapter.TryPasteDataObject(targetBox, target, data, out var updated)
            .Should().BeTrue();

        var runs = updated!.Paragraphs.Single().Runs;
        runs.Select(run => run.Text).Should().Equal("H", "i", "j", "k");
        runs[0].BaselineOffset.Should().BeNull();
        runs[1].BaselineOffset.Should().Be(25_000);
        runs[2].BaselineOffset.Should().Be(-25_000);
        runs[3].BaselineOffset.Should().BeNull();
    }

    [StaFact]
    public void TryPasteDataObject_PreservesXamlBaselineAlignment()
    {
        const string xaml =
            "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph><Run Text=\"base\"/><Run BaselineAlignment=\"Superscript\" Text=\"up\"/><Run BaselineAlignment=\"Subscript\" Text=\"down\"/></Paragraph></FlowDocument>";
        var target = InCanvasRichClipboardPayload.FromPlainText("replace me").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();
        var data = new DataObject();
        data.SetData(
            DataFormats.XamlPackage,
            new MemoryStream(CreateXamlPackage(xaml)),
            autoConvert: false);

        WpfRichTextClipboardAdapter.TryPasteDataObject(targetBox, target, data, out var updated)
            .Should().BeTrue();

        updated!.Paragraphs.Single().Runs.Select(run => run.BaselineOffset)
            .Should().Equal(null, 10_000, -10_000);
    }

    [StaFact]
    public void TryPasteDataObject_PreservesXamlFlowDirection()
    {
        const string xaml =
            "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" FlowDirection=\"RightToLeft\"><Paragraph><Run Text=\"אבג\"/><Run FlowDirection=\"LeftToRight\" Text=\"LTR\"/></Paragraph></FlowDocument>";
        var target = InCanvasRichClipboardPayload.FromPlainText("replace me").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();
        var data = new DataObject();
        data.SetData(
            DataFormats.XamlPackage,
            new MemoryStream(CreateXamlPackage(xaml)),
            autoConvert: false);

        WpfRichTextClipboardAdapter.TryPasteDataObject(targetBox, target, data, out var updated)
            .Should().BeTrue();

        updated!.Paragraphs.Single().RightToLeft.Should().BeTrue();
        updated.Paragraphs.Single().Runs.Select(run => run.RightToLeft)
            .Should().Equal(true, false);
    }

    [StaFact]
    public void TryPasteDataObject_PreservesXamlTextAlignment()
    {
        const string xaml =
            "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TextAlignment=\"Center\"><Paragraph>centered</Paragraph><Paragraph TextAlignment=\"Right\">right</Paragraph></FlowDocument>";
        var target = InCanvasRichClipboardPayload.FromPlainText("replace me").Body;
        var targetBox = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(target, 12));
        targetBox.SelectAll();
        var data = new DataObject();
        data.SetData(
            DataFormats.XamlPackage,
            new MemoryStream(CreateXamlPackage(xaml)),
            autoConvert: false);

        WpfRichTextClipboardAdapter.TryPasteDataObject(targetBox, target, data, out var updated)
            .Should().BeTrue();

        updated!.Paragraphs.Select(paragraph => paragraph.Align)
            .Should().Equal(TextAlign.Center, TextAlign.Right);
    }

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
    public void BuildDataObject_NativeXamlPackageIsReadableBySharedRichPlanner()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run { Text = "native ", Bold = true },
                new Run
                {
                    Text = "xaml",
                    Italic = true,
                    Hyperlink = new Hyperlink { Url = "https://example.com/native" },
                },
            },
        });
        var box = new RichTextBox(TextBodyFlowDocumentConverter.ToFlowDocument(source, 12));
        box.SelectAll();
        var payload = InCanvasRichClipboardPlanner.Capture(
            source,
            new InCanvasEditorTextSelection(0, InCanvasTextEditPlanner.ExtractPlainText(source).Length));

        var data = WpfRichTextClipboardAdapter.BuildDataObject(box, payload);
        var bytes = ((MemoryStream)data.GetData(DataFormats.XamlPackage, autoConvert: false)!).ToArray();
        var restored = ExternalXamlClipboardPlanner.TryParseXamlPackage(bytes);

        restored.Should().NotBeNull();
        restored!.PlainText.Should().Be("native xaml");
        restored.Body.Paragraphs.Single().Runs.Should().Contain(run =>
            run.Text == "native " && run.Bold);
        restored.Body.Paragraphs.Single().Runs.Should().Contain(run =>
            run.Text == "xaml"
            && run.Italic
            && run.Hyperlink!.Url == "https://example.com/native");
    }

    [StaFact]
    public void SharedXamlPackage_IsAcceptedByNativeWpfTextRangeLoader()
    {
        var source = new TextBody();
        source.Paragraphs.Add(new Paragraph
        {
            Runs =
            {
                new Run { Text = "sixteen", FontSizePt = 16, Bold = true },
                new Run { Text = " point" },
            },
        });
        var payload = new InCanvasRichClipboardPayload(
            source,
            InCanvasTextEditPlanner.ExtractPlainText(source));
        var packageBytes = ExternalXamlClipboardPlanner.SerializeXamlPackage(payload);

        using (var package = new ZipArchive(
                   new MemoryStream(packageBytes, writable: false),
                   ZipArchiveMode.Read))
        {
            package.Entries.Select(entry => entry.FullName)
                .Should().Contain("Xaml/Document.xaml");
        }

        var document = new System.Windows.Documents.FlowDocument();
        using var stream = new MemoryStream(packageBytes, writable: false);
        new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd)
            .Load(stream, DataFormats.XamlPackage);

        new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd).Text
            .Should().Be("sixteen point\r\n");
        var run = document.Blocks.OfType<System.Windows.Documents.Paragraph>()
            .Single().Inlines.OfType<System.Windows.Documents.Run>().First();
        run.Text.Should().Be("sixteen");
        run.FontWeight.Should().Be(FontWeights.Bold);
        run.FontSize.Should().BeApproximately(16 / 0.75, 0.01);
    }

    [StaFact]
    public void SharedXamlPackage_WithInlineImage_IsAcceptedByNativeWpfTextRangeLoader()
    {
        var imageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
        var source = new TextBody
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new Run { Text = "Before " },
                        new Run
                        {
                            Text = "\uFFFC",
                            InlineImage = new ImagePart
                            {
                                Bytes = imageBytes,
                                ContentType = "image/png",
                            },
                            InlineImageWidthEmu = 228_600,
                            InlineImageHeightEmu = 114_300,
                        },
                        new Run { Text = " after" },
                    },
                },
            },
        };
        var payload = new InCanvasRichClipboardPayload(
            source,
            InCanvasTextEditPlanner.ExtractPlainText(source));
        var packageBytes = ExternalXamlClipboardPlanner.SerializeXamlPackage(payload);

        var document = new System.Windows.Documents.FlowDocument();
        using var stream = new MemoryStream(packageBytes, writable: false);
        new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd)
            .Load(stream, DataFormats.XamlPackage);

        var paragraph = document.Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<System.Windows.Documents.Paragraph>().Subject;
        paragraph.Inlines.Select(inline => inline.GetType().Name)
            .Should().Equal("Run", "InlineUIContainer", "Run");
        paragraph.Inlines.OfType<System.Windows.Documents.Run>()
            .Select(run => run.Text).Should().Equal("Before ", " after");
        var imageHost = paragraph.Inlines.ElementAt(1)
            .Should().BeOfType<System.Windows.Documents.InlineUIContainer>().Subject;
        var image = imageHost.Child.Should().BeOfType<System.Windows.Controls.Image>().Subject;
        image.Width.Should().BeApproximately(24, 0.01);
        image.Height.Should().BeApproximately(12, 0.01);
        image.Source.Should().BeOfType<System.Windows.Media.Imaging.BitmapImage>();
        var bitmap = (System.Windows.Media.Imaging.BitmapImage)image.Source;
        bitmap.PixelWidth.Should().Be(1);
        bitmap.PixelHeight.Should().Be(1);
    }

    [StaFact]
    public void SharedXamlPackage_WithStrikethrough_IsAcceptedByNativeWpfTextRangeLoader()
    {
        var source = new TextBody
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new Run { Text = "underlined", Underline = true, Strikethrough = true },
                        new Run { Text = " plain" },
                    },
                },
            },
        };
        var payload = new InCanvasRichClipboardPayload(
            source,
            InCanvasTextEditPlanner.ExtractPlainText(source));
        var packageBytes = ExternalXamlClipboardPlanner.SerializeXamlPackage(payload);

        var document = new System.Windows.Documents.FlowDocument();
        using var stream = new MemoryStream(packageBytes, writable: false);
        new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd)
            .Load(stream, DataFormats.XamlPackage);

        var runs = document.Blocks.OfType<System.Windows.Documents.Paragraph>()
            .Single().Inlines.OfType<System.Windows.Documents.Run>().ToArray();
        runs.Select(run => run.Text).Should().Equal("underlined", " plain");
        runs[0].TextDecorations.Should().Contain(decoration =>
            decoration.Location == System.Windows.TextDecorationLocation.Underline);
        runs[0].TextDecorations.Should().Contain(decoration =>
            decoration.Location == System.Windows.TextDecorationLocation.Strikethrough);
        runs[1].TextDecorations.Should().BeEmpty();
    }

    [StaFact]
    public void NativeWpfXamlPackage_PreservesStrikethroughAndSharedPlannerReadsIt()
    {
        var source = new TextBody
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new Run { Text = "native strike", Strikethrough = true },
                        new Run
                        {
                            Text = " link",
                            Strikethrough = true,
                            Hyperlink = new Hyperlink { Url = "https://example.test/native-wave161" },
                        },
                    },
                },
            },
        };
        var box = new RichTextBox
        {
            Document = TextBodyFlowDocumentConverter.ToFlowDocument(source, 12),
        };
        box.SelectAll();
        var payload = InCanvasRichClipboardPlanner.Capture(
            source,
            new InCanvasEditorTextSelection(0, InCanvasTextEditPlanner.ExtractPlainText(source).Length));

        var data = WpfRichTextClipboardAdapter.BuildDataObject(box, payload);
        var packageBytes = ((MemoryStream)data.GetData(
            DataFormats.XamlPackage,
            autoConvert: false)!).ToArray();

        var document = new System.Windows.Documents.FlowDocument();
        using (var stream = new MemoryStream(packageBytes, writable: false))
        {
            new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd)
                .Load(stream, DataFormats.XamlPackage);
        }

        var nativeRuns = document.Blocks.OfType<System.Windows.Documents.Paragraph>()
            .Single().Inlines.OfType<System.Windows.Documents.Run>().ToArray();
        nativeRuns.Should().Contain(run =>
            run.Text == "native strike"
            && run.TextDecorations.Any(decoration =>
                decoration.Location == System.Windows.TextDecorationLocation.Strikethrough));

        var restored = ExternalXamlClipboardPlanner.TryParseXamlPackage(packageBytes);
        restored.Should().NotBeNull();
        restored!.Body.Paragraphs.Single().Runs.Should().Contain(run =>
            run.Text == "native strike" && run.Strikethrough);
        restored.Body.Paragraphs.Single().Runs.Should().Contain(run =>
            run.Text == " link"
            && run.Strikethrough
            && run.Hyperlink!.Url == "https://example.test/native-wave161");
    }

    [StaFact]
    public void SharedXamlPackage_WithInlineTable_IsAcceptedByNativeWpfTextRangeLoader()
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.AddRange([914_400, 914_400]);
        var row = new TableRow { HeightEmu = 304_800 };
        row.Cells.Add(new TableCell
        {
            TextBody = InCanvasRichClipboardPayload.FromPlainText("Cell A").Body,
            Fill = new ShapeFill.Solid(new SrgbColor(0x20, 0x40, 0x60)),
            Anchor = TableCellAnchor.Middle,
            InsetLeftPt = 3,
            Borders = new TableCellBorders
            {
                Left = new ShapeOutline.Visible(new SrgbColor(0x10, 0x20, 0x30), 1),
            },
        });
        row.Cells.Add(new TableCell
        {
            TextBody = new TextBody
            {
                Paragraphs =
                {
                    new Paragraph
                    {
                        Runs = { new Run { Text = "Cell B", Bold = true, FontSizePt = 16 } },
                    },
                },
            },
        });
        table.Rows.Add(row);
        var source = new TextBody
        {
            Paragraphs =
            {
                new Paragraph
                {
                    Runs =
                    {
                        new Run { Text = "Before " },
                        new Run { Text = "\uFFFC", InlineTable = new InlineTableInfo { Table = table } },
                        new Run { Text = " after" },
                    },
                },
            },
        };
        var payload = new InCanvasRichClipboardPayload(
            source,
            InCanvasTextEditPlanner.ExtractPlainText(source));
        var packageBytes = ExternalXamlClipboardPlanner.SerializeXamlPackage(payload);

        var document = new System.Windows.Documents.FlowDocument();
        using var stream = new MemoryStream(packageBytes, writable: false);
        new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd)
            .Load(stream, DataFormats.XamlPackage);

        var blocks = document.Blocks.Cast<System.Windows.Documents.Block>().ToArray();
        blocks.Should().HaveCount(3);
        new System.Windows.Documents.TextRange(blocks[0].ContentStart, blocks[0].ContentEnd)
            .Text.Should().Be("Before ");
        var nativeTable = blocks[1].Should().BeOfType<System.Windows.Documents.Table>().Subject;
        var nativeCells = nativeTable.RowGroups.Single().Rows.Single().Cells;
        nativeCells.Should().HaveCount(2);
        new System.Windows.Documents.TextRange(nativeCells[0].ContentStart, nativeCells[0].ContentEnd)
            .Text.Should().Be("Cell A");
        new System.Windows.Documents.TextRange(nativeCells[1].ContentStart, nativeCells[1].ContentEnd)
            .Text.Should().Be("Cell B");
        new System.Windows.Documents.TextRange(blocks[2].ContentStart, blocks[2].ContentEnd)
            .Text.Should().Be(" after");
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

    private static byte[] CreateXamlPackage(string xaml)
    {
        using var output = new MemoryStream();
        using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(package.CreateEntry("Xaml/Document.xaml").Open(), Encoding.UTF8))
            writer.Write(xaml);
        return output.ToArray();
    }
}
