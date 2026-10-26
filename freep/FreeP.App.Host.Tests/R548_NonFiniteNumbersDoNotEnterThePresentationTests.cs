using System.IO;
using System.IO.Compression;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r548: the sibling of r547, asked of FreeP. r486 guarded the animation and media parsers in this
/// reader; it did not sweep the chart, geometry and path parsers beside them, so those still turned
/// an overflowing literal into a non-finite value in the presentation model.
///
/// <para>No hostile token is needed. Since .NET Core stopped throwing on overflow,
/// <c>double.TryParse("1e400")</c> returns TRUE with Infinity -- a fact r485's own entry recorded
/// while fixing a single parser.</para>
/// </summary>
public sealed class R548_NonFiniteNumbersDoNotEnterThePresentationTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "freep-r548-" + Guid.NewGuid().ToString("N"));

    public R548_NonFiniteNumbersDoNotEnterThePresentationTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private static Presentation AdjustedArrow()
    {
        var pres = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 1,
            Name = "Adjusted right arrow",
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.RightArrow,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
            PresetGeometryAdjustments = { ["adj1"] = 18553 },
        });
        pres.Slides.Add(slide);
        return pres;
    }

    private string WriteAndPatchSlide(Presentation pres, string find, string replace)
    {
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(pres, path);

        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            var entry = zip.GetEntry("ppt/slides/slide1.xml")!;
            string xml;
            using (var reader = new StreamReader(entry.Open()))
                xml = reader.ReadToEnd();

            Assert.Contains(find, xml);
            var patched = xml.Replace(find, replace);

            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("ppt/slides/slide1.xml").Open());
            writer.Write(patched);
        }

        return path;
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void An_unusable_geometry_adjustment_does_not_reach_the_model(string hostile)
    {
        // The adjustment drives preset-geometry rendering, so a non-finite here propagates into the
        // shape's drawn path rather than staying in the file.
        var path = WriteAndPatchSlide(AdjustedArrow(), "val 18553", "val " + hostile);

        var shape = PptxPackageReader.Read(path).Slides[0].Shapes.Single(s => s.Id == 1);

        Assert.All(
            shape.PresetGeometryAdjustments.Values,
            v => Assert.True(double.IsFinite(v), "adjustment read back as " + v));
    }

    [Fact]
    public void An_ordinary_geometry_adjustment_still_round_trips()
    {
        // Non-vacuity: the guard must reject only what it cannot represent. Without this, a fix that
        // dropped every adjustment would satisfy the assertions above.
        var path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".pptx");
        PptxPackageWriter.Write(AdjustedArrow(), path);

        var shape = PptxPackageReader.Read(path).Slides[0].Shapes.Single(s => s.Id == 1);

        Assert.Equal(18553, shape.PresetGeometryAdjustments["adj1"]);
    }
}
