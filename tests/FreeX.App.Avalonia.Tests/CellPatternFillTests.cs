using Avalonia.Media;
using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for <see cref="CellPatternFill"/> — the pure pattern-fill brush builder that mirrors
/// WPF's <c>DrawFillPattern</c>.  No UI thread required for the logic tests; Avalonia types are
/// constructed in-process without a running application (brushes and drawing primitives are
/// data-only in Avalonia 12).
/// </summary>
public sealed class CellPatternFillTests
{
    private static readonly WorkbookTheme DefaultTheme = WorkbookTheme.Office;

    // ── NeedsPatternBrush gate ────────────────────────────────────────────────────────────────────

    [Fact]
    public void NeedsPatternBrush_NullStyle_ReturnsFalse()
    {
        CellPatternFill.NeedsPatternBrush(null).Should().BeFalse();
    }

    [Fact]
    public void NeedsPatternBrush_None_ReturnsFalse()
    {
        var style = new CellStyle { FillPatternStyle = CellFillPatternStyle.None };
        CellPatternFill.NeedsPatternBrush(style).Should().BeFalse();
    }

    [Fact]
    public void NeedsPatternBrush_Solid_ReturnsFalse()
    {
        var style = new CellStyle { FillPatternStyle = CellFillPatternStyle.Solid };
        CellPatternFill.NeedsPatternBrush(style).Should().BeFalse();
    }

    [Theory]
    [InlineData(CellFillPatternStyle.Gray0625)]
    [InlineData(CellFillPatternStyle.Gray125)]
    [InlineData(CellFillPatternStyle.LightGray)]
    [InlineData(CellFillPatternStyle.MediumGray)]
    [InlineData(CellFillPatternStyle.DarkGray)]
    [InlineData(CellFillPatternStyle.LightHorizontal)]
    [InlineData(CellFillPatternStyle.DarkHorizontal)]
    [InlineData(CellFillPatternStyle.LightVertical)]
    [InlineData(CellFillPatternStyle.DarkVertical)]
    [InlineData(CellFillPatternStyle.LightDown)]
    [InlineData(CellFillPatternStyle.DarkDown)]
    [InlineData(CellFillPatternStyle.LightUp)]
    [InlineData(CellFillPatternStyle.DarkUp)]
    [InlineData(CellFillPatternStyle.LightGrid)]
    [InlineData(CellFillPatternStyle.DarkGrid)]
    [InlineData(CellFillPatternStyle.LightTrellis)]
    [InlineData(CellFillPatternStyle.DarkTrellis)]
    public void NeedsPatternBrush_AllVisiblePatterns_ReturnsTrue(CellFillPatternStyle patternStyle)
    {
        var style = new CellStyle { FillPatternStyle = patternStyle };
        CellPatternFill.NeedsPatternBrush(style).Should().BeTrue();
    }

    // ── Gray pattern: IsGrayPattern ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CellFillPatternStyle.Gray0625,   true)]
    [InlineData(CellFillPatternStyle.Gray125,    true)]
    [InlineData(CellFillPatternStyle.LightGray,  true)]
    [InlineData(CellFillPatternStyle.MediumGray, true)]
    [InlineData(CellFillPatternStyle.DarkGray,   true)]
    [InlineData(CellFillPatternStyle.LightHorizontal, false)]
    [InlineData(CellFillPatternStyle.DarkGrid,   false)]
    [InlineData(CellFillPatternStyle.LightTrellis, false)]
    public void IsGrayPattern_ReturnsExpected(CellFillPatternStyle style, bool expected)
    {
        CellPatternFill.IsGrayPattern(style).Should().Be(expected);
    }

    // ── Gray opacity table ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CellFillPatternStyle.Gray0625,   0.12)]
    [InlineData(CellFillPatternStyle.Gray125,    0.18)]
    [InlineData(CellFillPatternStyle.LightGray,  0.28)]
    [InlineData(CellFillPatternStyle.MediumGray, 0.45)]
    [InlineData(CellFillPatternStyle.DarkGray,   0.62)]
    public void GrayPatternOpacity_MatchesWpfTable(CellFillPatternStyle style, double expected)
    {
        CellPatternFill.GrayPatternOpacity(style).Should().BeApproximately(expected, precision: 0.001);
    }

    [Fact]
    public void GrayPatternOpacity_NonGrayPattern_ReturnsZero()
    {
        CellPatternFill.GrayPatternOpacity(CellFillPatternStyle.LightHorizontal).Should().Be(0.0);
    }

