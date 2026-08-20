using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r157 remediation, widened in r158 into a guard for the CLASS of function, not the two profiles
/// the r157 fixer happened to be looking at.
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
/// Round 157's guard only enumerated <see cref="OpcMediaExtensionProfile.PresentationZoomCoverImage"/>
/// and <see cref="OpcMediaExtensionProfile.PresentationSmartArtImage"/> -- both reached through
/// <see cref="OpcMediaTypes.GetMediaFileExtension"/>. It missed the separate function
/// <see cref="OpcMediaTypes.GetDrawingMediaExtension"/>, which has the identical shape (content type
/// in, part extension out) and the identical missing-webp bug, and which is the function
/// FreeP.Core.IO.PptxPackageWriter actually uses to name parts for ordinary Picture shapes, media
/// poster images, picture fills, bullet images, and layout/master placeholders -- the single most
/// common picture-insertion path in FreeP, not a corner case like SmartArt.
/// </para>
///
/// <para>
/// This class enumerates every function in <see cref="OpcMediaTypes"/> whose job is "derive an
/// image PART EXTENSION from a content type" -- found by grepping the file for that shape, not by
/// listing the ones already known to be involved:
/// <list type="bullet">
/// <item>GetMediaFileExtension(profile: PresentationZoomCoverImage) -- delegates to
/// GetPresentationZoomCoverExtension</item>
/// <item>GetMediaFileExtension(profile: PresentationSmartArtImage) -- delegates to
/// GetPresentationSmartArtExtension</item>
/// <item>GetDrawingMediaExtension -- the function this round's bug lives in</item>
/// <item>GetImageExtension -- the function XlsxPackagePath uses for FreeX/Excel picture parts</item>
/// </list>
/// (GetTemporaryPlaybackExtension, GetPackageTransitionSoundExtension,
/// GetPresentationPackageMediaExtension and GetAudioVideoExtension are the same shape but for
/// audio/video content types, a disjoint format space from the webp defect class this guard exists
/// for, so they are out of scope here.)
/// </para>
///
/// <para>
/// A test naming webp would only have caught this one value. These assert the INVARIANT: for every
/// function of this shape, and every content type that function's real caller can actually produce,
/// the extension chosen for it must describe the same format as the content type. Adding a format to
/// one function without its siblings now fails here rather than in a package a user opens.
/// </para>
/// </summary>
public sealed class R157_OpcImageExtensionAgreesWithContentTypeTests
{
    // The three functions that all consume a content type produced by
    // OpcMediaContentTypeProfile.PresentationPictureInsertion -- FreeP's ordinary Insert Picture
    // content-type inference (SlideObjectInsertionPlanner.InferPictureContentType). GetDrawingMediaExtension
    // is the one this round's bug lives in; the other two are the round-157 profiles, kept here so a
    // future regression in any of the three is caught by the same matrix.
    private static readonly (string Name, Func<string, string> ChooseExtension)[] PresentationInsertionChoosers =
    [
        (
            nameof(OpcMediaExtensionProfile.PresentationZoomCoverImage),
            contentType => OpcMediaTypes.GetMediaFileExtension(
                contentType, OpcMediaExtensionProfile.PresentationZoomCoverImage)
        ),
        (
            nameof(OpcMediaExtensionProfile.PresentationSmartArtImage),
            contentType => OpcMediaTypes.GetMediaFileExtension(
                contentType, OpcMediaExtensionProfile.PresentationSmartArtImage)
        ),
        (
            nameof(OpcMediaTypes.GetDrawingMediaExtension),
            OpcMediaTypes.GetDrawingMediaExtension
        ),
    ];

    private static readonly Dictionary<string, Func<string, string>> PresentationInsertionChoosersByName =
        PresentationInsertionChoosers.ToDictionary(chooser => chooser.Name, chooser => chooser.ChooseExtension);

