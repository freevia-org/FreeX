using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r560: a shape's custom geometry carries its own connection sites (<c>a:custGeom/a:cxnLst</c>),
/// and each site's X and Y are GUIDE TOKENS taken verbatim from the file.
/// <c>ConnectionSiteHelper.TryResolveGeometryCoordinate</c> parsed them with
/// <c>double.TryParse</c> and no finite check, so "1e400" resolved to Infinity and the site
/// landed at <c>(long)Math.Round(Infinity)</c> -- long.MaxValue EMU. Any connector attached to
/// that site is then drawn to an absurd coordinate.
///
/// <para>This is r546's saturating-cast class arriving in a coordinate rather than an attribute:
/// the cast is to long, which is the right width for EMU, so nothing overflows the TYPE -- what
/// is wrong is the value reaching the cast at all. The remedy is the resolver's own contract: a
/// token it cannot resolve returns false, and the caller already falls back to the shape's
/// bounding box.</para>
/// </summary>
public sealed class R560_ConnectionSiteCoordinatesAreFiniteTests
{
    private static SlideShape ShapeWithAuthoredSite(string x, string y)
    {
        var shape = new SlideShape
        {
            Id = 11,
            Kind = SlideShapeKind.AutoShape,
            OffsetXEmu = 100_000,
            OffsetYEmu = 200_000,
            ExtentCxEmu = 900_000,
            ExtentCyEmu = 400_000,
        };
        shape.CustomGeometry.Add(new CustomGeometryPath { PathW = 1000, PathH = 1000 });
        shape.CustomConnectionSites.Add(new CustomGeometryConnectionSite { X = x, Y = y });
        return shape;
    }

    [Theory]
    [InlineData("1e400", "500")]
    [InlineData("500", "1e400")]
    [InlineData("Infinity", "500")]
    [InlineData("NaN", "500")]
    [InlineData("500", "NaN")]
    public void An_unusable_authored_site_falls_back_instead_of_resolving_to_a_saturated_coordinate(
        string x,
        string y)
    {
        var shape = ShapeWithAuthoredSite(x, y);

        var (siteX, siteY) = ConnectionSiteHelper.Resolve(shape, 0);

        // The fallback is the shape's own bounding box, so a resolved site must sit within the
        // shape rather than at the far end of the coordinate space.
        siteX.Should().BeInRange(shape.OffsetXEmu, shape.OffsetXEmu + shape.ExtentCxEmu);
        siteY.Should().BeInRange(shape.OffsetYEmu, shape.OffsetYEmu + shape.ExtentCyEmu);
    }

    [Fact]
    public void An_ordinary_authored_site_is_still_resolved_from_the_geometry()
    {
        // Non-vacuity: a guard that rejected every token would satisfy the range assertions above
        // by always falling back to the bbox, silently ignoring authored connection sites. A site
        // at 500/1000 of the path maps to the centre of the shape.
        var shape = ShapeWithAuthoredSite("500", "500");

        var (siteX, siteY) = ConnectionSiteHelper.Resolve(shape, 0);

        siteX.Should().Be(shape.OffsetXEmu + shape.ExtentCxEmu / 2);
        siteY.Should().Be(shape.OffsetYEmu + shape.ExtentCyEmu / 2);
    }

    [Fact]
    public void An_unrecognized_guide_token_still_falls_back_as_it_always_did()
    {
        // Pins the pre-existing contract the fix reuses, so the two paths cannot be separated.
        var shape = ShapeWithAuthoredSite("someGuideName", "500");

        var (siteX, siteY) = ConnectionSiteHelper.Resolve(shape, 0);

        siteX.Should().BeInRange(shape.OffsetXEmu, shape.OffsetXEmu + shape.ExtentCxEmu);
        siteY.Should().BeInRange(shape.OffsetYEmu, shape.OffsetYEmu + shape.ExtentCyEmu);
    }
}
