using System.Globalization;
using FluentAssertions;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.DrawingUI;

public sealed class FormatPicturePlannerTests
{
    public FormatPicturePlannerTests()
    {
        // Pin culture so the invariant-style parse/format assertions are deterministic.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Fact]
    public void Capture_Picture_ReportsLockAspectSupported()
    {
        var picture = new PictureModel
        {
            Width = 200,
            Height = 100,
            RotationDegrees = 30,
            LockAspectRatio = true,
            AltText = "logo",
        };

        var values = FormatPicturePlanner.Capture(picture);

        values.Width.Should().Be(200);
        values.Height.Should().Be(100);
        values.RotationDegrees.Should().Be(30);
        values.LockAspectRatio.Should().BeTrue();
        values.LockAspectRatioSupported.Should().BeTrue();
        values.AltText.Should().Be("logo");
    }

    [Fact]
    public void Capture_Shape_DoesNotSupportLockAspect()
    {
        var shape = new DrawingShapeModel { Width = 120, Height = 60, AltText = null };

        var values = FormatPicturePlanner.Capture(shape);

        values.LockAspectRatioSupported.Should().BeFalse();
        values.LockAspectRatio.Should().BeFalse();
        values.AltText.Should().BeEmpty();
    }

    [Fact]
    public void Capture_TextBox_DoesNotSupportLockAspect()
    {
        var textBox = new TextBoxModel
        {
            Width = 140,
            Height = 70,
            RotationDegrees = 25,
            AltText = "note"
        };

        var values = FormatPicturePlanner.Capture(textBox);

        values.Width.Should().Be(140);
        values.Height.Should().Be(70);
        values.RotationDegrees.Should().Be(25);
        values.LockAspectRatioSupported.Should().BeFalse();
        values.LockAspectRatio.Should().BeFalse();
        values.AltText.Should().Be("note");
    }

    [Fact]
    public void CreateDialogState_FormatsFieldsAndCapturesAspectPolicy()
    {
        var values = new FormatPicturePlanner.FormatObjectValues(
            Width: 200,
            Height: 80,
            RotationDegrees: 45,
            LockAspectRatio: true,
            LockAspectRatioSupported: true,
            AltText: "logo");

        var state = FormatPicturePlanner.CreateDialogState(values, CultureInfo.InvariantCulture);

        state.WidthText.Should().Be("200");
        state.HeightText.Should().Be("80");
        state.RotationText.Should().Be("45");
        state.AspectRatio.Should().Be(2.5);
        state.LockAspectRatio.Should().BeTrue();
        state.LockAspectRatioSupported.Should().BeTrue();
        state.AltText.Should().Be("logo");
    }

    [Fact]
    public void SyncHeightFromWidth_PreservesAspectRatio()
    {
        var ratio = FormatPicturePlanner.AspectRatio(200, 100); // 2.0
        FormatPicturePlanner.SyncHeightFromWidth("300", ratio).Should().Be(150);
    }

    [Fact]
    public void SyncWidthFromHeight_PreservesAspectRatio()
    {
        var ratio = FormatPicturePlanner.AspectRatio(200, 100); // 2.0
        FormatPicturePlanner.SyncWidthFromHeight("50", ratio).Should().Be(100);
    }

    [Fact]
    public void NumericSync_PreservesAspectRatio()
    {
        var ratio = FormatPicturePlanner.AspectRatio(200, 100); // 2.0
        FormatPicturePlanner.SyncHeightFromWidth(300, ratio).Should().Be(150);
        FormatPicturePlanner.SyncWidthFromHeight(50, ratio).Should().Be(100);
    }

    [Fact]
    public void Sync_ReturnsNull_WhenAspectRatioNonPositive()
    {
        FormatPicturePlanner.SyncHeightFromWidth("100", 0).Should().BeNull();
        FormatPicturePlanner.SyncWidthFromHeight("100", -1).Should().BeNull();
    }

    [Fact]
    public void TryCreateSizeResult_AcceptsDelimitedWidthByHeightText()
    {
        FormatPicturePlanner.TryCreateSizeResult("320 x 180", out var result).Should().BeTrue();

        result.Should().Be(new FormatPicturePlanner.SizeResult(320, 180));
    }

    [Theory]
    [InlineData("450", 90)]
    [InlineData("-90", 270)]
    [InlineData("720", 0)]
    public void TryCreateRotationResult_NormalizesFullTurns(string text, double expected)
    {
        FormatPicturePlanner.TryCreateRotationResult(text, out var result).Should().BeTrue();

        result.Should().Be(new FormatPicturePlanner.RotationResult(expected));
    }

    [Fact]
    public void TryCreateResult_TrimsAltTextAndParsesValues()
    {
        var ok = FormatPicturePlanner.TryCreateResult(
            "150", "75", "45", lockAspectRatio: true, "  hello  ", out var result, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        result!.Width.Should().Be(150);
        result.Height.Should().Be(75);
        result.RotationDegrees.Should().Be(45);
        result.LockAspectRatio.Should().BeTrue();
        result.AltText.Should().Be("hello");
    }

    [Fact]
    public void TryCreateResult_NormalizesRotation()
    {
        var ok = FormatPicturePlanner.TryCreateResult(
            "150", "75", "450", lockAspectRatio: true, "hello", out var result, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        result!.RotationDegrees.Should().Be(90);
    }

    [Fact]
    public void TryCreateResult_BlankAltTextBecomesNull()
    {
        FormatPicturePlanner.TryCreateResult("10", "10", "0", false, "   ", out var result, out _);
        result!.AltText.Should().BeNull();
    }

    [Fact]
    public void TryCreateResult_AcceptsSubmissionPayload()
    {
        var submission = new FormatPicturePlanner.FormatObjectSubmission("150", "75", "405", true, "  hello  ");

        FormatPicturePlanner.TryCreateResult(submission, out var result, out var error).Should().BeTrue();

        error.Should().BeNull();
        result.Should().Be(new FormatPicturePlanner.FormatObjectResult(150, 75, 45, true, "hello"));
    }

    [Fact]
    public void TryCreatePictureResult_ParsesSizeRotationCropAndAltText()
    {
        FormatPicturePlanner.TryCreatePictureResult(
                "320 x 180",
                "405",
                false,
                "10, 5, 0, 20",
                " Revenue chart ",
                out var result,
                out var error)
            .Should()
            .BeTrue();

        error.Should().BeNull();
        result!.Format.Should().Be(new FormatPicturePlanner.FormatObjectResult(320, 180, 45, false, "Revenue chart"));
        result.Crop.Should().Be(new PictureCropDialogPlanner.CropResult(0.10, 0.05, 0, 0.20));
    }

    [Fact]
    public void TryCreatePictureResult_ReportsSizeBeforeRotation()
    {
        FormatPicturePlanner.TryCreatePictureResult(
                "0 x 180",
                "spin",
                false,
                "0, 0, 0, 0",
                null,
                out var result,
                out var error)
            .Should()
            .BeFalse();

        result.Should().BeNull();
        error.Should().Be(FormatPicturePlanner.FormatPictureDialogValidationError.Size);
    }

    [Fact]
    public void ResolveInvalidField_UsesSharedSizeAndRotationPriority()
    {
        FormatPicturePlanner.ResolveInvalidField("10", "bad", "spin")
            .Should()
            .Be(FormatPicturePlanner.FormatObjectDialogField.Height);
        FormatPicturePlanner.ResolveInvalidField("10", "20", "spin")
            .Should()
            .Be(FormatPicturePlanner.FormatObjectDialogField.Rotation);
    }

    [Theory]
    [InlineData("0", "10")]
    [InlineData("10", "0")]
    [InlineData("abc", "10")]
    [InlineData("-5", "10")]
    public void TryCreateResult_RejectsNonPositiveOrUnparsableSize(string width, string height)
    {
        var ok = FormatPicturePlanner.TryCreateResult(width, height, "0", false, null, out var result, out var error);

        ok.Should().BeFalse();
        result.Should().BeNull();
        error.Should().Be(FormatPicturePlanner.InvalidSizeMessage);
    }

    [Fact]
    public void TryCreateResult_RejectsUnparsableRotation()
    {
        var ok = FormatPicturePlanner.TryCreateResult("10", "10", "spin", false, null, out var result, out var error);

        ok.Should().BeFalse();
        result.Should().BeNull();
        error.Should().Be(FormatPicturePlanner.InvalidRotationMessage);
    }
}
