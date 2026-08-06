using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SmartArtTextPaneReachabilitySourceTests
{
    [Fact]
    public void AvaloniaSmartArtTextPane_UsesWrappingCommandBandForFixedWidthHost()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../FreeP.App.Avalonia/MainWindow.cs"));
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain("_smartArtTextPaneCommandActions = new WrapPanel");
        source.Should().Contain("HorizontalAlignment = HorizontalAlignment.Left");
        source.Should().Contain("DockPanel.SetDock(_smartArtTextPaneCommandActions, Dock.Bottom)");
        source.Should().Contain("_smartArtTextPaneCommandActions.Children.Add(_smartArtTextPaneAssistantButton)");
        source.Should().Contain("_smartArtTextPaneCommandActions.Children.Add(_smartArtTextPaneApplyButton)");
        source.Should().Contain("_smartArtTextPaneCommandActions.Children.Add(_smartArtTextPaneCloseButton)");
        source.Should().Contain("Width = 320");
    }

    [Fact]
    public void AvaloniaSmartArtTextPane_RefreshesAfterEditorUndoRedo()
    {
        var mainWindowPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../FreeP.App.Avalonia/MainWindow.cs"));
        var endpointPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../FreeP.App.Avalonia/MainWindow.WorkareaEndpoint.cs"));
        var workareaSessionPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../FreeP.App.Presentation/PresentationWorkareaSession.cs"));
        var mainWindow = File.ReadAllText(mainWindowPath);
        var endpoint = File.ReadAllText(endpointPath);
        var workareaSession = File.ReadAllText(workareaSessionPath);

        mainWindow.Should().Contain("_workareaSession = new PresentationWorkareaSession(this);");
        endpoint.Should().Contain("PresentationWorkareaPane.SmartArtText => IsSmartArtTextPaneVisible");
        endpoint.Should().Contain(
            "PresentationWorkareaOperation.RefreshSmartArtPane => () => ShowSmartArtTextPane()");
        workareaSession.Should().Contain(
            "_endpoint.IsPaneVisible(PresentationWorkareaPane.SmartArtText)");
        workareaSession.Should().Contain(
            "operations.Add(PresentationWorkareaOperation.RefreshSmartArtPane);");
    }

    [Fact]
    public void AvaloniaSmartArtTextPane_IsReachableFromTheRibbonRegistry()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../FreeP.App.Avalonia/MainWindow.cs"));
        var source = File.ReadAllText(sourcePath);
        var workflow = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../FreeP.App.Presentation/Ribbon/FreePRibbonCommandWorkflow.cs")));

        workflow.Should().Contain("SmartArtEditingPlanner.OpenTextPaneCommandId");
        source.Should().Contain("OpenSmartArtTextPane = () => ShowSmartArtTextPane(),");
    }

    [Fact]
    public void SharedSmartArtSession_RejectsFailedNativeRefreshBeforeUndoCommit()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../FreeP.App.Presentation/PresentationSmartArtTextPaneSession.cs"));
        var source = File.ReadAllText(sourcePath);

        source.Should().Contain(
            "private bool CommitMutation(");
        source.Should().Contain(
            "if (LastDataPartRewriteResult is not { Applied: true })");
        source.Should().Contain(
            "return LastDrawingCacheRegenerationResult is { Applied: true };");
        source.Should().Contain(
            "Message = NativeRefreshFailureMessage");
    }
}
