using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// freep-media F2 remediation -- the audio counterpart of
/// <see cref="R157_OpcImageExtensionAgreesWithContentTypeTests"/>, which fixed the same defect
/// CLASS for pictures and explicitly scoped audio/video out ("a disjoint format space ... out of
/// scope here").
///
/// <para>
/// FreeP's Insert-Audio file picker offers *.aac, *.ogg, *.flac directly
/// (freep/FreeP.App.Presentation/PresentationMediaFileTypeCatalog.cs AudioFilePatterns), but
/// <see cref="OpcMediaTypes.GetContentTypeForFileNameOrExtension"/> under the
/// <see cref="OpcMediaContentTypeProfile.PresentationAudioInsertion"/> profile only recognized
/// mp3/m4a/wav/wma and mislabelled every other extension -- including these three -- as
/// "audio/mpeg". Two independent consumers of that one wrong tag then broke:
/// </para>
/// <list type="bullet">
/// <item>Playback: SlideShowMediaController names the temp file handed to the media player from
/// <see cref="OpcMediaExtensionProfile.TemporaryPlaybackMaterialization"/>, which maps
/// "audio/mpeg" to ".mp3" -- a ".mp3" file containing real FLAC/OGG/AAC bytes fails to decode.</item>
/// <item>Save: PptxPackageWriter.WriteSlideMediaFiles names the on-disk part from
/// <see cref="OpcMediaExtensionProfile.PresentationPackageMediaPart"/>, which (before this fix)
/// had no aac/ogg/flac cases at all and fell back to its "mp4" default -- so even once the
/// content type is corrected, an uncorrected part-naming function would embed FLAC/OGG/AAC bytes
/// under a ".mp4" name declared "video/mp4" by the [Content_Types].xml Default entry for "mp4"
/// (PptxPackageWriter.BuildContentTypesXml, mediaExtensions -&gt; TryGetPackageDefaultContentType)
/// -- an audio part mislabelled as video, worse than the pre-fix "audio/mpeg" mislabel.</item>
/// <item>Reopen: PptxPackageReader re-derives MediaInfo.ContentType for a media shape from the
/// saved part's extension via <see cref="OpcMediaTypes.GetAudioVideoContentType"/>, which (before
/// this fix) recognized the same mp4/mov/avi/wmv/mp3/m4a/wav/wma set and nothing else -- so even
/// with the writer fixed, reopening a presentation containing a correctly-saved .aac/.ogg/.flac
/// part would relabel it "video/mp4" on the very next open, moving the bug from insert-time to
/// reopen-time instead of fixing it.</item>
/// </list>
/// <para>
/// This class asserts the round-trip INVARIANT across every function of the "content type in,
/// real-world extension out" shape that the Insert-Audio save path actually uses: the extension
/// chosen for the saved package part, and the Default content type PptxPackageWriter would
/// register for that extension, must both describe the SAME format the user picked -- not just
/// that the content-type inference alone was corrected.
/// </para>
/// </summary>
public sealed class R169_OpcAudioExtensionAgreesWithContentTypeTests
{
    // The three previously-unhandled formats the Insert-Audio picker advertises but the content
    // type inference and package-part naming did not recognize.
    private static readonly (string Extension, string ContentType)[] PreviouslyUnhandledAudioFormats =
    [
        ("aac", "audio/aac"),
        ("ogg", "audio/ogg"),
        ("flac", "audio/flac"),
    ];

    // The formats that already worked before this fix -- must keep working identically.
    private static readonly (string Extension, string ContentType)[] AlreadyHandledAudioFormats =
    [
        ("mp3", "audio/mpeg"),
        ("m4a", "audio/mp4"),
        ("wav", "audio/wav"),
        ("wma", "audio/x-ms-wma"),
    ];

    [Theory]
    [MemberData(nameof(PreviouslyUnhandledMatrix))]
    public void InsertAudio_PreviouslyUnhandledFormat_InfersTheCorrectContentType(
        string sourceExtension, string expectedContentType)
    {
        var inferred = OpcMediaTypes.GetContentTypeForFileNameOrExtension(
            "clip." + sourceExtension,
            OpcMediaContentTypeProfile.PresentationAudioInsertion);

        inferred.Should().Be(
            expectedContentType,
            "the Insert-Audio picker offers this extension directly, so it must not fall through " +
            "to the audio/mpeg default meant for genuinely unrecognized files");
    }

