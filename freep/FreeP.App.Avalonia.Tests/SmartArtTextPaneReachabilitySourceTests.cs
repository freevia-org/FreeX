using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SmartArtTextPaneReachabilitySourceTests
{
    [Fact]
    public void AvaloniaSmartArtTextPane_UsesWrappingCommandBandForFixedWidthHost()
    {
        var sourcePath = TestWorkspaceFileLocator.Find(
            "freep", "FreeP.App.Avalonia", "MainWindow.cs");
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
        var mainWindowPath = TestWorkspaceFileLocator.Find(
            "freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var endpointPath = TestWorkspaceFileLocator.Find(
            "freep", "FreeP.App.Avalonia", "MainWindow.WorkareaEndpoint.cs");
        var workareaSessionPath = TestWorkspaceFileLocator.Find(
            "freep", "FreeP.App.Presentation", "PresentationWorkareaSession.cs");
        var mainWindow = File.ReadAllText(mainWindowPath);
        var endpoint = File.ReadAllText(endpointPath);
        var workareaSession = File.ReadAllText(workareaSessionPath);

        mainWindow.Should().Contain(
            "_workareaSession = new PresentationWorkareaSession(CreateWorkareaEndpoint());");
        mainWindow.Should().Contain(
            "_workareaSession.Panes.Show(PresentationWorkareaPane.SmartArtText)");
        endpoint.Should().Contain("RefreshSmartArtPane = () => ShowSmartArtTextPane()");
        workareaSession.Should().Contain(
            "Panes.IsVisible(PresentationWorkareaPane.SmartArtText)");
        workareaSession.Should().Contain(
            "operations.Add(PresentationWorkareaOperation.RefreshSmartArtPane);");
    }

    [Fact]
    public void AvaloniaSmartArtTextPane_IsReachableFromTheRibbonRegistry()
    {
        var sourcePath = TestWorkspaceFileLocator.Find(
            "freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var source = File.ReadAllText(sourcePath);
        var workflow = TestWorkspaceFileLocator.ReadAllText(
            "freep", "FreeP.App.Presentation", "Ribbon", "FreePRibbonCommandWorkflow.cs");

        workflow.Should().Contain("SmartArtEditingPlanner.OpenTextPaneCommandId");
        source.Should().Contain("OpenSmartArtTextPane = () => ShowSmartArtTextPane(),");
    }

    [Fact]
    public void SharedSmartArtSession_RejectsFailedNativeRefreshBeforeUndoCommit()
    {
        var sourcePath = TestWorkspaceFileLocator.Find(
            "freep", "FreeP.App.Presentation", "PresentationSmartArtTextPaneSession.cs");
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
