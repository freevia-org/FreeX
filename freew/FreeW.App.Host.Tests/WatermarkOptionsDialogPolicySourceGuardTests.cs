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
        source.Should().Contain("WatermarkOptionsDialogPlanner.FormatPickedImageLabel(");
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
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(FindRepositoryRoot(), "freew", "FreeW.App.Host", fileName);
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
