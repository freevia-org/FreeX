using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class WatermarkOptionsDialogPolicySourceGuardTests
{
    [Fact]
    public void WatermarkOptionsDialog_DelegatesPolicyToPresentationPlanner()
    {
        var source = ReadHostSource("WatermarkOptionsDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("WatermarkOptionsDialogPlanner.BuildInitialState(");
        source.Should().Contain("WatermarkOptionsDialogPlanner.TryBuildTextResult(");
        source.Should().Contain("WatermarkOptionsDialogPlanner.TryBuildPictureResult(");
        source.Should().Contain("WatermarkOptionsDialogPlanner.BuildImageImportPlan(");
        source.Should().Contain("WatermarkOptionsDialogPlanner.FormatImageReadFailure(");
        source.Should().Contain("WatermarkOptionsDialogPlanner.TextModeLabel");
        source.Should().Contain("WatermarkOptionsDialogPlanner.PictureModeLabel");
        source.Should().Contain("WatermarkOptionsDialogPlanner.SelectPictureButton");
        source.Should().Contain("WatermarkOptionsDialogPlanner.ActionButtons");
        source.Should().Contain("WatermarkOptionsDialogPlanner.WatermarkImageFilter");
    }

    [Fact]
    public void WatermarkOptionsDialog_DoesNotOwnValidationParsingDefaultsOrResultConstruction()
    {
        var source = ReadHostSource("WatermarkOptionsDialog.cs");

        source.Should().NotContain("ColorConverter");
        source.Should().NotContain("int.TryParse");
        source.Should().NotContain("NumberStyles.Integer");
        source.Should().NotContain("new WatermarkOptions(");
        source.Should().NotContain("Enter watermark text");
        source.Should().NotContain("Enter a valid colour hex value");
        source.Should().NotContain("Scale must be 0");
        source.Should().NotContain("FontFamily   =");
        source.Should().NotContain("ScalePct     =");
        source.Should().NotContain("Content = \"Text watermark\"");
        source.Should().NotContain("Content = \"Picture watermark\"");
        source.Should().NotContain("Content = \"Remove Watermark\"");
        source.Should().NotContain("Title = \"Select a watermark image\"");
        source.Should().NotContain("$\"Could not read image file:");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

}
