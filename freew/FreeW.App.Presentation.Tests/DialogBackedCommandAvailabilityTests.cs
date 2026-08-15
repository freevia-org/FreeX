namespace FreeW.App.Presentation.Tests;

public sealed class DialogBackedCommandAvailabilityTests
{
    [Fact]
    public void AvaloniaUsesSafeEditorFallbacksExceptForSplitCellWithoutNativeDialogs()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWAvaloniaRibbonCommands.cs"));

        source.Should().Contain("callbacks.OpenDateTimeDialog ?? (() => editor.InsertField(RunFieldKind.Date))");
        source.Should().Contain("OptionalHostCommand(callbacks.OpenSplitCellDialog)");
        source.Should().Contain("callbacks.InsertObject ?? (() => editor.InsertEmbeddedObject())");

        source.Should().NotContain("callbacks.OpenSplitCellDialog ??");
        source.Should().NotContain("editor.SplitCurrentCell()");
    }

    [Fact]
    public void AvaloniaProductionProfileSuppliesEveryRequiredNativeDialog()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("OpenDateTimeDialog: () => _ = OpenDateTimeDialogAsync()");
        source.Should().Contain("OpenSplitCellDialog: () => _ = OpenSplitCellDialogAsync()");
        source.Should().Contain("InsertObject:        () => _ = InsertEmbeddedObjectAsync()");
    }

    [Fact]
    public void WpfCounterpartsRemainDialogBackedCommands()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs"));

        source.Should().Contain("new InsertDateTimeCommand(resolveFieldTarget)");
        source.Should().Contain("new SplitCellRibbonCommand(editor)");
        source.Should().Contain("new InsertEmbeddedObjectCommand(editor)");
    }
}
