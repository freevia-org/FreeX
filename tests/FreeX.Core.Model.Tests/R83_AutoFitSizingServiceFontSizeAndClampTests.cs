using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R83-commands-rowcol-size-5-2/5-3: AutoFit Row Height must scale a cell's own line height by its
/// FontSize (not just react to WrapText/TextRotation), and the internal max-height clamp must match
/// Excel's real 409.5-point row-height ceiling (converted to the pixel unit the model stores) instead
/// of an arbitrary, far-lower cap.
/// </summary>
public class R83_AutoFitSizingServiceFontSizeAndClampTests
{
    [Fact]
    public void EstimateRowHeight_LargeFontUnwrappedUnrotatedCell_GrowsPastDefaultHeight()
    {
        // A 48pt heading cell with Wrap Text off and no rotation must still grow the row -- before
        // the fix, AutoFitCellText carried no FontSize at all, so EstimateLineCount always returned
        // 1 line and the row collapsed to max(defaultHeight, 1 * defaultLineHeight) == defaultHeight.
        var heading = new AutoFitCellText("Heading", FontSize: 48);

        var height = AutoFitSizingService.EstimateRowHeight([heading], defaultHeight: 20);

        height.Should().BeApproximately(87.27, 0.01); // 48pt * (20px / 11pt) default-font line-height ratio
        height.Should().BeGreaterThan(20 * 3);
    }

    [Theory]
    [InlineData(11.0)] // matches the sheet's assumed default font size
    [InlineData(0.0)]  // unset -- falls back to the row's default line height
    public void EstimateRowHeight_DefaultOrUnsetFontSize_StaysAtDefaultHeight(double fontSize)
    {
        // Sibling no-regression: a plain default-font (or FontSize-unset) unwrapped/unrotated cell
        // must still autofit to exactly the default row height, exactly as before FontSize existed.
        var cell = new AutoFitCellText("Heading", FontSize: fontSize);

        var height = AutoFitSizingService.EstimateRowHeight([cell], defaultHeight: 20);

        height.Should().Be(20);
    }

    [Fact]
    public void EstimateRowHeight_LongWrappedText_ClampsAtExcelPointEquivalentNotOldPixelCap()
    {
        // A Wrap-Text cell in a default-width column holding a long string wraps to far more visual
        // lines than the old 220px (~165pt) clamp allowed. The clamp must match Excel's real 409.5pt
        // ceiling (converted to the pixel unit RowHeights stores: 409.5 * 96/72 = 546), not 220.
        var longText = new string('x', 300);
        var wrapped = new AutoFitCellText(longText, WrapText: true, ColumnWidth: 8.43);

        var height = AutoFitSizingService.EstimateRowHeight([wrapped], defaultHeight: 20);

        height.Should().Be(AutoFitSizingService.MaximumRowHeight);
        AutoFitSizingService.MaximumRowHeight.Should().BeApproximately(546.0, 0.01);
        height.Should().BeGreaterThan(220); // must exceed the old, too-low pixel-unit cap
    }

    [Fact]
    public void EstimateRowHeight_ShortWrappedText_StaysBelowMaximumClamp()
    {
        // Sibling no-regression: content that doesn't approach the ceiling must be unaffected by
        // raising the clamp -- it still sizes to its own (unclamped) wrapped line count.
        var shortText = new string('x', 60);
        var wrapped = new AutoFitCellText(shortText, WrapText: true, ColumnWidth: 8.43);

        var height = AutoFitSizingService.EstimateRowHeight([wrapped], defaultHeight: 20);

        height.Should().BeLessThan(AutoFitSizingService.MaximumRowHeight);
        height.Should().Be(200); // 10 wrapped lines * 20px line height, unclamped
    }
}