    [Theory]
    [MemberData(nameof(PreviouslyUnhandledMatrix))]
    public void InsertAudio_PreviouslyUnhandledFormat_SavedPackagePartAgreesWithContentType(
        string sourceExtension, string expectedContentType)
    {
        var inferred = OpcMediaTypes.GetContentTypeForFileNameOrExtension(
            "clip." + sourceExtension,
            OpcMediaContentTypeProfile.PresentationAudioInsertion);
        inferred.Should().Be(expectedContentType, "inference is asserted separately above");

        // This is exactly what PptxPackageWriter.WriteSlideMediaFiles calls to name the on-disk
        // part, and what it calls (on the same content type) to populate the mediaExtensions set
        // that drives BuildContentTypesXml's Default entries.
        var chosenExtension = OpcMediaTypes.GetMediaFileExtension(
            inferred,
            OpcMediaExtensionProfile.PresentationPackageMediaPart);

        chosenExtension.Should().Be(
            sourceExtension,
            "the saved part must keep the source format's own extension -- falling back to the " +
            "profile's \"mp4\" default here means real FLAC/OGG/AAC bytes get named as an mp4 " +
            "part in the saved .pptx");

        // PptxPackageWriter.BuildContentTypesXml only emits a [Content_Types].xml Default entry
        // for an extension if this succeeds (TryGetPackageDefaultContentType); if it fails the
        // part is written with NO content-type declaration at all -- an invalid OOXML package.
        OpcMediaTypes.TryGetDefaultContentType(chosenExtension, out var declaredContentType)
            .Should().BeTrue(
                "every extension WriteSlideMediaFiles can choose must have a registered package " +
                "Default content type, or the saved part is declared nowhere in [Content_Types].xml");

        declaredContentType.Should().Be(
            inferred,
            "the part is named from the extension but the writer's own Default-content-type table " +
            "must still describe the same format the user actually inserted");

        // Playback (SlideShowMediaController.TempMediaFileWriter) must independently agree too --
        // this was already correct before this fix, but the invariant only holds if all three
        // functions stay in lock-step, so it is asserted here rather than assumed.
        var playbackExtension = OpcMediaTypes.GetMediaFileExtension(
            inferred,
            OpcMediaExtensionProfile.TemporaryPlaybackMaterialization);

        playbackExtension.TrimStart('.').Should().Be(
            sourceExtension,
            "the slideshow temp file extension must match the real bytes or Media Foundation " +
            "fails to decode the very clip the user just inserted");

        // Round trip through the READER too: PptxPackageReader (line ~4906/4936) re-derives
        // MediaInfo.ContentType from the saved part's extension via GetAudioVideoContentType when
        // it re-opens the saved .pptx. If that function does not also know this extension, FreeP
        // would mislabel its own correctly-saved audio on the very next open -- moving this same
        // bug from "insert" time to "reopen" time instead of actually fixing it.
        var reopenedPath = "ppt/media/slide1_video1." + chosenExtension;
        OpcMediaTypes.GetAudioVideoContentType(reopenedPath).Should().Be(
            inferred,
            "PptxPackageReader must read back the same content type PptxPackageWriter saved, or " +
            "reopening a presentation with this audio format degrades it every time it is opened");
    }

    [Theory]
    [MemberData(nameof(AlreadyHandledMatrix))]
    public void InsertAudio_AlreadyHandledFormat_StillRoundTripsUnchanged(
        string sourceExtension, string expectedContentType)
    {
        var inferred = OpcMediaTypes.GetContentTypeForFileNameOrExtension(
            "clip." + sourceExtension,
            OpcMediaContentTypeProfile.PresentationAudioInsertion);
        inferred.Should().Be(expectedContentType);

        var chosenExtension = OpcMediaTypes.GetMediaFileExtension(
            inferred,
            OpcMediaExtensionProfile.PresentationPackageMediaPart);
        chosenExtension.Should().Be(sourceExtension);

        OpcMediaTypes.TryGetDefaultContentType(chosenExtension, out var declaredContentType)
            .Should().BeTrue();
        declaredContentType.Should().Be(inferred);

        OpcMediaTypes.GetAudioVideoContentType("ppt/media/slide1_video1." + chosenExtension)
            .Should().Be(inferred);
    }

    [Fact]
    public void InsertAudio_GenuinelyUnrecognizedExtension_StillFallsBackToMpegAndMp4()
    {
        // A true unknown must still hit the documented fallback constants for each profile --
        // this fix narrows the set of inputs that fall back, it does not remove the fallback.
        OpcMediaTypes.GetContentTypeForFileNameOrExtension(
                "clip.xyz", OpcMediaContentTypeProfile.PresentationAudioInsertion)
            .Should().Be("audio/mpeg");

        OpcMediaTypes.GetMediaFileExtension(
                "audio/x-made-up", OpcMediaExtensionProfile.PresentationPackageMediaPart)
            .Should().Be("mp4");
    }

    public static TheoryData<string, string> PreviouslyUnhandledMatrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var (extension, contentType) in PreviouslyUnhandledAudioFormats)
            data.Add(extension, contentType);
        return data;
    }

    public static TheoryData<string, string> AlreadyHandledMatrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var (extension, contentType) in AlreadyHandledAudioFormats)
            data.Add(extension, contentType);
        return data;
    }
}
