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
        source.Should().Contain("private readonly NativeMenuItem _undoMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _redoMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _cutMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _copyMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _pasteMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _clearContentsMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _boldMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _italicMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _underlineMenuItem = new();");
        source.Should().Contain("_openMenuItem.Header = \"Open...\";");
        source.Should().Contain("_openMenuItem.Gesture = new KeyGesture(Key.O, KeyModifiers.Meta);");
        source.Should().Contain("_openMenuItem.Click += async (_, _) => await OpenWorkbookAsync();");
        source.Should().Contain("_saveMenuItem.Header = \"Save\";");
        source.Should().Contain("_saveMenuItem.Gesture = new KeyGesture(Key.S, KeyModifiers.Meta);");
        source.Should().Contain("_saveMenuItem.Click += async (_, _) => await SaveCurrentWorkbookAsync();");
        source.Should().Contain("_saveAsMenuItem.Header = \"Save As...\";");
        source.Should().Contain("_saveAsMenuItem.Gesture = new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift);");
        source.Should().Contain("_saveAsMenuItem.Click += async (_, _) => await SaveWorkbookAsAsync();");
        source.Should().Contain("_undoMenuItem.Header = \"Undo\";");
        source.Should().Contain("_undoMenuItem.Gesture = new KeyGesture(Key.Z, KeyModifiers.Meta);");
        source.Should().Contain("_undoMenuItem.Click += (_, _) => UndoLastEdit();");
        source.Should().Contain("_redoMenuItem.Header = \"Redo\";");
        source.Should().Contain("_redoMenuItem.Gesture = new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift);");
        source.Should().Contain("_redoMenuItem.Click += (_, _) => RedoLastEdit();");
        source.Should().Contain("_cutMenuItem.Header = \"Cut\";");
        source.Should().Contain("_cutMenuItem.Gesture = new KeyGesture(Key.X, KeyModifiers.Meta);");
        source.Should().Contain("_cutMenuItem.Click += async (_, _) => await CutSelectedRangeToClipboardAsync();");
        source.Should().Contain("_copyMenuItem.Header = \"Copy\";");
        source.Should().Contain("_copyMenuItem.Gesture = new KeyGesture(Key.C, KeyModifiers.Meta);");
        source.Should().Contain("_copyMenuItem.Click += async (_, _) => await CopySelectedRangeToClipboardAsync();");
        source.Should().Contain("_pasteMenuItem.Header = \"Paste\";");
        source.Should().Contain("_pasteMenuItem.Gesture = new KeyGesture(Key.V, KeyModifiers.Meta);");
        source.Should().Contain("_pasteMenuItem.Click += async (_, _) => await PasteClipboardTextAsync();");
        source.Should().Contain("_clearContentsMenuItem.Header = \"Clear Contents\";");
        source.Should().Contain("_clearContentsMenuItem.Gesture = new KeyGesture(Key.Delete);");
        source.Should().Contain("_clearContentsMenuItem.Click += (_, _) => ClearSelectedRangeContents();");
        source.Should().Contain("_boldMenuItem.Header = \"Bold\";");
        source.Should().Contain("_boldMenuItem.Gesture = new KeyGesture(Key.B, KeyModifiers.Meta);");
        source.Should().Contain("_boldMenuItem.Click += (_, _) => ToggleSelectedRangeBold();");
        source.Should().Contain("_italicMenuItem.Header = \"Italic\";");
        source.Should().Contain("_italicMenuItem.Gesture = new KeyGesture(Key.I, KeyModifiers.Meta);");
        source.Should().Contain("_italicMenuItem.Click += (_, _) => ToggleSelectedRangeItalic();");
        source.Should().Contain("_underlineMenuItem.Header = \"Underline\";");
        source.Should().Contain("_underlineMenuItem.Gesture = new KeyGesture(Key.U, KeyModifiers.Meta);");
        source.Should().Contain("_underlineMenuItem.Click += (_, _) => ToggleSelectedRangeUnderline();");
        source.Should().Contain("_quitMenuItem.Header = \"Quit FreeX\";");
        source.Should().Contain("_quitMenuItem.Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta);");
        source.Should().Contain("_quitMenuItem.Click += (_, _) => TryQuitApplication();");
        source.Should().Contain("editMenu.Items.Add(_undoMenuItem);");
        source.Should().Contain("editMenu.Items.Add(_redoMenuItem);");
        source.Should().Contain("editMenu.Items.Add(_cutMenuItem);");
        source.Should().Contain("editMenu.Items.Add(_copyMenuItem);");
        source.Should().Contain("editMenu.Items.Add(_pasteMenuItem);");
        source.Should().Contain("editMenu.Items.Add(_clearContentsMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_boldMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_italicMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_underlineMenuItem);");
        source.Should().Contain("Header = \"Edit\"");
        source.Should().Contain("Header = \"Format\"");
        source.Should().Contain("NativeMenu.SetMenu(this, _nativeMenu);");
        source.Should().Contain("_nativeMenu.NeedsUpdate += (_, _) => UpdateSaveButton();");
        source.Should().Contain("_openMenuItem.IsEnabled = _openButton.IsEnabled;");
        source.Should().Contain("_saveMenuItem.IsEnabled = _saveButton.IsEnabled;");
        source.Should().Contain("_saveAsMenuItem.IsEnabled = _saveAsButton.IsEnabled;");
        source.Should().Contain("_undoMenuItem.IsEnabled = _undoButton.IsEnabled;");
        source.Should().Contain("_redoMenuItem.IsEnabled = _redoButton.IsEnabled;");
        source.Should().Contain("_cutMenuItem.IsEnabled = _cutButton.IsEnabled;");
        source.Should().Contain("_copyMenuItem.IsEnabled = _copyButton.IsEnabled;");
        source.Should().Contain("_pasteMenuItem.IsEnabled = _pasteButton.IsEnabled;");
        source.Should().Contain("_clearContentsMenuItem.IsEnabled = _clearContentsButton.IsEnabled;");
        source.Should().Contain("_boldMenuItem.IsEnabled = _boldButton.IsEnabled;");
        source.Should().Contain("_italicMenuItem.IsEnabled = _italicButton.IsEnabled;");
        source.Should().Contain("_underlineMenuItem.IsEnabled = _underlineButton.IsEnabled;");
        source.Should().Contain("e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Shift)");
        source.Should().Contain("await SaveWorkbookAsAsync();");
        source.Should().Contain("TryQuitApplication()");
        source.Should().Contain("Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop");
        source.Should().Contain("desktop.TryShutdown(0);");
    }

    [Fact]
    public void App_WiresMacOsLaunchSmokeToRuntimeSnapshot()
    {
        var appSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "App.cs"));
        var programSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "Program.cs"));
        var smokeSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MacOsLaunchSmoke.cs"));
        var windowSource = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        programSource.Should().Contain("MacOsLaunchSmokeOptions.TryParse(");
        programSource.Should().Contain("out var launchSmokeOptions");
        programSource.Should().Contain("out var startupArguments");
        programSource.Should().Contain("App.LaunchSmokeOptions = launchSmokeOptions;");
        programSource.Should().Contain("StartWithClassicDesktopLifetime(startupArguments)");
        appSource.Should().Contain("internal static MacOsLaunchSmokeOptions? LaunchSmokeOptions { get; set; }");
        appSource.Should().Contain("if (LaunchSmokeOptions is { } launchSmokeOptions)");
        appSource.Should().Contain("MacOsLaunchSmokeCoordinator.Start(mainWindow, launchSmokeOptions);");
        smokeSource.Should().Contain("public const string Argument = \"--macos-launch-smoke\";");
        smokeSource.Should().Contain("startupArguments = filteredArguments.ToArray();");
        smokeSource.Should().Contain("mainWindow.Opened += async (_, _) => await RunAsync(mainWindow, options);");
        smokeSource.Should().Contain("mainWindow.CreateLaunchSmokeSnapshot()");
        smokeSource.Should().Contain("macos_launch_smoke={(snapshot.IsPassed ? \"passed\" : \"failed\")}");
        smokeSource.Should().Contain("opened_source_path={snapshot.OpenedSourcePath ?? \"\"}");
        smokeSource.Should().Contain("native_file_menu={FormatBool(snapshot.HasNativeFileMenu)}");
        smokeSource.Should().Contain("native_edit_menu={FormatBool(snapshot.HasNativeEditMenu)}");
        smokeSource.Should().Contain("native_format_menu={FormatBool(snapshot.HasNativeFormatMenu)}");
        smokeSource.Should().Contain("native_undo_menu_item={FormatBool(snapshot.HasNativeUndoMenuItem)}");
        smokeSource.Should().Contain("native_redo_menu_item={FormatBool(snapshot.HasNativeRedoMenuItem)}");
        smokeSource.Should().Contain("native_cut_menu_item={FormatBool(snapshot.HasNativeCutMenuItem)}");
        smokeSource.Should().Contain("native_copy_menu_item={FormatBool(snapshot.HasNativeCopyMenuItem)}");
        smokeSource.Should().Contain("native_paste_menu_item={FormatBool(snapshot.HasNativePasteMenuItem)}");
        smokeSource.Should().Contain("native_clear_contents_menu_item={FormatBool(snapshot.HasNativeClearContentsMenuItem)}");
        smokeSource.Should().Contain("native_bold_menu_item={FormatBool(snapshot.HasNativeBoldMenuItem)}");
        smokeSource.Should().Contain("native_italic_menu_item={FormatBool(snapshot.HasNativeItalicMenuItem)}");
        smokeSource.Should().Contain("native_underline_menu_item={FormatBool(snapshot.HasNativeUnderlineMenuItem)}");
        smokeSource.Should().Contain("desktop.TryShutdown(exitCode);");
        windowSource.Should().Contain("private readonly NativeMenuItem _quitMenuItem = new();");
        windowSource.Should().Contain("private NativeMenu? _nativeMenu;");
        windowSource.Should().Contain("NativeMenu.SetMenu(this, _nativeMenu);");
        windowSource.Should().Contain("internal MacOsLaunchSmokeSnapshot CreateLaunchSmokeSnapshot()");
        windowSource.Should().Contain("_nativeMenu?.Items.OfType<NativeMenuItem>().Any");
        windowSource.Should().Contain("WindowShown: IsVisible");
        windowSource.Should().Contain("OpenedSourcePath: _session.CurrentFilePath");
        windowSource.Should().Contain("HasNativeEditMenu: hasNativeEditMenu");
        windowSource.Should().Contain("HasNativeFormatMenu: hasNativeFormatMenu");
        windowSource.Should().Contain("HasNativeUndoMenuItem: HasNativeMenuItem(_undoMenuItem, \"Undo\")");
        windowSource.Should().Contain("HasNativeRedoMenuItem: HasNativeMenuItem(_redoMenuItem, \"Redo\")");
        windowSource.Should().Contain("HasNativeCutMenuItem: HasNativeMenuItem(_cutMenuItem, \"Cut\")");
        windowSource.Should().Contain("HasNativeCopyMenuItem: HasNativeMenuItem(_copyMenuItem, \"Copy\")");
        windowSource.Should().Contain("HasNativePasteMenuItem: HasNativeMenuItem(_pasteMenuItem, \"Paste\")");
        windowSource.Should().Contain("HasNativeClearContentsMenuItem: HasNativeMenuItem(_clearContentsMenuItem, \"Clear Contents\")");
        windowSource.Should().Contain("HasNativeBoldMenuItem: HasNativeMenuItem(_boldMenuItem, \"Bold\")");
        windowSource.Should().Contain("HasNativeItalicMenuItem: HasNativeMenuItem(_italicMenuItem, \"Italic\")");
        windowSource.Should().Contain("HasNativeUnderlineMenuItem: HasNativeMenuItem(_underlineMenuItem, \"Underline\")");
        windowSource.Should().Contain("HasNativeQuitMenuItem: HasNativeMenuItem(_quitMenuItem, \"Quit FreeX\")");
    }

    [Fact]
    public void MainWindow_RendersNonInteractiveDrawingObjectBoundsOverlay()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("_sessionFactory.Create(source, InitialViewportHeight, InitialViewportWidth, includeObjects: true)");
        source.Should().Contain("_sessionFactory.CreateOpened(target, result, viewportHeight, viewportWidth, includeObjects: true)");
        source.Should().Contain("private Canvas BuildDrawingObjectOverlay(ViewportModel viewport)");
        source.Should().Contain("viewport.DrawingObjects is not { Count: > 0 }");
        source.Should().Contain("TryGetDisplayedDrawingObjectBounds(");
        source.Should().Contain("drawingObject.AnchorCol");
        source.Should().Contain("drawingObject.AnchorRow");
        source.Should().Contain("IsHitTestVisible = false");
        source.Should().Contain("Canvas.SetLeft(marker, left);");
        source.Should().Contain("Canvas.SetTop(marker, top);");
        source.Should().Contain("GetDisplayedColumnWidth(metric)");
        source.Should().Contain("GetDisplayedRowHeight(metric)");
        source.Should().Contain("new RotateTransform(drawingObject.RotationDegrees)");
    }

    [Fact]
    public void MainWindow_WiresManualWorksheetScrollBarsToSessionViewport()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly ScrollBar _verticalWorksheetScrollBar = new();");
        source.Should().Contain("private readonly ScrollBar _horizontalWorksheetScrollBar = new();");
        source.Should().Contain("private bool _isUpdatingWorksheetScrollBars;");
        source.Should().Contain("root.Children.Add(BuildWorksheetViewportChrome());");
        source.Should().Contain("_sheetScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;");
        source.Should().Contain("_sheetScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;");
        source.Should().Contain("_verticalWorksheetScrollBar.Orientation = Orientation.Vertical;");
        source.Should().Contain("_horizontalWorksheetScrollBar.Orientation = Orientation.Horizontal;");
        source.Should().Contain("_verticalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;");
        source.Should().Contain("_horizontalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;");
        source.Should().Contain("WorkbookViewportScrollPlanner.Create(_session.ActiveSheet, _session.Viewport)");
        source.Should().Contain("ApplyWorksheetScrollAxis(_verticalWorksheetScrollBar, state.Vertical);");
        source.Should().Contain("ApplyWorksheetScrollAxis(_horizontalWorksheetScrollBar, state.Horizontal);");
        source.Should().Contain("WorkbookViewportScrollPlanner.CalculateViewportOrigin(");
        source.Should().Contain("_session.SetViewportOrigin(topRow, leftCol)");
        source.Should().Contain("UpdateViewportScrollBars();");
    }

    [Fact]
    public void MainWindow_WiresCellEditingThroughFormulaBoxAndKeyboardEntry()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly TextBox _formulaBox = new();");
        source.Should().Contain("private string? _formulaBoxEditOriginalText;");
        source.Should().Contain("_formulaBox.GotFocus += FormulaBox_GotFocus;");
        source.Should().Contain("_formulaBox.KeyDown += FormulaBox_KeyDown;");
        source.Should().Contain("TextInput += MainWindow_TextInput;");
        source.Should().Contain("border.DoubleTapped += (_, args) =>");
        source.Should().Contain("BeginFormulaEdit(address);");
        source.Should().Contain("private void FormulaBox_GotFocus(object? sender, FocusChangedEventArgs e)");
        source.Should().Contain("_session.BeginFormulaEdit(_session.ActiveCell);");
        source.Should().Contain("_formulaBoxEditOriginalText = _formulaBox.Text ?? \"\";");
        source.Should().Contain("if (e.Key == Key.F2)");
        source.Should().Contain("BeginFormulaEdit(_session.ActiveCell);");
        source.Should().Contain("private void MainWindow_TextInput(object? sender, TextInputEventArgs e)");
        source.Should().Contain("string.IsNullOrEmpty(e.Text)");
        source.Should().Contain("char.IsControl(character)");
        source.Should().Contain("BeginFormulaEdit(_session.ActiveCell, e.Text);");
        source.Should().Contain("else if (e.Key == Key.Tab)");
        source.Should().Contain("e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1");
        source.Should().Contain("_session.MoveActiveCell(0, colDelta);");
        source.Should().Contain("var result = _session.CommitCellText(_formulaBox.Text ?? \"\");");
        source.Should().Contain("RefreshShell($\"Edited {FormatCellReference(address)}\");");
        source.Should().Contain("private bool TryCommitPendingFormulaEdit()");
        source.Should().Contain("private bool HasPendingFormulaEditText() =>");
        source.Should().Contain("_session.CancelFormulaEdit();");
        source.Should().Contain("StringComparison.Ordinal");
        source.Should().Contain("if (!TryCommitPendingFormulaEdit())");
        source.Should().Contain("Finish the current cell edit before opening another workbook.");
    }

    [Fact]
    public void MainWindow_WiresUndoRedoThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly Button _undoButton = new();");
        source.Should().Contain("private readonly Button _redoButton = new();");
        source.Should().Contain("_undoButton.Content = \"Undo\";");
        source.Should().Contain("_redoButton.Content = \"Redo\";");
        source.Should().Contain("_undoButton.Click += UndoButton_Click;");
        source.Should().Contain("_redoButton.Click += RedoButton_Click;");
        source.Should().Contain("_undoButton.IsEnabled = isIdle && _session.CanUndo;");
        source.Should().Contain("_redoButton.IsEnabled = isIdle && _session.CanRedo;");
        source.Should().Contain("e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)");
        source.Should().Contain("RedoLastEdit();");
        source.Should().Contain("else if (e.Key == Key.Z)");
        source.Should().Contain("UndoLastEdit();");
        source.Should().Contain("else if (e.Key == Key.Y)");
        source.Should().Contain("private void UndoLastEdit()");
        source.Should().Contain("private void RedoLastEdit()");
        source.Should().Contain("if (!TryCommitPendingFormulaEdit())");
        source.Should().Contain("ApplyEditHistoryResult(_session.UndoLastEdit(), \"Undid last edit\");");
        source.Should().Contain("ApplyEditHistoryResult(_session.RedoLastEdit(), \"Redid last edit\");");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Edit history unavailable.\");");
    }

    [Fact]
    public void MainWindow_WiresClipboardCopyPasteThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly Button _cutButton = new();");
        source.Should().Contain("private readonly Button _copyButton = new();");
        source.Should().Contain("private readonly Button _pasteButton = new();");
        source.Should().Contain("_cutButton.Content = \"Cut\";");
        source.Should().Contain("_copyButton.Content = \"Copy\";");
        source.Should().Contain("_pasteButton.Content = \"Paste\";");
        source.Should().Contain("_cutButton.Click += CutButton_Click;");
        source.Should().Contain("_copyButton.Click += CopyButton_Click;");
        source.Should().Contain("_pasteButton.Click += PasteButton_Click;");
        source.Should().Contain("_cutButton.IsEnabled = isIdle;");
        source.Should().Contain("_copyButton.IsEnabled = isIdle;");
        source.Should().Contain("_pasteButton.IsEnabled = isIdle;");
        source.Should().Contain("private async Task CutSelectedRangeToClipboardAsync()");
        source.Should().Contain("private async Task CopySelectedRangeToClipboardAsync()");
        source.Should().Contain("private async Task PasteClipboardTextAsync()");
        source.Should().Contain("using Avalonia.Input.Platform;");
        source.Should().Contain("TopLevel.GetTopLevel(this)?.Clipboard");
        source.Should().Contain("await clipboard.SetTextAsync(_session.CutSelectedRangeText());");
        source.Should().Contain("await clipboard.SetTextAsync(_session.CopySelectedRangeText());");
        source.Should().Contain("var text = await clipboard.TryGetTextAsync();");
        source.Should().Contain("_session.PasteClipboardTextAtActiveCell(text)");
        source.Should().Contain("_session.SelectedRange.Contains(address)");
        source.Should().Contain("private bool IsSelectedColumn(uint col)");
        source.Should().Contain("private bool IsSelectedRow(uint row)");
        source.Should().Contain("args.KeyModifiers.HasFlag(KeyModifiers.Shift)");
        source.Should().Contain("_session.SelectRange(new GridRange(_session.ActiveCell, address));");
        source.Should().Contain("private static string FormatRangeReference(GridRange range)");
        source.Should().Contain("if (_formulaBox.IsFocused &&");
        source.Should().Contain("e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.B or Key.I or Key.U or Key.D4 or Key.NumPad4)");
        source.Should().Contain("else if (e.Key == Key.X)");
        source.Should().Contain("await CutSelectedRangeToClipboardAsync();");
        source.Should().Contain("else if (e.Key == Key.C)");
        source.Should().Contain("await CopySelectedRangeToClipboardAsync();");
        source.Should().Contain("else if (e.Key == Key.V)");
        source.Should().Contain("await PasteClipboardTextAsync();");
        source.Should().Contain("ShowEditIssue(\"Clipboard unavailable on this platform.\");");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Paste failed.\");");
    }

    [Fact]
    public void MainWindow_WiresClearContentsThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly Button _clearContentsButton = new();");
        source.Should().Contain("_clearContentsButton.Content = \"Clear\";");
        source.Should().Contain("_clearContentsButton.Click += ClearContentsButton_Click;");
        source.Should().Contain("_clearContentsButton.IsEnabled = isIdle;");
        source.Should().Contain("private void ClearContentsButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void ClearSelectedRangeContents()");
        source.Should().Contain("var result = _session.ClearSelectedRangeContents();");
        source.Should().Contain("RefreshShell($\"Cleared {rangeReference}\");");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Clear Contents failed.\");");
        source.Should().Contain("if (e.Key == Key.Delete)");
        source.Should().Contain("ClearSelectedRangeContents();");
    }

    [Fact]
    public void MainWindow_WiresBoldThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly ToggleButton _boldButton = new();");
        source.Should().Contain("_boldButton.Content = \"B\";");
        source.Should().Contain("_boldButton.FontWeight = FontWeight.Bold;");
        source.Should().Contain("_boldButton.Click += BoldButton_Click;");
        source.Should().Contain("_boldButton.IsChecked = _session.IsSelectedRangeStartBold;");
        source.Should().Contain("_boldButton.IsEnabled = isIdle;");
        source.Should().Contain("private void BoldButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeBold(_boldButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeBold()");
        source.Should().Contain("private void ApplySelectedRangeBold(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeBold(enabled);");
        source.Should().Contain("_boldButton.IsChecked = _session.IsSelectedRangeStartBold;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Bold failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Bolded\" : \"Unbolded\")} {rangeReference}\");");
        source.Should().Contain("private static bool HasOnlyCommandModifier(KeyModifiers modifiers)");
        source.Should().Contain("else if (e.Key == Key.B && HasOnlyCommandModifier(e.KeyModifiers))");
        source.Should().Contain("ToggleSelectedRangeBold();");
    }

    [Fact]
    public void MainWindow_WiresItalicThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly ToggleButton _italicButton = new();");
        source.Should().Contain("_italicButton.Content = \"I\";");
        source.Should().Contain("_italicButton.FontStyle = FontStyle.Italic;");
        source.Should().Contain("_italicButton.Click += ItalicButton_Click;");
        source.Should().Contain("_italicButton.IsChecked = _session.IsSelectedRangeStartItalic;");
        source.Should().Contain("_italicButton.IsEnabled = isIdle;");
        source.Should().Contain("private void ItalicButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeItalic(_italicButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeItalic()");
        source.Should().Contain("private void ApplySelectedRangeItalic(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeItalic(enabled);");
        source.Should().Contain("_italicButton.IsChecked = _session.IsSelectedRangeStartItalic;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Italic failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Italicized\" : \"Unitalicized\")} {rangeReference}\");");
        source.Should().Contain("FontStyle = fontStyle,");
        source.Should().Contain("var fontStyle = style?.Italic == true ? FontStyle.Italic : FontStyle.Normal;");
        source.Should().Contain("else if (e.Key == Key.I && HasOnlyCommandModifier(e.KeyModifiers))");
        source.Should().Contain("ToggleSelectedRangeItalic();");
    }

    [Fact]
    public void MainWindow_WiresUnderlineThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly ToggleButton _underlineButton = new();");
        source.Should().Contain("_underlineButton.Content = new TextBlock");
        source.Should().Contain("TextDecorations = TextDecorations.Underline,");
        source.Should().Contain("_underlineButton.Click += UnderlineButton_Click;");
        source.Should().Contain("_underlineButton.IsChecked = _session.IsSelectedRangeStartUnderline;");
        source.Should().Contain("_underlineButton.IsEnabled = isIdle;");
        source.Should().Contain("private void UnderlineButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeUnderline(_underlineButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeUnderline()");
        source.Should().Contain("private void ApplySelectedRangeUnderline(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeUnderline(enabled);");
        source.Should().Contain("_underlineButton.IsChecked = _session.IsSelectedRangeStartUnderline;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Underline failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Underlined\" : \"Removed underline from\")} {rangeReference}\");");
        source.Should().Contain("var textDecorations = style?.Underline == true ? TextDecorations.Underline : null;");
        source.Should().Contain("TextDecorations = textDecorations,");
        source.Should().Contain("else if (e.Key == Key.U && HasOnlyCommandModifier(e.KeyModifiers))");
        source.Should().Contain("else if (e.Key is Key.D4 or Key.NumPad4 && HasOnlyControlModifier(e.KeyModifiers))");
        source.Should().Contain("ToggleSelectedRangeUnderline();");
    }

    [Fact]
    public void MainWindow_RendersSelectedRangeStatsThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly TextBlock _selectionStatsText = new();");
        source.Should().Contain("_selectionStatsText.FontSize = 12;");
        source.Should().Contain("_selectionStatsText.MaxWidth = 420;");
        source.Should().Contain("_selectionStatsText.TextTrimming = TextTrimming.CharacterEllipsis;");
        source.Should().Contain("_statusText,");
        source.Should().Contain("_selectionStatsText,");
        source.Should().Contain("_statusText.Text = status;");
        source.Should().Contain("_selectionStatsText.Text = _session.SelectionStatsText;");
        source.Should().Contain("_session.SelectRange(new GridRange(_session.ActiveCell, address));");
        source.Should().Contain("RefreshShell(\"Ready\");");
    }
}
