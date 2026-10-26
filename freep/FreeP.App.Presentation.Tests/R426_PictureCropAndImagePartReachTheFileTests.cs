using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r426: a picture's crop and its image bytes must survive a .pptx round trip.
///
/// <para>r413's shape sweep excluded <c>PictureFrameGeometry</c> as picture-only and covered it with
/// a dedicated test; the crop rectangle and the image part itself were never covered by either. A
/// crop is the case worth caring about: an author crops a photo to the part that matters, and a
/// dropped crop silently restores the whole original image -- including whatever they cropped OUT,
/// which may be exactly what they did not want in the deck.</para>
/// </summary>
public sealed class R426_PictureCropAndImagePartReachTheFileTests
{
    /// <summary>A minimal valid PNG, so the writer has real bytes to place in the package.</summary>
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    private static SlideShape RoundTrip(Action<SlideShape> configure)
    {
        var presentation = new Presentation();
        var slide = new Slide();
        var shape = new SlideShape
        {
            Id = 2,
            Name = "Pic",
            Kind = SlideShapeKind.Picture,
            OffsetXEmu = 100000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 1000000,
            ExtentCyEmu = 1000000,
            Picture = new ImagePart { Bytes = MinimalPng(), ContentType = "image/png" },
        };

        configure(shape);
        slide.Shapes.Add(shape);
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var reloaded = PptxPackageReader.Read(stream).Slides[0].Shapes.FirstOrDefault();
        reloaded.Should().NotBeNull("the picture shape must survive before its crop can be judged");
        return reloaded!;
    }

    [Fact]
    public void TheImageBytesAndContentTypeSurvive()
    {
        // The control for the crop cases: if the image part itself did not round-trip, a crop
        // comparison would be describing a picture that is not there.
        var shape = RoundTrip(_ => { });

        shape.Picture.Should().NotBeNull("a picture shape without its image renders as an empty frame");
        shape.Picture!.Bytes.Should().Equal(MinimalPng(), "the image must come back byte-identical");
        shape.Picture.ContentType.Should().Be("image/png");
    }

    [Fact]
    public void AllFourCropEdgesSurvive()
    {
        // Four DIFFERENT values, deliberately: a writer that emitted one edge for all four, or
        // transposed left and right, passes a test using symmetric crops and fails this one.
        var shape = RoundTrip(picture =>
        {
            picture.PictureFormat = new PictureFormat { CropLeft = 0.10, CropTop = 0.20, CropRight = 0.30, CropBottom = 0.40 };
        });

        shape.PictureFormat!.CropLeft.Should().BeApproximately(0.10, 1e-6, "a lost left crop reveals what the author removed");
        shape.PictureFormat!.CropTop.Should().BeApproximately(0.20, 1e-6);
        shape.PictureFormat!.CropRight.Should().BeApproximately(0.30, 1e-6);
        shape.PictureFormat!.CropBottom.Should().BeApproximately(0.40, 1e-6);
    }

    [Fact]
    public void AOneSidedCropSurvives()
    {
        // The common real case -- an author trims one edge. A writer that only emits the crop
        // element when every edge is set would drop this entirely while passing the test above.
        var shape = RoundTrip(picture => picture.PictureFormat = new PictureFormat { CropBottom = 0.25 });

        shape.PictureFormat!.CropBottom.Should().BeApproximately(0.25, 1e-6, "a single trimmed edge must persist on its own");
        shape.PictureFormat!.CropLeft.Should().Be(0, "the untouched edges must stay untouched");
        shape.PictureFormat!.CropTop.Should().Be(0);
        shape.PictureFormat!.CropRight.Should().Be(0);
    }

    [Fact]
    public void AnUncroppedPictureGainsNoCrop()
    {
        // Every assertion above checks that something set survives, so a reader that invented a crop
        // would satisfy them all. An invented crop is the worst failure of the set: it would HIDE
        // part of the author's image rather than reveal it.
        var shape = RoundTrip(_ => { });

        // Either representation of "no crop" is correct: the model documents a null PictureFormat as
        // meaning no crop, and an all-zero one says the same thing. Asserting only the null form
        // would fail on a legitimate writer choice rather than on a defect.
        var cropped = shape.PictureFormat is { } format &&
                      (format.CropLeft != 0 || format.CropTop != 0 ||
                       format.CropRight != 0 || format.CropBottom != 0);

        cropped.Should().BeFalse("an uncropped picture must not acquire a crop, which would HIDE part of the author's image");
    }
}
