using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class SheetBackgroundPickerPlannerTests
{
    [Fact]
    public void BuildOpenDialogPlan_UsesNativeSingleFileGuardrails()
    {
        var plan = SheetBackgroundPickerPlanner.BuildOpenDialogPlan();

        plan.CheckFileExists.Should().BeTrue();
        plan.Multiselect.Should().BeFalse();
    }

    [Fact]
    public void BuildOpenPickerPlan_UsesSupportedBackgroundImagePatterns()
    {
        var plan = SheetBackgroundPickerPlanner.BuildOpenPickerPlan();

        plan.FileTypes.Should().ContainSingle();
        var fileType = plan.FileTypes[0];
        fileType.DisplayName.Should().Be(SheetBackgroundPickerPlanner.ImagePickerDisplayName);
        fileType.Patterns.Should().Equal(SheetBackgroundPickerPlanner.SupportedImagePatterns);
    }

    [Theory]
    [InlineData("sheet.png", "image/png")]
    [InlineData("sheet.JPG", "image/jpeg")]
    [InlineData("sheet.jpeg", "image/jpeg")]
    [InlineData("sheet.bmp", "image/bmp")]
    [InlineData("sheet.gif", "image/gif")]
    public void TryResolveContentTypeForPath_RecognizesBackgroundFormats(
        string path,
        string expectedContentType)
    {
        SheetBackgroundPickerPlanner.TryResolveContentTypeForPath(path, out var contentType)
            .Should().BeTrue();
        contentType.Should().Be(expectedContentType);
    }

    [Theory]
    [InlineData("sheet.webp")]
    [InlineData("sheet.tif")]
    [InlineData("sheet.tiff")]
    [InlineData("sheet.txt")]
    [InlineData("sheet")]
    public void TryResolveContentTypeForPath_RejectsUnsupportedBackgroundFormats(string path)
    {
        SheetBackgroundPickerPlanner.TryResolveContentTypeForPath(path, out var contentType)
            .Should().BeFalse();
        contentType.Should().BeEmpty();
    }

    [Fact]
    public void TryBuildBackgroundImage_UsesFileNameAndContentType()
    {
        var bytes = new byte[] { 1, 2, 3 };

        SheetBackgroundPickerPlanner.TryBuildBackgroundImage(
                bytes,
                @"C:\Temp\sheet-bg.jpeg",
                out var background)
            .Should().BeTrue();

        background.Should().Be(new WorksheetBackgroundImage(bytes, "image/jpeg", "sheet-bg.jpeg"));
    }
}
