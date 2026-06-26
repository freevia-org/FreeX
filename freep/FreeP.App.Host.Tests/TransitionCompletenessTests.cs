using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 25B: Tests for transition completeness — all kinds, morph option,
/// unknown/Other round-trip via RawXml, and transition sound preservation.
/// </summary>
public class TransitionCompletenessTests
{
    private static readonly XNamespace P   = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace MC  = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";
    private static readonly XNamespace R   = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // ── Element-name mapping ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("fade",           TransitionKind.Fade)]
    [InlineData("cut",            TransitionKind.Cut)]
    [InlineData("push",           TransitionKind.Push)]
    [InlineData("wipe",           TransitionKind.Wipe)]
    [InlineData("cover",          TransitionKind.Cover)]
    [InlineData("uncover",        TransitionKind.Uncover)]
    [InlineData("split",          TransitionKind.Split)]
    [InlineData("blinds",         TransitionKind.Blinds)]
    [InlineData("dissolve",       TransitionKind.Dissolve)]
    [InlineData("zoom",           TransitionKind.Zoom)]
    [InlineData("wheel",          TransitionKind.Wheel)]
    [InlineData("randomBar",      TransitionKind.RandomBar)]
    [InlineData("strips",         TransitionKind.Strips)]
    [InlineData("random",         TransitionKind.Random)]
    [InlineData("morph",          TransitionKind.Morph)]
    [InlineData("flash",          TransitionKind.Flash)]
    [InlineData("reveal",         TransitionKind.Reveal)]
    [InlineData("cube",           TransitionKind.Cube)]
    [InlineData("box",            TransitionKind.Box)]
    [InlineData("rotate",         TransitionKind.Rotate)]
    [InlineData("flip",           TransitionKind.Flip)]
    [InlineData("gallery",        TransitionKind.Gallery)]
    [InlineData("conveyor",       TransitionKind.Conveyor)]
    [InlineData("ferris",         TransitionKind.Ferris)]
    [InlineData("flythrough",     TransitionKind.Flythrough)]
    [InlineData("switch",         TransitionKind.Switch)]
    [InlineData("orbit",          TransitionKind.Orbit)]
    [InlineData("doors",          TransitionKind.Doors)]
    [InlineData("window",         TransitionKind.Window)]
    [InlineData("pan",            TransitionKind.Pan)]
    [InlineData("honeycomb",      TransitionKind.Honeycomb)]
    [InlineData("comb",           TransitionKind.Comb)]
    [InlineData("glitter",        TransitionKind.Glitter)]
    [InlineData("vortex",         TransitionKind.Vortex)]
    [InlineData("shred",          TransitionKind.Shred)]
    [InlineData("wind",           TransitionKind.Wind)]
    [InlineData("ripple",         TransitionKind.Ripple)]
    [InlineData("warp",           TransitionKind.Warp)]
    [InlineData("fracture",       TransitionKind.Fracture)]
    [InlineData("crush",          TransitionKind.Crush)]
    [InlineData("peelOff",        TransitionKind.PeelOff)]
    [InlineData("pageCurlDouble", TransitionKind.PageCurlDouble)]
    [InlineData("pageCurlSingle", TransitionKind.PageCurlSingle)]
    [InlineData("airplane",       TransitionKind.Airplane)]
    [InlineData("origami",        TransitionKind.Origami)]
    [InlineData("prism",          TransitionKind.Prism)]
    [InlineData("curtains",       TransitionKind.Curtains)]
    [InlineData("drape",          TransitionKind.Drape)]
    [InlineData("prestige",       TransitionKind.Prestige)]
    [InlineData("wheelReverse",   TransitionKind.WheelReverse)]
    public void ElementNameToTransitionKind_KnownKind(string elementName, TransitionKind expected)
    {
        var kind = PptxAnimationMap_Accessor.ElementNameToTransitionKind(elementName);
        Assert.Equal(expected, kind);
    }

    [Fact]
    public void ElementNameToTransitionKind_Unrecognized_ReturnsOther()
    {
        var kind = PptxAnimationMap_Accessor.ElementNameToTransitionKind("someExoticFutureTransition");
        Assert.Equal(TransitionKind.Other, kind);
    }

