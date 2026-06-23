using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Unit tests for the pure logic helpers on <see cref="PrintSettingsPlanner"/>:
/// scale-index mappings, sides-mode mappings, copies clamping, and page-range validation.
/// </summary>
public sealed class PrintSettingsPlannerTests
{
    // ─── ScaleIndexToScaleToFit ──────────────────────────────────────────────

    [Theory]
    [InlineData(0)] // No Scaling
    [InlineData(99)] // out-of-range → treated as No Scaling
    public void ScaleIndexToScaleToFit_DefaultForUnrecognisedIndex(int index)
    {
        PrintSettingsPlanner.ScaleIndexToScaleToFit(index).Should().Be(WorksheetScaleToFit.Default);
    }

    [Fact]
    public void ScaleIndexToScaleToFit_Index1_FitsSheetOnOnePage()
    {
        var result = PrintSettingsPlanner.ScaleIndexToScaleToFit(1);

        result.FitToPagesWide.Should().Be(1);
        result.FitToPagesTall.Should().Be(1);
        result.ScalePercent.Should().BeNull();
    }

    [Fact]
    public void ScaleIndexToScaleToFit_Index2_FitsAllColumnsOnOnePage()
    {
        var result = PrintSettingsPlanner.ScaleIndexToScaleToFit(2);

        result.FitToPagesWide.Should().Be(1);
        result.FitToPagesTall.Should().BeNull();
        result.ScalePercent.Should().BeNull();
    }

    [Fact]
    public void ScaleIndexToScaleToFit_Index3_FitsAllRowsOnOnePage()
    {
        var result = PrintSettingsPlanner.ScaleIndexToScaleToFit(3);

        result.FitToPagesWide.Should().BeNull();
        result.FitToPagesTall.Should().Be(1);
        result.ScalePercent.Should().BeNull();
    }

    // ─── ScaleToFitToIndex ───────────────────────────────────────────────────

    [Fact]
    public void ScaleToFitToIndex_Default_ReturnsZero()
    {
        PrintSettingsPlanner.ScaleToFitToIndex(WorksheetScaleToFit.Default).Should().Be(0);
    }

    [Fact]
    public void ScaleToFitToIndex_FitSheet_ReturnsOne()
    {
        PrintSettingsPlanner.ScaleToFitToIndex(new WorksheetScaleToFit(null, 1, 1)).Should().Be(1);
    }

    [Fact]
    public void ScaleToFitToIndex_FitColumns_ReturnsTwo()
    {
        PrintSettingsPlanner.ScaleToFitToIndex(new WorksheetScaleToFit(null, 1, null)).Should().Be(2);
    }

    [Fact]
    public void ScaleToFitToIndex_FitRows_ReturnsThree()
    {
        PrintSettingsPlanner.ScaleToFitToIndex(new WorksheetScaleToFit(null, null, 1)).Should().Be(3);
    }

    [Fact]
    public void ScaleIndexRoundTrip_IsSymmetric()
    {
        for (var i = 0; i <= 3; i++)
        {
            var stf = PrintSettingsPlanner.ScaleIndexToScaleToFit(i);
            PrintSettingsPlanner.ScaleToFitToIndex(stf).Should().Be(i, $"index {i} should round-trip");
        }
    }

    // ─── SidesIndexToMode ────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, PrintPreviewSidesMode.OneSided)]
    [InlineData(1, PrintPreviewSidesMode.TwoSidedLongEdge)]
    [InlineData(2, PrintPreviewSidesMode.TwoSidedShortEdge)]
    [InlineData(99, PrintPreviewSidesMode.OneSided)] // out-of-range → OneSided
    public void SidesIndexToMode_MapsAllKnownIndices(int index, PrintPreviewSidesMode expected)
    {
        PrintSettingsPlanner.SidesIndexToMode(index).Should().Be(expected);
    }

    // ─── SidesModeToIndex ────────────────────────────────────────────────────

