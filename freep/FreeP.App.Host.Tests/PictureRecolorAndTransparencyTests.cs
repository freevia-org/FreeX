using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Host.Tests;

/// <summary>
/// freep-picture-effects F1: PowerPoint's Picture Format &gt; Color &gt; Recolor gallery (any
/// preset other than "No Recolor") serializes as an <c>a:duotone</c> child of <c>a:blip</c>, and
/// Picture Format &gt; Color &gt; Set Transparent Color serializes as an <c>a:clrChange</c>
/// child. ReadPictureFormat never looked for either element, so opening a real-PowerPoint
/// picture with either feature applied silently dropped it -- the picture came back out plain
/// and opaque -- and because BuildBlipEl only ever re-emitted the effects it knew about, the
/// very next save permanently deleted the a:duotone/a:clrChange XML from the file.
///
/// The fix captures a:duotone and a:clrChange verbatim into
/// <see cref="PictureFormat.DuotoneXml"/> / <see cref="PictureFormat.ClrChangeXml"/> on read
/// (mirroring the existing ArtisticEffectXml verbatim round trip), and BuildBlipEl re-emits them
/// verbatim into a:blip on write.
/// </summary>
public sealed class PictureRecolorAndTransparencyTests
{
    private static byte[] Minimal1x1Png() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8" +
            "z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

    // The exact shape PowerPoint writes for a picture with a Recolor preset applied (Picture
    // Format > Color > Recolor > e.g. "Orange, Accent color 6 Dark").
    private const string DuotoneXml =
        "<a:duotone xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:schemeClr val=\"accent1\"/><a:prstClr val=\"white\"/></a:duotone>";

    // The exact shape PowerPoint writes for a picture with Set Transparent Color applied
    // (Picture Format > Color > Set Transparent Color, click a pixel).
    private const string ClrChangeXml =
        "<a:clrChange useA=\"1\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        "<a:clrFrom><a:srgbClr val=\"FFFFFF\"/></a:clrFrom>" +
        "<a:clrTo><a:srgbClr val=\"FFFFFF\"><a:alpha val=\"0\"/></a:srgbClr></a:clrTo>" +
        "</a:clrChange>";

    private static byte[] WriteToBytes(PresentationModel pres)
    {
        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a package with a picture whose a:blip carries real a:duotone and a:clrChange
    /// elements, exactly as PowerPoint itself writes them, then patches them into slide1.xml --
    /// this exercises the reader precisely on the shape described by the finding without
    /// depending on FreeP's own writer already being able to emit either effect.
    /// </summary>
    private static byte[] BuildPackageWithRecolorAndTransparentColorPicture()
    {
        var pres = PresentationModel.CreateEmpty();
        var img = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id          = 99,
            Name        = "Pic1",
            Kind        = SlideShapeKind.Picture,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            Picture     = img,
        });
        var original = WriteToBytes(pres);

        using var ms = new MemoryStream();
        ms.Write(original, 0, original.Length);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("ppt/slides/slide1.xml")!;
            XDocument doc;
            using (var s = entry.Open()) doc = XDocument.Load(s);

            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var blip = doc.Descendants(a + "blip").Single();
            blip.Add(XElement.Parse(DuotoneXml));
            blip.Add(XElement.Parse(ClrChangeXml));

            entry.Delete();
            var newEntry = zip.CreateEntry("ppt/slides/slide1.xml", CompressionLevel.NoCompression);
            using var outStream = newEntry.Open();
            doc.Save(outStream);
        }

        return ms.ToArray();
    }

    [Fact]
    public void ReadPictureFormat_DuotoneAndClrChange_CapturedVerbatim()
    {
        var bytes = BuildPackageWithRecolorAndTransparentColorPicture();

        var reloaded = PptxPackageReader.Read(new MemoryStream(bytes));

        var pic = reloaded.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Picture);
        pic.PictureFormat.Should().NotBeNull(
            "a picture whose only blip children are a:duotone/a:clrChange must still get a PictureFormat");
        pic.PictureFormat!.DuotoneXml.Should().NotBeNullOrEmpty();
        pic.PictureFormat.DuotoneXml.Should().Contain("accent1");
        pic.PictureFormat.ClrChangeXml.Should().NotBeNullOrEmpty();
        pic.PictureFormat.ClrChangeXml.Should().Contain("clrFrom");
        pic.PictureFormat.ClrChangeXml.Should().Contain("clrTo");
    }

    [Fact]
    public void RoundTrip_DuotoneAndClrChange_SurvivesOpenThenSave()
    {
        var bytes = BuildPackageWithRecolorAndTransparentColorPicture();
        var firstRead = PptxPackageReader.Read(new MemoryStream(bytes));

        // Simulate "open the file, then just Ctrl+S it back out" -- the exact user gesture named
        // by the finding.
        var resavedBytes = WriteToBytes(firstRead);
        var secondRead = PptxPackageReader.Read(new MemoryStream(resavedBytes));

        var pic = secondRead.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Picture);
        pic.PictureFormat.Should().NotBeNull(
            "the recolor and transparent-color effects must still be present after a save -- " +
            "this is the exact symptom in the finding: the a:duotone/a:clrChange XML is " +
            "permanently deleted from the file");
        pic.PictureFormat!.DuotoneXml.Should().Contain("accent1");
        pic.PictureFormat.ClrChangeXml.Should().Contain("clrFrom");

        // And the actual written part must contain the elements, not just the in-memory model --
        // proves BuildBlipEl really re-emits them rather than merely retaining them in memory.
        using var zms = new MemoryStream(resavedBytes);
        using var zip = new ZipArchive(zms, ZipArchiveMode.Read);
        using var slideStream = zip.GetEntry("ppt/slides/slide1.xml")!.Open();
        var slideXml = new StreamReader(slideStream).ReadToEnd();
        slideXml.Should().Contain("a:duotone");
        slideXml.Should().Contain("a:clrChange");
    }

    /// <summary>
    /// Sibling no-regression case: an ordinary picture with a normal colour effect (grayscale +
    /// crop, no recolor and no transparent color at all) must round-trip exactly as before -- the
    /// new a:duotone/a:clrChange parsing/emission must not disturb the existing
    /// grayscl/biLevel/lum/alphaModFix/crop path, and DuotoneXml/ClrChangeXml must stay null.
    /// </summary>
    [Fact]
    public void RoundTrip_GrayscaleAndCropWithoutRecolorOrTransparentColor_Unaffected()
    {
        var pres = PresentationModel.CreateEmpty();
        var img = new ImagePart { Bytes = Minimal1x1Png(), ContentType = "image/png" };
        pres.Slides[0].Shapes.Add(new SlideShape
        {
            Id          = 1,
            Name        = "Pic1",
            Kind        = SlideShapeKind.Picture,
            OffsetXEmu  = 914400,
            OffsetYEmu  = 457200,
            ExtentCxEmu = 2743200,
            ExtentCyEmu = 1828800,
            Picture     = img,
            PictureFormat = new PictureFormat { Grayscale = true, CropLeft = 0.1, CropRight = 0.2 },
        });

        var bytes = WriteToBytes(pres);
        var reloaded = PptxPackageReader.Read(new MemoryStream(bytes));

        var pic = reloaded.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Picture);
        pic.PictureFormat.Should().NotBeNull();
        pic.PictureFormat!.Grayscale.Should().BeTrue();
        pic.PictureFormat.CropLeft.Should().Be(0.1);
        pic.PictureFormat.CropRight.Should().Be(0.2);
        pic.PictureFormat.DuotoneXml.Should().BeNull();
        pic.PictureFormat.ClrChangeXml.Should().BeNull();
    }
}
