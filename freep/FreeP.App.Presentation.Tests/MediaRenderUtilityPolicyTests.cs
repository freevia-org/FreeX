using Free.Shared.Drawing;
using Free.Shared.Opc;

namespace FreeP.App.Compositor.Tests;

public sealed class MediaRenderUtilityPolicyTests
{
    [Theory]
    [InlineData(" ##0a10fF ", RgbColorTextProfile.DrawingMl, true, 0x0A, 0x10, 0xFF)]
    [InlineData(" #0a10fF ", RgbColorTextProfile.TrimmedHashOrBare, true, 0x0A, 0x10, 0xFF)]
    [InlineData("##0a10fF", RgbColorTextProfile.TrimmedHashOrBare, false, 0, 0, 0)]
    [InlineData(" FF00 ", RgbColorTextProfile.PlaybackSixCharacter, true, 0x00, 0xFF, 0x00)]
    [InlineData("0a10fF", RgbColorTextProfile.CaptionPayload, true, 0x0A, 0x10, 0xFF)]
    [InlineData("#0a10fF", RgbColorTextProfile.CaptionPayload, false, 0, 0, 0)]
    [InlineData("0xabc", RgbColorTextProfile.FlexibleInk, true, 0xAA, 0xBB, 0xCC)]
    [InlineData("#80A1B2C3", RgbColorTextProfile.FlexibleInk, true, 0xA1, 0xB2, 0xC3)]
    public void RgbColorTextCodec_PreservesEachAuthoredGrammar(
        string text,
        RgbColorTextProfile profile,
        bool expectedSuccess,
        int red,
        int green,
        int blue)
    {
        var success = RgbColorTextCodec.TryParse(text, profile, out var color);

        success.Should().Be(expectedSuccess);
        if (expectedSuccess)
            color.Should().Be(new DrawingMlRgbColor((byte)red, (byte)green, (byte)blue));
    }

    [Theory]
    [InlineData("video/mp4", OpcMediaExtensionProfile.EmbeddedPlayback, true, ".mp4")]
    [InlineData("audio/x-flac", OpcMediaExtensionProfile.EmbeddedPlayback, true, ".bin")]
    [InlineData("audio/x-flac", OpcMediaExtensionProfile.TransitionSound, true, ".flac")]
    [InlineData("audio/unknown", OpcMediaExtensionProfile.TransitionSound, true, ".mp3")]
    [InlineData(" video/mp4 ", OpcMediaExtensionProfile.PackageAudioVideo, false, "mp4")]
    [InlineData(" video/mp4 ", OpcMediaExtensionProfile.EmbeddedPlayback, false, "bin")]
    public void OpcMediaTypes_UsesExplicitExtensionProfiles(
        string contentType,
        OpcMediaExtensionProfile profile,
        bool includeDot,
        string expected) =>
        OpcMediaTypes.GetMediaFileExtension(contentType, profile, includeDot)
            .Should()
            .Be(expected);

    [Theory]
    [InlineData("photo.jpeg", OpcMediaContentTypeProfile.PresentationPictureInsertion, "image/jpeg")]
    [InlineData("photo.tiff", OpcMediaContentTypeProfile.PresentationPictureInsertion, "image/png")]
    [InlineData("movie.m4v", OpcMediaContentTypeProfile.PresentationVideoInsertion, "video/x-m4v")]
    [InlineData("sound.m4a", OpcMediaContentTypeProfile.PresentationAudioInsertion, "audio/mp4")]
    [InlineData("unknown", OpcMediaContentTypeProfile.PresentationAudioInsertion, "audio/mpeg")]
    public void OpcMediaTypes_PreservesInsertionContentTypeProfiles(
        string input,
        OpcMediaContentTypeProfile profile,
        string expected) =>
        OpcMediaTypes.GetContentTypeForFileNameOrExtension(input, profile)
            .Should()
            .Be(expected);

    [Fact]
    public void RenderersAndWorkflows_DelegateMediaAndColorPoliciesToSharedOwners()
    {
        var wpfCanvas = Read("freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");
        var avaloniaCanvas = Read("freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs");
        var wpfMedia = Read("freep", "FreeP.App.Host", "SlideShowMediaController.cs");
        var avaloniaMedia = Read("freep", "FreeP.App.Avalonia", "AvaloniaSlideShowMediaController.cs");
        var transitionSound = Read("freep", "FreeP.App.Host", "TransitionSoundTempFile.cs");
        var insertion = Read("freep", "FreeP.App.Presentation", "SlideObjectInsertionPlanner.cs");

        foreach (var canvas in new[] { wpfCanvas, avaloniaCanvas })
        {
            canvas.Should().Contain("plan.FrameCornerRadiusDip");
            canvas.Should().Contain("plan.MediaPlayGlyph");
        }

        foreach (var media in new[] { wpfMedia, avaloniaMedia })
        {
            media.Should().Contain("RgbColorTextCodec.TryParse");
            media.Should().Contain("RgbColorTextProfile.CaptionPayload");
            media.Should().NotContain("Color.Parse(\"#\" + colorHex)");
            media.Should().NotContain("ColorConverter.ConvertFromString(\"#\" + colorHex)");
        }

        wpfMedia.Should().Contain("OpcMediaExtensionProfile.EmbeddedPlayback");
        wpfMedia.Should().NotContain("contentType.ToLowerInvariant() switch");
        transitionSound.Should().Contain("OpcMediaExtensionProfile.TransitionSound");
        transitionSound.Should().NotContain("contentType?.ToLowerInvariant() switch");
        insertion.Should().Contain("OpcMediaTypes.GetContentTypeForFileNameOrExtension");
        insertion.Should().NotContain("\"jpg\" or \"jpeg\"");
        insertion.Should().NotContain("\"m4v\" => \"video/x-m4v\"");
    }

    [Fact]
    public void AuthoredRgbConsumers_UseTheSharedCodecWithExplicitProfiles()
    {
        var consumers = new[]
        {
            (Read("freep", "FreeP.App.Presentation", "SlideCompositor.cs"), RgbColorTextProfile.DrawingMl),
            (Read("freep", "FreeP.App.Presentation", "Ribbon", "FreePRibbonCommandWorkflow.cs"), RgbColorTextProfile.TrimmedHashOrBare),
            (Read("freep", "FreeP.App.Presentation", "ChartPointOptionsPlanner.cs"), RgbColorTextProfile.TrimmedHashOrBare),
            (Read("freep", "FreeP.App.Presentation", "SlideShowInkRenderPlanner.cs"), RgbColorTextProfile.FlexibleInk),
            (Read("freep", "FreeP.App.Presentation", "SlideShowPlaybackPlanner.cs"), RgbColorTextProfile.PlaybackSixCharacter),
            (Read("freep", "FreeP.Core.IO", "PresentationPdfExporter.cs"), RgbColorTextProfile.FlexibleInk),
            (Read("freep", "FreeP.Core.IO", "PptxPackageReader.cs"), RgbColorTextProfile.DrawingMl),
        };

        foreach (var (source, profile) in consumers)
        {
            source.Should().Contain("RgbColorTextCodec.TryParse");
            source.Should().Contain($"RgbColorTextProfile.{profile}");
        }
    }

    private static string Read(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
