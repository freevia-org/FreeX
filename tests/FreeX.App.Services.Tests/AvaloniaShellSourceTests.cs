using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AvaloniaShellSourceTests
{
    [Fact]
    public void MainWindow_WiresDroppedWorkbookFilesToSharedOpenPipeline()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ConfigureWorkbookDropTarget();");
        source.Should().Contain("DragDrop.SetAllowDrop(this, true);");
        source.Should().Contain("DragDrop.AddDragOverHandler(this, MainWindow_DragOver);");
        source.Should().Contain("DragDrop.AddDropHandler(this, MainWindow_Drop);");
        source.Should().Contain("e.DataTransfer.TryGetFiles()");
        source.Should().Contain("file.TryGetLocalPath()");
        source.Should().Contain("_isOpening || _isSaving");
        source.Should().Contain("_session.IsDirty");
        source.Should().Contain("Directory.Exists(candidate)");
        source.Should().Contain("File.Exists(candidate)");
        source.Should().Contain("_session.TryResolveOpenTarget(candidate, out _, out unsupportedMessage)");
        source.Should().Contain("ShowOpenIssue(message)");
        source.Should().Contain("await OpenWorkbookPathAsync(path!)");
        source.Should().Contain("await OpenWorkbookFromTargetAsync(target!)");
        source.Should().Contain("DragDropEffects.Copy");
        source.Should().Contain("DragDropEffects.None");
    }
}
