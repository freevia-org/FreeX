using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AvaloniaShellSourceTests
{
    [Fact]
    public void App_WiresMacOsFileActivationToMainWindowOpenPipeline()
    {
        var appSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "App.cs"));
        var programSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Program.cs"));
        var windowSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        programSource.Should().NotContain("DisableAvaloniaAppDelegate");
        appSource.Should().Contain("new MainWindow(StartupArguments)");
        appSource.Should().Contain("desktop.MainWindow = mainWindow;");
        appSource.Should().Contain("this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime");
        appSource.Should().Contain("activatableLifetime.Activated += async (_, args) => await MainWindow_ActivatedAsync(mainWindow, args);");
        appSource.Should().Contain("args is not FileActivatedEventArgs fileArgs");
        appSource.Should().Contain("fileArgs.Kind != ActivationKind.File");
        appSource.Should().Contain("mainWindow.Show();");
        appSource.Should().Contain("mainWindow.Activate();");
        appSource.Should().Contain("await mainWindow.OpenActivatedFilesAsync(fileArgs.Files);");

        windowSource.Should().Contain("public async Task OpenActivatedFilesAsync(IReadOnlyList<IStorageItem> files)");
        windowSource.Should().Contain("private bool TrySelectOpenableLocalWorkbookPath(IEnumerable<IStorageItem> files, out string? path, out string message)");
        windowSource.Should().Contain("TrySelectOpenableLocalWorkbookPath(files, out var path, out var message)");
        windowSource.Should().Contain("file.TryGetLocalPath()");
        windowSource.Should().Contain("ShowOpenIssue(message);");
        windowSource.Should().Contain("await OpenWorkbookPathAsync(path!)");
    }

    [Fact]
    public void MainWindow_WiresDroppedWorkbookFilesToSharedOpenPipeline()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ConfigureWorkbookDropTarget();");
        source.Should().Contain("DragDrop.SetAllowDrop(this, true);");
        source.Should().Contain("DragDrop.AddDragOverHandler(this, MainWindow_DragOver);");
        source.Should().Contain("DragDrop.AddDropHandler(this, MainWindow_Drop);");
        source.Should().Contain("e.DataTransfer.TryGetFiles()");
        source.Should().Contain("TrySelectOpenableLocalWorkbookPath(files, out path, out message)");
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

    [Fact]
    public void MainWindow_WiresNativeFileMenuToSharedOpenSavePipeline()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("ConfigureNativeMenu();");
        source.Should().Contain("private readonly NativeMenuItem _openMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _saveMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _saveAsMenuItem = new();");
        source.Should().Contain("_openMenuItem.Header = \"Open...\";");
        source.Should().Contain("_openMenuItem.Gesture = new KeyGesture(Key.O, KeyModifiers.Meta);");
        source.Should().Contain("_openMenuItem.Click += async (_, _) => await OpenWorkbookAsync();");
        source.Should().Contain("_saveMenuItem.Header = \"Save\";");
        source.Should().Contain("_saveMenuItem.Gesture = new KeyGesture(Key.S, KeyModifiers.Meta);");
        source.Should().Contain("_saveMenuItem.Click += async (_, _) => await SaveCurrentWorkbookAsync();");
        source.Should().Contain("_saveAsMenuItem.Header = \"Save As...\";");
        source.Should().Contain("_saveAsMenuItem.Gesture = new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift);");
        source.Should().Contain("_saveAsMenuItem.Click += async (_, _) => await SaveWorkbookAsAsync();");
        source.Should().Contain("Header = \"Quit FreeX\"");
        source.Should().Contain("Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta)");
        source.Should().Contain("quitMenuItem.Click += (_, _) => TryQuitApplication();");
        source.Should().Contain("NativeMenu.SetMenu(this, menu);");
        source.Should().Contain("menu.NeedsUpdate += (_, _) => UpdateSaveButton();");
        source.Should().Contain("_openMenuItem.IsEnabled = _openButton.IsEnabled;");
        source.Should().Contain("_saveMenuItem.IsEnabled = _saveButton.IsEnabled;");
        source.Should().Contain("_saveAsMenuItem.IsEnabled = _saveAsButton.IsEnabled;");
        source.Should().Contain("e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Shift)");
        source.Should().Contain("await SaveWorkbookAsAsync();");
        source.Should().Contain("TryQuitApplication()");
        source.Should().Contain("Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop");
        source.Should().Contain("desktop.TryShutdown(0);");
    }
}
