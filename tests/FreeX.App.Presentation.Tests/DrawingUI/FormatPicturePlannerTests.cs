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
    public void Sync_ReturnsNull_WhenAspectRatioNonPositive()
    {
        FormatPicturePlanner.SyncHeightFromWidth("100", 0).Should().BeNull();
        FormatPicturePlanner.SyncWidthFromHeight("100", -1).Should().BeNull();
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
    public void TryCreateResult_BlankAltTextBecomesNull()
    {
        FormatPicturePlanner.TryCreateResult("10", "10", "0", false, "   ", out var result, out _);
        result!.AltText.Should().BeNull();
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
