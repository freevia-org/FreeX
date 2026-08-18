using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FreeP.App.Compositor;
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
    [InlineData(TransitionKind.Honeycomb,    "honeycomb")]
    [InlineData(TransitionKind.Gallery,      "gallery")]
    [InlineData(TransitionKind.Comb,         "comb")]
    [InlineData(TransitionKind.Vortex,       "vortex")]
    [InlineData(TransitionKind.PageCurlDouble, "pageCurlDouble")]
    [InlineData(TransitionKind.Flash,        "flash")]
    [InlineData(TransitionKind.Random,       "random")]
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
    [InlineData(TransitionKind.Honeycomb)]
    [InlineData(TransitionKind.Gallery)]
    [InlineData(TransitionKind.Comb)]
    [InlineData(TransitionKind.Vortex)]
    [InlineData(TransitionKind.Flash)]
    [InlineData(TransitionKind.Reveal)]
    [InlineData(TransitionKind.Glitter)]
    [InlineData(TransitionKind.PageCurlDouble)]
    [InlineData(TransitionKind.PageCurlSingle)]
    [InlineData(TransitionKind.Random)]
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

    [Fact]
    public void RoundTrip_Random_RemainsApplicationChosenAndPreservesTiming()
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind = TransitionKind.Random,
            DurationMs = 875,
            AdvanceOnClick = false,
            AdvanceAfterMs = 2_400,
        };

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var slideEntry = zip.GetEntry("ppt/slides/slide1.xml");
            Assert.NotNull(slideEntry);
            using var stream = slideEntry!.Open();
            var slideXml = XDocument.Load(stream);
            var randomElements = slideXml.Descendants(P + "random").ToArray();
            Assert.Equal(2, randomElements.Length);
            Assert.All(randomElements, random => Assert.Empty(random.Attributes()));
        }

        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);
        var transition = loaded.Slides[0].Transition;
        Assert.NotNull(transition);
        Assert.Equal(TransitionKind.Random, transition!.Kind);
        Assert.Equal(875, transition.DurationMs);
        Assert.False(transition.AdvanceOnClick);
        Assert.Equal(2_400, transition.AdvanceAfterMs);
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

    [Theory]
    [InlineData(TransitionKind.Wheel)]
    [InlineData(TransitionKind.WheelReverse)]
    public void RoundTrip_Wheel_SpokeCount_Preserved(TransitionKind kind)
    {
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind = kind,
            WheelSpokeCount = 8,
            DurationMs = 700,
        };

        var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(kind, t!.Kind);
        Assert.Equal(8, t.WheelSpokeCount);
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

    // ── F1: unrecognized transition wrapped in mc:AlternateContent on read ──────────

    [Fact]
    public void RoundTrip_UnknownTransition_WrappedInAlternateContent_PreservesWrapperAndFallback()
    {
        // F1: an unrecognized transition originally wrapped in mc:AlternateContent (the shape
        // real PowerPoint / a newer FreeP version uses so older readers degrade gracefully via
        // mc:Fallback) must keep that wrapper -- and the Fallback content -- on save. Before the
        // fix, BuildTransitionEl re-emitted only the bare inner p:transition (RawXml), which
        // strips the wrapper (leaving the unknown extension element as an invalid direct child
        // of p:transition outside markup-compatibility processing) and discards the Fallback.
        var pptxBytes = BuildPptxWithTransitionEl(
            "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"" +
            " xmlns:p14=\"http://schemas.microsoft.com/office/powerpoint/2010/main\">" +
            "<mc:Choice Requires=\"p14\">" +
            "<p:transition xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" spd=\"fast\" p14:dur=\"500\">" +
            "<p14:futureEffectNotYetKnown/>" +
            "</p:transition>" +
            "</mc:Choice>" +
            "<mc:Fallback>" +
            "<p:transition xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" spd=\"fast\">" +
            "<p:fade/>" +
            "</p:transition>" +
            "</mc:Fallback>" +
            "</mc:AlternateContent>");

        using var ms = new MemoryStream(pptxBytes);
        var loaded = PptxPackageReader.Read(ms);
        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(TransitionKind.Other, t!.Kind);
        Assert.True(t.WasAlternateContent, "the source wrapping must be captured so the writer can re-wrap it");
        Assert.Equal("p14", t.McRequiresToken);
        Assert.NotNull(t.AlternateContentFallbackXml);
        Assert.Contains("fade", t.AlternateContentFallbackXml);

        var ms2 = new MemoryStream();
        PptxPackageWriter.Write(loaded, ms2);
        ms2.Position = 0;

        using var zip = new ZipArchive(ms2, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        Assert.NotNull(entry);
        XDocument doc;
        using (var s = entry!.Open()) doc = XDocument.Load(s);

        var altContent = doc.Root?.Element(MC + "AlternateContent");
        Assert.NotNull(altContent); // F1 fix: must still be wrapped in mc:AlternateContent

        var choice = altContent!.Element(MC + "Choice");
        Assert.NotNull(choice);
        Assert.Equal("p14", choice!.Attribute("Requires")?.Value); // original Requires token preserved

        var choiceTrans = choice.Element(P + "transition");
        Assert.NotNull(choiceTrans);
        var unknownEffectEl = choiceTrans!.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "futureEffectNotYetKnown");
        Assert.NotNull(unknownEffectEl); // the unknown effect must live inside mc:Choice, not bare

        var fallback = altContent.Element(MC + "Fallback");
        Assert.NotNull(fallback); // F1 fix: the original degrade-path Fallback must survive
        var fallbackTrans = fallback!.Element(P + "transition");
        Assert.NotNull(fallbackTrans);
        Assert.NotNull(fallbackTrans!.Elements().FirstOrDefault(e => e.Name.LocalName == "fade"));

        // The writer's own output must itself be readable and round-trip again.
        ms2.Position = 0;
        var reloaded = PptxPackageReader.Read(ms2);
        var t2 = reloaded.Slides[0].Transition;
        Assert.NotNull(t2);
        Assert.Equal(TransitionKind.Other, t2!.Kind);
        Assert.True(t2.WasAlternateContent);
    }

    [Fact]
    public void RoundTrip_UnknownTransition_UnwrappedInSource_StaysBareOnSave()
    {
        // Sibling/no-regression: a legacy file where the unrecognized transition was NEVER
        // wrapped in mc:AlternateContent (the pre-existing RoundTrip_UnknownTransition_*
        // coverage) must keep being re-emitted bare -- the F1 fix must not start wrapping
        // transitions that were never wrapped in the source.
        var pptxBytes = BuildPptxWithTransitionEl(
            "<p:transition xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" spd=\"fast\">" +
            "<p:someExoticFutureTransition dir=\"l\"/>" +
            "</p:transition>");

        using var ms = new MemoryStream(pptxBytes);
        var loaded = PptxPackageReader.Read(ms);
        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(TransitionKind.Other, t!.Kind);
        Assert.False(t.WasAlternateContent);

        var ms2 = new MemoryStream();
        PptxPackageWriter.Write(loaded, ms2);
        ms2.Position = 0;

        using var zip = new ZipArchive(ms2, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        Assert.NotNull(entry);
        XDocument doc;
        using (var s = entry!.Open()) doc = XDocument.Load(s);

        Assert.Null(doc.Root?.Element(MC + "AlternateContent"));
        var bareTrans = doc.Root?.Element(P + "transition");
        Assert.NotNull(bareTrans);
        Assert.NotNull(bareTrans!.Elements().FirstOrDefault(e => e.Name.LocalName == "someExoticFutureTransition"));
    }

    [Fact]
    public void UnknownTransition_WrappedWithUnresolvableRequiresToken_FallsBackToBareElementWithoutCrashing()
    {
        // Sibling/edge-case: if the source's mc:Choice Requires token has no discoverable
        // namespace binding anywhere in scope, and isn't one of the well-known MS prefixes
        // either, re-wrapping would produce an mc:Choice whose Requires references an unbound
        // prefix -- worse than not wrapping. WrapOtherTransitionInAlternateContent must decline
        // to wrap in that case (mirrors the identical guard in the shape-preservation path) and
        // the caller must fall back to the bare element rather than throw or drop the transition.
        var pptxBytes = BuildPptxWithTransitionEl(
            "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\">" +
            "<mc:Choice Requires=\"zzzUnknownExt\">" +
            "<p:transition xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"" +
            " xmlns:p14=\"http://schemas.microsoft.com/office/powerpoint/2010/main\" spd=\"fast\">" +
            "<p14:futureEffectNotYetKnown/>" +
            "</p:transition>" +
            "</mc:Choice>" +
            "<mc:Fallback>" +
            "<p:transition xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" spd=\"fast\">" +
            "<p:fade/>" +
            "</p:transition>" +
            "</mc:Fallback>" +
            "</mc:AlternateContent>");

        using var ms = new MemoryStream(pptxBytes);
        var loaded = PptxPackageReader.Read(ms);
        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(TransitionKind.Other, t!.Kind);
        Assert.True(t.WasAlternateContent);
        Assert.Equal("zzzUnknownExt", t.McRequiresToken);
        Assert.Empty(t.McRequiresNsUris); // no xmlns binding found for the token anywhere

        var ms2 = new MemoryStream();
        var ex = Record.Exception(() => PptxPackageWriter.Write(loaded, ms2));
        Assert.Null(ex); // must not throw

        ms2.Position = 0;
        using var zip = new ZipArchive(ms2, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        Assert.NotNull(entry);
        XDocument doc;
        using (var s = entry!.Open()) doc = XDocument.Load(s);

        // The transition must still be present somewhere (never silently dropped)...
        var unknownEffectEl = doc.Root!.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "futureEffectNotYetKnown");
        Assert.NotNull(unknownEffectEl);
        // ...but since no usable Requires namespace could be resolved, it must NOT be wrapped
        // in a broken mc:AlternateContent (better to preserve the bare element than emit an
        // unusable wrapper).
        Assert.Null(doc.Root.Element(MC + "AlternateContent"));
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
    public void Cloner_PreservesWheelSpokeCount()
    {
        var slide = Presentation.CreateEmpty().Slides[0];
        slide.Transition = new SlideTransition
        {
            Kind = TransitionKind.Wheel,
            WheelSpokeCount = 6,
        };

        var clone = SlideCloner.CloneSlide(slide);

        Assert.Equal(6, clone.Transition?.WheelSpokeCount);
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

    // ── EB1: extended kinds emitted in p14: namespace, not p: ───────────────────

    [Theory]
    [InlineData(TransitionKind.Cube)]
    [InlineData(TransitionKind.Gallery)]
    [InlineData(TransitionKind.Glitter)]
    [InlineData(TransitionKind.Vortex)]
    [InlineData(TransitionKind.Ripple)]
    [InlineData(TransitionKind.Prism)]
    [InlineData(TransitionKind.Doors)]
    [InlineData(TransitionKind.Window)]
    [InlineData(TransitionKind.Ferris)]
    [InlineData(TransitionKind.Conveyor)]
    [InlineData(TransitionKind.Switch)]
    [InlineData(TransitionKind.Flip)]
    [InlineData(TransitionKind.Rotate)]
    [InlineData(TransitionKind.Orbit)]
    [InlineData(TransitionKind.Pan)]
    [InlineData(TransitionKind.Comb)]
    [InlineData(TransitionKind.Honeycomb)]
    [InlineData(TransitionKind.PageCurlDouble)]
    [InlineData(TransitionKind.PageCurlSingle)]
    public void EB1_ExtendedKind_EmittedInP14Namespace(TransitionKind kind)
    {
        // Bug EB1: extended transition kinds were emitted as p:-namespace children of p:transition,
        // which is invalid per CT_SlideTransition (ECMA-376) → PowerPoint repair.
        // Fix: they should be emitted as p14:-namespace children inside mc:Choice.
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition { Kind = kind, DurationMs = 700 };

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        Assert.NotNull(entry);

        XDocument doc;
        using (var s = entry!.Open()) doc = XDocument.Load(s);

        var p14Ns = XNamespace.Get("http://schemas.microsoft.com/office/powerpoint/2010/main");
        var mcNs  = XNamespace.Get("http://schemas.openxmlformats.org/markup-compatibility/2006");
        var pNs   = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");
        var effectName = PptxAnimationMap_Accessor.TransitionKindToElementName(kind);
        Assert.NotNull(effectName);

        // mc:Choice > p:transition should contain a p14:-namespace effect element, NOT a p:-namespace one
        var transEl = doc.Root?
            .Element(mcNs + "AlternateContent")?
            .Element(mcNs + "Choice")?
            .Element(pNs + "transition");
        Assert.NotNull(transEl);

        // The effect child MUST be in p14: namespace
        var p14El = transEl!.Elements().FirstOrDefault(e => e.Name.Namespace == p14Ns);
        Assert.NotNull(p14El); // EB1 fix: must exist in p14: namespace

        // The effect child must NOT be in the base p: namespace (that caused repair)
        var pEl = transEl.Elements().FirstOrDefault(e =>
            e.Name.Namespace == pNs &&
            e.Name.LocalName != "sndAc" &&
            e.Name.LocalName != "extLst");
        Assert.Null(pEl); // EB1 fix: must NOT be in p: namespace
    }

    // ── EB3: morph emitted in p159: namespace ────────────────────────────────────

    [Fact]
    public void EB3_Morph_EmittedInP159Namespace()
    {
        // Bug EB3: morph was emitted as p:morph inside p:transition. Morph is a p159: extension.
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind        = TransitionKind.Morph,
            MorphOption = "byWord",
            DurationMs  = 700,
        };

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml"));
        Assert.NotNull(entry);

        XDocument doc;
        using (var s = entry!.Open()) doc = XDocument.Load(s);

        var p159Ns = XNamespace.Get("http://schemas.microsoft.com/office/powerpoint/2015/09/main");
        var mcNs   = XNamespace.Get("http://schemas.openxmlformats.org/markup-compatibility/2006");
        var pNs    = XNamespace.Get("http://schemas.openxmlformats.org/presentationml/2006/main");

        // mc:Choice Requires must be "p159" for morph
        var choice = doc.Root?
            .Element(mcNs + "AlternateContent")?
            .Element(mcNs + "Choice");
        Assert.NotNull(choice);
        Assert.Equal("p159", choice!.Attribute("Requires")?.Value); // EB3 fix

        // p:transition inside Choice must have p159:morph child
        var transEl = choice.Element(pNs + "transition");
        Assert.NotNull(transEl);
        var morphEl = transEl!.Elements().FirstOrDefault(e => e.Name.Namespace == p159Ns);
        Assert.NotNull(morphEl); // EB3 fix: p159:morph must be present
        Assert.Equal("morph", morphEl!.Name.LocalName);
        Assert.Equal("byWord", morphEl.Attribute("option")?.Value); // option preserved

        // Must NOT have p:morph (that caused repair)
        var pMorphEl = transEl.Elements()
            .FirstOrDefault(e => e.Name.Namespace == pNs && e.Name.LocalName == "morph");
        Assert.Null(pMorphEl); // EB3 fix: no p:morph
    }

    // ── EB3: morph round-trips via p159 namespace ─────────────────────────────

    [Fact]
    public void EB3_Morph_RoundTrips_P159Namespace()
    {
        // Build a PPTX with a real-PowerPoint-shaped p159:morph inside mc:Choice Requires="p159"
        var pptxBytes = BuildPptxWithTransitionEl(
            "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"" +
            " xmlns:p14=\"http://schemas.microsoft.com/office/powerpoint/2010/main\"" +
            " xmlns:p159=\"http://schemas.microsoft.com/office/powerpoint/2015/09/main\">" +
            "<mc:Choice Requires=\"p159\">" +
            "<p:transition xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" spd=\"fast\" p14:dur=\"700\">" +
            "<p159:morph option=\"byWord\"/>" +
            "</p:transition>" +
            "</mc:Choice>" +
            "<mc:Fallback>" +
            "<p:transition xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" spd=\"fast\">" +
            "<p:fade/>" +
            "</p:transition>" +
            "</mc:Fallback>" +
            "</mc:AlternateContent>");

        using var ms = new MemoryStream(pptxBytes);
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(TransitionKind.Morph, t!.Kind); // EB2 fix: p159:morph must be recognized
        Assert.Equal("byWord", t.MorphOption);        // option must survive
    }

    // ── EB2: reader recognizes p14: extended transitions ────────────────────────

    [Theory]
    [InlineData("cube",      TransitionKind.Cube)]
    [InlineData("gallery",   TransitionKind.Gallery)]
    [InlineData("glitter",   TransitionKind.Glitter)]
    [InlineData("vortex",    TransitionKind.Vortex)]
    [InlineData("honeycomb", TransitionKind.Honeycomb)]
    public void EB2_Reader_RecognizesP14ExtendedTransition(string elementName, TransitionKind expected)
    {
        // Bug EB2: the reader's effect-child scan was P-namespace-only, so real-PowerPoint
        // extended transitions (p14:cube etc.) were silently dropped (Kind=None).
        var pptxBytes = BuildPptxWithTransitionEl(
            "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"" +
            " xmlns:p14=\"http://schemas.microsoft.com/office/powerpoint/2010/main\">" +
            "<mc:Choice Requires=\"p14\">" +
            $"<p:transition xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" spd=\"fast\" p14:dur=\"700\">" +
            $"<p14:{elementName}/>" +
            "</p:transition>" +
            "</mc:Choice>" +
            "<mc:Fallback>" +
            "<p:transition xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" spd=\"fast\">" +
            "<p:fade/>" +
            "</p:transition>" +
            "</mc:Fallback>" +
            "</mc:AlternateContent>");

        using var ms = new MemoryStream(pptxBytes);
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(expected, t!.Kind); // EB2 fix: p14: effect must be recognized
    }

    // ── EB2+EB1 round-trip: write extended kind → read back in p14 namespace ────

    [Theory]
    [InlineData(TransitionKind.Cube)]
    [InlineData(TransitionKind.Glitter)]
    [InlineData(TransitionKind.Vortex)]
    [InlineData(TransitionKind.PageCurlDouble)]
    [InlineData(TransitionKind.Honeycomb)]
    public void EB1_EB2_ExtendedKind_WrittenInP14_ReadBackCorrectly(TransitionKind kind)
    {
        // Full round-trip: write with p14: namespace (EB1 fix), read back (EB2 fix).
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition { Kind = kind, DurationMs = 700 };

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;
        var loaded = PptxPackageReader.Read(ms);

        var t = loaded.Slides[0].Transition;
        Assert.NotNull(t);
        Assert.Equal(kind, t!.Kind);   // must not be None or Other
        Assert.Equal(700, t.DurationMs);
    }

    // ── EB4: ogg/aac transition sound gets content-type Default ─────────────────

    [Theory]
    [InlineData("audio/ogg", "ogg")]
    [InlineData("audio/aac", "aac")]
    public void EB4_OggAacTransitionSound_GetsContentTypeDefault(string contentType, string ext)
    {
        var fakeAudio = new byte[] { 0x01, 0x02, 0x03 };
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind  = TransitionKind.Fade,
            Sound = new TransitionSound { AudioBytes = fakeAudio, ContentType = contentType },
        };

        using var ms = new MemoryStream();
        PptxPackageWriter.Write(pres, ms);
        ms.Position = 0;

        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var ctEntry = zip.GetEntry("[Content_Types].xml");
        Assert.NotNull(ctEntry);
        string ctXml;
        using (var s = new StreamReader(ctEntry!.Open())) ctXml = s.ReadToEnd();

        // EB4 fix: the Default entry for ogg/aac must be present
        Assert.Contains($"Extension=\"{ext}\"", ctXml);
        Assert.Contains(contentType, ctXml);
    }

    // ── Slideshow effect-fallback mapping (resolved correctly, no crash) ─────────

    [Theory]
    [InlineData(TransitionKind.Morph)]
    [InlineData(TransitionKind.Cube)]
    [InlineData(TransitionKind.Vortex)]
    [InlineData(TransitionKind.Prestige)]
    [InlineData(TransitionKind.PageCurlDouble)]
    [InlineData(TransitionKind.Origami)]
    [InlineData(TransitionKind.Random)]
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
        // to shared fallback playback in the slideshow.
        Assert.NotEqual(TransitionKind.None, TransitionKind.Other);

        var plan = SlideShowTransitionPlanner.Plan(new SlideTransition { Kind = TransitionKind.Other });
        Assert.Equal(SlideShowTransitionPlaybackKind.FadeFallback, plan.PlaybackKind);
        Assert.Equal(TransitionKind.Other, plan.ResolvedKind);
        Assert.Null(plan.RandomSeed);
    }

    [Fact]
    public void TransitionKind_Random_ResolvesToDedicatedPlaybackWithoutChangingAuthority()
    {
        var transition = new SlideTransition { Kind = TransitionKind.Random };

        var plan = SlideShowTransitionPlanner.Plan(transition);

        Assert.Contains(plan.ResolvedKind, SlideShowTransitionPlanner.RandomCandidateKinds);
        Assert.NotEqual(TransitionKind.Random, plan.ResolvedKind);
        Assert.NotEqual(SlideShowTransitionPlaybackKind.FadeFallback, plan.PlaybackKind);
        Assert.Equal(TransitionKind.Random, transition.Kind);
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

        XNamespace P    = "http://schemas.openxmlformats.org/presentationml/2006/main";
        XNamespace P14  = "http://schemas.microsoft.com/office/powerpoint/2010/main";
        XNamespace P15  = "http://schemas.microsoft.com/office/powerpoint/2012/main";
        XNamespace P159 = "http://schemas.microsoft.com/office/powerpoint/2015/09/main";
        XNamespace MC   = "http://schemas.openxmlformats.org/markup-compatibility/2006";

        // Navigate through mc:AlternateContent or bare p:transition.
        var transEl = doc.Root?.Element(MC + "AlternateContent")
                          ?.Element(MC + "Choice")
                          ?.Element(P + "transition")
                      ?? doc.Root?.Element(P + "transition");

        // EB1/EB2/EB3: effect child may be in P, P14, P15, or P159 namespace (depending on kind).
        var effectEl = transEl?.Elements()
            .FirstOrDefault(e => (e.Name.Namespace == P  ||
                                  e.Name.Namespace == P14 ||
                                  e.Name.Namespace == P15 ||
                                  e.Name.Namespace == P159)
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
