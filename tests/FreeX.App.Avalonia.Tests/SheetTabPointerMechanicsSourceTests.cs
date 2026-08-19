namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class SheetTabPointerMechanicsSourceTests
{
    [Fact]
    public void SheetTabs_UseStableCaptureAndWpfCompatibleRoutes()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.SheetTabPointer.cs"));

        source.Should().Contain("_sheetTabsHost.PointerMoved += SheetTabDragPointerMoved");
        source.Should().Contain("_sheetTabsHost.PointerReleased += SheetTabDragPointerReleased");
        source.Should().Contain("_sheetTabsHost.PointerCaptureLost += SheetTabDragPointerCaptureLost");
        source.Should().Contain("_sheetTabsHost.PointerMoved -= SheetTabDragPointerMoved");
        source.Should().Contain("_sheetTabsHost.PointerReleased -= SheetTabDragPointerReleased");
        source.Should().Contain("_sheetTabsHost.PointerCaptureLost -= SheetTabDragPointerCaptureLost");
        source.Should().Contain("_session.MoveActiveSheetTo(toIndex)");
        source.Should().Contain("BeginShowActivateSheetDialogFromSheetNav");
        source.Should().Contain("SelectSheetForContextCommand(sheetId)");
        source.Should().Contain("if (args.ClickCount >= 2)");
        source.Should().Contain("RunGuarded(RenameActiveSheetAsync);");

        var pointerMove = source[
            source.IndexOf("private void SheetTabDragPointerMoved", StringComparison.Ordinal)..
            source.IndexOf("private void SheetTabDragPointerReleased", StringComparison.Ordinal)];
        pointerMove.Should().Contain("if (!point.Properties.IsLeftButtonPressed)");
        pointerMove.Should().Contain("CompleteSheetTabPointerRelease();");
        pointerMove.Should().NotContain("CommitSheetTabDragDrop();");
    }

    [Fact]
    public void SheetTabs_ArrowClicksScrollTheViewportAndRightClickOpensActivateRoute()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("button.Click += (_, _) => ScrollSheetTabs(direction);");
        source.Should().Contain("_sheetTabsScroller.ScrollChanged +=");
        source.Should().Contain("_sheetTabLeftNavButton.IsEnabled = hasOverflow;");
        source.Should().Contain("_sheetTabRightNavButton.IsEnabled = hasOverflow;");
        source.Should().Contain("var overflowViewportWidth = Math.Max(80, baseTabsViewportWidth - _sheetTabRightNavButton.Width);");
        source.Should().Contain("_sheetTabsContourLayer.ZIndex = 1;");
        source.Should().Contain("ContentPresenter.ForegroundProperty");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
