using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeP.App.Compositor;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Round-166 F1: p:cTn/@repeatCount is ST_TLTimeNodeRepeatCount — a percentage scaled by
/// 1000 (100000 == 100% == one pass), the same convention used for @accel/@decel on the same
/// element. A real PowerPoint "Repeat = 3" is authored as repeatCount="300000".
///
/// These tests deliberately do NOT compare the writer's output against the reader's own
/// interpretation (that symmetry is exactly what hid the bug on FreeP-to-FreeP round trips).
/// Instead:
///  - the read-side test splices a literal, spec-known repeatCount="300000" into an
///    otherwise-valid slide (simulating a file authored by real PowerPoint) and asserts the
///    resulting model value, independent of the writer;
///  - the write-side test asserts the literal XML text the writer emits for a model
///    RepeatCount of 3, independent of the reader.
/// </summary>
public sealed class AnimationRepeatCountScalingTests
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";

    private static Presentation BuildPulsePresentation(int? repeatCount)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = Free.Shared.Drawing.DrawingShapeKind.Rectangle,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 1371600,
        });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            Kind = AnimationKind.Emphasis,
            Preset = AnimationPreset.Pulse,
            RepeatCount = repeatCount,
        });
        return presentation;
    }

    private static XElement FindWithEffectCTn(XDocument slideXml) =>
        slideXml.Descendants(P + "cTn")
            .Single(element => element.Attribute("nodeType")?.Value == "withEffect");

    /// <summary>
    /// Read side: a foreign (real-PowerPoint-shaped) slide with a literal, spec-known
    /// repeatCount="300000" ("Repeat = 3" authored in the PowerPoint UI) must be read into
    /// the model as RepeatCount == 3, not 300000.
    /// </summary>
    [Fact]
    public void ReadingForeignScaledRepeatCountProducesUnscaledModelValue()
    {
        // Start from a structurally-valid package (correct presetClass/presetID/spTgt/etc.)
        // written by our own writer with no repeat authored...
        var baseline = BuildPulsePresentation(repeatCount: null);
        using var baselineStream = new MemoryStream();
        PptxPackageWriter.Write(baseline, baselineStream);

        // ...then splice in a literal repeatCount="300000", exactly as real PowerPoint would
        // author "Effect Options > Timing > Repeat = 3". This value is NOT derived from our
        // reader or writer in any way.
        byte[] patched;
        using (var msOut = new MemoryStream())
        {
            using (var srcZip = new ZipArchive(new MemoryStream(baselineStream.ToArray()), ZipArchiveMode.Read))
            using (var dstZip = new ZipArchive(msOut, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in srcZip.Entries)
                {
                    var dstEntry = dstZip.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                    using var src = entry.Open();
                    using var dst = dstEntry.Open();
                    if (entry.FullName == "ppt/slides/slide1.xml")
                    {
                        using var reader = new StreamReader(src);
                        var slideXml = XDocument.Parse(reader.ReadToEnd());
                        var cTn = FindWithEffectCTn(slideXml);
                        cTn.SetAttributeValue("repeatCount", "300000");
                        slideXml.Save(dst, SaveOptions.DisableFormatting);
                    }
                    else
                    {
                        src.CopyTo(dst);
                    }
                }
            }
            patched = msOut.ToArray();
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(patched));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.RepeatCount.Should().Be(3,
            "repeatCount=\"300000\" is 300% == a 3x repeat per ST_TLTimeNodeRepeatCount, not a raw count of 300000");
        animation.RepeatIndefinitely.Should().BeFalse();
    }

    /// <summary>
    /// Write side: a model RepeatCount of 3 must be authored as the literal OOXML text
    /// "300000" (per ST_TLTimeNodeRepeatCount), asserted directly — not by reading it back
    /// through our own reader, which would just confirm the two bugs still agree with
    /// each other.
    /// </summary>
    [Fact]
    public void WritingFiniteRepeatCountEmitsOoxmlScaledLiteral()
    {
        var presentation = BuildPulsePresentation(repeatCount: 3);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        using var archive = new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open());
        var slideXml = XDocument.Parse(reader.ReadToEnd());

        var cTn = FindWithEffectCTn(slideXml);
        cTn.Attribute("repeatCount")!.Value.Should().Be("300000",
            "a 3x repeat must be authored as 300% (100000 per pass) per ST_TLTimeNodeRepeatCount");
    }

    /// <summary>
    /// Sibling no-regression: accel/decel on the very same p:cTn element were already
    /// correctly scaled (ReadTimingPercentage / AddAccelerationAttributes) and must stay
    /// that way — this fix must not touch that sibling behavior.
    /// </summary>
    [Fact]
    public void AccelerationAndDecelerationRemainCorrectlyScaledAlongsideRepeatCountFix()
    {
        var presentation = BuildPulsePresentation(repeatCount: 3);
        presentation.Slides[0].Animations.Single().Acceleration = 25000;
        presentation.Slides[0].Animations.Single().Deceleration = 35000;

        using var first = new MemoryStream();
        PptxPackageWriter.Write(presentation, first);
        using (var archive = new ZipArchive(new MemoryStream(first.ToArray()), ZipArchiveMode.Read))
        using (var reader = new StreamReader(archive.GetEntry("ppt/slides/slide1.xml")!.Open()))
        {
            var slideXml = XDocument.Parse(reader.ReadToEnd());
            var cTn = FindWithEffectCTn(slideXml);
            cTn.Attribute("accel")!.Value.Should().Be("25000");
            cTn.Attribute("decel")!.Value.Should().Be("35000");
            cTn.Attribute("repeatCount")!.Value.Should().Be("300000");
        }

        var reloaded = PptxPackageReader.Read(new MemoryStream(first.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();
        animation.Acceleration.Should().Be(25000);
        animation.Deceleration.Should().Be(35000);
        animation.RepeatCount.Should().Be(3);
    }

    /// <summary>
    /// Sibling no-regression: repeatCount="indefinite" is a distinct literal token (not a
    /// scaled number) and must keep round-tripping as RepeatIndefinitely, untouched by the
    /// numeric scaling fix.
    /// </summary>
    [Fact]
    public void IndefiniteRepeatTokenIsUnaffectedByScalingFix()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 7,
            Kind = SlideShapeKind.AutoShape,
        });
        presentation.Slides[0].Animations.Add(new ShapeAnimation
        {
            ShapeId = 7,
            RepeatIndefinitely = true,
        });

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        var reloaded = PptxPackageReader.Read(new MemoryStream(stream.ToArray()));
        var animation = reloaded.Slides[0].Animations.Single();

        animation.RepeatIndefinitely.Should().BeTrue();
        animation.RepeatCount.Should().BeNull();
    }
}
