using System.Globalization;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests;

public sealed class WatermarkOptionsDialogPlannerTests
{
    [Fact]
    public void BuildInitialState_UsesTextDefaultsForMissingWatermark()
    {
        var state = WatermarkOptionsDialogPlanner.BuildInitialState(null, CultureInfo.InvariantCulture);

        state.IsPicture.Should().BeFalse();
        state.Text.Should().Be(WatermarkOptionsDialogPlanner.DefaultText);
        state.FontFamily.Should().Be(WatermarkOptionsDialogPlanner.DefaultFontFamily);
        state.FontColorHex.Should().Be(WatermarkOptionsDialogPlanner.DefaultFontColorHex);
        state.TextIsHorizontal.Should().BeFalse();
        state.TextIsSemitransparent.Should().BeTrue();
        state.PicturePathText.Should().Be(WatermarkOptionsDialogPlanner.DefaultPicturePathText);
        state.ScaleText.Should().Be("0");
        state.PictureWashout.Should().BeTrue();
    }

    [Fact]
    public void BuildInitialState_ProjectsPictureWatermarkState()
    {
        var state = WatermarkOptionsDialogPlanner.BuildInitialState(
            new WatermarkOptions("ignored")
            {
                ImageBytes = new byte[3072],
                ScalePct = 125,
                Layout = WatermarkLayout.Horizontal,
                Opacity = 1.0,
            },
            CultureInfo.InvariantCulture);

        state.IsPicture.Should().BeTrue();
        state.Text.Should().Be(WatermarkOptionsDialogPlanner.DefaultText);
        state.PicturePathText.Should().Be("(image loaded - 3 KB)");
        state.ScaleText.Should().Be("125");
        state.PictureIsHorizontal.Should().BeTrue();
        state.PictureWashout.Should().BeFalse();
    }

