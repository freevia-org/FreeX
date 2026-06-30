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
        var path = relativePath.Aggregate(FindRepositoryRoot(), Path.Combine);
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