    [Theory]
    [InlineData(PrintPreviewSidesMode.OneSided, 0)]
    [InlineData(PrintPreviewSidesMode.TwoSidedLongEdge, 1)]
    [InlineData(PrintPreviewSidesMode.TwoSidedShortEdge, 2)]
    public void SidesModeToIndex_MapsAllModes(PrintPreviewSidesMode mode, int expected)
    {
        PrintSettingsPlanner.SidesModeToIndex(mode).Should().Be(expected);
    }

    [Fact]
    public void SidesRoundTrip_IsSymmetric()
    {
        foreach (var mode in Enum.GetValues<PrintPreviewSidesMode>())
        {
            var index = PrintSettingsPlanner.SidesModeToIndex(mode);
            PrintSettingsPlanner.SidesIndexToMode(index).Should().Be(mode, $"mode {mode} should round-trip");
        }
    }

    // ─── ClampCopies ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1)]    // below minimum → clamp to 1
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(500, 500)]
    [InlineData(999, 999)]
    [InlineData(1000, 999)] // above maximum → clamp to 999
    [InlineData(int.MaxValue, 999)]
    public void ClampCopies_ClampsToValidRange(int input, int expected)
    {
        PrintSettingsPlanner.ClampCopies(input).Should().Be(expected);
    }

    // ─── TryValidatePageRange ────────────────────────────────────────────────

    [Fact]
    public void TryValidatePageRange_BothNull_ReturnsTrueWithFullRange()
    {
        var result = PrintSettingsPlanner.TryValidatePageRange(null, null, 5, out var from, out var to);

        result.Should().BeTrue();
        from.Should().Be(1);
        to.Should().Be(5);
    }

    [Fact]
    public void TryValidatePageRange_ValidRange_ReturnsTrueWithNormalisedValues()
    {
        var result = PrintSettingsPlanner.TryValidatePageRange(2, 4, 5, out var from, out var to);

        result.Should().BeTrue();
        from.Should().Be(2);
        to.Should().Be(4);
    }

    [Fact]
    public void TryValidatePageRange_FromOnlyProvided_ResolvesToLastPage()
    {
        var result = PrintSettingsPlanner.TryValidatePageRange(3, null, 5, out var from, out var to);

        result.Should().BeTrue();
        from.Should().Be(3);
        to.Should().Be(5);
    }

    [Fact]
    public void TryValidatePageRange_ToOnlyProvided_ResolvesFromFirst()
    {
        var result = PrintSettingsPlanner.TryValidatePageRange(null, 3, 5, out var from, out var to);

        result.Should().BeTrue();
        from.Should().Be(1);
        to.Should().Be(3);
    }

    [Theory]
    [InlineData(0, 3, 5)]   // from < 1
    [InlineData(3, 0, 5)]   // to < 1
    [InlineData(4, 3, 5)]   // from > to
    [InlineData(6, 8, 5)]   // both exceed totalPages
    [InlineData(1, 6, 5)]   // to exceeds totalPages
    public void TryValidatePageRange_InvalidRange_ReturnsFalse(int? from, int? to, int total)
    {
        var result = PrintSettingsPlanner.TryValidatePageRange(from, to, total, out _, out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryValidatePageRange_SinglePage_IsValid()
    {
        var result = PrintSettingsPlanner.TryValidatePageRange(3, 3, 5, out var from, out var to);

        result.Should().BeTrue();
        from.Should().Be(3);
        to.Should().Be(3);
    }

    [Fact]
    public void TryValidatePageRange_ZeroTotalPages_BothNullStillReturnsTrue()
    {
        // When totalPages = 0, Math.Max(1, 0) = 1, so both null = all pages (1..1)
        var result = PrintSettingsPlanner.TryValidatePageRange(null, null, 0, out var from, out var to);

        result.Should().BeTrue();
        from.Should().Be(1);
        to.Should().Be(1);
    }
}
