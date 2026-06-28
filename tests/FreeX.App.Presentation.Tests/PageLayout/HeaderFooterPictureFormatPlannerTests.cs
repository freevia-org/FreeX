using System.Globalization;
using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class HeaderFooterPictureFormatPlannerTests
{
    [Fact]
    public void CreateState_UsesDefaultFileNameAndWidthFirstFocusPolicy()
    {
        var picture = new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "  ", 120.5, 48);

        var state = HeaderFooterPictureFormatPlanner.CreateState(
            picture,
            "Picture",
            CultureInfo.InvariantCulture);

        state.FileName.Should().Be("Picture");
        state.WidthText.Should().Be("120.5");
        state.HeightText.Should().Be("48");
        state.OriginalSize.Should().Be(new ObjectSizeDialogSize(120.5, 48));
        state.InitialFocusField.Should().Be(ObjectSizeDialogField.Width);
        state.FirstInvalidField.Should().Be(ObjectSizeDialogField.Width);
        state.LockAspectRatio.Should().BeTrue();
    }

    [Fact]
    public void TryCreateResult_ShapesPictureResultAndPreservesImagePayload()
    {
        var picture = new WorksheetHeaderFooterPicture([1, 2, 3], "image/png", "logo.png", 120, 48);

        HeaderFooterPictureFormatPlanner.TryCreateResult(
                picture,
                "240",
                "96",
                out var result,
                out var invalidField)
            .Should()
            .BeTrue();

        result.Should().NotBeNull();
        result!.Width.Should().Be(240);
        result.Height.Should().Be(96);
        result.FileName.Should().Be("logo.png");
        result.ImageBytes.Should().Equal([1, 2, 3]);
        invalidField.Should().Be(ObjectSizeDialogField.Width);
    }

    [Fact]
    public void TryCreateResult_RejectsInvalidSizeAndTargetsWidthFirst()
    {
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "logo.png", 120, 48);

        HeaderFooterPictureFormatPlanner.TryCreateResult(
                picture,
                "bad",
                "0",
                out var result,
                out var invalidField)
            .Should()
            .BeFalse();

        result.Should().BeNull();
        invalidField.Should().Be(ObjectSizeDialogField.Width);
    }

    [Fact]
    public void ResetSize_ReturnsBoundedOriginalSize()
    {
        var picture = new WorksheetHeaderFooterPicture([1], "image/png", "logo.png", 0, double.NaN);
        var state = HeaderFooterPictureFormatPlanner.CreateState(
            picture,
            "Picture",
            CultureInfo.InvariantCulture);

        HeaderFooterPictureFormatPlanner.ResetSize(state)
            .Should()
            .Be(new ObjectSizeDialogSize(1, 1));
    }

    [Fact]
    public void AspectSync_UsesSharedObjectSizeMath()
    {
        var state = HeaderFooterPictureFormatPlanner.CreateState(
            new WorksheetHeaderFooterPicture([1], "image/png", "logo.png", 120, 48),
            "Picture",
            CultureInfo.InvariantCulture);

        HeaderFooterPictureFormatPlanner.SyncHeightFromWidth("240", state.OriginalSize)
            .Should()
            .Be(96);
        HeaderFooterPictureFormatPlanner.SyncWidthFromHeight("24", state.OriginalSize)
            .Should()
            .Be(60);
    }
}
