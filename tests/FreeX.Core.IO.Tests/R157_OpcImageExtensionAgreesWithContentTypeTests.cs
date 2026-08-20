using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r157 remediation, and a guard for the class rather than the instance.
///
/// <para>
/// Round 157 taught the content-type inference about WebP so a .webp picture stopped being
/// labelled image/png. The extension mapper for SmartArt pictures was not taught the same thing,
/// and the result was worse than the bug being fixed: PptxPackageWriter names a SmartArt picture
/// part from the extension mapper while writing its [Content_Types].xml Override from the stored
/// content type, so the package gained a part called "picture1.png" declared as image/webp --
/// internally inconsistent and spec-violating, where before it was merely mislabelled and
/// self-consistent.
/// </para>
///
/// <para>
/// A test naming webp would only have caught this one value. These assert the INVARIANT: for every
/// image profile, an inferred content type and the extension chosen for it must describe the same
/// format. Adding a format to one side without the other now fails here rather than in a package a
/// user opens.
/// </para>
/// </summary>
public sealed class R157_OpcImageExtensionAgreesWithContentTypeTests
{
    private static readonly OpcMediaExtensionProfile[] ImageProfiles =
    [
        OpcMediaExtensionProfile.PresentationZoomCoverImage,
        OpcMediaExtensionProfile.PresentationSmartArtImage,
    ];

    // The formats the insertion inference can actually produce, each with the extension that
    // describes the same bytes. "png" is the fallback and is covered by the round-trip below.
    private static readonly (string Extension, string ContentType)[] KnownImageFormats =
    [
        ("jpg", "image/jpeg"),
        ("jpeg", "image/jpeg"),
        ("gif", "image/gif"),
        ("bmp", "image/bmp"),
        ("svg", "image/svg+xml"),
        ("webp", "image/webp"),
        ("png", "image/png"),
    ];

    [Theory]
    [MemberData(nameof(ProfileFormatMatrix))]
    public void EveryImageProfile_ChoosesAnExtensionThatMatchesTheInferredContentType(
        OpcMediaExtensionProfile profile, string sourceExtension, string expectedContentType)
    {
        var inferred = OpcMediaTypes.GetContentTypeForFileNameOrExtension(
            "picture." + sourceExtension,
            OpcMediaContentTypeProfile.PresentationPictureInsertion);

        inferred.Should().Be(
            expectedContentType,
            "inference is the half that was fixed; if this drifts the rest of the assertion is moot");

        var chosenExtension = OpcMediaTypes.GetMediaFileExtension(inferred, profile);

        var roundTripped = OpcMediaTypes.GetContentTypeForFileNameOrExtension(
            "picture." + chosenExtension,
            OpcMediaContentTypeProfile.PresentationPictureInsertion);

        roundTripped.Should().Be(
            inferred,
            "the part is NAMED from the extension and DECLARED from the content type, so the two "
            + "must describe the same format -- otherwise the written package is internally "
            + "inconsistent, which is worse than a part that is merely mislabelled");
    }

    public static TheoryData<OpcMediaExtensionProfile, string, string> ProfileFormatMatrix()
    {
        var data = new TheoryData<OpcMediaExtensionProfile, string, string>();
        foreach (var profile in ImageProfiles)
        {
            foreach (var (extension, contentType) in KnownImageFormats)
                data.Add(profile, extension, contentType);
        }

        return data;
    }
}
