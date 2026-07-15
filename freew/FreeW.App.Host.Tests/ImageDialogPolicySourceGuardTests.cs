using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ImageDialogPolicySourceGuardTests
{
    [Theory]
    [InlineData("ImageAdjustDialog.cs", "ImageAdjustDialogPlanner.BuildInitialState(", "new ImageAdjustDialogInput(", "ImageAdjustDialogPlanner.TryBuildResult(")]
    [InlineData("ImageBorderDialog.cs", "ImageBorderDialogPlanner.BuildInitialState(", "new ImageBorderDialogInput(", "ImageBorderDialogPlanner.TryBuildResult(")]
    [InlineData("ImageCropDialog.cs", "ImageCropDialogPlanner.BuildInitialState(", "new ImageCropDialogInput(", "ImageCropDialogPlanner.TryBuildResult(")]
    [InlineData("ImagePositionDialog.cs", "ImagePositionDialogPlanner.BuildInitialState(", "new ImagePositionDialogInput(", "ImagePositionDialogPlanner.TryBuildResult(")]
    [InlineData("ImageSizeDialog.cs", "ImageSizeDialogPlanner.BuildInitialState(", "new ImageSizeDialogInput(", "ImageSizeDialogPlanner.TryBuildResult(")]
    public void ImageDialogs_DelegateInitialStateValidationAndResultPolicyToPresentationPlanners(
        string fileName,
        string initialStateCall,
        string inputConstruction,
        string resultCall)
    {
        var source = ReadHostSource(fileName);

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain(initialStateCall);
        source.Should().Contain(inputConstruction);
        source.Should().Contain(resultCall);
    }

    [Theory]
    [InlineData("ImageAdjustDialog.cs")]
    [InlineData("ImageBorderDialog.cs")]
    [InlineData("ImageCropDialog.cs")]
    [InlineData("ImagePositionDialog.cs")]
    [InlineData("ImageSizeDialog.cs")]
    public void ImageDialogs_DoNotOwnNumericParsingFormattingOrValidationMessages(string fileName)
    {
        var source = ReadHostSource(fileName);

        source.Should().NotContain("double.TryParse(");
        source.Should().NotContain("NumberStyles.");
        source.Should().NotContain(".ToString(\"0.##\"");
        source.Should().NotContain(".ToString(\"0.#\"");
        source.Should().NotContain(" must be a number between ");
        source.Should().NotContain("Enter a positive border width in points.");
        source.Should().NotContain("Each crop value must be a percentage between 0 and 99.");
        source.Should().NotContain("Left + Right and Top + Bottom must each total less than 100%.");
        source.Should().NotContain("Enter valid numeric offsets in points.");
        source.Should().NotContain("Enter positive values for both width and height (in points).");
    }

    [Fact]
    public void ImageBorderDialog_DoesNotOwnDashCatalogColorValidationOrDefaultWidth()
    {
        var source = ReadHostSource("ImageBorderDialog.cs");

        source.Should().Contain("ImageBorderDialogPlanner.DashItems");
        source.Should().NotContain("DashStyles");
        source.Should().NotContain("Regex.IsMatch");
        source.Should().NotContain("System.Text.RegularExpressions");
        source.Should().NotContain("0.75");
    }

    [Fact]
    public void ImagePositionDialog_DoesNotOwnAnchorCatalogsOrConversions()
    {
        var source = ReadHostSource("ImagePositionDialog.cs");

        source.Should().Contain("ImagePositionDialogPlanner.HorizontalAnchorItems");
        source.Should().Contain("ImagePositionDialogPlanner.VerticalAnchorItems");
        source.Should().NotContain("HAnchorLabels");
        source.Should().NotContain("VAnchorLabels");
        source.Should().NotContain("ParseH(");
        source.Should().NotContain("ParseV(");
        source.Should().NotContain("LabelH(");
        source.Should().NotContain("LabelV(");
    }

    [Fact]
    public void ImageSizeDialog_RoutesAspectLockMathThroughPlanner()
    {
        var source = ReadHostSource("ImageSizeDialog.cs");

        source.Should().Contain("ImageSizeDialogPlanner.TryBuildLockedHeightText(");
        source.Should().Contain("ImageSizeDialogPlanner.TryBuildLockedWidthText(");
        source.Should().NotContain("currentHeightPt / currentWidthPt");
        source.Should().NotContain("w * _aspect");
        source.Should().NotContain("h / _aspect");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

}
