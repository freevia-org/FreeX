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
        var outputWorkflow = ReadPresentationSource("Shell", "FreeWOutputWorkflow.cs");

        combined.Should().Contain("WpfFileDialogService.ShowOpenDialog(");
        combined.Should().Contain("WpfFileDialogService.ShowSaveDialog(");
        combined.Should().Contain("defaultExtensionWithDot: \".docx\"");
        combined.Should().Contain("\"Insert Text from File\"");
        combined.Should().Contain("\"Insert Picture\"");
        combined.Should().Contain("\"Insert Object\"");
        combined.Should().Contain("OlePackagePayloadBuilder.Create(");
        combined.Should().Contain("EmbeddedObject.Create(payload, OlePackagePayloadBuilder.ProgId)");
        combined.Should().NotContain("SampleEmbeddedObject");
        combined.Should().Contain("FreeWExportWorkflow.CreatePlan(");
        outputWorkflow.Should().Contain("FreeWFileTextResources.ExportPdfPickerTitle");
        outputWorkflow.Should().Contain("FreeWFileTextResources.ExportXpsPickerTitle");
        combined.Should().Contain("ReviewCompareCombineWorkflow.CompareOriginalPickerTitle");
        combined.Should().Contain("ReviewCompareCombineWorkflow.CombineOriginalPickerTitle");
        combined.Should().Contain("ReviewCompareCombineWorkflow.CombineReviewerBPickerTitle");
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

    private static string ReadPresentationSource(params string[] relativeParts)
    {
        var path = Path.Combine(new[] { TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Presentation" }.Concat(relativeParts).ToArray());
        return File.ReadAllText(path);
    }

}
