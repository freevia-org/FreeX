using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r350: every <c>a:fmtScheme</c> style list is <c>minOccurs="3"</c> in DrawingML, so a theme read
/// from a source with fewer entries must be padded rather than written back as-is.
///
/// <para>The writer handled an EMPTY list (generic 3-slot defaults) and a full one (reuse verbatim,
/// which is what keeps real effect styles from being discarded on save). One and two fell between
/// the branches and were written straight out, producing a package <c>OpenXmlValidator</c> rejects:
/// "The element has incomplete content" on <c>fillStyleLst</c> and <c>lnStyleLst</c>.</para>
///
/// <para>Both directions are pinned here. Padding a short list is worthless if it silently pads or
/// reorders a complete one, because that is the round-trip fidelity the verbatim branch exists to
/// protect -- so the second test asserts a three-entry theme survives unchanged, in order.</para>
/// </summary>
public sealed class R350_ThemeStyleListsMeetSchemaMinimumTests
{
    private static readonly XNamespace A =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static XElement SolidFill(string colour) =>
        new(A + "solidFill", new XElement(A + "srgbClr", new XAttribute("val", colour)));

    private static XElement Line(int width) =>
        new(A + "ln",
            new XAttribute("w", width),
            new XElement(A + "solidFill",
                new XElement(A + "schemeClr", new XAttribute("val", "phClr"))));

    private static byte[] Write(Presentation presentation)
    {
        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static string[] SchemaErrors(byte[] bytes)
    {
        using var package = PresentationDocument.Open(new MemoryStream(bytes), isEditable: false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(package)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => error.Description + " @ " + error.Path?.XPath)
            .ToArray();
    }

    private static XElement FormatScheme(byte[] bytes)
    {
        using var package = PresentationDocument.Open(new MemoryStream(bytes), isEditable: false);
        var themePart = package.PresentationPart!.SlideMasterParts.First().ThemePart!;
        using var themeStream = themePart.GetStream();
        return XDocument.Load(themeStream)
            .Descendants(A + "fmtScheme")
            .First();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AThemeWithFewerEntriesThanTheSchemaRequiresStillValidates(int entryCount)
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        presentation.Theme.FillStyles = Enumerable.Range(0, entryCount)
            .Select(index => SolidFill(index == 0 ? "FF0000" : "00FF00"))
            .ToArray();
        presentation.Theme.LineStyles = Enumerable.Range(0, entryCount)
            .Select(index => Line(6350 * (index + 1)))
            .ToArray();

        var errors = SchemaErrors(Write(presentation));

        errors.Should().BeEmpty(string.Join("\n", errors));
    }

    [Fact]
    public void PaddingKeepsTheThemesOwnEntriesInTheirOriginalPositions()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        presentation.Theme.FillStyles = new[] { SolidFill("FF0000") };

        var fills = FormatScheme(Write(presentation))
            .Element(A + "fillStyleLst")!
            .Elements()
            .ToList();

        // A fillRef idx is a 1-based index into this list, so slot 1 must still be the theme's own
        // fill; the padding repeats it rather than substituting a generic placeholder.
        fills.Should().HaveCount(3);
        fills.Should().AllSatisfy(fill =>
            fill.Descendants(A + "srgbClr").Single().Attribute("val")!.Value.Should().Be("FF0000"));
    }

    [Fact]
    public void ACompleteStyleListIsWrittenBackVerbatim()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        presentation.Theme.FillStyles = new[]
        {
            SolidFill("111111"), SolidFill("222222"), SolidFill("333333"),
        };

        var fills = FormatScheme(Write(presentation))
            .Element(A + "fillStyleLst")!
            .Elements()
            .ToList();

        fills.Should().HaveCount(3);
        fills.Select(fill => fill.Descendants(A + "srgbClr").Single().Attribute("val")!.Value)
            .Should().Equal("111111", "222222", "333333");
    }

    [Fact]
    public void AFourEntryStyleListKeepsItsFourthEntry()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());
        presentation.Theme.FillStyles = new[]
        {
            SolidFill("111111"), SolidFill("222222"), SolidFill("333333"), SolidFill("444444"),
        };

        var fills = FormatScheme(Write(presentation))
            .Element(A + "fillStyleLst")!
            .Elements()
            .ToList();

        // The minimum is three, not a cap: truncating here would discard a real style.
        fills.Should().HaveCount(4);
    }
}
