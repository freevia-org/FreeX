using Free.Shared.Opc;

namespace FreeP.App.Compositor.Tests;

public sealed class OpcMediaCatalogAdoptionTests
{
    [Theory]
    [InlineData(" Video/MP4 ", OpcMediaExtensionProfile.TemporaryPlaybackMaterialization, true, null, ".mp4")]
    [InlineData("video/x-ms-wmv", OpcMediaExtensionProfile.TemporaryPlaybackMaterialization, true, null, ".bin")]
    [InlineData("audio/x-wav", OpcMediaExtensionProfile.PackageTransitionSound, false, null, "mp3")]
    [InlineData("Audio/Wav", OpcMediaExtensionProfile.PackageTransitionSound, false, null, "mp3")]
    [InlineData("video/x-ms-wmv", OpcMediaExtensionProfile.PresentationPackageMediaPart, false, null, "wmv")]
    [InlineData("VIDEO/X-MS-WMV", OpcMediaExtensionProfile.PresentationPackageMediaPart, false, null, "mp4")]
    [InlineData("audio/ogg", OpcMediaExtensionProfile.PresentationPackageMediaPart, false, null, "mp4")]
    [InlineData("image/webp", OpcMediaExtensionProfile.PresentationZoomCoverImage, true, null, ".webp")]
    [InlineData("IMAGE/JPEG", OpcMediaExtensionProfile.PresentationZoomCoverImage, true, null, ".png")]
    [InlineData("IMAGE/JPEG", OpcMediaExtensionProfile.PresentationSmartArtImage, false, null, "jpg")]
    [InlineData(" image/jpeg ", OpcMediaExtensionProfile.PresentationSmartArtImage, false, null, "png")]
    [InlineData("application/ttaf+xml", OpcMediaExtensionProfile.PresentationCaptionTrack, false, null, "ttml")]
    [InlineData("unknown", OpcMediaExtensionProfile.PresentationCaptionTrack, false, "https://example.test/captions.DFXP?download=1#track", "dfxp")]
    [InlineData("unknown", OpcMediaExtensionProfile.PresentationCaptionTrack, false, null, "vtt")]
    public void ExtensionProfiles_PreserveCallerSpecificAliasesAndFallbacks(
        string contentType,
        OpcMediaExtensionProfile profile,
        bool includeDot,
        string? fallbackSource,
        string expected) =>
        OpcMediaTypes.GetMediaFileExtension(
                contentType,
                profile,
                includeDot,
                fallbackSource)
            .Should()
            .Be(expected);

    [Theory]
    [InlineData("photo.TIFF", OpcMediaContentTypeProfile.ExternalXamlPicture, "image/tiff")]
    [InlineData("photo.svg", OpcMediaContentTypeProfile.ExternalXamlPicture, "image/png")]
    [InlineData("bullet.WMF", OpcMediaContentTypeProfile.PresentationListGalleryPicture, "image/x-wmf")]
    [InlineData("bullet.tiff", OpcMediaContentTypeProfile.PresentationListGalleryPicture, "image/png")]
    [InlineData("https://example.test/captions.DFXP?download=1", OpcMediaContentTypeProfile.PresentationCaptionTrack, "application/ttml+xml")]
    [InlineData("SRT", OpcMediaContentTypeProfile.PresentationCaptionTrack, "application/x-subrip")]
    [InlineData("unknown", OpcMediaContentTypeProfile.PresentationCaptionTrack, "")]
    [InlineData("report.XLS", OpcMediaContentTypeProfile.OfficeEmbeddedObjectInsertion, "application/vnd.ms-excel")]
    [InlineData("payload.bin", OpcMediaContentTypeProfile.OfficeEmbeddedObjectInsertion, "application/octet-stream")]
    [InlineData("payload.BIN", OpcMediaContentTypeProfile.OfficeEmbeddedObjectPackageRead, "application/vnd.ms-office.activeX+xml")]
    [InlineData("legacy.xls", OpcMediaContentTypeProfile.OfficeEmbeddedObjectPackageRead, "application/octet-stream")]
    public void ContentTypeProfiles_PreserveImageCaptionAndOleContracts(
        string input,
        OpcMediaContentTypeProfile profile,
        string expected) =>
        OpcMediaTypes.GetContentTypeForFileNameOrExtension(input, profile)
            .Should()
            .Be(expected);

