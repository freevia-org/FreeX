using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// r546: FreeW's image adjustment properties are plain unclamped doubles on the model
/// (RotationAngle, BrightnessPct, ContrastPct, SaturationPct, ColorTemperature), but the
/// DrawingML attributes they are written to are bounded integer types -- a:xfrm/@rot is
/// ST_Angle and a:lum/@bright, a:lum/@contrast and a:satMod/@val are percentage types, all
/// xsd:int. Scaling a double and casting to long lets .NET's SATURATING conversion emit
/// long.MaxValue, a value the schema cannot represent at all, so Word rejects the document.
///
/// <para>These tests pin the FORMAT boundary rather than a policy: each asserts the written
/// attribute parses as the xsd:int the schema declares, not that it equals a particular
/// substitute. That survives a future decision to clamp differently, but not a value the file
/// format cannot hold. Ordinary values must still round-trip untouched, so a fix that mangled
/// real documents could not hide behind the boundary cases.</para>
/// </summary>
public class R546_ImageEffectsWrittenToTheFileFitTheFormatTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static XDocument WriteDocument(InlineImage image)
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(Run.FromImage(image));
        doc.Blocks.Add(para);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static string? AttributeOf(InlineImage image, string element, string attribute) =>
        WriteDocument(image).Descendants(A + element).FirstOrDefault()?.Attribute(attribute)?.Value;

    /// The schema check every case shares: whatever policy produced the text, it has to be a
    /// value the declared xsd:int can actually hold.
    private static void AssertFitsXsdInt(string? written, string what)
    {
        Assert.NotNull(written);
        Assert.True(
            int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            what + " wrote \"" + written + "\", which the schema's xsd:int cannot represent");
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e18)]
    [InlineData(-1e18)]
    public void Rotation_fits_the_angle_type(double angle)
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { RotationAngle = angle };
        AssertFitsXsdInt(AttributeOf(image, "xfrm", "rot"), "a:xfrm/@rot");
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e15)]
    public void Brightness_and_contrast_fit_the_percentage_type(double pct)
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { BrightnessPct = pct, ContrastPct = pct };
        AssertFitsXsdInt(AttributeOf(image, "lum", "bright"), "a:lum/@bright");
        AssertFitsXsdInt(AttributeOf(image, "lum", "contrast"), "a:lum/@contrast");
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e15)]
    public void Saturation_fits_the_percentage_type(double pct)
    {
        var image = new InlineImage(MinimalPng(), 100, 80) { SaturationPct = pct };
        AssertFitsXsdInt(AttributeOf(image, "satMod", "val"), "a:satMod/@val");
    }

    [Fact]
    public void Ordinary_values_are_written_unchanged()
    {
        // Non-vacuity: the clamp must not touch a document anyone would actually author. 45
        // degrees is 45 * 60000 angle units, and +20% brightness is 20 * 1000 percentage units.
        var rotated = new InlineImage(MinimalPng(), 100, 80) { RotationAngle = 45 };
        Assert.Equal("2700000", AttributeOf(rotated, "xfrm", "rot"));

        var adjusted = new InlineImage(MinimalPng(), 100, 80) { BrightnessPct = 20, ContrastPct = -10 };
        Assert.Equal("20000", AttributeOf(adjusted, "lum", "bright"));
        Assert.Equal("-10000", AttributeOf(adjusted, "lum", "contrast"));

        var saturated = new InlineImage(MinimalPng(), 100, 80) { SaturationPct = 150 };
        Assert.Equal("150000", AttributeOf(saturated, "satMod", "val"));
    }
}
