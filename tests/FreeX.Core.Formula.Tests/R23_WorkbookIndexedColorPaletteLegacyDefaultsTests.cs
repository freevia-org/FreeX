using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Locks in Excel's true legacy default-palette colors for indexed colors 4 and 5.
/// The legacy palette predates Office brand colors: ColorIndex 4 is pure green (#00FF00)
/// and ColorIndex 5 is pure blue (#0000FF) — NOT the modern Office "Standard Colors"
/// #00B050 / #0070C0. Same wrong-value class already fixed/guarded for the [Green]/[Blue]
/// named number-format directives in NumberFormatColorMapperTests.cs.
/// </summary>
public sealed class R23_WorkbookIndexedColorPaletteLegacyDefaultsTests
{
    [Fact]
    public void TryGetDefaultColor_Index4_IsLegacyPureGreen_NotOfficeBrandGreen()
    {
        var found = WorkbookIndexedColorPalette.TryGetDefaultColor(4, out var color);

        found.Should().BeTrue();
        color.Should().Be(new CellColor(0x00, 0xFF, 0x00), because: "Excel's legacy ColorIndex 4 default is pure green #00FF00");
        color.Should().NotBe(new CellColor(0x00, 0xB0, 0x50), because: "ColorIndex 4 must not be the modern Office brand green #00B050");
    }

    [Fact]
    public void TryGetDefaultColor_Index5_IsLegacyPureBlue_NotOfficeBrandBlue()
    {
        var found = WorkbookIndexedColorPalette.TryGetDefaultColor(5, out var color);

        found.Should().BeTrue();
        color.Should().Be(new CellColor(0x00, 0x00, 0xFF), because: "Excel's legacy ColorIndex 5 default is pure blue #0000FF");
        color.Should().NotBe(new CellColor(0x00, 0x70, 0xC0), because: "ColorIndex 5 must not be the modern Office brand blue #0070C0");
    }
}