    // The formats PresentationPictureInsertion inference can actually produce, each with the
    // extension that describes the same bytes. "png" is the fallback and is covered by the
    // round-trip below.
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
    public void EveryPresentationInsertionChooser_ChoosesAnExtensionThatMatchesTheInferredContentType(
        string chooserName, string sourceExtension, string expectedContentType)
    {
        var chooseExtension = PresentationInsertionChoosersByName[chooserName];

        var inferred = OpcMediaTypes.GetContentTypeForFileNameOrExtension(
            "picture." + sourceExtension,
            OpcMediaContentTypeProfile.PresentationPictureInsertion);

        inferred.Should().Be(
            expectedContentType,
            "inference is the half that was fixed; if this drifts the rest of the assertion is moot");

        var chosenExtension = chooseExtension(inferred);

        var roundTripped = OpcMediaTypes.GetContentTypeForFileNameOrExtension(
            "picture." + chosenExtension,
            OpcMediaContentTypeProfile.PresentationPictureInsertion);

        roundTripped.Should().Be(
            inferred,
            $"{chooserName} names the part from the extension while the writer declares it from the "
            + "content type, so the two must describe the same format -- otherwise the written "
            + "package is internally inconsistent, which is worse than a part that is merely "
            + "mislabelled");
    }

    public static TheoryData<string, string, string> ProfileFormatMatrix()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (chooserName, _) in PresentationInsertionChoosers)
        {
            foreach (var (extension, contentType) in KnownImageFormats)
                data.Add(chooserName, extension, contentType);
        }

        return data;
    }

    // GetImageExtension is the fourth function of this shape, but it is not fed by
    // PresentationPictureInsertion -- it backs XlsxPackagePath.GetImageExtension, which FreeX/Excel
    // picture insertion feeds from InsertPictureCommandFactory.ContentTypeForPath
    // (src/FreeX.App.Services/InsertPictureCommandFactory.cs). That pipeline's domain is png, jpg,
    // jpeg, gif, bmp, webp, tif and tiff -- no svg, since Excel picture insertion never produces an
    // svg content type. So this function's round trip is exercised against ITS OWN real domain,
    // using OpcMediaTypes.GetImageContentType (an independent, extension-driven oracle) rather than
    // the PresentationPictureInsertion profile used above.
    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("gif", "image/gif")]
    [InlineData("bmp", "image/bmp")]
    [InlineData("webp", "image/webp")]
    [InlineData("tif", "image/tiff")]
    [InlineData("tiff", "image/tiff")]
    public void GetImageExtension_ChoosesAnExtensionThatMatchesTheExcelInsertionContentType(
        string sourceExtension, string contentType)
    {
        OpcMediaTypes.GetImageContentType("picture." + sourceExtension).Should().Be(
            contentType,
            "the InlineData table itself must describe matching bytes before it can be used as an oracle");

        var chosenExtension = OpcMediaTypes.GetImageExtension(contentType);

        var roundTripped = OpcMediaTypes.GetImageContentType("picture." + chosenExtension);

        roundTripped.Should().Be(
            contentType,
            "XlsxPackagePath names the picture part from this extension while the picture's stored "
            + "ContentType (from InsertPictureCommandFactory) is written to [Content_Types].xml, so "
            + "the two must describe the same format");
    }

    // Documents, rather than silently omits, the one place GetImageExtension's domain is narrower
    // than GetDrawingMediaExtension's: svg. This is not a live bug -- FreeX's Excel picture
    // insertion pipeline (InsertPictureCommandFactory.ContentTypeForPath) has no ".svg" case, so
    // GetImageExtension is never actually called with "image/svg+xml" in production. If that ever
    // changes, this test is the tripwire: it fails the moment svg becomes a reachable content type
    // without GetImageExtension also being taught it.
    [Fact]
    public void GetImageExtension_SvgIsAnUnreachableInputByDesign_NotASilentGap()
    {
        // FreeX.App.Services.InsertPictureCommandFactory.ContentTypeForPath (the only production
        // source of a picture ContentType feeding GetImageExtension via XlsxPackagePath) maps
        // ".png"/".jpg"/".jpeg"/".gif"/".bmp"/".webp"/".tif"/".tiff" and returns null for anything
        // else, including ".svg" -- so no InsertPictureCommand it builds can ever carry an
        // "image/svg+xml" ContentType. FreeX.Core.IO.Tests cannot reference FreeX.App.Services to
        // assert that directly, so this test instead pins the fallback GetImageExtension takes for
        // svg today: if svg ever becomes reachable without this function being taught it, the
        // PresentationInsertionChooser matrix above (GetDrawingMediaExtension already knows svg)
        // is the shape this test exists to make someone reconcile.
        OpcMediaTypes.GetImageExtension("image/svg+xml").Should().Be(
            "png",
            "svg falls back to png here today; that is only safe while Excel picture insertion "
            + "never produces an image/svg+xml content type");
    }
}
