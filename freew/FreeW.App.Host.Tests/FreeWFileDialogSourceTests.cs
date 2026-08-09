using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class FreeWFileDialogSourceTests
{
    [Fact]
    public void RemainingFileDialogWorkflows_UseSharedWpfFileDialogService()
    {
        var sources = new[]
        {
            ReadHostSource("CompareDocumentsDialog.cs"),
            ReadHostSource("CombineDocumentsDialog.cs"),
            ReadHostSource("MainWindow.cs"),
            ReadHostSource("WatermarkOptionsDialog.cs"),
            ReadHostSource("Ribbon", "FreeWRibbonCommands.cs")
        };
        var combined = string.Join(Environment.NewLine, sources);

        combined.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        combined.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
        combined.Should().Contain("defaultExtensionWithDot: \".docx\"");
        combined.Should().Contain("\"Insert Text from File\"");
        combined.Should().Contain("\"Insert Picture\"");
        combined.Should().Contain("\"Insert Object\"");
        combined.Should().Contain("OlePackagePayloadBuilder.Create(");
        combined.Should().Contain("EmbeddedObject.Create(payload, OlePackagePayloadBuilder.ProgId)");
        combined.Should().NotContain("SampleEmbeddedObject");
        combined.Should().Contain("\"Export to PDF\"");
        combined.Should().Contain("\"Export to XPS\"");
        combined.Should().Contain("\"Compare: pick the ORIGINAL document\"");
        combined.Should().Contain("\"Combine: pick the ORIGINAL (base) document\"");
        combined.Should().Contain("\"Combine: pick Reviewer B's revised document\"");
        combined.Should().Contain("WatermarkOptionsDialogPlanner.SelectWatermarkImageTitle");
        combined.Should().Contain("WatermarkOptionsDialogPlanner.WatermarkImageFilter");
        combined.Should().NotContain("using Microsoft.Win32;");
        combined.Should().NotContain("new OpenFileDialog");
        combined.Should().NotContain("new Microsoft.Win32.OpenFileDialog");
        combined.Should().NotContain("new SaveFileDialog");
        combined.Should().NotContain("new Microsoft.Win32.SaveFileDialog");
    }

    private static string ReadHostSource(params string[] relativeParts)
    {
        var path = Path.Combine(new[] { TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host" }.Concat(relativeParts).ToArray());
        return File.ReadAllText(path);
    }

}
