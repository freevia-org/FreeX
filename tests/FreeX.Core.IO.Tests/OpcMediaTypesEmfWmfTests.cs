using FluentAssertions;
using Free.Shared.Opc;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 31 (R31-io-drawing-anchor-deep-1): GetImageContentType/GetImageExtension had no
/// emf/wmf branch, so an EMF/WMF picture was content-typed image/png and re-written as
/// .png with EMF/WMF bytes (corrupt) when it round-tripped through the authored writer
/// (e.g. via Duplicate Sheet). Verifies the added emf/wmf branches, alongside a
/// representative already-working sibling extension/content-type (png/jpg).
/// </summary>
public sealed class OpcMediaTypesEmfWmfTests
{
    [Theory]
    [InlineData("xl/media/image1.emf", "image/x-emf")]
    [InlineData("xl/media/image1.EMF", "image/x-emf")]
    [InlineData("xl/media/image1.wmf", "image/x-wmf")]
    [InlineData("xl/media/image1.WMF", "image/x-wmf")]
    [InlineData("xl/media/image1.png", "image/png")]
    [InlineData("xl/media/image1.jpg", "image/jpeg")]
    public void GetImageContentType_MapsEmfWmfAndLeavesSiblingsUnaffected(string path, string expected)
    {
        OpcMediaTypes.GetImageContentType(path).Should().Be(expected);
    }

    [Theory]
    [InlineData("image/x-emf", "emf")]
    [InlineData("IMAGE/X-EMF", "emf")]
    [InlineData("image/emf", "emf")]
    [InlineData("image/x-wmf", "wmf")]
    [InlineData("IMAGE/X-WMF", "wmf")]
    [InlineData("image/wmf", "wmf")]
    [InlineData("image/png", "png")]
    [InlineData("image/jpeg", "jpg")]
    public void GetImageExtension_MapsEmfWmfAndLeavesSiblingsUnaffected(string contentType, string expected)
    {
        OpcMediaTypes.GetImageExtension(contentType).Should().Be(expected);
    }
}