    // ── Build: null / None → returns null ────────────────────────────────────────────────────────

    [Fact]
    public void Build_NullStyle_ReturnsNull()
    {
        CellPatternFill.Build(null, DefaultTheme).Should().BeNull();
    }

    [Fact]
    public void Build_NonePattern_ReturnsNull()
    {
        var style = new CellStyle { FillPatternStyle = CellFillPatternStyle.None };
        CellPatternFill.Build(style, DefaultTheme).Should().BeNull();
    }

    [Fact]
    public void Build_SolidPattern_ReturnsNull()
    {
        var style = new CellStyle { FillPatternStyle = CellFillPatternStyle.Solid };
        CellPatternFill.Build(style, DefaultTheme).Should().BeNull();
    }

    // ── Build: gray patterns → SolidColorBrush with correct opacity ──────────────────────────────

    [Theory]
    [InlineData(CellFillPatternStyle.Gray0625,   0.12)]
    [InlineData(CellFillPatternStyle.Gray125,    0.18)]
    [InlineData(CellFillPatternStyle.LightGray,  0.28)]
    [InlineData(CellFillPatternStyle.MediumGray, 0.45)]
    [InlineData(CellFillPatternStyle.DarkGray,   0.62)]
    public void Build_GrayPattern_ReturnsSolidColorBrushWithCorrectOpacity(
        CellFillPatternStyle patternStyle, double expectedOpacity)
    {
        var style = new CellStyle
        {
            FillPatternStyle = patternStyle,
            FillPatternColor = CellColor.Black,
        };

        var brush = CellPatternFill.Build(style, DefaultTheme);

        brush.Should().BeOfType<SolidColorBrush>();
        var solid = (SolidColorBrush)brush!;
        solid.Opacity.Should().BeApproximately(expectedOpacity, precision: 0.001);
    }

    [Fact]
    public void Build_GrayPattern_UsesPatternForegroundColor()
    {
        var style = new CellStyle
        {
            FillPatternStyle = CellFillPatternStyle.MediumGray,
            FillPatternColor = new CellColor(255, 0, 0),  // red foreground
        };

        var brush = (SolidColorBrush)CellPatternFill.Build(style, DefaultTheme)!;

        brush.Color.R.Should().Be(255);
        brush.Color.G.Should().Be(0);
        brush.Color.B.Should().Be(0);
    }

    // ── Build: hatch patterns → DrawingBrush ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(CellFillPatternStyle.LightHorizontal)]
    [InlineData(CellFillPatternStyle.DarkHorizontal)]
    [InlineData(CellFillPatternStyle.LightVertical)]
    [InlineData(CellFillPatternStyle.DarkVertical)]
    [InlineData(CellFillPatternStyle.LightGrid)]
    [InlineData(CellFillPatternStyle.DarkGrid)]
    [InlineData(CellFillPatternStyle.LightDown)]
    [InlineData(CellFillPatternStyle.DarkDown)]
    [InlineData(CellFillPatternStyle.LightUp)]
    [InlineData(CellFillPatternStyle.DarkUp)]
    [InlineData(CellFillPatternStyle.LightTrellis)]
    [InlineData(CellFillPatternStyle.DarkTrellis)]
    public void Build_HatchPattern_ReturnsDrawingBrush(CellFillPatternStyle patternStyle)
    {
        var style = new CellStyle { FillPatternStyle = patternStyle };
        var brush = CellPatternFill.Build(style, DefaultTheme);
        brush.Should().BeOfType<DrawingBrush>();
    }

    [Theory]
    [InlineData(CellFillPatternStyle.LightHorizontal)]
    [InlineData(CellFillPatternStyle.DarkHorizontal)]
    [InlineData(CellFillPatternStyle.LightVertical)]
    [InlineData(CellFillPatternStyle.DarkVertical)]
    [InlineData(CellFillPatternStyle.LightGrid)]
    [InlineData(CellFillPatternStyle.DarkGrid)]
    public void Build_LineHatchPatterns_UseTileModeTile(CellFillPatternStyle patternStyle)
    {
        var style = new CellStyle { FillPatternStyle = patternStyle };
        var brush = (DrawingBrush)CellPatternFill.Build(style, DefaultTheme)!;
        brush.TileMode.Should().Be(TileMode.Tile);
    }

