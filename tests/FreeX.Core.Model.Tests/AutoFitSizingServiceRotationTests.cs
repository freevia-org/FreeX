using FreeX.Core.Commands;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R39-render-rotation-overflow-2-1: AutoFit Row Height must account for TextRotation
/// (angled or stacked/vertical text) instead of ignoring it, so rotated text gets a taller
/// auto-grown row instead of being clipped at the unrotated default height.
/// </summary>
public class AutoFitSizingServiceRotationTests
{
    private const string LongText = "This is a thirty character line!!";

    [Fact]
    public void EstimateRowHeight_RotatedFortyFiveDegreeText_IsTallerThanUnrotatedSameText()
    {
        var unrotated = AutoFitSizingService.EstimateRowHeight(
            [new AutoFitCellText(LongText)],
            defaultHeight: 20);

        var rotated = AutoFitSizingService.EstimateRowHeight(
            [new AutoFitCellText(LongText, TextRotation: 45)],
            defaultHeight: 20);

        // A non-rotated long single-line cell keeps the default row height (it widens its
        // column instead, per AutoFitSizingService_RowHeightKeepsLongUnwrappedTextAtDefaultHeight).
        unrotated.Should().BeApproximately(20, 0.01);

        // The same text rotated 45 degrees must grow the row noticeably -- the projected
        // rotated bounding box is much taller than a single unrotated line.
        rotated.Should().BeGreaterThan(unrotated);
        rotated.Should().BeGreaterThan(60); // several line-heights taller, not a rounding nudge
    }

    [Fact]
    public void EstimateRowHeight_NegativeRotationAngle_ProjectsSameHeightAsPositiveAngle()
    {
        // Excel's rotation is signed (-90..90); the magnitude of the angle -- not its sign --
        // determines how tall the projected bounding box is.
        var positive = AutoFitSizingService.EstimateRowHeight(
            [new AutoFitCellText(LongText, TextRotation: 45)],
            defaultHeight: 20);
        var negative = AutoFitSizingService.EstimateRowHeight(
            [new AutoFitCellText(LongText, TextRotation: -45)],
            defaultHeight: 20);

        negative.Should().BeApproximately(positive, 0.01);
    }

    [Fact]
    public void EstimateRowHeight_StackedVerticalText_StacksTallerThanUnrotatedText()
    {
        var unrotated = AutoFitSizingService.EstimateRowHeight(
            [new AutoFitCellText("ABCDEFGHIJ")],
            defaultHeight: 20);

        var stacked = AutoFitSizingService.EstimateRowHeight(
            [new AutoFitCellText("ABCDEFGHIJ", TextRotation: 255)],
            defaultHeight: 20);

        unrotated.Should().BeApproximately(20, 0.01);

        // 10 stacked characters need roughly 10 line-heights.
        stacked.Should().BeGreaterThan(unrotated);
        stacked.Should().BeApproximately(200, 0.01);
    }

    [Fact]
    public void EstimateRowHeight_ZeroRotation_MatchesPriorUnrotatedBehaviorExactly()
    {
        // Sibling no-regression case: explicitly passing TextRotation: 0 (the default) must
        // behave identically to the pre-existing non-rotation-aware overloads/behavior.
        var viaExplicitZero = AutoFitSizingService.EstimateRowHeight(
            [new AutoFitCellText("first line\nsecond line\nthird line", TextRotation: 0)],
            defaultHeight: 20);

        var viaStringOverload = AutoFitSizingService.EstimateRowHeight(
            ["first line\nsecond line\nthird line"],
            defaultHeight: 20);

        viaExplicitZero.Should().Be(viaStringOverload);
        viaExplicitZero.Should().BeGreaterThan(20);
    }

    [Fact]
    public void EstimateRowHeight_RotatedShortText_DoesNotExceedMaximumRowHeight()
    {
        var height = AutoFitSizingService.EstimateRowHeight(
            [new AutoFitCellText("Hi", TextRotation: 90)],
            defaultHeight: 20);

        height.Should().BeLessThanOrEqualTo(AutoFitSizingService.MaximumRowHeight);
        height.Should().BeGreaterThanOrEqualTo(AutoFitSizingService.MinimumRowHeight);
    }
}
