using System.IO;
using System.Windows.Media.Imaging;
using FreeW.App.Host.Editing;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Tests for the SVG→PNG rasterization path added to InsertPictureCommand (W8 slice):
/// inserting a local .svg file must produce a non-empty PNG <see cref="InlineImage"/> that
/// round-trips through the docx writer/reader.
/// Runs on STA because DocumentView needs STA + Dispatcher.
/// </summary>
public sealed class SvgInsertPictureTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeW.SvgInsertPictureTests-");

    public void Dispose() => _temporaryDirectory.Dispose();

    // A minimal valid SVG — one filled blue rectangle on a white background, no external deps.
    private const string MinimalSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="100" height="100">
          <rect x="0" y="0" width="100" height="100" fill="white"/>
          <rect x="10" y="10" width="80" height="80" fill="blue"/>
        </svg>
        """;

    // Write the SVG to a temp file and return its path.
    private string WriteTempSvg(string svgContent)
    {
        var path = Path.Combine(_temporaryDirectory.Path, $"source-{Guid.NewGuid():N}.svg");
        File.WriteAllText(path, svgContent);
        return path;
    }

    [StaFact]
    public void InsertSvg_ProducesNonEmptyPngInlineImage()
    {
        var svgPath = WriteTempSvg(MinimalSvg);
        try
        {
            // Call the same rasterization path that InsertPictureCommand uses.
            var image = SvgRasterizerHelper.RasterizeToInlineImage(svgPath);

            image.Should().NotBeNull();
            image.PngBytes.Should().NotBeEmpty("rasterizing even a minimal SVG must yield PNG bytes");
            image.WidthPt.Should().BeGreaterThan(0);
            image.HeightPt.Should().BeGreaterThan(0);

            // Decode the PNG bytes to confirm they form a valid PNG with non-zero dimensions.
            var bitmap = new BitmapImage();
            using var ms = new MemoryStream(image.PngBytes);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.PixelWidth.Should().BeGreaterThan(0);
            bitmap.PixelHeight.Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(svgPath))
                File.Delete(svgPath);
        }
    }

    [StaFact]
    public void InsertSvg_RoundTripsAsInlineImageInDocx()
    {
        var svgPath = WriteTempSvg(MinimalSvg);
        try
        {
            var image = SvgRasterizerHelper.RasterizeToInlineImage(svgPath);

            // Insert into a document, round-trip via DocxWriter/DocxReader.
            var doc = new TextDocument();
            doc.Blocks.Clear();
            var para = new Paragraph();
            para.Runs.Add(Run.FromImage(image));
            doc.Blocks.Add(para);

            using var ms = new MemoryStream();
            FreeW.Core.IO.DocxWriter.Write(doc, ms);
            ms.Position = 0;
            var read = FreeW.Core.IO.DocxReader.Read(ms);

            var recoveredRun = read.Paragraphs.Single().Runs.Single(r => r.Image is not null);
            recoveredRun.Image!.PngBytes.Should().NotBeEmpty("SVG-rasterized PNG bytes must survive the docx round-trip");
            recoveredRun.Image.WidthPt.Should().BeGreaterThan(0);
            recoveredRun.Image.HeightPt.Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(svgPath))
                File.Delete(svgPath);
        }
    }

    [StaFact]
    public void InsertSvg_PreservesAspectRatio()
    {
        // 200×100 SVG — should produce an image approximately twice as wide as it is tall.
        const string wideSvg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" width="200" height="100">
              <rect x="0" y="0" width="200" height="100" fill="red"/>
            </svg>
            """;
        var svgPath = WriteTempSvg(wideSvg);
        try
        {
            var image = SvgRasterizerHelper.RasterizeToInlineImage(svgPath);

            // Width should be roughly 2× height (±5% tolerance for rounding).
            var ratio = image.WidthPt / image.HeightPt;
            ratio.Should().BeApproximately(2.0, 0.15, "a 200×100 SVG must yield an image roughly twice as wide as tall");
        }
        finally
        {
            if (File.Exists(svgPath))
                File.Delete(svgPath);
        }
    }
}