    [Theory]
    [InlineData("ppt/media/caption.VTT?download=1#english", "vtt")]
    [InlineData("ppt/media/movie.MP4", "mp4")]
    [InlineData("ppt/media/no-extension", "")]
    [InlineData("", "")]
    public void SourceExtension_StripsQueryAndFragmentWithoutChangingCasePolicy(
        string source,
        string expected) =>
        OpcMediaTypes.GetSourceExtension(source).Should().Be(expected);

    [Fact]
    public void FreePMediaConsumers_DelegateCatalogOwnershipToOpcMediaTypes()
    {
        var mediaPlayback = Read("freep", "FreeP.App.Media", "MediaPlaybackContracts.cs");
        var commands = Read("freep", "FreeP.Core.Model", "PresentationCommands.cs");
        var smartArtInsertion = Read("freep", "FreeP.App.Presentation", "SmartArtInsertionFactory.cs");
        var smartArtEditing = Read("freep", "FreeP.App.Presentation", "SmartArtEditingPlanner.cs");
        var externalXaml = Read("freep", "FreeP.App.Presentation", "ExternalXamlClipboardPlanner.cs");
        var listGallery = Read("freep", "FreeP.App.Presentation", "PresentationListGalleryPlanner.cs");
        var oleInsertion = Read("freep", "FreeP.App.Presentation", "OleInsertionPlanner.cs");
        var packageReader = Read("freep", "FreeP.Core.IO", "PptxPackageReader.cs");
        var packageWriter = Read("freep", "FreeP.Core.IO", "PptxPackageWriter.cs");

        mediaPlayback.Should().Contain("OpcMediaExtensionProfile.TemporaryPlaybackMaterialization");
        mediaPlayback.Should().NotContain("MediaContentTypeExtensions");
        commands.Should().Contain("OpcMediaExtensionProfile.PresentationZoomCoverImage");
        commands.Should().NotContain("private static string ExtensionFor");
        smartArtInsertion.Should().Contain("OpcMediaExtensionProfile.PresentationSmartArtImage");
        smartArtEditing.Should().Contain("OpcMediaExtensionProfile.PresentationSmartArtImage");
        externalXaml.Should().Contain("OpcMediaContentTypeProfile.ExternalXamlPicture");
        listGallery.Should().Contain("OpcMediaContentTypeProfile.PresentationListGalleryPicture");
        oleInsertion.Should().Contain("OpcMediaContentTypeProfile.OfficeEmbeddedObjectInsertion");
        packageReader.Should().Contain("OpcMediaContentTypeProfile.OfficeEmbeddedObjectPackageRead");
        packageReader.Should().Contain("OpcMediaContentTypeProfile.PresentationCaptionTrack");
        packageReader.Should().NotContain("OleExtensionToContentType");
        packageWriter.Should().Contain("OpcMediaExtensionProfile.PackageTransitionSound");
        packageWriter.Should().Contain("OpcMediaExtensionProfile.PresentationPackageMediaPart");
        packageWriter.Should().Contain("OpcMediaTypes.GetCaptionTrackExtension");
        packageWriter.Should().Contain("OpcMediaContentTypeProfile.PresentationCaptionTrack");
        packageWriter.Should().NotContain("private static string GetCaptionTrackExtension");
        packageWriter.Should().NotContain("private static string GetAudioVideoExtension");
        packageWriter.Should().NotContain("var ext = media.ContentType switch");
    }

    private static string Read(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
