using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Styles authored through Word's Create New Style dialog (paragraph, character and linked types, the
/// "Add to the Styles gallery" flag, and the paragraph formatting reachable from the Format menu) must
/// survive DOCX save and reload with the right <c>w:type</c>, <c>w:link</c> and <c>w:qFormat</c>.
/// </summary>
public sealed class AuthoredStyleRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XElement WrittenStyle(TextDocument document, string styleId)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/styles.xml")!.Open();
        return XDocument.Load(entry).Root!
            .Elements(W + "style")
            .Single(style => style.Attribute(W + "styleId")?.Value == styleId);
    }

    [Fact]
    public void CharacterStyle_RoundTripsTypeFormattingAndRunReference()
    {
        var doc = TextDocument.CreateEmpty();
        var style = StyleManager.CreateStyle(
            doc,
            "Code",
            null,
            new RunFormatting { FontFamily = "Consolas", Strikethrough = true, HighlightColorHex = "#FFFF00" },
            ParagraphFormatting.Default,
            null,
            StyleType.Character);
        var paragraph = new Paragraph("styled");
        paragraph.Runs[0].StyleId = style.Id;
        doc.Blocks.Add(paragraph);

        WrittenStyle(doc, style.Id).Attribute(W + "type")!.Value.Should().Be("character");
        var reloaded = RoundTrip(doc);

        var read = reloaded.Styles[style.Id];
        read.Type.Should().Be(StyleType.Character);
        read.Name.Should().Be("Code");
        read.Run.FontFamily.Should().Be("Consolas");
        read.Run.Strikethrough.Should().BeTrue();
        read.Run.HighlightColorHex.Should().Be("#FFFF00");
        reloaded.Paragraphs.Last().Runs[0].StyleId.Should().Be(style.Id);
    }

    [Fact]
    public void LinkedStyle_RoundTripsBothHalvesWithMutualLink()
    {
        var doc = TextDocument.CreateEmpty();
        var linked = StyleManager.CreateLinkedStyle(
            doc,
            "Callout",
            "Normal",
            new RunFormatting { Bold = true },
            new ParagraphFormatting { Alignment = TextAlignment.Center },
            "Normal");
        doc.Blocks.Add(new Paragraph("styled") { StyleId = linked.Id });

        var paragraphElement = WrittenStyle(doc, linked.Id);
        var characterElement = WrittenStyle(doc, linked.LinkedStyleId!);
        paragraphElement.Attribute(W + "type")!.Value.Should().Be("paragraph");
        paragraphElement.Element(W + "link")!.Attribute(W + "val")!.Value.Should().Be(linked.LinkedStyleId);
        characterElement.Attribute(W + "type")!.Value.Should().Be("character");
        characterElement.Element(W + "link")!.Attribute(W + "val")!.Value.Should().Be(linked.Id);

        var reloaded = RoundTrip(doc);

        var paragraphStyle = reloaded.Styles[linked.Id];
        paragraphStyle.Type.Should().Be(StyleType.Paragraph);
        paragraphStyle.Paragraph.Alignment.Should().Be(TextAlignment.Center);
        paragraphStyle.NextStyleId.Should().Be("Normal");
        var characterStyle = StyleManager.FindLinkedCharacterStyle(reloaded, linked.Id);
        characterStyle.Should().NotBeNull();
        characterStyle!.Name.Should().Be("Callout Char");
        characterStyle.LinkedStyleId.Should().Be(linked.Id);
        characterStyle.Run.Bold.Should().BeTrue();
        reloaded.Paragraphs.Last().StyleId.Should().Be(linked.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GalleryFlag_RoundTripsAsQFormat(bool addToGallery)
    {
        var doc = TextDocument.CreateEmpty();
        var style = StyleManager.CreateStyle(
            doc, "Aside", null, RunFormatting.Default, ParagraphFormatting.Default, quickFormat: addToGallery);

        var element = WrittenStyle(doc, style.Id);
        (element.Element(W + "qFormat") is not null).Should().Be(addToGallery);
        if (addToGallery)
        {
            // CT_Style order: w:qFormat sits before w:pPr / w:rPr and after w:name / w:basedOn / w:link.
            element.Element(W + "qFormat")!.ElementsAfterSelf()
                .Select(child => child.Name.LocalName)
                .Should().NotContain(["name", "basedOn", "next", "link"]);
        }

        RoundTrip(doc).Styles[style.Id].QuickFormat.Should().Be(addToGallery);
    }

    private static MemoryStream WithoutQuickFormat(TextDocument document)
    {
        // What FreeW wrote before the gallery flag existed: the same package with no w:qFormat anywhere.
        var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("word/styles.xml")!;
            XDocument styles;
            using (var read = entry.Open())
                styles = XDocument.Load(read);
            styles.Descendants(W + "qFormat").Remove();
            entry.Delete();
            using var write = zip.CreateEntry("word/styles.xml").Open();
            styles.Save(write);
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void LegacyDocument_WithNoQFormatAnywhere_KeepsEveryCustomStyleInTheGallery()
    {
        var doc = TextDocument.CreateEmpty();
        var paragraphStyle = StyleManager.CreateStyle(
            doc, "Legacy Body", null, RunFormatting.Default, ParagraphFormatting.Default, quickFormat: false);
        var characterStyle = StyleManager.CreateStyle(
            doc, "Legacy Code", null, RunFormatting.Default, ParagraphFormatting.Default, null, StyleType.Character, quickFormat: false);
        foreach (var style in doc.Styles.Values.ToArray())
            doc.Styles[style.Id] = new DocumentStyle(style) { QuickFormat = false };
        using var legacy = WithoutQuickFormat(doc);

        var reloaded = DocxReader.Read(legacy);

        reloaded.Styles[paragraphStyle.Id].QuickFormat.Should().BeTrue();
        reloaded.Styles[characterStyle.Id].QuickFormat.Should().BeTrue();
        reloaded.Styles.Values.Should().OnlyContain(style => style.QuickFormat);
    }

    [Fact]
    public void WordAuthoredDocument_WithQFormatOnSomeStyles_ShowsOnlyTheFlaggedOnes()
    {
        var doc = TextDocument.CreateEmpty();
        var shown = StyleManager.CreateStyle(
            doc, "Shown", null, RunFormatting.Default, ParagraphFormatting.Default, quickFormat: true);
        var hidden = StyleManager.CreateStyle(
            doc, "Hidden", null, RunFormatting.Default, ParagraphFormatting.Default, quickFormat: false);

        var reloaded = RoundTrip(doc);

        reloaded.Styles[shown.Id].QuickFormat.Should().BeTrue();
        reloaded.Styles[hidden.Id].QuickFormat.Should().BeFalse();
        reloaded.Styles["Normal"].QuickFormat.Should().BeTrue();
    }

    [Fact]
    public void LegacyDocument_OnceSavedByTheNewWriter_PreservesVisibilityThroughQFormat()
    {
        var doc = TextDocument.CreateEmpty();
        var kept = StyleManager.CreateStyle(
            doc, "Kept", null, RunFormatting.Default, ParagraphFormatting.Default);
        var removed = StyleManager.CreateStyle(
            doc, "Removed", null, RunFormatting.Default, ParagraphFormatting.Default);
        using var legacy = WithoutQuickFormat(doc);
        var opened = DocxReader.Read(legacy);
        opened.Styles[removed.Id].QuickFormat.Should().BeTrue("a legacy document shows every style");

        // The user removes one style from the gallery, saves, and reopens.
        StyleManager.ModifyStyle(opened, removed.Id, quickFormat: false);
        WrittenStyle(opened, kept.Id).Element(W + "qFormat").Should().NotBeNull();
        WrittenStyle(opened, removed.Id).Element(W + "qFormat").Should().BeNull();
        var resaved = RoundTrip(opened);

        resaved.Styles[kept.Id].QuickFormat.Should().BeTrue();
        resaved.Styles[removed.Id].QuickFormat.Should().BeFalse();
        RoundTrip(resaved).Styles[removed.Id].QuickFormat.Should().BeFalse();
    }

    [Fact]
    public void ParagraphStyle_RoundTripsFlowBorderShadingAndTabs()
    {
        var doc = TextDocument.CreateEmpty();
        var style = StyleManager.CreateStyle(
            doc,
            "Boxed",
            null,
            RunFormatting.Default,
            new ParagraphFormatting
            {
                KeepWithNext = true,
                KeepLinesTogether = true,
                PageBreakBefore = true,
                Border = new ParagraphBorder("#FF0000", 1.5),
                ShadingColorHex = "#FFFF00",
                TabStops = [new TabStop(72, TabStopAlignment.Right)],
                IndentLeftPt = 18,
            });

        var names = WrittenStyle(doc, style.Id).Element(W + "pPr")!.Elements()
            .Select(child => child.Name.LocalName)
            .ToArray();
        names.Should().ContainInOrder("keepNext", "keepLines", "pageBreakBefore", "pBdr", "shd", "tabs", "ind");

        var read = RoundTrip(doc).Styles[style.Id].Paragraph;
        read.KeepWithNext.Should().BeTrue();
        read.KeepLinesTogether.Should().BeTrue();
        read.PageBreakBefore.Should().BeTrue();
        read.Border.Should().NotBeNull();
        read.Border!.ColorHex.Should().BeEquivalentTo("#FF0000");
        read.ShadingColorHex.Should().BeEquivalentTo("#FFFF00");
        read.TabStops.Should().ContainSingle().Which.PositionPt.Should().BeApproximately(72, 0.01);
        read.IndentLeftPt.Should().BeApproximately(18, 0.01);
    }
}
