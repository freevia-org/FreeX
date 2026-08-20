using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Host.Tests;

/// <summary>
/// freep-picture-effects F3: a PowerPoint Artistic Effect (Pencil Sketch, Glow Diffused, Mosaic
/// Bubbles, etc.) is stored as an a14:artisticEffect element under a:blip/a:extLst (the a14
/// "imgEffect" ISO/IEC 29500 transitional extension). ReadPictureFormat never looked at
/// a:extLst under a:blip at all, so opening a real-PowerPoint picture with an Artistic Effect
/// applied and simply re-saving it from FreeP silently dropped the effect -- the picture came
/// back out as the plain, unmodified photo.
///
/// The fix captures the a:ext element carrying a14:artisticEffect verbatim into
/// <see cref="PictureFormat.ArtisticEffectXml"/> on read, and BuildBlipEl
/// re-emits it verbatim into a:blip/a:extLst on write.
/// </summary>
public sealed class PictureArtisticEffectTests
{
    private static byte[] Minimal1x1Png() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8" +
            "z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

    // The exact a14 imgEffect shape PowerPoint writes for a picture with "Pencil Sketch"
    // applied (Picture Format > Artistic Effects). Namespace-qualified exactly like real Office
    // output, including the a14 declaration on the ext element itself.
    private const string PencilSketchExtXml =
        "<a:ext xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "uri=\"{BEBA8EAE-BF5A-486C-A8C5-ECC9F3942E4B}\">" +
        "<a14:artisticEffect xmlns:a14=\"http://schemas.microsoft.com/office/drawing/2010/main\" " +
        "type=\"pencilSketch\"><a14:artisticEffectData><a14:pencilSketchProps trans=\"31000\" " +
        "pressure=\"55000\" /></a14:artisticEffectData></a14:artisticEffect></a:ext>";

    private static byte[] WriteToBytes(PresentationModel pres)
    {
        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Builds a package with a picture whose a:blip carries a real a14:artisticEffect extLst,
    /// exactly as PowerPoint itself writes it, then patches that blip into slide1.xml -- this
    /// exercises the reader precisely on the shape described by the finding without depending on
    /// FreeP's own writer already being able to emit the effect.
    /// </summary>
    private static byte[] BuildPackageWithArtisticEffectPicture()
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
            blip.Add(new XElement(a + "extLst", XElement.Parse(PencilSketchExtXml)));

            entry.Delete();
            var newEntry = zip.CreateEntry("ppt/slides/slide1.xml", CompressionLevel.NoCompression);
            using var outStream = newEntry.Open();
            doc.Save(outStream);
        }

        return ms.ToArray();
    }

    [Fact]
    public void ReadPictureFormat_ArtisticEffect_CapturedVerbatim()
    {
        var bytes = BuildPackageWithArtisticEffectPicture();

        var reloaded = PptxPackageReader.Read(new MemoryStream(bytes));

        var pic = reloaded.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Picture);
        pic.PictureFormat.Should().NotBeNull(
            "a picture whose only blip child is an artistic effect must still get a PictureFormat");
        pic.PictureFormat!.ArtisticEffectXml.Should().NotBeNullOrEmpty();
        pic.PictureFormat.ArtisticEffectXml.Should().Contain("pencilSketch");
        pic.PictureFormat.ArtisticEffectXml.Should().Contain("{BEBA8EAE-BF5A-486C-A8C5-ECC9F3942E4B}");
    }

    [Fact]
    public void RoundTrip_ArtisticEffect_SurvivesOpenThenSave()
    {
        var bytes = BuildPackageWithArtisticEffectPicture();
        var firstRead = PptxPackageReader.Read(new MemoryStream(bytes));

        // Simulate "open the file, then just Ctrl+S it back out" -- the exact user gesture named
        // by the finding.
        var resavedBytes = WriteToBytes(firstRead);
        var secondRead = PptxPackageReader.Read(new MemoryStream(resavedBytes));

        var pic = secondRead.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Picture);
        pic.PictureFormat.Should().NotBeNull(
            "the artistic effect must still be present after a save -- this is the exact symptom " +
            "in the finding: 'the saved file's picture renders as the plain, unmodified photo'");
        pic.PictureFormat!.ArtisticEffectXml.Should().Contain("pencilSketch");

        // And the actual written part must contain the extension, not just the in-memory model --
        // proves BuildBlipEl really re-emits it rather than merely retaining it in memory.
        using var zms = new MemoryStream(resavedBytes);
        using var zip = new ZipArchive(zms, ZipArchiveMode.Read);
        using var slideStream = zip.GetEntry("ppt/slides/slide1.xml")!.Open();
        var slideXml = new StreamReader(slideStream).ReadToEnd();
        slideXml.Should().Contain("artisticEffect");
        slideXml.Should().Contain("pencilSketch");
    }

    /// <summary>
    /// Sibling no-regression case: an ordinary picture with a normal colour effect (no artistic
    /// effect at all) must round-trip exactly as before -- the new extLst parsing/emission must
    /// not disturb the existing grayscl/biLevel/lum/alphaModFix path, and ArtisticEffectXml must
    /// stay null.
    /// </summary>
    [Fact]
    public void RoundTrip_GrayscaleWithoutArtisticEffect_Unaffected()
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
            PictureFormat = new PictureFormat { Grayscale = true },
        });

        var bytes = WriteToBytes(pres);
        var reloaded = PptxPackageReader.Read(new MemoryStream(bytes));

        var pic = reloaded.Slides[0].Shapes.Single(s => s.Kind == SlideShapeKind.Picture);
        pic.PictureFormat.Should().NotBeNull();
        pic.PictureFormat!.Grayscale.Should().BeTrue();
        pic.PictureFormat.ArtisticEffectXml.Should().BeNull();
    }
}
