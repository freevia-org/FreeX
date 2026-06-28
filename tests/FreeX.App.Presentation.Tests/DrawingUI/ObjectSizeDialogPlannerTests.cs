using System.Globalization;
using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class ObjectSizeDialogPlannerTests
{
    public ObjectSizeDialogPlannerTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void CreateState_BoundsInitialSizeAndCapturesFocusPolicy()
    {
        var state = ObjectSizeDialogPlanner.CreateState(
            width: 0,
            height: double.NaN,
            ObjectSizeDialogField.Height,
            ObjectSizeDialogField.Width,
            CultureInfo.InvariantCulture);

        state.WidthText.Should().Be("1");
        state.HeightText.Should().Be("1");
        state.OriginalSize.Should().Be(new ObjectSizeDialogSize(1, 1));
        state.InitialFocusField.Should().Be(ObjectSizeDialogField.Height);
        state.FirstInvalidField.Should().Be(ObjectSizeDialogField.Width);
        state.LockAspectRatio.Should().BeTrue();
    }

    [Fact]
    public void TryCreateDelimitedSize_AcceptsWidthByHeightText()
    {
        ObjectSizeDialogPlanner.TryCreateDelimitedSize("320 x 180", out var result, out var invalidField)
            .Should()
            .BeTrue();

        result.Should().Be(new ObjectSizeDialogSize(320, 180));
        invalidField.Should().Be(ObjectSizeDialogField.Width);
    }

    [Theory]
    [InlineData("bad", "0", ObjectSizeDialogField.Width, ObjectSizeDialogField.Width)]
    [InlineData("bad", "0", ObjectSizeDialogField.Height, ObjectSizeDialogField.Height)]
    [InlineData("bad", "10", ObjectSizeDialogField.Height, ObjectSizeDialogField.Width)]
    [InlineData("10", "0", ObjectSizeDialogField.Width, ObjectSizeDialogField.Height)]
    public void TryCreateSize_ReturnsInvalidFieldUsingDialogPriority(
        string width,
        string height,
        ObjectSizeDialogField firstInvalidField,
        ObjectSizeDialogField expectedInvalidField)
    {
        ObjectSizeDialogPlanner.TryCreateSize(width, height, firstInvalidField, out var result, out var invalidField)
            .Should()
            .BeFalse();

        result.Should().Be(default(ObjectSizeDialogSize));
        invalidField.Should().Be(expectedInvalidField);
    }

    [Fact]
    public void SyncSize_PreservesOriginalAspectRatio()
    {
        var originalSize = new ObjectSizeDialogSize(120, 60);

        ObjectSizeDialogPlanner.SyncHeightFromWidth("240", originalSize).Should().Be(120);
        ObjectSizeDialogPlanner.SyncWidthFromHeight("90", originalSize).Should().Be(180);
    }

    [Fact]
    public void CalculateLockedAspectSize_FallsBackToTypedDimensionWhenOriginalSizeIsInvalid()
    {
        ObjectSizeDialogPlanner.CalculateLockedAspectHeight(200, originalWidth: 0, originalHeight: 50)
            .Should()
            .Be(200);
        ObjectSizeDialogPlanner.CalculateLockedAspectWidth(75, originalWidth: 50, originalHeight: 0)
            .Should()
            .Be(75);
    }

    [Fact]
    public void FormatSize_NormalizesMinimumAndRoundsToTwoDecimals()
    {
        ObjectSizeDialogPlanner.FormatSize(12.345, CultureInfo.InvariantCulture).Should().Be("12.34");
        ObjectSizeDialogPlanner.FormatSize(0, CultureInfo.InvariantCulture).Should().Be("1");
    }
}
