using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 157 (shared-image-codecs F2): GetContentTypeForFileNameOrExtension's
/// PresentationPictureInsertion branch had no "webp" case, so a WebP file selected via
/// FreeP's "Set Zoom Cover Image" picker (which explicitly offers *.webp) was
/// content-typed "image/png" and re-written into the .pptx as a .png-named part while
/// actually containing WebP bytes -- an invalid, spec-violating OPC part. Verifies the
/// added webp branch, alongside representative already-working sibling extensions
/// (jpeg/gif/bmp/svg) and the deliberately-unmapped "tiff" fallback that a pinned
/// contract test (MediaRenderUtilityPolicyTests.OpcMediaTypes_PreservesInsertionContentTypeProfiles)
/// locks to "image/png" for this specific profile.
/// </summary>
public sealed class OpcMediaTypesPresentationPictureWebpTests
{
    [Theory]
    [InlineData("cover.webp", "image/webp")]
    [InlineData("cover.WEBP", "image/webp")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("photo.gif", "image/gif")]
    [InlineData("photo.bmp", "image/bmp")]
    [InlineData("photo.svg", "image/svg+xml")]
    public void GetContentTypeForFileNameOrExtension_MapsWebpAndLeavesSiblingsUnaffected(
        string fileName,
        string expected)
    {
        OpcMediaTypes.GetContentTypeForFileNameOrExtension(
                fileName,
                OpcMediaContentTypeProfile.PresentationPictureInsertion)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void GetContentTypeForFileNameOrExtension_WebpRoundTripsThroughZoomCoverExtensionMapping()
    {
        // The write side (GetPresentationZoomCoverExtension) already special-cased
        // "image/webp" => "webp" before this fix; the bug was that the read/inference
        // side never produced "image/webp" in the first place. Assert the two paths
        // now agree end-to-end: infer -> extension round-trips back to "webp", not "png".
        var inferredContentType = OpcMediaTypes.GetContentTypeForFileNameOrExtension(
            "cover.webp",
            OpcMediaContentTypeProfile.PresentationPictureInsertion);

        inferredContentType.Should().Be("image/webp");

        OpcMediaTypes.GetMediaFileExtension(
                inferredContentType,
                OpcMediaExtensionProfile.PresentationZoomCoverImage)
            .Should()
            .Be("webp");
    }

    [Fact]
    public void GetContentTypeForFileNameOrExtension_TiffFallsBackToPngForThisProfile()
    {
        // Deliberately locked by MediaRenderUtilityPolicyTests (FreeP.App.Presentation.Tests):
        // [InlineData("photo.tiff", OpcMediaContentTypeProfile.PresentationPictureInsertion, "image/png")].
        // This is a distinct, already-covered contract -- left untouched by this fix.
        OpcMediaTypes.GetContentTypeForFileNameOrExtension(
                "photo.tiff",
                OpcMediaContentTypeProfile.PresentationPictureInsertion)
            .Should()
            .Be("image/png");
    }
}
