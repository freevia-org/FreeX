using System.Globalization;
using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class PictureCropDialogPlannerTests
{
    public PictureCropDialogPlannerTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void Capture_ReportsImageCroppable()
    {
        var picture = new PictureModel
        {
            Kind = PictureKind.Image,
            CropLeft = 0.1,
            CropTop = 0.2,
            CropRight = 0.05,
            CropBottom = 0.15,
        };

        var values = PictureCropDialogPlanner.Capture(picture);

        values.IsCroppable.Should().BeTrue();
        values.Left.Should().Be(0.1);
        values.Bottom.Should().Be(0.15);
    }

    [Fact]
    public void Capture_RangeSnapshotIsNotCroppable()
    {
        var picture = new PictureModel { Kind = PictureKind.CellRangeSnapshot };
        PictureCropDialogPlanner.Capture(picture).IsCroppable.Should().BeFalse();
    }

    [Fact]
    public void FormatPercent_AndParse_RoundTrip()
    {
        var text = PictureCropDialogPlanner.FormatPercent(0.25);
        text.Should().Be("25");
        PictureCropDialogPlanner.TryParsePercent(text, out var fraction).Should().BeTrue();
        fraction.Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void TryParsePercent_AcceptsTrailingPercentSign()
    {
        PictureCropDialogPlanner.TryParsePercent("10%", out var fraction).Should().BeTrue();
        fraction.Should().BeApproximately(0.1, 1e-9);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("100")]
    [InlineData("150")]
    [InlineData("nope")]
    public void TryParsePercent_RejectsOutOfRangeOrUnparsable(string text)
    {
        PictureCropDialogPlanner.TryParsePercent(text, out _).Should().BeFalse();
    }

    [Fact]
    public void TryCreateResult_AcceptsAVisibleCrop()
    {
        var ok = PictureCropDialogPlanner.TryCreateResult("10", "20", "30", "5", out var result, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        result!.Left.Should().BeApproximately(0.1, 1e-9);
        result.Right.Should().BeApproximately(0.3, 1e-9);
    }

    [Fact]
    public void TryCreateResult_AcceptsDelimitedCropText()
    {
        var ok = PictureCropDialogPlanner.TryCreateResult("10, 20; 30, 5", out var result, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        result!.Left.Should().BeApproximately(0.1, 1e-9);
        result.Right.Should().BeApproximately(0.3, 1e-9);
    }

    [Theory]
    [InlineData("60", "0", "60", "0")] // left + right >= 1
    [InlineData("0", "70", "0", "40")] // top + bottom >= 1
    public void TryCreateResult_RejectsCropWithNoVisibleRegion(string l, string t, string r, string b)
    {
        var ok = PictureCropDialogPlanner.TryCreateResult(l, t, r, b, out var result, out var error);

        ok.Should().BeFalse();
        result.Should().BeNull();
        error.Should().Be(PictureCropDialogPlanner.InvalidPercentMessage);
    }

    [Fact]
    public void TryCreateResult_RejectsDelimitedCropTextWithWrongEdgeCount()
    {
        var ok = PictureCropDialogPlanner.TryCreateResult("10, 20, 30", out var result, out var error);

        ok.Should().BeFalse();
        result.Should().BeNull();
        error.Should().Be(PictureCropDialogPlanner.InvalidPercentMessage);
    }

    [Fact]
    public void BuildCommand_MapsValidatedCropAndResetToCoreCommands()
    {
        var sheetId = SheetId.New();
        var pictureId = Guid.NewGuid();
        var crop = new PictureCropDialogPlanner.CropResult(0.1, 0.2, 0.3, 0.05);

        PictureCropDialogPlanner.BuildCommand(sheetId, pictureId, crop)
            .Should().BeOfType<SetPictureCropCommand>();
        PictureCropDialogPlanner.BuildResetCommand(sheetId, pictureId)
            .Should().BeOfType<SetPictureCropCommand>();
    }
}
