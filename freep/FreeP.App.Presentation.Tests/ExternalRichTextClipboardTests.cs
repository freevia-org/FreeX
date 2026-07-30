using System.IO.Compression;
using System.Text;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class ExternalRichTextClipboardTests
{
    [Fact]
    public void XamlPackageFlowDocument_PreservesCommonParagraphAndInlineFormatting()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Paragraph TextAlignment="Center" Margin="12,4,0,8">
                <Run FontFamily="Arial" FontSize="16" FontWeight="Bold" Foreground="#FF0080C0" Text="Title" />
                <Span FontStyle="Italic"><Run Text=" and detail" /></Span>
                <LineBreak />
                <Underline>underlined</Underline>
              </Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Title and detail\nunderlined");
        payload.Body.Paragraphs.Should().ContainSingle();
        var paragraph = payload.Body.Paragraphs.Single();
        paragraph.Align.Should().Be(TextAlign.Center);
        paragraph.MarginLeftEmu.Should().Be(114300);
        paragraph.SpaceBeforePt.Should().Be(3);
        paragraph.SpaceAfterPt.Should().Be(6);
        paragraph.Runs[0].FontFamily.Should().Be("Arial");
        paragraph.Runs[0].FontSizePt.Should().Be(12);
        paragraph.Runs[0].Bold.Should().BeTrue();
        paragraph.Runs[0].Color!.Resolved.Should().Be(SrgbColor.FromRgb(0x0080C0));
        paragraph.Runs[1].Italic.Should().BeTrue();
        paragraph.Runs[2].Text.Should().Be("\n");
        paragraph.Runs[3].Underline.Should().BeTrue();
    }

    [Fact]
    public void XamlPackageFlowDocument_FlattensTablesLikeWpfProjection_AndPreservesCellFormatting()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Paragraph>Before</Paragraph>
              <Table>
                <TableRowGroup>
                  <TableRow>
                    <TableCell><Paragraph><Bold>Header</Bold></Paragraph></TableCell>
                    <TableCell><Paragraph><Italic>Value</Italic></Paragraph></TableCell>
                  </TableRow>
                  <TableRow>
                    <TableCell><Paragraph>Left</Paragraph></TableCell>
                    <TableCell><Paragraph><Underline>Right</Underline></Paragraph></TableCell>
                  </TableRow>
                </TableRowGroup>
              </Table>
              <Paragraph>After</Paragraph>
            </FlowDocument>
            """;

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Before\nHeader\tValue\nLeft\tRight\nAfter");
        payload.Body.Paragraphs.Should().HaveCount(4);
        payload.Body.Paragraphs[1].Runs.Should().Contain(run => run.Text == "Header" && run.Bold);
        payload.Body.Paragraphs[1].Runs.Should().Contain(run => run.Text == "Value" && run.Italic);
        payload.Body.Paragraphs[2].Runs.Should().Contain(run => run.Text == "Right" && run.Underline);
    }

    [Fact]
    public void XamlPackageFlowDocument_PreservesAllImagePayloadsInDocumentOrder()
    {
        const string xaml = """
            <FlowDocument xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <BlockUIContainer><Image Source="Images/first.png" /></BlockUIContainer>
              <BlockUIContainer><Image Source="Images/second.jpg" /></BlockUIContainer>
            </FlowDocument>
            """;
        var first = new byte[] { 0x01, 0x02 };
        var second = new byte[] { 0x03, 0x04, 0x05 };

        var payload = ExternalXamlClipboardPlanner.TryParseXamlPackage(
            CreateXamlPackage(xaml,
                ("Images/first.png", first),
                ("Images/second.jpg", second)));

        payload.Should().NotBeNull();
        payload!.GetImagePayloads().Should().HaveCount(2);
        payload.GetImagePayloads()[0].Bytes.Should().Equal(first);
        payload.GetImagePayloads()[0].ContentType.Should().Be("image/png");
        payload.GetImagePayloads()[1].Bytes.Should().Equal(second);
        payload.GetImagePayloads()[1].ContentType.Should().Be("image/jpeg");
        payload.ImageBytes.Should().Equal(first);
        payload.ImageContentType.Should().Be("image/png");
    }

    [Fact]
    public void XamlPackageFlowDocument_RejectsOversizedTableRows()
    {
        var cells = string.Concat(Enumerable.Repeat(
            "<TableCell><Paragraph>x</Paragraph></TableCell>",
            ExternalXamlClipboardPlanner.MaxTableCellsPerRow + 1));
        var xaml = $"<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Table><TableRowGroup><TableRow>{cells}</TableRow></TableRowGroup></Table></FlowDocument>";

        ExternalXamlClipboardPlanner.TryParseXamlPackage(CreateXamlPackage(xaml))
            .Should().BeNull();
    }

    [Fact]
    public void Rtf1Success_PreservesParagraphsRunsFontColorUnicodeTabsAndLineBreaks()
    {
        const string rtf =
            @"{\rtf1\ansi\ansicpg1252\deff0\uc1
{\fonttbl{\f0 Calibri;}{\f1 Arial;}}
{\colortbl;\red192\green0\blue0;\red0\green128\blue0;}
\f0\fs24\b Bold\b0\tab\cf1 Red\cf0\ul Under\ul0\par
\f1\fs18\i\u945?\i0\line Plain}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Bold\tRedUnder\n\u03B1\nPlain");
        payload.Body.Paragraphs.Should().HaveCount(2);
        payload.Body.Paragraphs[0].Runs.Should().HaveCount(4);
        payload.Body.Paragraphs[0].Runs[0].Text.Should().Be("Bold");
        payload.Body.Paragraphs[0].Runs[0].FontFamily.Should().Be("Calibri");
        payload.Body.Paragraphs[0].Runs[0].FontSizePt.Should().Be(12);
        payload.Body.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        payload.Body.Paragraphs[0].Runs[1].Text.Should().Be("\t");
        payload.Body.Paragraphs[0].Runs[2].Text.Should().Be("Red");
        payload.Body.Paragraphs[0].Runs[2].Color!.Resolved.Should().Be(SrgbColor.FromRgb(0xC00000));
        payload.Body.Paragraphs[0].Runs[3].Text.Should().Be("Under");
        payload.Body.Paragraphs[0].Runs[3].Underline.Should().BeTrue();
        payload.Body.Paragraphs[1].Runs[0].Text.Should().Be("\u03B1");
        payload.Body.Paragraphs[1].Runs[0].FontFamily.Should().Be("Arial");
        payload.Body.Paragraphs[1].Runs[0].FontSizePt.Should().Be(9);
        payload.Body.Paragraphs[1].Runs[0].Italic.Should().BeTrue();
        payload.Body.Paragraphs[1].Runs[1].Text.Should().Be("\nPlain");
    }

    [Fact]
    public void RtfPict_PreservesPngPayloadAlongsideText()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi Before {\pict\pngblip " + Convert.ToHexString(png) + @"} After}");

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(rtf);

        payload.Should().NotBeNull();
        payload!.HasImage.Should().BeTrue();
        payload.ImageContentType.Should().Be("image/png");
        payload.ImageBytes.Should().Equal(png);
        payload.PlainText.Should().Be("Before  After");
    }

    [Fact]
    public void RtfPict_RecognizesJpegSignature()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xD9];
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi{\pict\jpegblip " + Convert.ToHexString(jpeg) + "}}");

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(rtf);

        payload.Should().NotBeNull();
        payload!.ImageContentType.Should().Be("image/jpeg");
        payload.ImageBytes.Should().Equal(jpeg);
    }

    [Fact]
    public void RtfPict_PreservesEveryImageAndRichClipboardRoundTripsThem()
    {
        var first = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUAAf+mXJtdAAAAAElFTkSuQmCC");
        var second = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var rtf = Encoding.ASCII.GetBytes(
            @"{\rtf1\ansi Before {\pict\pngblip " + Convert.ToHexString(first)
            + @"} middle {\pict\jpegblip " + Convert.ToHexString(second) + @"} After}");

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(rtf);

        payload.Should().NotBeNull();
        payload!.GetImagePayloads().Should().HaveCount(2);
        payload.GetImagePayloads()[0].Bytes.Should().Equal(first);
        payload.GetImagePayloads()[0].ContentType.Should().Be("image/png");
        payload.GetImagePayloads()[1].Bytes.Should().Equal(second);
        payload.GetImagePayloads()[1].ContentType.Should().Be("image/jpeg");
        payload.PlainText.Should().Be("Before  middle  After");

        var reopened = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        reopened.Should().NotBeNull();
        reopened!.GetImagePayloads().Select(image => image.ContentType)
            .Should().Equal("image/png", "image/jpeg");
        reopened.GetImagePayloads()[0].Bytes.Should().Equal(first);
        reopened.GetImagePayloads()[1].Bytes.Should().Equal(second);
    }

    [Fact]
    public void RtfObject_PreservesVisibleResultAndEmbeddedPayload()
    {
        const string rtf =
            @"{\rtf1\ansi Before {\object{\*\objclass Word.Document.12}{\*\objdata 010203}{\result Embedded result}} After}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Before Embedded result After");
        payload.GetObjectPayloads().Should().ContainSingle();
        payload.GetObjectPayloads()[0].Bytes.Should().Equal(0x01, 0x02, 0x03);
        payload.GetObjectPayloads()[0].FileName.Should().Be("Embedded.docx");

        var restored = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        restored.Should().NotBeNull();
        restored!.GetObjectPayloads().Should().ContainSingle();
        restored.GetObjectPayloads()[0].Bytes.Should().Equal(0x01, 0x02, 0x03);
        restored.GetObjectPayloads()[0].FileName.Should().Be("Embedded.docx");
    }

    [Fact]
    public void UnsupportedAndMalformedRtf_IsBoundedAndNeverThrows()
    {
        var partial = ExternalRichTextClipboardPlanner.TryParseRtf(
            Encoding.ASCII.GetBytes(@"{\rtf1\ansi Before {\*\generator ignored} After\b bold"));

        partial.Should().NotBeNull();
        partial!.PlainText.Should().Be("Before  Afterbold");
        ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes("plain text"))
            .Should().BeNull();

        var oversized = Encoding.ASCII.GetBytes(
            "{\\rtf1\\ansi " + new string('x', ExternalRichTextClipboardPlanner.MaxOutputCharacters + 1));
        ExternalRichTextClipboardPlanner.TryParseRtf(oversized).Should().BeNull();
    }

    [Fact]
    public void RtfCharacterDirection_RtlchAndLtrch_PreserveMixedRunOverrides()
    {
        const string rtf =
            @"{\rtf1\ansi\rtlpar\rtlch\u1488?\u1489?\u1490?\ltrch LTR}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var runs = payload!.Body.Paragraphs.Single().Runs;
        runs.Should().Contain(run => run.Text == "\u05D0\u05D1\u05D2" && run.RightToLeft == true);
        runs.Should().Contain(run => run.Text == "LTR" && run.RightToLeft == false);
    }

    [Fact]
    public void PlannerApply_PastesExternalFragmentWithItsRichRuns()
    {
        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(
            Encoding.ASCII.GetBytes(@"{\rtf1\ansi\b External\b0\par second}"));
        var destination = InCanvasRichClipboardPayload.FromPlainText("BeforeAfter").Body;

        payload.Should().NotBeNull();
        var updated = InCanvasRichClipboardPlanner.Apply(
            destination,
            new InCanvasEditorTextSelection(6, 6),
            payload!,
            out var caret);

        caret.Should().Be(6 + payload!.PlainText.Length);
        InCanvasTextEditPlanner.ExtractPlainText(updated).Should().Be("BeforeExternal\nsecondAfter");
        updated.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().Contain(run => run.Text == "External" && run.Bold);
    }

    [Fact]
    public void WordListTable_PreservesNestedLevelsNumberFormatRestartAndParagraphLayout()
    {
        const string rtf =
            @"{\rtf1\ansi
{\listtable
{\list\listid1
{\listlevel\levelnfc0\levelstartat3\leveltext\'02\'00.;\levelnumbers\'01;}
{\listlevel\levelnfc23\levelstartat1\leveltext\'01\u8226?;\levelnumbers;}
}}
{\listoverridetable{\listoverride\listid1\ls1}}
\pard\ls1\ilvl0\li720\fi-360\ql\sb120\sa240 First\par
\pard\ls1\ilvl1\li1440\fi-360\qc Nested\par
\pard\ls1\ilvl0\qr Second}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("First\nNested\nSecond");
        payload.Body.Paragraphs.Should().HaveCount(3);

        var first = payload.Body.Paragraphs[0];
        first.BulletKind.Should().Be(BulletKind.Auto);
        first.AutoNumType.Should().Be(AutoNumType.ArabicPeriod);
        first.AutoNumStartAt.Should().Be(3);
        first.AutoNumStartAtSpecified.Should().BeTrue();
        first.Level.Should().Be(0);
        first.Align.Should().Be(TextAlign.Left);
        first.MarginLeftEmu.Should().Be(457200);
        first.IndentEmu.Should().Be(-228600);
        first.SpaceBeforePt.Should().Be(6);
        first.SpaceAfterPt.Should().Be(12);

        var nested = payload.Body.Paragraphs[1];
        nested.BulletKind.Should().Be(BulletKind.Char);
        nested.BulletChar.Should().Be("\u2022");
        nested.Level.Should().Be(1);
        nested.Align.Should().Be(TextAlign.Center);

        var continuation = payload.Body.Paragraphs[2];
        continuation.BulletKind.Should().Be(BulletKind.Auto);
        continuation.AutoNumStartAt.Should().Be(3);
        continuation.AutoNumStartAtSpecified.Should().BeFalse();
        continuation.Align.Should().Be(TextAlign.Right);
    }

    [Fact]
    public void WordTableControls_FlattenRowsAndCellsLikeWpfProjection_AndPreserveCellFormatting()
    {
        const string rtf =
            @"{\rtf1\ansi
\trowd\trgaph108\cellx1440\cellx2880
{\b Header}\cell{\i Value}\cell\row
\trowd\cellx1440\cellx2880
Left\cell{\ul Right}\ul0\cell\row}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Header\tValue\nLeft\tRight");
        payload.TableColumnWidthsEmu.Should().Equal(914400L, 914400L);
        payload.Body.Paragraphs.Should().HaveCount(2);
        payload.Body.Paragraphs[0].Runs.Should().Contain(run => run.Text == "Header" && run.Bold);
        payload.Body.Paragraphs[0].Runs.Should().Contain(run => run.Text == "Value" && run.Italic);
        payload.Body.Paragraphs[1].Runs.Should().Contain(run => run.Text == "Right" && run.Underline);
    }

    [Fact]
    public void WordTableCellStyles_PreserveSolidFillAndCommonBorders()
    {
        const string rtf =
            @"{\rtf1\ansi
{\colortbl;\red255\green255\blue0;\red31\green78\blue121;}
\trowd\clcbpat1\clvertalc\clpadl120\clpadr240\clpadt60\clpadb180\clbrdrl\brdrs\brdrw10\brdrcf2\cellx1440\cellx2880
Header\cell Value\cell\row}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.TableCellStyles.Should().HaveCount(2);
        payload.TableCellStyles![0].FillRgb.Should().Be(0xFFFF00);
        var left = payload.TableCellStyles[0].Left;
        left.Should().NotBeNull();
        left!.ColorRgb.Should().Be(0x1F4E79);
        left.WidthPt.Should().Be(0.5);
        payload.TableCellStyles[0].Anchor.Should().Be(TableCellAnchor.Middle);
        payload.TableCellStyles[0].InsetLeftPt.Should().Be(6);
        payload.TableCellStyles[0].InsetRightPt.Should().Be(12);
        payload.TableCellStyles[0].InsetTopPt.Should().Be(3);
        payload.TableCellStyles[0].InsetBottomPt.Should().Be(9);
        payload.TableCellStyles[1].FillRgb.Should().BeNull();
    }

    [Fact]
    public void NestedTableGroups_UseSameBoundedCellAndRowProjection()
    {
        const string rtf =
            @"{\rtf1\ansi\trowd\cellx1440\cellx2880
Outer {\b one}\cell{\i two}\cell\row
\trowd\cellx1440\cellx2880
Three\cell Four\cell\row}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Outer one\ttwo\nThree\tFour");
        payload.Body.Paragraphs[0].Runs.Should().Contain(run => run.Text == "one" && run.Bold);
        payload.Body.Paragraphs[0].Runs.Should().Contain(run => run.Text == "two" && run.Italic);
    }

    [Fact]
    public void ExcessiveTableCells_AreRejectedAsUntrustedInput()
    {
        var cells = string.Concat(Enumerable.Repeat("x\\cell ",
            ExternalRichTextClipboardPlanner.MaxTableCellsPerRow + 1));
        var rtf = Encoding.ASCII.GetBytes("{\\rtf1\\ansi\\trowd " + cells + "\\row}");

        ExternalRichTextClipboardPlanner.TryParseRtf(rtf).Should().BeNull();
    }

    [Fact]
    public void HyperlinkField_PreservesResultTextAndRejectsUnsafeTargets()
    {
        const string rtf =
            @"{\rtf1\ansi Before {\field{\*\fldinst HYPERLINK ""https://example.com/review""}{\fldrslt Click here}} "
            + @"{\field{\*\fldinst HYPERLINK ""javascript:alert(1)""}{\fldrslt Unsafe}} After}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("Before Click here Unsafe After");
        payload.Body.Paragraphs.Single().Runs
            .Single(run => run.Text == "Click here")
            .Hyperlink!.Url.Should().Be("https://example.com/review");
        payload.Body.Paragraphs.Single().Runs
            .Single(run => run.Text.Contains("Unsafe", StringComparison.Ordinal))
            .Hyperlink.Should().BeNull();
    }

    [Fact]
    public void HyperlinkField_PreservesLocalFileTargetAndRejectsRemoteFileHost()
    {
        const string rtf =
            @"{\rtf1\ansi {\field{\*\fldinst HYPERLINK ""file:///C:/Reports/budget.xlsx""}{\fldrslt Open workbook}} "
            + @"{\field{\*\fldinst HYPERLINK ""file://server/share/budget.xlsx""}{\fldrslt Remote workbook}}}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var runs = payload!.Body.Paragraphs.Single().Runs;
        runs.Select(run => run.Text).Should().Contain("Open workbook");
        runs.Single(run => run.Text == "Open workbook").Hyperlink!.Url
            .Should().Be("file:///C:/Reports/budget.xlsx");
        runs.Single(run => run.Text.Contains("Remote workbook", StringComparison.Ordinal))
            .Hyperlink.Should().BeNull();
    }

    [Fact]
    public void RtfField_PreservesNonHyperlinkTypeCachedResultAndClipboardRoundTrip()
    {
        const string rtf =
            @"{\rtf1\ansi Before {\field{\*\fldinst PAGE \\* MERGEFORMAT}{\fldrslt 2}} After}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        var fieldRun = payload!.Body.Paragraphs.Single().Runs
            .Single(run => run.Text == "2");
        fieldRun.Field.Should().NotBeNull();
        fieldRun.Field!.FieldType.Should().Be("PAGE");
        fieldRun.Field.CachedText.Should().Be("2");
        fieldRun.Hyperlink.Should().BeNull();

        var restored = InCanvasRichClipboardPlanner.Deserialize(
            InCanvasRichClipboardPlanner.Serialize(payload));
        restored.Should().NotBeNull();
        var restoredField = restored!.Body.Paragraphs.Single().Runs
            .Single(run => run.Text == "2");
        restoredField.Field!.FieldType.Should().Be("PAGE");
        restoredField.Field.CachedText.Should().Be("2");
    }

    [Fact]
    public void LegacyPnGroups_PreserveBulletLevelAndExplicitNumberRestart()
    {
        const string rtf =
            @"{\rtf1\ansi{\pn\pnlvlblt\pnseclvl2}\pard\li360\fi-360 Bullet\par
{\pn\pnlvlbody\pnstart4}\pard\li720\fi-360 Number}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.Body.Paragraphs.Should().HaveCount(2);
        payload.Body.Paragraphs[0].BulletKind.Should().Be(BulletKind.Char);
        payload.Body.Paragraphs[0].BulletChar.Should().Be("\u2022");
        payload.Body.Paragraphs[0].Level.Should().Be(1);
        payload.Body.Paragraphs[1].BulletKind.Should().Be(BulletKind.Auto);
        payload.Body.Paragraphs[1].AutoNumStartAt.Should().Be(4);
        payload.Body.Paragraphs[1].AutoNumStartAtSpecified.Should().BeTrue();
    }

    [Fact]
    public void LibreOfficeAndMalformedFragments_KeepEscapesBoundedAndDoNotLeakDestinations()
    {
        const string rtf =
            @"{\rtf1\ansi\uc1\b LibreOffice\b0 {\*\generator LibreOffice} \u233? {\object ignored} \{literal\}\par
\pard\qj\li360\sa80 Text {\field{\*\fldinst NOT_A_HYPERLINK}{\fldrslt field text}}";

        var payload = ExternalRichTextClipboardPlanner.TryParseRtf(Encoding.ASCII.GetBytes(rtf));

        payload.Should().NotBeNull();
        payload!.PlainText.Should().Be("LibreOffice \u00E9  {literal}\nText field text");
        payload.Body.Paragraphs.Should().HaveCount(2);
        payload.Body.Paragraphs[1].Align.Should().Be(TextAlign.Justify);
        payload.Body.Paragraphs[1].MarginLeftEmu.Should().Be(228600);
        payload.Body.Paragraphs[1].SpaceAfterPt.Should().Be(4);
        payload.Body.Paragraphs[1].Runs
            .Single(run => run.Text.Contains("field text", StringComparison.Ordinal))
            .Hyperlink.Should().BeNull();

        ExternalRichTextClipboardPlanner.TryParseRtf(
                Encoding.ASCII.GetBytes(@"{\rtf1\ansi {\field{\*\fldinst HYPERLINK ""https://example.com""}"))
            .Should().NotBeNull();
    }

    private static byte[] CreateXamlPackage(
        string xaml,
        params (string Name, byte[] Bytes)[] resources)
    {
        using var output = new MemoryStream();
        using (var package = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var resource in resources)
            {
                var entry = package.CreateEntry(resource.Name);
                using var stream = entry.Open();
                stream.Write(resource.Bytes);
            }

            using var writer = new StreamWriter(package.CreateEntry("Xaml/Document.xaml").Open(), Encoding.UTF8);
            writer.Write(xaml);
        }
        return output.ToArray();
    }
}
