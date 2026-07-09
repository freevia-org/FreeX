using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-15 regression tests for <see cref="StructuredTableStyleBandingResolver"/>.
/// </summary>
public sealed class R15_tablestyle_Tests
{
    // R15-table-slicer-styles-2: themed Light styles (Light16-21 under a non-Office theme) must have
    // no interior border, matching the fixed-palette Light path ("Light has no interior borders").
    // Before the fix, CreateThemedLightBanding always set Border = theme.ResolveColor(...), so a
    // themed Light style got spurious four-sided interior gridlines that a fixed-palette Light style
    // never has.
    [Fact]
    public void Resolve_ThemedLight16_UnderNonOfficeTheme_HasNoInteriorBorder()
    {
        var customTheme = WorkbookTheme.Office.WithName("Custom");

        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleLight16", customTheme);

        banding.Border.Should().BeNull();
    }

    // R15-table-slicer-styles-1: the Dark family previously had only 6 accent tuples (Light/Medium
    // have 7) with no progressive tint offset, so Dark7 cycled back to the exact same accent tuple as
    // Dark1 and rendered byte-identical (header, odd row, and even row fills all equal). Excel's
    // built-in Dark7-11 styles are visually distinct from Dark1-5.
    [Fact]
    public void Resolve_Dark7_DiffersFromDark1()
    {
        var dark1 = StructuredTableStyleBandingResolver.Resolve("TableStyleDark1", WorkbookTheme.Office);
        var dark7 = StructuredTableStyleBandingResolver.Resolve("TableStyleDark7", WorkbookTheme.Office);

        dark7.HeaderFill.Should().NotBe(dark1.HeaderFill);
    }
}
