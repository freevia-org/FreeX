namespace Free.Shared.AppServices.Tests;

public sealed class FileByteReadWorkflowSourceGuardTests
{
    [Fact]
    public void SharedWorkflow_OwnsPathAndStreamByteReads()
    {
        var source = Read("shared", "Free.Shared.AppServices", "FileByteReadWorkflow.cs");

        source.Should().Contain("FileShare.Read");
        source.Should().Contain("FileOptions.Asynchronous | FileOptions.SequentialScan");
        source.Should().Contain("bytes.Length == 0 ? FileByteReadOutcome.Empty");
        source.Should().Contain("ReadLocalPathBytesAsync(");
        source.Should().Contain("ReadStreamBytesAsync(");
        File.Exists(Path("src", "FreeX.App.Services", "FileByteReadWorkflow.cs"))
            .Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(DirectImportReaderPaths))]
    public void DirectImportReaders_UseSharedWorkflow(string[] parts)
    {
        var source = Read(parts);

        source.Should().Contain("FileByteReadWorkflow.");
        source.Should().NotContain("File.ReadAllBytes(");
        source.Should().NotContain("File.ReadAllBytesAsync(");
        source.Should().NotContain("CopyToAsync(memory");
        source.Should().NotContain("CopyToAsync(output");
    }

    public static TheoryData<string[]> DirectImportReaderPaths => new()
    {
        { ["src", "FreeX.App.Host", "MainWindow.Drawing.cs"] },
        { ["src", "FreeX.App.Host", "MainWindow.PageLayout.cs"] },
        { ["src", "FreeX.App.Host", "HeaderFooterDialog.Pictures.cs"] },
        { ["src", "FreeX.App.Avalonia", "MainWindow.GetData.cs"] },
        { ["src", "FreeX.App.Avalonia", "MainWindow.InsertObjects.cs"] },
        { ["src", "FreeX.App.Avalonia", "MainWindow.PageLayout.cs"] },
        { ["src", "FreeX.App.Avalonia", "MainWindow.RibbonMenuWires.cs"] },
        { ["freew", "FreeW.App.Host", "PictureImport", "WpfPictureImportPorts.cs"] },
        { ["freew", "FreeW.App.Avalonia", "PictureImport", "AvaloniaPictureImportPorts.cs"] },
        { ["freew", "FreeW.App.Host", "DocumentFragments", "WpfDocumentFragmentImportPorts.cs"] },
        { ["freew", "FreeW.App.Avalonia", "DocumentFragments", "AvaloniaDocumentFragmentImportPorts.cs"] },
        { ["freew", "FreeW.App.Host", "WatermarkOptionsDialog.cs"] },
        { ["freew", "FreeW.App.Avalonia", "DesignDialogs.cs"] },
        { ["freep", "FreeP.App.Host", "MainWindow.AssetImports.cs"] },
        { ["freep", "FreeP.App.Avalonia", "MainWindow.AssetImports.cs"] },
    };

    private static string Read(params string[] parts) => File.ReadAllText(Path(parts));

    private static string Path(params string[] parts) =>
        TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts);
}