    [Fact]
    public void TryBuildTextResult_NormalizesHexAndDefaultsBlankFont()
    {
        WatermarkOptionsDialogPlanner.TryBuildTextResult(
                new WatermarkTextDialogInput(
                    Text: "  Draft copy  ",
                    FontFamily: " ",
                    ColorText: "808080",
                    IsHorizontal: true,
                    IsSemitransparent: false),
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().NotBeNull();
        result!.Text.Should().Be("Draft copy");
        result.FontFamily.Should().Be(WatermarkOptionsDialogPlanner.DefaultFontFamily);
        result.FontColorHex.Should().Be("#808080");
        result.Layout.Should().Be(WatermarkLayout.Horizontal);
        result.Opacity.Should().Be(1.0);
    }

    [Theory]
    [InlineData("abc", "#abc")]
    [InlineData("#abcd", "#abcd")]
    [InlineData("80ff0000", "#80ff0000")]
    [InlineData("#00AAee", "#00AAee")]
    public void TryBuildTextResult_PreservesWatermarkUiHexContract(string input, string expected)
    {
        WatermarkOptionsDialogPlanner.TryBuildTextResult(
                new WatermarkTextDialogInput(
                    Text: "Draft",
                    FontFamily: "Calibri",
                    ColorText: input,
                    IsHorizontal: false,
                    IsSemitransparent: true),
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().NotBeNull();
        result!.FontColorHex.Should().Be(expected);
    }

    [Theory]
    [InlineData("", "#808080", WatermarkDialogValidationTarget.Text, WatermarkOptionsDialogPlanner.TextValidationMessage)]
    [InlineData("Draft", "not-hex", WatermarkDialogValidationTarget.Color, WatermarkOptionsDialogPlanner.ColorValidationMessage)]
    public void TryBuildTextResult_ReportsValidationTarget(
        string text,
        string color,
        WatermarkDialogValidationTarget target,
        string message)
    {
        WatermarkOptionsDialogPlanner.TryBuildTextResult(
                new WatermarkTextDialogInput(text, "Calibri", color, IsHorizontal: false, IsSemitransparent: true),
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(new WatermarkOptionsDialogValidation(target, message));
    }

    [Fact]
    public void TryBuildPictureResult_ConstructsPictureWatermark()
    {
        var image = new byte[] { 1, 2, 3 };

        WatermarkOptionsDialogPlanner.TryBuildPictureResult(
                new WatermarkPictureDialogInput(image, "500", IsHorizontal: false, IsWashout: true),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeTrue();

        validation.Should().BeNull();
        result.Should().NotBeNull();
        result!.Text.Should().BeEmpty();
        result.ImageBytes.Should().BeSameAs(image);
        result.ScalePct.Should().Be(500);
        result.Layout.Should().Be(WatermarkLayout.Diagonal);
        result.Opacity.Should().Be(0.3);
        result.FontFamily.Should().Be(WatermarkOptionsDialogPlanner.DefaultFontFamily);
        result.FontColorHex.Should().Be(WatermarkOptionsDialogPlanner.DefaultFontColorHex);
    }

    [Theory]
    [InlineData(null, "100", WatermarkDialogValidationTarget.Image, WatermarkOptionsDialogPlanner.ImageValidationMessage)]
    [InlineData(new byte[] { 1 }, "-1", WatermarkDialogValidationTarget.Scale, WatermarkOptionsDialogPlanner.ScaleValidationMessage)]
    [InlineData(new byte[] { 1 }, "501", WatermarkDialogValidationTarget.Scale, WatermarkOptionsDialogPlanner.ScaleValidationMessage)]
    [InlineData(new byte[] { 1 }, "abc", WatermarkDialogValidationTarget.Scale, WatermarkOptionsDialogPlanner.ScaleValidationMessage)]
    public void TryBuildPictureResult_ReportsValidationTarget(
        byte[]? imageBytes,
        string scale,
        WatermarkDialogValidationTarget target,
        string message)
    {
        WatermarkOptionsDialogPlanner.TryBuildPictureResult(
                new WatermarkPictureDialogInput(imageBytes, scale, IsHorizontal: true, IsWashout: false),
                CultureInfo.InvariantCulture,
                out var result,
                out var validation)
            .Should().BeFalse();

        result.Should().BeNull();
        validation.Should().Be(new WatermarkOptionsDialogValidation(target, message));
    }

    [Fact]
    public void FormatPickedImageLabel_UsesFileNameAndWholeKilobytes()
    {
        WatermarkOptionsDialogPlanner.FormatPickedImageLabel("logo.png", 4097)
            .Should().Be("logo.png (4 KB)");
    }

    [Fact]
    public void BuildImageImportPlan_NormalizesPathAndKeepsImportedBytes()
    {
        var bytes = new byte[2049];
        var path = Path.Combine("art", "logo.png");

        WatermarkOptionsDialogPlanner.BuildImageImportPlan(path, bytes)
            .Should().Be(new WatermarkImageImportPlan(bytes, "logo.png (2 KB)"));
    }

    [Theory]
    [InlineData("Access denied", "Could not read image file: Access denied")]
    [InlineData(" ", "Could not read image file: Unknown error.")]
    public void FormatImageReadFailure_OwnsPortableFailureText(string detail, string expected)
    {
        WatermarkOptionsDialogPlanner.FormatImageReadFailure(detail).Should().Be(expected);
    }

    [Fact]
    public void Session_owns_mode_pending_image_and_picture_submission_sequence()
    {
        var session = new WatermarkOptionsDialogSession(null, CultureInfo.InvariantCulture);
        var image = new byte[] { 1, 2, 3, 4 };

        session.Mode.Should().Be(WatermarkDialogMode.Text);
        session.SelectMode(WatermarkDialogMode.Picture);
        session.ImportImage("art/logo.png", image)
            .Should().Be(new WatermarkImageImportPlan(image, "logo.png (0 KB)"));

        var acceptance = session.Submit(ValidSubmission() with
        {
            PictureScaleText = "125",
            PictureIsHorizontal = true,
            PictureIsWashout = false,
        });

        acceptance.IsAccepted.Should().BeTrue();
        acceptance.Validation.Should().BeNull();
        acceptance.Result!.ImageBytes.Should().BeSameAs(image);
        acceptance.Result.ScalePct.Should().Be(125);
        acceptance.Result.Layout.Should().Be(WatermarkLayout.Horizontal);
        acceptance.Result.Opacity.Should().Be(1.0);
    }

    [Fact]
    public void Session_routes_active_mode_validation_and_remove_outcome()
    {
        var session = new WatermarkOptionsDialogSession(null, CultureInfo.InvariantCulture);

        var textValidation = session.Submit(ValidSubmission() with { Text = string.Empty });
        textValidation.IsAccepted.Should().BeFalse();
        textValidation.Validation!.Target.Should().Be(WatermarkDialogValidationTarget.Text);

        session.SelectMode(WatermarkDialogMode.Picture);
        var pictureValidation = session.Submit(ValidSubmission());
        pictureValidation.IsAccepted.Should().BeFalse();
        pictureValidation.Validation!.Target.Should().Be(WatermarkDialogValidationTarget.Image);

        session.Remove().Should().Be(new WatermarkOptionsDialogAcceptance(
            IsAccepted: true,
            RemoveRequested: true));
    }

    private static WatermarkOptionsDialogSubmission ValidSubmission() => new(
        Text: "DRAFT",
        FontFamily: "Calibri",
        ColorText: "#808080",
        TextIsHorizontal: false,
        TextIsSemitransparent: true,
        PictureScaleText: "100",
        PictureIsHorizontal: false,
        PictureIsWashout: true);
}