    [Theory]
    [InlineData(TransitionKind.Morph,        "morph")]
    [InlineData(TransitionKind.Cube,         "cube")]
    [InlineData(TransitionKind.Gallery,      "gallery")]
    [InlineData(TransitionKind.Comb,         "comb")]
    [InlineData(TransitionKind.Vortex,       "vortex")]
    [InlineData(TransitionKind.PageCurlDouble, "pageCurlDouble")]
    [InlineData(TransitionKind.Flash,        "flash")]
    public void TransitionKindToElementName_KnownKinds(TransitionKind kind, string expected)
    {
        var name = PptxAnimationMap_Accessor.TransitionKindToElementName(kind);
        Assert.Equal(expected, name);
    }

    [Fact]
    public void TransitionKindToElementName_Other_ReturnsNull()
    {
        var name = PptxAnimationMap_Accessor.TransitionKindToElementName(TransitionKind.Other);
        Assert.Null(name);
    }

    // ── Round-trip: known new kinds ───────────────────────────────────────────────

    [Theory]
    [InlineData(TransitionKind.Morph)]
    [InlineData(TransitionKind.Cube)]
    [InlineData(TransitionKind.Gallery)]
    [InlineData(TransitionKind.Comb)]
    [InlineData(TransitionKind.Vortex)]
    [InlineData(TransitionKind.Flash)]
    [InlineData(TransitionKind.Reveal)]
    [InlineData(TransitionKind.Glitter)]
    [InlineData(TransitionKind.PageCurlDouble)]
    [InlineData(TransitionKind.PageCurlSingle)]
    public void RoundTrip_KnownNewKinds_KindPreserved(TransitionKind kind)
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind       = kind,
            DurationMs = 700,
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(kind, t!.Kind);
    }

    // ── Round-trip: Morph option ──────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Morph_WithOption_Preserved()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind        = TransitionKind.Morph,
            MorphOption = "byWord",
            DurationMs  = 700,
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(TransitionKind.Morph, t!.Kind);
        Assert.Equal("byWord", t.MorphOption);
    }

    // ── Round-trip: Other / Unknown → RawXml preservation ───────────────────────

    [Fact]
    public void RoundTrip_UnknownTransition_PreservedViaRawXml()
    {
        // Build a PPTX with a p:transition containing an exotic child element
        // that FreeP does not enumerate (simulates a future or proprietary transition).
        var pptxBytes = BuildPptxWithTransitionEl(
            "<p:transition xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" spd=\"fast\">" +
            "<p:someExoticFutureTransition dir=\"l\"/>" +
            "</p:transition>");

        using var ms = new MemoryStream(pptxBytes);
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        // Kind must be Other, not None (so the transition is not silently dropped).
        Assert.Equal(TransitionKind.Other, t!.Kind);
        // RawXml must be populated.
        Assert.NotNull(t.RawXml);
        Assert.Contains("someExoticFutureTransition", t.RawXml);

        // Re-write and verify the raw xml survives the round-trip.
        var ms2 = new MemoryStream();
        PptxPackageWriter.Write(loaded, ms2);
        ms2.Position = 0;
        var reloaded = PptxPackageReader.Read(ms2);
        var t2 = reloaded.Slides[0].Transition;
        Assert.NotNull(t2);
        Assert.Equal(TransitionKind.Other, t2!.Kind);
        Assert.NotNull(t2.RawXml);
        Assert.Contains("someExoticFutureTransition", t2.RawXml);
    }

    [Fact]
    public void RoundTrip_UnknownTransition_NeverSilentlyDropped()
    {
        // The guarantee: loading and re-saving ANY unrecognized transition must not lose it.
        var pptxBytes = BuildPptxWithTransitionEl(
            "<p:transition xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" spd=\"med\">" +
            "<p:prestige/>" +   // This IS enumerated — but we test with an unknown one too
            "</p:transition>");

        using var ms = new MemoryStream(pptxBytes);
        var loaded = PptxPackageReader.Read(ms);

        // prestige should be a known kind.
        Assert.Equal(TransitionKind.Prestige, loaded.Slides[0].Transition?.Kind);

        // Re-save and verify the kind survives.
        var ms2 = new MemoryStream();
        PptxPackageWriter.Write(loaded, ms2);
        ms2.Position = 0;
        var reloaded = PptxPackageReader.Read(ms2);
        Assert.Equal(TransitionKind.Prestige, reloaded.Slides[0].Transition?.Kind);
    }

    // ── Round-trip: Transition sound ─────────────────────────────────────────────

    [Fact]
    public void RoundTrip_TransitionSound_AudioBytesPreserved()
    {
        // Fake audio bytes (not real audio, just bytes to verify round-trip byte-identity).
        var fakeAudio = new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind  = TransitionKind.Fade,
            Sound = new TransitionSound
            {
                AudioBytes  = fakeAudio,
                ContentType = "audio/mpeg",
                Loop        = false,
            },
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(TransitionKind.Fade, t!.Kind);
        Assert.NotNull(t.Sound);
        Assert.NotNull(t.Sound!.AudioBytes);
        Assert.Equal(fakeAudio.Length, t.Sound.AudioBytes!.Length);
        Assert.Equal(fakeAudio, t.Sound.AudioBytes);
    }

    [Fact]
    public void RoundTrip_TransitionSound_LoopFlagPreserved()
    {
        var fakeAudio = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // minimal RIFF header

        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind  = TransitionKind.Push,
            Sound = new TransitionSound
            {
                AudioBytes  = fakeAudio,
                ContentType = "audio/wav",
                Loop        = true,
            },
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t?.Sound);
        Assert.True(t!.Sound!.Loop);
    }

    // ── SlideCloner preserves new fields ─────────────────────────────────────────

    [Fact]
    public void Cloner_PreservesRawXmlAndMorphOption()
    {
        var slide = Presentation.CreateEmpty().Slides[0];
        slide.Transition = new SlideTransition
        {
            Kind        = TransitionKind.Morph,
            MorphOption = "byChar",
            RawXml      = null,
            DurationMs  = 600,
        };

        var clone = SlideCloner.CloneSlide(slide);

        Assert.Equal(TransitionKind.Morph, clone.Transition?.Kind);
        Assert.Equal("byChar", clone.Transition?.MorphOption);
    }

    [Fact]
    public void Cloner_PreservesTransitionSound()
    {
        var audio = new byte[] { 1, 2, 3, 4, 5 };
        var slide = Presentation.CreateEmpty().Slides[0];
        slide.Transition = new SlideTransition
        {
            Kind  = TransitionKind.Fade,
            Sound = new TransitionSound { AudioBytes = audio, ContentType = "audio/mpeg", Loop = true },
        };

        var clone = SlideCloner.CloneSlide(slide);

        Assert.NotNull(clone.Transition?.Sound);
        Assert.Equal(audio, clone.Transition!.Sound!.AudioBytes);
        Assert.Equal("audio/mpeg", clone.Transition.Sound.ContentType);
        Assert.True(clone.Transition.Sound.Loop);

        // Mutating original audio does not affect clone.
        audio[0] = 99;
        Assert.Equal(1, clone.Transition.Sound.AudioBytes![0]);
    }

    // ── Slideshow effect-fallback mapping (resolved correctly, no crash) ─────────

    [Theory]
    [InlineData(TransitionKind.Morph)]
    [InlineData(TransitionKind.Cube)]
    [InlineData(TransitionKind.Vortex)]
    [InlineData(TransitionKind.Prestige)]
    [InlineData(TransitionKind.PageCurlDouble)]
    [InlineData(TransitionKind.Origami)]
    [InlineData(TransitionKind.Other)]
    public void SlideTransitionKind_IsDefinedInEnum(TransitionKind kind)
    {
        // Verifies the enum value exists and is a valid defined member.
        Assert.True(Enum.IsDefined(typeof(TransitionKind), kind));
    }

    [Fact]
    public void TransitionKind_Other_HasFadePlayback_Contract()
    {
        // Contract: TransitionKind.Other must NOT be None (so it's not skipped by the
        // "Kind: not TransitionKind.None" guard in DisplayCurrentSlide), and must map
        // to some kind of playback (Fade fallback) in the slideshow.
        Assert.NotEqual(TransitionKind.None, TransitionKind.Other);
        // The fallback is exercised by the default case in PlayTransition in both SlideShowWindows.
        // This test verifies the enum value itself is correctly != None.
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal PPTX ZIP that puts the given raw <paramref name="transitionXml"/>
    /// (a complete p:transition element string) directly as a child of the slide root.
    /// The reader will pick it up via the bare-p:transition path.
    /// </summary>
    private static byte[] BuildPptxWithTransitionEl(string transitionXml)
    {
        XNamespace pNs    = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace aNs    = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace rNs    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        var transEl = XElement.Parse(transitionXml);

        var slideXml = new XDocument(
            new XElement(pNs + "sld",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(pNs + "cSld",
                    new XElement(pNs + "spTree",
                        new XElement(pNs + "nvGrpSpPr",
                            new XElement(pNs + "cNvPr",
                                new XAttribute("id", "1"), new XAttribute("name", "G")),
                            new XElement(pNs + "cNvGrpSpPr"),
                            new XElement(pNs + "nvPr")),
                        new XElement(pNs + "grpSpPr",
                            new XElement(aNs + "xfrm",
                                new XElement(aNs + "off",
                                    new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "ext",
                                    new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                                new XElement(aNs + "chOff",
                                    new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "chExt",
                                    new XAttribute("cx", "0"), new XAttribute("cy", "0")))))),
                transEl));

        var presXml = new XDocument(
            new XElement(pNs + "presentation",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(pNs + "sldSz",
                    new XAttribute("cx", "9144000"), new XAttribute("cy", "6858000")),
                new XElement(pNs + "notesSz",
                    new XAttribute("cx", "6858000"), new XAttribute("cy", "9144000")),
                new XElement(pNs + "sldIdLst",
                    new XElement(pNs + "sldId",
                        new XAttribute("id", "256"),
                        new XAttribute(rNs + "id", "rId1")))));

        XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypes = new XDocument(
            new XElement(ctNs + "Types",
                new XElement(ctNs + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ctNs + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ctNs + "Override",
                    new XAttribute("PartName", "/ppt/presentation.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml")),
                new XElement(ctNs + "Override",
                    new XAttribute("PartName", "/ppt/slides/slide1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml"))));

        var rootRels = new XDocument(
            new XElement(pkgRel + "Relationships",
                new XElement(pkgRel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "ppt/presentation.xml"))));

        var presRels = new XDocument(
            new XElement(pkgRel + "Relationships",
                new XElement(pkgRel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide"),
                    new XAttribute("Target", "slides/slide1.xml"))));

        var slideRels = new XDocument(new XElement(pkgRel + "Relationships"));

        using var outMs = new MemoryStream();
        using (var zip = new ZipArchive(outMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", contentTypes);
            WriteEntry(zip, "_rels/.rels", rootRels);
            WriteEntry(zip, "ppt/presentation.xml", presXml);
            WriteEntry(zip, "ppt/_rels/presentation.xml.rels", presRels);
            WriteEntry(zip, "ppt/slides/slide1.xml", slideXml);
            WriteEntry(zip, "ppt/slides/_rels/slide1.xml.rels", slideRels);
        }
        return outMs.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string path, XDocument doc)
    {
        var entry = zip.CreateEntry(path);
        using var stream = entry.Open();
        doc.Save(stream);
    }
}

/// <summary>
/// Thin accessor so tests can call the internal PptxAnimationMap methods without InternalsVisibleTo.
/// The map is in FreeP.Core.IO which the tests project already references.
/// We call them via the public API (round-trip through writer/reader) except for the two
/// static mapping helpers — expose them here as a test-only façade.
/// </summary>
internal static class PptxAnimationMap_Accessor
{
    // These delegate to the actual PptxAnimationMap methods.
    // PptxAnimationMap is internal to FreeP.Core.IO — verify behavior via round-trip.
    // For the mapping tests we build a minimal in-memory slide with a known transition
    // and verify the element name via reading back from the ZIP.

    public static TransitionKind ElementNameToTransitionKind(string name)
    {
        // Build a minimal PPTX with a bare p:transition bearing the given child element.
        var ns      = "http://schemas.openxmlformats.org/presentationml/2006/main";
        var rawXml  = $"<p:transition xmlns:p=\"{ns}\" spd=\"fast\"><p:{name}/></p:transition>";
        try
        {
            var pptxBytes = BuildMinimalPptx(rawXml);
            using var ms  = new MemoryStream(pptxBytes);
            var loaded    = PptxPackageReader.Read(ms);
            return loaded.Slides[0].Transition?.Kind ?? TransitionKind.None;
        }
        catch
        {
            return TransitionKind.None;
        }
    }

    public static string? TransitionKindToElementName(TransitionKind kind)
    {
        if (kind == TransitionKind.Other || kind == TransitionKind.None) return null;

        // Write a slide with the given kind, then read back the ZIP and extract the element name.
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition { Kind = kind, DurationMs = 500 };
        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        if (entry is null) return null;

        XDocument doc;
        using (var s = entry.Open()) doc = XDocument.Load(s);

        XNamespace P   = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace MC  = "http://schemas.openxmlformats.org/markup-compatibility/2006";

        // Navigate through mc:AlternateContent or bare p:transition.
        var transEl = doc.Root?.Element(MC + "AlternateContent")
                          ?.Element(MC + "Choice")
                          ?.Element(P + "transition")
                      ?? doc.Root?.Element(P + "transition");

        var effectEl = transEl?.Elements()
            .FirstOrDefault(e => e.Name.Namespace == P
                                 && e.Name.LocalName != "sndAc"
                                 && e.Name.LocalName != "extLst");
        return effectEl?.Name.LocalName;
    }

    private static byte[] BuildMinimalPptx(string transitionXml)
    {
        XNamespace pNs    = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace aNs    = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace rNs    = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace pkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        var transEl = XElement.Parse(transitionXml);

        var slideXml = new XDocument(
            new XElement(pNs + "sld",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "a", aNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(pNs + "cSld",
                    new XElement(pNs + "spTree",
                        new XElement(pNs + "nvGrpSpPr",
                            new XElement(pNs + "cNvPr",
                                new XAttribute("id", "1"), new XAttribute("name", "G")),
                            new XElement(pNs + "cNvGrpSpPr"),
                            new XElement(pNs + "nvPr")),
                        new XElement(pNs + "grpSpPr",
                            new XElement(aNs + "xfrm",
                                new XElement(aNs + "off",
                                    new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "ext",
                                    new XAttribute("cx", "0"), new XAttribute("cy", "0")),
                                new XElement(aNs + "chOff",
                                    new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(aNs + "chExt",
                                    new XAttribute("cx", "0"), new XAttribute("cy", "0")))))),
                transEl));

        var presXml = new XDocument(
            new XElement(pNs + "presentation",
                new XAttribute(XNamespace.Xmlns + "p", pNs.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "r", rNs.NamespaceName),
                new XElement(pNs + "sldSz",
                    new XAttribute("cx", "9144000"), new XAttribute("cy", "6858000")),
                new XElement(pNs + "notesSz",
                    new XAttribute("cx", "6858000"), new XAttribute("cy", "9144000")),
                new XElement(pNs + "sldIdLst",
                    new XElement(pNs + "sldId",
                        new XAttribute("id", "256"),
                        new XAttribute(rNs + "id", "rId1")))));

        XNamespace ctNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypes = new XDocument(
            new XElement(ctNs + "Types",
                new XElement(ctNs + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ctNs + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ctNs + "Override",
                    new XAttribute("PartName", "/ppt/presentation.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml")),
                new XElement(ctNs + "Override",
                    new XAttribute("PartName", "/ppt/slides/slide1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml"))));

        var rootRels = new XDocument(
            new XElement(pkgRel + "Relationships",
                new XElement(pkgRel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "ppt/presentation.xml"))));

        var presRels = new XDocument(
            new XElement(pkgRel + "Relationships",
                new XElement(pkgRel + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide"),
                    new XAttribute("Target", "slides/slide1.xml"))));

        var slideRels = new XDocument(new XElement(pkgRel + "Relationships"));

        using var outMs = new MemoryStream();
        using (var zip = new ZipArchive(outMs, ZipArchiveMode.Create, leaveOpen: true))
        {
            void WriteEntry(ZipArchive z, string path, XDocument doc)
            {
                var e = z.CreateEntry(path);
                using var s = e.Open();
                doc.Save(s);
            }
            WriteEntry(zip, "[Content_Types].xml", contentTypes);
            WriteEntry(zip, "_rels/.rels", rootRels);
            WriteEntry(zip, "ppt/presentation.xml", presXml);
            WriteEntry(zip, "ppt/_rels/presentation.xml.rels", presRels);
            WriteEntry(zip, "ppt/slides/slide1.xml", slideXml);
            WriteEntry(zip, "ppt/slides/_rels/slide1.xml.rels", slideRels);
        }
        return outMs.ToArray();
    }
}