    [Theory]
    [InlineData(CellFillPatternStyle.LightDown)]
    [InlineData(CellFillPatternStyle.DarkDown)]
    [InlineData(CellFillPatternStyle.LightUp)]
    [InlineData(CellFillPatternStyle.DarkUp)]
    [InlineData(CellFillPatternStyle.LightTrellis)]
    [InlineData(CellFillPatternStyle.DarkTrellis)]
    public void Build_DiagonalHatchPatterns_UseTileModeTile(CellFillPatternStyle patternStyle)
    {
        var style = new CellStyle { FillPatternStyle = patternStyle };
        var brush = (DrawingBrush)CellPatternFill.Build(style, DefaultTheme)!;
        brush.TileMode.Should().Be(TileMode.Tile);
    }

    [Fact]
    public void Build_GridPattern_HasTwoGeometryDrawings()
    {
        // LightGrid = horizontal + vertical → two line drawings in the DrawingGroup.
        var style = new CellStyle { FillPatternStyle = CellFillPatternStyle.LightGrid };
        var brush = (DrawingBrush)CellPatternFill.Build(style, DefaultTheme)!;
        var group = (DrawingGroup)brush.Drawing!;
        group.Children.Should().HaveCount(2);
    }

    [Fact]
    public void Build_TrellisPattern_HasTwoGeometryDrawings()
    {
        // LightTrellis = descending + ascending diagonal → two line drawings.
        var style = new CellStyle { FillPatternStyle = CellFillPatternStyle.LightTrellis };
        var brush = (DrawingBrush)CellPatternFill.Build(style, DefaultTheme)!;
        var group = (DrawingGroup)brush.Drawing!;
        group.Children.Should().HaveCount(2);
    }

    [Fact]
    public void Build_HorizontalPattern_HasOneGeometryDrawing()
    {
        var style = new CellStyle { FillPatternStyle = CellFillPatternStyle.LightHorizontal };
        var brush = (DrawingBrush)CellPatternFill.Build(style, DefaultTheme)!;
        var group = (DrawingGroup)brush.Drawing!;
        group.Children.Should().HaveCount(1);
    }

    [Fact]
    public void Build_DefaultPatternColor_IsBlack()
    {
        // When FillPatternColor is null, default is CellColor.Black.
        var style = new CellStyle { FillPatternStyle = CellFillPatternStyle.LightHorizontal };
        var brush = (DrawingBrush)CellPatternFill.Build(style, DefaultTheme)!;
        var group = (DrawingGroup)brush.Drawing!;
        var drawing = (GeometryDrawing)group.Children[0];
        var pen = drawing.Pen!;
        var solidBrush = (SolidColorBrush)pen.Brush!;
        solidBrush.Color.R.Should().Be(0);
        solidBrush.Color.G.Should().Be(0);
        solidBrush.Color.B.Should().Be(0);
    }
}

/// <summary>
/// Tests for the HAlign=Fill detection / cell-style alignment constants in the Avalonia renderer.
/// These verify the mapping logic used in <c>MapCellTextAlignment</c> indirectly via the model enum,
/// and the fill-alignment detection that guards text repetition.
/// </summary>
public sealed class CellAlignmentFillTests
{
    // ── CellHAlign.Fill is correctly defined in the model enum ───────────────────────────────────

    [Fact]
    public void HorizontalAlignment_Fill_IsDefinedInModel()
    {
        // Avalonia renderer uses: CellHAlign = FreeX.Core.Model.HorizontalAlignment
        // Should be distinct from General, Left, Center, Right, Justify, Distributed.
        var allValues = Enum.GetValues<HorizontalAlignment>();
        allValues.Should().Contain(HorizontalAlignment.Fill);
        allValues.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void HorizontalAlignment_Fill_IsNotGeneral()
    {
        HorizontalAlignment.Fill.Should().NotBe(HorizontalAlignment.General);
    }

    [Fact]
    public void HorizontalAlignment_Fill_IsNotLeft()
    {
        HorizontalAlignment.Fill.Should().NotBe(HorizontalAlignment.Left);
    }

    // ── CellStyle correctly stores and returns Fill alignment ─────────────────────────────────────

    [Fact]
    public void CellStyle_StoringFillAlignment_RoundTrips()
    {
        var style = new CellStyle { HorizontalAlignment = HorizontalAlignment.Fill };
        style.HorizontalAlignment.Should().Be(HorizontalAlignment.Fill);
    }

    [Fact]
    public void CellStyle_DefaultAlignment_IsGeneral()
    {
        var style = new CellStyle();
        style.HorizontalAlignment.Should().Be(HorizontalAlignment.General);
    }
}
