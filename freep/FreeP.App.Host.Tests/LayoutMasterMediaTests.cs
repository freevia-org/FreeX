using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// R136 finding B (MED): picture/media placeholder shapes on a slide LAYOUT or MASTER were
/// dropped on write. BuildSlideLayoutXml/BuildSlideMasterXml handed the shared shape-building
/// helper an empty mediaById map (and never wrote the underlying picture/video/audio bytes at
/// all), so any Picture/Media placeholder on a layout or master fell back to a hardcoded
/// "rIdMedia1"/"rIdVid1" r:embed/r:link with no matching &lt;Relationship&gt; element -- a
/// dangling relationship that appeared on every save, even of an untouched deck.
/// </summary>
public sealed class LayoutMasterMediaTests
{
    [Fact]
    public void LayoutPicturePlaceholder_RoundTrip_PreservesBytesAndAvoidsDanglingRelationship()
    {
        var pres = new Presentation();
        var master = new SlideMaster { Id = "rIdMaster1" };
        pres.Masters.Add(master);

        var layout = new SlideLayout { Id = "rIdLayout1", MasterId = master.Id, Name = "Picture Layout" };
        var pngBytes = CreateMinimal1x1Png();
        layout.Placeholders.Add(new SlideShape
        {
            Id          = 5,
            Name        = "Layout picture",
            Kind        = SlideShapeKind.Picture,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Picture     = new ImagePart { Bytes = pngBytes, ContentType = "image/png" },
        });
        pres.Layouts.Add(layout);
        pres.Slides.Add(new Slide { LayoutId = layout.Id });

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        var bytes = ms.ToArray();

        // Strict package-validity check, independent of PptxPackageReader's own tolerance: every
        // r:embed/r:link found on the layout part must resolve to a real Relationship element.
        AssertNoDanglingRelationships(bytes, "ppt/slideLayouts/slideLayout1.xml");

        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);
        var loadedShape = loaded.Layouts.Single().Placeholders.Single(s => s.Kind == SlideShapeKind.Picture);
        loadedShape.Picture.Should().NotBeNull("the layout picture placeholder's bytes must survive the round trip");
        loadedShape.Picture!.Bytes.Should().Equal(pngBytes);
    }

    [Fact]
    public void MasterVideoPlaceholder_RoundTrip_PreservesBytesAndAvoidsDanglingRelationship()
    {
        var pres = new Presentation();
        var master = new SlideMaster { Id = "rIdMaster1" };
        var videoBytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };
        master.Placeholders.Add(new SlideShape
        {
            Id          = 9,
            Name        = "Master video",
            Kind        = SlideShapeKind.Media,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            Media       = new MediaInfo { IsVideo = true, Bytes = videoBytes, ContentType = "video/mp4" },
        });
        pres.Masters.Add(master);

        var layout = new SlideLayout { Id = "rIdLayout1", MasterId = master.Id };
        pres.Layouts.Add(layout);
        pres.Slides.Add(new Slide { LayoutId = layout.Id });

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        var bytes = ms.ToArray();

        AssertNoDanglingRelationships(bytes, "ppt/slideMasters/slideMaster1.xml");

        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);
        var loadedShape = loaded.Masters.Single().Placeholders.Single(s => s.Kind == SlideShapeKind.Media);
        loadedShape.Media.Should().NotBeNull("the master video placeholder's media must survive the round trip");
        loadedShape.Media!.Bytes.Should().Equal(videoBytes);
    }

    /// <summary>
    /// Sibling/no-regression: a picture living on the SLIDE itself (not a layout/master
    /// placeholder) must be unaffected by wiring mediaById through BuildSlideLayoutXml/
    /// BuildSlideMasterXml -- the per-slide media pipeline is a separate call path.
    /// </summary>
    [Fact]
    public void SlideOwnedPicture_StillRoundTrips_AlongsideLayoutAndMasterMedia()
    {
        var pres = new Presentation();
        var master = new SlideMaster { Id = "rIdMaster1" };
        var masterBytes = CreateMinimal1x1Png();
        master.Placeholders.Add(new SlideShape
        {
            Id = 1, Name = "Master pic", Kind = SlideShapeKind.Picture,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
            Picture = new ImagePart { Bytes = masterBytes, ContentType = "image/png" },
        });
        pres.Masters.Add(master);

        var layout = new SlideLayout { Id = "rIdLayout1", MasterId = master.Id };
        var layoutBytes = CreateMinimal1x1Png();
        layout.Placeholders.Add(new SlideShape
        {
            Id = 2, Name = "Layout pic", Kind = SlideShapeKind.Picture,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
            Picture = new ImagePart { Bytes = layoutBytes, ContentType = "image/png" },
        });
        pres.Layouts.Add(layout);

        var slide = new Slide { LayoutId = layout.Id };
        var slideBytes = CreateMinimal1x1Png();
        slide.Shapes.Add(new SlideShape
        {
            Id = 3, Name = "Slide pic", Kind = SlideShapeKind.Picture,
            ExtentCxEmu = 914400, ExtentCyEmu = 914400,
            Picture = new ImagePart { Bytes = slideBytes, ContentType = "image/png" },
        });
        pres.Slides.Add(slide);

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        var bytes = ms.ToArray();

        AssertNoDanglingRelationships(bytes,
            "ppt/slides/slide1.xml", "ppt/slideLayouts/slideLayout1.xml", "ppt/slideMasters/slideMaster1.xml");

        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);
        loaded.Masters.Single().Placeholders.Single(s => s.Kind == SlideShapeKind.Picture)
            .Picture!.Bytes.Should().Equal(masterBytes);
        loaded.Layouts.Single().Placeholders.Single(s => s.Kind == SlideShapeKind.Picture)
            .Picture!.Bytes.Should().Equal(layoutBytes);
        loaded.Slides.Single().Shapes.Single(s => s.Kind == SlideShapeKind.Picture)
            .Picture!.Bytes.Should().Equal(slideBytes);
    }

    /// <summary>
    /// Same strict package-validity check used in the MediaFieldsTests media-link tests: every
    /// r:embed/r:link/r:id found in each of <paramref name="partPaths"/> must resolve to a
    /// &lt;Relationship Id="..."&gt; actually present in that part's sibling .rels file.
    /// </summary>
    private static void AssertNoDanglingRelationships(byte[] pptxBytes, params string[] partPaths)
    {
        var officeRelNs = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var packageRelNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");

        using var ms = new MemoryStream(pptxBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var partPath in partPaths)
        {
            var entry = archive.GetEntry(partPath);
            entry.Should().NotBeNull($"{partPath} must exist in the saved package");
            XDocument partXml;
            using (var stream = entry!.Open())
                partXml = XDocument.Load(stream);

            var referencedIds = partXml.Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attr => attr.Name.Namespace == officeRelNs)
                .Select(attr => attr.Value)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToArray();

            if (referencedIds.Length == 0)
                continue;

            var lastSlash = partPath.LastIndexOf('/');
            var relsPath = lastSlash >= 0
                ? partPath[..(lastSlash + 1)] + "_rels/" + partPath[(lastSlash + 1)..] + ".rels"
                : "_rels/" + partPath + ".rels";

            var relsEntry = archive.GetEntry(relsPath);
            relsEntry.Should().NotBeNull(
                $"{partPath} references relationship id(s) [{string.Join(", ", referencedIds)}] but has no {relsPath} part");

            XDocument relsXml;
            using (var stream = relsEntry!.Open())
                relsXml = XDocument.Load(stream);

            var declaredIds = relsXml.Root!.Elements(packageRelNs + "Relationship")
                .Select(e => e.Attribute("Id")?.Value)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var id in referencedIds)
            {
                declaredIds.Should().Contain(id,
                    $"{partPath} references relationship id '{id}' but {relsPath} has no matching " +
                    $"<Relationship Id=\"{id}\"> -- this is a dangling relationship");
            }
        }
    }

    private static byte[] CreateMinimal1x1Png() =>
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8" +
            "z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");
}
