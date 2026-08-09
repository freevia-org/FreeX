using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip tests for Word's linked style pairs (<c>w:style/w:link</c>): a paragraph style paired with
/// its counterpart character style (e.g. the built-in "Heading 1" / "Heading 1 Char" pair) must survive a
/// load -> save -> load cycle intact on <see cref="DocumentStyle.LinkedStyleId"/>, serialising right after
/// <c>w:next</c> in <c>styles.xml</c>'s <c>CT_Style</c> element order.
/// </summary>
public class LinkedStyleRoundTripTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static XDocument WriteStylesXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/styles.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static TextDocument BuildDocumentWithLinkedStylePair()
    {
        var doc = new TextDocument();
        doc.Styles["Heading1"] = new DocumentStyle
        {
            Id = "Heading1",
            Name = "heading 1",
            NextStyleId = "Normal",
            LinkedStyleId = "Heading1Char",
            Run = new RunFormatting { Bold = true, FontSizePt = 16 }
        };
        doc.Styles["Heading1Char"] = new DocumentStyle
        {
            Id = "Heading1Char",
            Name = "Heading 1 Char",
            Type = StyleType.Character,
            LinkedStyleId = "Heading1",
            Run = new RunFormatting { Bold = true, FontSizePt = 16 }
        };
        // A style with no link, to prove the sibling (unlinked) case keeps emitting no w:link.
        doc.Styles["Emphasis"] = new DocumentStyle
        {
            Id = "Emphasis",
            Name = "emphasis",
            Type = StyleType.Character,
            Run = new RunFormatting { Italic = true }
        };
        doc.Blocks.Add(new Paragraph("A heading") { StyleId = "Heading1" });
        return doc;
    }

    [Fact]
    public void LinkedStylePair_SerialisesWLinkAfterWNext()
    {
        var styles = WriteStylesXml(BuildDocumentWithLinkedStylePair());

        var heading = styles.Root!.Elements(W + "style")
            .Single(e => e.Attribute(W + "styleId")?.Value == "Heading1");
        var names = heading.Elements().Select(e => e.Name).ToList();
        var nextIndex = names.IndexOf(W + "next");
        var linkIndex = names.IndexOf(W + "link");
        nextIndex.Should().BeGreaterThanOrEqualTo(0);
        linkIndex.Should().BeGreaterThanOrEqualTo(0);
        nextIndex.Should().BeLessThan(linkIndex);
        heading.Element(W + "link")!.Attribute(W + "val")!.Value.Should().Be("Heading1Char");

        var headingChar = styles.Root!.Elements(W + "style")
            .Single(e => e.Attribute(W + "styleId")?.Value == "Heading1Char");
        headingChar.Element(W + "link")!.Attribute(W + "val")!.Value.Should().Be("Heading1");
    }

    [Fact]
    public void LinkedStylePair_RoundTrips_PreservingBothDirections()
    {
        var reloaded = RoundTrip(BuildDocumentWithLinkedStylePair());

        reloaded.Styles["Heading1"].LinkedStyleId.Should().Be("Heading1Char");
        reloaded.Styles["Heading1Char"].LinkedStyleId.Should().Be("Heading1");
        // Non-linked style keeps round-tripping with no link.
        reloaded.Styles["Emphasis"].LinkedStyleId.Should().BeNull();
    }

    // Sibling no-regression: an ordinary (unlinked) style must not gain a spurious w:link.
    [Fact]
    public void UnlinkedStyle_EmitsNoWLink()
    {
        var doc = BuildDocumentWithLinkedStylePair();
        var styles = WriteStylesXml(doc);

        var emphasis = styles.Root!.Elements(W + "style")
            .Single(e => e.Attribute(W + "styleId")?.Value == "Emphasis");
        emphasis.Element(W + "link").Should().BeNull();
    }
}
