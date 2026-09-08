using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r544: DrawingML percentages -- a picture's crop, brightness, contrast, bi-level threshold and
/// alpha modulation -- are ST_Percentage, which is an xsd:int in 1/1000 of a percent. The writer
/// scaled the model's double and cast straight to long, and .NET's saturating conversions then wrote
/// an infinite crop as l="9223372036854775807": a number the format cannot represent, in a file
/// PowerPoint rejects outright.
///
/// <para>The model does not defend against this -- CropLeft, Brightness, Contrast, BiLevelThreshold
/// and AlphaModPct are plain auto-properties with no clamping -- so the writer is the last place the
/// invariant can hold, and it is the right place: the constraint belongs to the FILE FORMAT, not to
/// any one command that might set the value.</para>
///
/// <para>These tests pin the boundary rather than a chosen policy. The clamp is to what an xsd:int
/// can hold, which is a fact about ST_Percentage; clamping to "100% maximum" would be a guess about
/// intent that the format itself does not make.</para>
/// </summary>
public sealed class R544_PercentagesWrittenToTheFileFitTheFormatTests
{
    private static string CropLeftAttribute(double crop)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Pic",
            Kind = SlideShapeKind.Picture,
            PictureFormat = new PictureFormat { CropLeft = crop },
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.Entries.First(e => e.FullName.Contains("slide1.xml", StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());

        var srcRect = XDocument.Parse(reader.ReadToEnd())
            .Descendants()
            .First(element => element.Name.LocalName == "srcRect");

        return srcRect.Attribute("l")!.Value;
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    [InlineData(1e18)]
    [InlineData(-1e18)]
    public void A_percentage_the_format_cannot_represent_is_never_written(double crop)
    {
        var written = CropLeftAttribute(crop);

        // The assertion is the format's own constraint: whatever we write must parse as the xsd:int
        // the schema declares. Asserting a particular substitute value instead would pin this test
        // to today's policy rather than to the invariant that makes the file valid.
        int.TryParse(written, out _)
            .Should().BeTrue("srcRect/@l is an xsd:int, and " + written + " is not one");
    }

    [Fact]
    public void An_ordinary_crop_is_untouched()
    {
        // Non-vacuity: a clamp that mangled real values would satisfy every assertion above while
        // silently destroying every cropped picture in the product.
        CropLeftAttribute(0.25).Should().Be("25000");
    }

    [Fact]
    public void A_crop_at_the_representable_edge_survives_as_itself()
    {
        // int.MaxValue/100000 rounds to the largest fraction that still fits, so this value must
        // pass through rather than be clamped -- the boundary is inclusive.
        CropLeftAttribute(21474.0).Should().Be("2147400000");
    }
}
