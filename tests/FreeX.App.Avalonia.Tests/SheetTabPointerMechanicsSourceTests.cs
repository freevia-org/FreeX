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
        source.Should().Contain("_ = RenameActiveSheetAsync();");

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
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;
        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");
        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
