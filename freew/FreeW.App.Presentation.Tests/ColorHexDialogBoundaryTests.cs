using System.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class ColorHexDialogBoundaryTests
{
    [Fact]
    public void PresentationColorHexPoliciesStayLocalToDialogContracts()
    {
        var watermark = ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "WatermarkOptionsDialogPlanner.cs");
        watermark.Should().Contain("Word-dialog-friendly color text");
        watermark.Should().Contain("3/4/6/8 hex digits");
        watermark.Should().NotContain("DrawingMlRgbColor.TryParseHexRgb(");
        watermark.Should().NotContain("ThemeColor.FromHex(");

        var imageBorder = ReadSource("freew", "FreeW.App.Presentation", "Dialogs", "ImageBorderDialogPlanner.cs");
        imageBorder.Should().Contain("blank removes the border");
        imageBorder.Should().Contain("bare six-digit RGB");
        imageBorder.Should().NotContain("ThemeColor.FromHex(");
    }

    private static string ReadSource(params string[] relativePath)
    {
        var path = relativePath.Aggregate(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine);
        return File.ReadAllText(path);
    }

}
