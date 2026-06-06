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
        source.Should().Contain("private readonly NativeMenuItem _doubleUnderlineMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _strikethroughMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _increaseFontSizeMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _decreaseFontSizeMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _fillColorMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _clearFillMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _fontColorMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _horizontalTextMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _angleCounterclockwiseMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _angleClockwiseMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _verticalTextMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _rotateTextUpMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _rotateTextDownMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _currencyFormatMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _percentFormatMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _commaStyleMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _increaseDecimalMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _decreaseDecimalMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignLeftMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignCenterMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignRightMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignTopMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignMiddleMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _alignBottomMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _wrapTextMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _decreaseIndentMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _increaseIndentMenuItem = new();");
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
        source.Should().Contain("_doubleUnderlineMenuItem.Header = \"Double Underline\";");
        source.Should().Contain("_doubleUnderlineMenuItem.Click += (_, _) => ToggleSelectedRangeDoubleUnderline();");
        source.Should().Contain("_strikethroughMenuItem.Header = \"Strikethrough\";");
        source.Should().Contain("_strikethroughMenuItem.Gesture = new KeyGesture(Key.D5, KeyModifiers.Control);");
        source.Should().Contain("_strikethroughMenuItem.Click += (_, _) => ToggleSelectedRangeStrikethrough();");
        source.Should().Contain("_increaseFontSizeMenuItem.Header = \"Increase Font Size\";");
        source.Should().Contain("_increaseFontSizeMenuItem.Click += (_, _) => IncreaseSelectedRangeFontSize();");
        source.Should().Contain("_decreaseFontSizeMenuItem.Header = \"Decrease Font Size\";");
        source.Should().Contain("_decreaseFontSizeMenuItem.Click += (_, _) => DecreaseSelectedRangeFontSize();");
        source.Should().Contain("_fillColorMenuItem.Header = \"Fill Color\";");
        source.Should().Contain("_fillColorMenuItem.Click += (_, _) => ApplyDefaultSelectedRangeFillColor();");
        source.Should().Contain("_clearFillMenuItem.Header = \"No Fill\";");
        source.Should().Contain("_clearFillMenuItem.Click += (_, _) => ClearSelectedRangeFill();");
        source.Should().Contain("_fontColorMenuItem.Header = \"Font Color\";");
        source.Should().Contain("_fontColorMenuItem.Click += (_, _) => ApplyDefaultSelectedRangeFontColor();");
        source.Should().Contain("_horizontalTextMenuItem.Header = \"Horizontal\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(0, \"Set horizontal text for\", \"Horizontal Text failed.\");");
        source.Should().Contain("_angleCounterclockwiseMenuItem.Header = \"Angle Counterclockwise\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(45, \"Angled text counterclockwise for\", \"Angle Counterclockwise failed.\");");
        source.Should().Contain("_angleClockwiseMenuItem.Header = \"Angle Clockwise\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(-45, \"Angled text clockwise for\", \"Angle Clockwise failed.\");");
        source.Should().Contain("_verticalTextMenuItem.Header = \"Vertical Text\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(255, \"Set vertical text for\", \"Vertical Text failed.\");");
        source.Should().Contain("_rotateTextUpMenuItem.Header = \"Rotate Text Up\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(90, \"Rotated text up for\", \"Rotate Text Up failed.\");");
        source.Should().Contain("_rotateTextDownMenuItem.Header = \"Rotate Text Down\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(-90, \"Rotated text down for\", \"Rotate Text Down failed.\");");
        source.Should().Contain("_currencyFormatMenuItem.Header = \"Accounting Number Format\";");
        source.Should().Contain("_currencyFormatMenuItem.Click += (_, _) => ApplySelectedRangeCurrencyFormat();");
        source.Should().Contain("_percentFormatMenuItem.Header = \"Percent Style\";");
        source.Should().Contain("_percentFormatMenuItem.Click += (_, _) => ApplySelectedRangePercentFormat();");
        source.Should().Contain("_commaStyleMenuItem.Header = \"Comma Style\";");
        source.Should().Contain("_commaStyleMenuItem.Click += (_, _) => ApplySelectedRangeCommaStyle();");
        source.Should().Contain("_increaseDecimalMenuItem.Header = \"Increase Decimal Places\";");
        source.Should().Contain("_increaseDecimalMenuItem.Click += (_, _) => IncreaseSelectedRangeDecimalPlaces();");
        source.Should().Contain("_decreaseDecimalMenuItem.Header = \"Decrease Decimal Places\";");
        source.Should().Contain("_decreaseDecimalMenuItem.Click += (_, _) => DecreaseSelectedRangeDecimalPlaces();");
        source.Should().Contain("_alignTopMenuItem.Header = \"Align Top\";");
        source.Should().Contain("_alignTopMenuItem.Click += (_, _) => ApplySelectedRangeVerticalAlignment(CellVAlign.Top);");
        source.Should().Contain("_alignMiddleMenuItem.Header = \"Align Middle\";");
        source.Should().Contain("_alignMiddleMenuItem.Click += (_, _) => ApplySelectedRangeVerticalAlignment(CellVAlign.Center);");
        source.Should().Contain("_alignBottomMenuItem.Header = \"Align Bottom\";");
        source.Should().Contain("_alignBottomMenuItem.Click += (_, _) => ApplySelectedRangeVerticalAlignment(CellVAlign.Bottom);");
        source.Should().Contain("_wrapTextMenuItem.Header = \"Wrap Text\";");
        source.Should().Contain("_wrapTextMenuItem.Click += (_, _) => ToggleSelectedRangeWrapText();");
        source.Should().Contain("_decreaseIndentMenuItem.Header = \"Decrease Indent\";");
        source.Should().Contain("_decreaseIndentMenuItem.Click += (_, _) => DecreaseSelectedRangeIndent();");
        source.Should().Contain("_increaseIndentMenuItem.Header = \"Increase Indent\";");
        source.Should().Contain("_increaseIndentMenuItem.Click += (_, _) => IncreaseSelectedRangeIndent();");
        source.Should().Contain("_alignLeftMenuItem.Header = \"Align Left\";");
        source.Should().Contain("_alignLeftMenuItem.Click += (_, _) => ApplySelectedRangeHorizontalAlignment(CellHAlign.Left);");
        source.Should().Contain("_alignCenterMenuItem.Header = \"Align Center\";");
        source.Should().Contain("_alignCenterMenuItem.Click += (_, _) => ApplySelectedRangeHorizontalAlignment(CellHAlign.Center);");
        source.Should().Contain("_alignRightMenuItem.Header = \"Align Right\";");
        source.Should().Contain("_alignRightMenuItem.Click += (_, _) => ApplySelectedRangeHorizontalAlignment(CellHAlign.Right);");
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
        source.Should().Contain("formatMenu.Items.Add(_doubleUnderlineMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_strikethroughMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_increaseFontSizeMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_decreaseFontSizeMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_fillColorMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_clearFillMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_fontColorMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_horizontalTextMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_angleCounterclockwiseMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_angleClockwiseMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_verticalTextMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_rotateTextUpMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_rotateTextDownMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_currencyFormatMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_percentFormatMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_commaStyleMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_increaseDecimalMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_decreaseDecimalMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_alignTopMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_alignMiddleMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_alignBottomMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_wrapTextMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_decreaseIndentMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_increaseIndentMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_alignLeftMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_alignCenterMenuItem);");
        source.Should().Contain("formatMenu.Items.Add(_alignRightMenuItem);");
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
        source.Should().Contain("_doubleUnderlineMenuItem.IsEnabled = _doubleUnderlineButton.IsEnabled;");
        source.Should().Contain("_strikethroughMenuItem.IsEnabled = _strikethroughButton.IsEnabled;");
        source.Should().Contain("_increaseFontSizeMenuItem.IsEnabled = _increaseFontSizeButton.IsEnabled;");
        source.Should().Contain("_decreaseFontSizeMenuItem.IsEnabled = _decreaseFontSizeButton.IsEnabled;");
        source.Should().Contain("_fillColorMenuItem.IsEnabled = _fillColorButton.IsEnabled;");
        source.Should().Contain("_clearFillMenuItem.IsEnabled = _fillColorButton.IsEnabled;");
        source.Should().Contain("_fontColorMenuItem.IsEnabled = _fontColorButton.IsEnabled;");
        source.Should().Contain("_horizontalTextMenuItem.IsEnabled = isIdle;");
        source.Should().Contain("_angleCounterclockwiseMenuItem.IsEnabled = isIdle;");
        source.Should().Contain("_angleClockwiseMenuItem.IsEnabled = isIdle;");
        source.Should().Contain("_verticalTextMenuItem.IsEnabled = isIdle;");
        source.Should().Contain("_rotateTextUpMenuItem.IsEnabled = isIdle;");
        source.Should().Contain("_rotateTextDownMenuItem.IsEnabled = isIdle;");
        source.Should().Contain("_currencyFormatMenuItem.IsEnabled = _currencyFormatButton.IsEnabled;");
        source.Should().Contain("_percentFormatMenuItem.IsEnabled = _percentFormatButton.IsEnabled;");
        source.Should().Contain("_commaStyleMenuItem.IsEnabled = _commaStyleButton.IsEnabled;");
        source.Should().Contain("_increaseDecimalMenuItem.IsEnabled = _increaseDecimalButton.IsEnabled;");
        source.Should().Contain("_decreaseDecimalMenuItem.IsEnabled = _decreaseDecimalButton.IsEnabled;");
        source.Should().Contain("_alignTopMenuItem.IsEnabled = _alignTopButton.IsEnabled;");
        source.Should().Contain("_alignMiddleMenuItem.IsEnabled = _alignMiddleButton.IsEnabled;");
        source.Should().Contain("_alignBottomMenuItem.IsEnabled = _alignBottomButton.IsEnabled;");
        source.Should().Contain("_wrapTextMenuItem.IsEnabled = _wrapTextButton.IsEnabled;");
        source.Should().Contain("_decreaseIndentMenuItem.IsEnabled = _decreaseIndentButton.IsEnabled;");
        source.Should().Contain("_increaseIndentMenuItem.IsEnabled = _increaseIndentButton.IsEnabled;");
        source.Should().Contain("_alignLeftMenuItem.IsEnabled = _alignLeftButton.IsEnabled;");
        source.Should().Contain("_alignCenterMenuItem.IsEnabled = _alignCenterButton.IsEnabled;");
        source.Should().Contain("_alignRightMenuItem.IsEnabled = _alignRightButton.IsEnabled;");
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
        smokeSource.Should().Contain("native_double_underline_menu_item={FormatBool(snapshot.HasNativeDoubleUnderlineMenuItem)}");
        smokeSource.Should().Contain("native_strikethrough_menu_item={FormatBool(snapshot.HasNativeStrikethroughMenuItem)}");
        smokeSource.Should().Contain("native_increase_font_size_menu_item={FormatBool(snapshot.HasNativeIncreaseFontSizeMenuItem)}");
        smokeSource.Should().Contain("native_decrease_font_size_menu_item={FormatBool(snapshot.HasNativeDecreaseFontSizeMenuItem)}");
        smokeSource.Should().Contain("native_fill_color_menu_item={FormatBool(snapshot.HasNativeFillColorMenuItem)}");
        smokeSource.Should().Contain("native_clear_fill_menu_item={FormatBool(snapshot.HasNativeClearFillMenuItem)}");
        smokeSource.Should().Contain("native_font_color_menu_item={FormatBool(snapshot.HasNativeFontColorMenuItem)}");
        smokeSource.Should().Contain("native_horizontal_text_menu_item={FormatBool(snapshot.HasNativeHorizontalTextMenuItem)}");
        smokeSource.Should().Contain("native_angle_counterclockwise_menu_item={FormatBool(snapshot.HasNativeAngleCounterclockwiseMenuItem)}");
        smokeSource.Should().Contain("native_angle_clockwise_menu_item={FormatBool(snapshot.HasNativeAngleClockwiseMenuItem)}");
        smokeSource.Should().Contain("native_vertical_text_menu_item={FormatBool(snapshot.HasNativeVerticalTextMenuItem)}");
        smokeSource.Should().Contain("native_rotate_text_up_menu_item={FormatBool(snapshot.HasNativeRotateTextUpMenuItem)}");
        smokeSource.Should().Contain("native_rotate_text_down_menu_item={FormatBool(snapshot.HasNativeRotateTextDownMenuItem)}");
        smokeSource.Should().Contain("native_currency_format_menu_item={FormatBool(snapshot.HasNativeCurrencyFormatMenuItem)}");
        smokeSource.Should().Contain("native_percent_format_menu_item={FormatBool(snapshot.HasNativePercentFormatMenuItem)}");
        smokeSource.Should().Contain("native_comma_style_menu_item={FormatBool(snapshot.HasNativeCommaStyleMenuItem)}");
        smokeSource.Should().Contain("native_increase_decimal_menu_item={FormatBool(snapshot.HasNativeIncreaseDecimalMenuItem)}");
        smokeSource.Should().Contain("native_decrease_decimal_menu_item={FormatBool(snapshot.HasNativeDecreaseDecimalMenuItem)}");
        smokeSource.Should().Contain("native_align_top_menu_item={FormatBool(snapshot.HasNativeAlignTopMenuItem)}");
        smokeSource.Should().Contain("native_align_middle_menu_item={FormatBool(snapshot.HasNativeAlignMiddleMenuItem)}");
        smokeSource.Should().Contain("native_align_bottom_menu_item={FormatBool(snapshot.HasNativeAlignBottomMenuItem)}");
        smokeSource.Should().Contain("native_wrap_text_menu_item={FormatBool(snapshot.HasNativeWrapTextMenuItem)}");
        smokeSource.Should().Contain("native_decrease_indent_menu_item={FormatBool(snapshot.HasNativeDecreaseIndentMenuItem)}");
        smokeSource.Should().Contain("native_increase_indent_menu_item={FormatBool(snapshot.HasNativeIncreaseIndentMenuItem)}");
        smokeSource.Should().Contain("native_align_left_menu_item={FormatBool(snapshot.HasNativeAlignLeftMenuItem)}");
        smokeSource.Should().Contain("native_align_center_menu_item={FormatBool(snapshot.HasNativeAlignCenterMenuItem)}");
        smokeSource.Should().Contain("native_align_right_menu_item={FormatBool(snapshot.HasNativeAlignRightMenuItem)}");
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
        windowSource.Should().Contain("HasNativeDoubleUnderlineMenuItem: HasNativeMenuItem(_doubleUnderlineMenuItem, \"Double Underline\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeStrikethroughMenuItem: HasNativeMenuItem(_strikethroughMenuItem, \"Strikethrough\")");
        windowSource.Should().Contain("HasNativeIncreaseFontSizeMenuItem: HasNativeMenuItem(_increaseFontSizeMenuItem, \"Increase Font Size\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeDecreaseFontSizeMenuItem: HasNativeMenuItem(_decreaseFontSizeMenuItem, \"Decrease Font Size\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeFillColorMenuItem: HasNativeMenuItem(_fillColorMenuItem, \"Fill Color\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeClearFillMenuItem: HasNativeMenuItem(_clearFillMenuItem, \"No Fill\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeFontColorMenuItem: HasNativeMenuItem(_fontColorMenuItem, \"Font Color\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeHorizontalTextMenuItem: HasNativeMenuItem(_horizontalTextMenuItem, \"Horizontal\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeAngleCounterclockwiseMenuItem: HasNativeMenuItem(_angleCounterclockwiseMenuItem, \"Angle Counterclockwise\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeAngleClockwiseMenuItem: HasNativeMenuItem(_angleClockwiseMenuItem, \"Angle Clockwise\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeVerticalTextMenuItem: HasNativeMenuItem(_verticalTextMenuItem, \"Vertical Text\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeRotateTextUpMenuItem: HasNativeMenuItem(_rotateTextUpMenuItem, \"Rotate Text Up\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeRotateTextDownMenuItem: HasNativeMenuItem(_rotateTextDownMenuItem, \"Rotate Text Down\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeCurrencyFormatMenuItem: HasNativeMenuItem(_currencyFormatMenuItem, \"Accounting Number Format\", requireGesture: false)");
        windowSource.Should().Contain("HasNativePercentFormatMenuItem: HasNativeMenuItem(_percentFormatMenuItem, \"Percent Style\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeCommaStyleMenuItem: HasNativeMenuItem(_commaStyleMenuItem, \"Comma Style\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeIncreaseDecimalMenuItem: HasNativeMenuItem(_increaseDecimalMenuItem, \"Increase Decimal Places\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeDecreaseDecimalMenuItem: HasNativeMenuItem(_decreaseDecimalMenuItem, \"Decrease Decimal Places\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeAlignTopMenuItem: HasNativeMenuItem(_alignTopMenuItem, \"Align Top\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeAlignMiddleMenuItem: HasNativeMenuItem(_alignMiddleMenuItem, \"Align Middle\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeAlignBottomMenuItem: HasNativeMenuItem(_alignBottomMenuItem, \"Align Bottom\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeWrapTextMenuItem: HasNativeMenuItem(_wrapTextMenuItem, \"Wrap Text\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeDecreaseIndentMenuItem: HasNativeMenuItem(_decreaseIndentMenuItem, \"Decrease Indent\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeIncreaseIndentMenuItem: HasNativeMenuItem(_increaseIndentMenuItem, \"Increase Indent\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeAlignLeftMenuItem: HasNativeMenuItem(_alignLeftMenuItem, \"Align Left\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeAlignCenterMenuItem: HasNativeMenuItem(_alignCenterMenuItem, \"Align Center\", requireGesture: false)");
        windowSource.Should().Contain("HasNativeAlignRightMenuItem: HasNativeMenuItem(_alignRightMenuItem, \"Align Right\", requireGesture: false)");
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
        source.Should().Contain("e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.B or Key.I or Key.U or Key.D4 or Key.NumPad4 or Key.D5 or Key.NumPad5)");
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
        source.Should().Contain("var textDecorations = BuildTextDecorations(style);");
        source.Should().Contain("if (style.Underline || style.DoubleUnderline)");
        source.Should().Contain("TextDecorations = textDecorations,");
        source.Should().Contain("else if (e.Key == Key.U && HasOnlyCommandModifier(e.KeyModifiers))");
        source.Should().Contain("else if (e.Key is Key.D4 or Key.NumPad4 && HasOnlyControlModifier(e.KeyModifiers))");
        source.Should().Contain("ToggleSelectedRangeUnderline();");
    }

    [Fact]
    public void MainWindow_WiresDoubleUnderlineThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly ToggleButton _doubleUnderlineButton = new();");
        source.Should().Contain("_doubleUnderlineButton.Content = new StackPanel");
        source.Should().Contain("Text = \"U\",");
        source.Should().Contain("new Border");
        source.Should().Contain("_doubleUnderlineButton.Click += DoubleUnderlineButton_Click;");
        source.Should().Contain("_doubleUnderlineButton.IsChecked = _session.IsSelectedRangeStartDoubleUnderline;");
        source.Should().Contain("_doubleUnderlineButton.IsEnabled = isIdle;");
        source.Should().Contain("private void DoubleUnderlineButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeDoubleUnderline(_doubleUnderlineButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeDoubleUnderline()");
        source.Should().Contain("private void ApplySelectedRangeDoubleUnderline(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeDoubleUnderline(enabled);");
        source.Should().Contain("_doubleUnderlineButton.IsChecked = _session.IsSelectedRangeStartDoubleUnderline;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Double Underline failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Double underlined\" : \"Removed double underline from\")} {rangeReference}\");");
        source.Should().Contain("var textDecorations = BuildTextDecorations(style);");
        source.Should().Contain("private const double DoubleUnderlineSecondStrokeOffset = 2;");
        source.Should().Contain("if (style.Underline || style.DoubleUnderline)");
        source.Should().Contain("if (style.DoubleUnderline)");
        source.Should().Contain("Location = TextDecorationLocation.Underline,");
        source.Should().Contain("StrokeThickness = 1,");
        source.Should().Contain("StrokeThicknessUnit = TextDecorationUnit.Pixel,");
        source.Should().Contain("StrokeOffset = DoubleUnderlineSecondStrokeOffset,");
        source.Should().Contain("StrokeOffsetUnit = TextDecorationUnit.Pixel,");
        source.Should().Contain("ToggleSelectedRangeDoubleUnderline();");
    }

    [Fact]
    public void MainWindow_WiresStrikethroughThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly ToggleButton _strikethroughButton = new();");
        source.Should().Contain("_strikethroughButton.Content = new TextBlock");
        source.Should().Contain("TextDecorations = TextDecorations.Strikethrough,");
        source.Should().Contain("_strikethroughButton.Click += StrikethroughButton_Click;");
        source.Should().Contain("_strikethroughButton.IsChecked = _session.IsSelectedRangeStartStrikethrough;");
        source.Should().Contain("_strikethroughButton.IsEnabled = isIdle;");
        source.Should().Contain("private void StrikethroughButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeStrikethrough(_strikethroughButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeStrikethrough()");
        source.Should().Contain("private void ApplySelectedRangeStrikethrough(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeStrikethrough(enabled);");
        source.Should().Contain("_strikethroughButton.IsChecked = _session.IsSelectedRangeStartStrikethrough;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Strikethrough failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Struck through\" : \"Removed strikethrough from\")} {rangeReference}\");");
        source.Should().Contain("private static TextDecorationCollection? BuildTextDecorations(CellStyle? style)");
        source.Should().Contain("if (style.Strikethrough)");
        source.Should().Contain("foreach (var decoration in TextDecorations.Strikethrough)");
        source.Should().Contain("else if (e.Key is Key.D5 or Key.NumPad5 && HasOnlyControlModifier(e.KeyModifiers))");
        source.Should().Contain("ToggleSelectedRangeStrikethrough();");
    }

    [Fact]
    public void MainWindow_WiresFontSizeThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly Button _increaseFontSizeButton = new();");
        source.Should().Contain("private readonly Button _decreaseFontSizeButton = new();");
        source.Should().Contain("_increaseFontSizeButton.Content = \"A+\";");
        source.Should().Contain("_decreaseFontSizeButton.Content = \"A-\";");
        source.Should().Contain("_increaseFontSizeButton.Click += IncreaseFontSizeButton_Click;");
        source.Should().Contain("_decreaseFontSizeButton.Click += DecreaseFontSizeButton_Click;");
        source.Should().Contain("_increaseFontSizeButton.IsEnabled = isIdle;");
        source.Should().Contain("_decreaseFontSizeButton.IsEnabled = isIdle;");
        source.Should().Contain("private void IncreaseFontSizeButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void DecreaseFontSizeButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void IncreaseSelectedRangeFontSize()");
        source.Should().Contain("var result = _session.IncreaseSelectedRangeFontSize();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Increase Font Size failed.\");");
        source.Should().Contain("RefreshShell($\"Increased font size for {rangeReference}\");");
        source.Should().Contain("private void DecreaseSelectedRangeFontSize()");
        source.Should().Contain("var result = _session.DecreaseSelectedRangeFontSize();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Decrease Font Size failed.\");");
        source.Should().Contain("RefreshShell($\"Decreased font size for {rangeReference}\");");
        source.Should().Contain("var fontSize = style?.FontSize ?? CellStyle.Default.FontSize;");
        source.Should().Contain("FontSize = fontSize,");
    }

    [Fact]
    public void MainWindow_WiresFillAndFontColorThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private static readonly CellColor DefaultFillColor = new(255, 255, 0);");
        source.Should().Contain("private static readonly CellColor DefaultFontColor = new(255, 0, 0);");
        source.Should().Contain("private readonly Button _fillColorButton = new();");
        source.Should().Contain("private readonly Button _fontColorButton = new();");
        source.Should().Contain("_fillColorButton.Content = \"Fill\";");
        source.Should().Contain("_fontColorButton.Content = \"A\";");
        source.Should().Contain("_fillColorButton.Click += FillColorButton_Click;");
        source.Should().Contain("_fontColorButton.Click += FontColorButton_Click;");
        source.Should().Contain("_fillColorButton.IsEnabled = isIdle;");
        source.Should().Contain("_fontColorButton.IsEnabled = isIdle;");
        source.Should().Contain("private void FillColorButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplyDefaultSelectedRangeFillColor();");
        source.Should().Contain("private void FontColorButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplyDefaultSelectedRangeFontColor();");
        source.Should().Contain("private void ApplySelectedRangeFillColor(CellColor fillColor)");
        source.Should().Contain("var result = _session.SetSelectedRangeFillColor(fillColor);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Fill Color failed.\");");
        source.Should().Contain("RefreshShell($\"Applied fill color to {rangeReference}\");");
        source.Should().Contain("private void ClearSelectedRangeFill()");
        source.Should().Contain("var result = _session.ClearSelectedRangeFill();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"No Fill failed.\");");
        source.Should().Contain("RefreshShell($\"Cleared fill from {rangeReference}\");");
        source.Should().Contain("private void ApplySelectedRangeFontColor(CellColor fontColor)");
        source.Should().Contain("var result = _session.SetSelectedRangeFontColor(fontColor);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Font Color failed.\");");
        source.Should().Contain("RefreshShell($\"Applied font color to {rangeReference}\");");
        source.Should().Contain("var background = style?.ResolveFillColor(_session.Workbook.Theme) is { } fillColor");
        source.Should().Contain(": Brush(style.ResolveFontColor(_session.Workbook.Theme));");
    }

    [Fact]
    public void MainWindow_WiresTextRotationThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly NativeMenuItem _horizontalTextMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _angleCounterclockwiseMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _angleClockwiseMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _verticalTextMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _rotateTextUpMenuItem = new();");
        source.Should().Contain("private readonly NativeMenuItem _rotateTextDownMenuItem = new();");
        source.Should().Contain("private readonly DropDownButton _orientationButton = new();");
        source.Should().Contain("_orientationButton.Content = \"Orient\";");
        source.Should().Contain("_orientationButton.Flyout = CreateTextRotationFlyout();");
        source.Should().Contain("_orientationButton.IsEnabled = isIdle;");
        source.Should().Contain("_orientationButton,");
        source.Should().Contain("_horizontalTextMenuItem.Header = \"Horizontal\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(0, \"Set horizontal text for\", \"Horizontal Text failed.\");");
        source.Should().Contain("_angleCounterclockwiseMenuItem.Header = \"Angle Counterclockwise\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(45, \"Angled text counterclockwise for\", \"Angle Counterclockwise failed.\");");
        source.Should().Contain("_angleClockwiseMenuItem.Header = \"Angle Clockwise\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(-45, \"Angled text clockwise for\", \"Angle Clockwise failed.\");");
        source.Should().Contain("_verticalTextMenuItem.Header = \"Vertical Text\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(255, \"Set vertical text for\", \"Vertical Text failed.\");");
        source.Should().Contain("_rotateTextUpMenuItem.Header = \"Rotate Text Up\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(90, \"Rotated text up for\", \"Rotate Text Up failed.\");");
        source.Should().Contain("_rotateTextDownMenuItem.Header = \"Rotate Text Down\";");
        source.Should().Contain("ApplySelectedRangeTextRotation(-90, \"Rotated text down for\", \"Rotate Text Down failed.\");");
        source.Should().Contain("private void ApplySelectedRangeTextRotation(int textRotation, string successAction, string failureMessage)");
        source.Should().Contain("var result = _session.SetSelectedRangeTextRotation(textRotation);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? failureMessage);");
        source.Should().Contain("RefreshShell($\"{successAction} {rangeReference}\");");
        source.Should().Contain("var textRotation = style?.TextRotation ?? CellStyle.Default.TextRotation;");
        source.Should().Contain("textRotation);");
        source.Should().Contain("private static string FormatTextForRotation(string text, int textRotation)");
        source.Should().Contain("textRotation == 255");
        source.Should().Contain("string.Join(Environment.NewLine, text.ToCharArray())");
        source.Should().Contain("private static int NormalizeTextRotationForDisplay(int textRotation)");
        source.Should().Contain("textRotation is >= -90 and <= 90 ? textRotation : 0;");
        source.Should().Contain("private static RotateTransform? CreateTextRotationTransform(int textRotation)");
        source.Should().Contain("var displayRotation = NormalizeTextRotationForDisplay(textRotation);");
        source.Should().Contain("return displayRotation == 0 ? null : new RotateTransform(-displayRotation);");
        source.Should().Contain("textBlock.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);");
        source.Should().Contain("textBlock.RenderTransform = transform;");
        source.Should().Contain("ClipToBounds = true,");
        source.Should().Contain("private MenuFlyout CreateTextRotationFlyout()");
        source.Should().Contain("CreateTextRotationMenuItem(\"Horizontal\", 0, \"Set horizontal text for\", \"Horizontal Text failed.\")");
        source.Should().Contain("CreateTextRotationMenuItem(\"Angle Counterclockwise\", 45, \"Angled text counterclockwise for\", \"Angle Counterclockwise failed.\")");
        source.Should().Contain("CreateTextRotationMenuItem(\"Angle Clockwise\", -45, \"Angled text clockwise for\", \"Angle Clockwise failed.\")");
        source.Should().Contain("CreateTextRotationMenuItem(\"Vertical Text\", 255, \"Set vertical text for\", \"Vertical Text failed.\")");
        source.Should().Contain("CreateTextRotationMenuItem(\"Rotate Text Up\", 90, \"Rotated text up for\", \"Rotate Text Up failed.\")");
        source.Should().Contain("CreateTextRotationMenuItem(\"Rotate Text Down\", -90, \"Rotated text down for\", \"Rotate Text Down failed.\")");
        source.Should().Contain("private MenuItem CreateTextRotationMenuItem(");
        source.Should().Contain("menuItem.Click += (_, _) => ApplySelectedRangeTextRotation(textRotation, successAction, failureMessage);");
    }

    [Fact]
    public void MainWindow_WiresNumberFormatsThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private const string CurrencyNumberFormat = \"$#,##0.00\";");
        source.Should().Contain("private const string PercentNumberFormat = \"0%\";");
        source.Should().Contain("private const string CommaNumberFormat = \"#,##0.00\";");
        source.Should().Contain("private readonly Button _currencyFormatButton = new();");
        source.Should().Contain("private readonly Button _percentFormatButton = new();");
        source.Should().Contain("private readonly Button _commaStyleButton = new();");
        source.Should().Contain("private readonly Button _increaseDecimalButton = new();");
        source.Should().Contain("private readonly Button _decreaseDecimalButton = new();");
        source.Should().Contain("_currencyFormatButton.Content = \"$\";");
        source.Should().Contain("_percentFormatButton.Content = \"%\";");
        source.Should().Contain("_commaStyleButton.Content = \",\";");
        source.Should().Contain("_increaseDecimalButton.Content = \"+.0\";");
        source.Should().Contain("_decreaseDecimalButton.Content = \"-.0\";");
        source.Should().Contain("_currencyFormatButton.Click += CurrencyFormatButton_Click;");
        source.Should().Contain("_percentFormatButton.Click += PercentFormatButton_Click;");
        source.Should().Contain("_commaStyleButton.Click += CommaStyleButton_Click;");
        source.Should().Contain("_increaseDecimalButton.Click += IncreaseDecimalButton_Click;");
        source.Should().Contain("_decreaseDecimalButton.Click += DecreaseDecimalButton_Click;");
        source.Should().Contain("_currencyFormatButton.IsEnabled = isIdle;");
        source.Should().Contain("_percentFormatButton.IsEnabled = isIdle;");
        source.Should().Contain("_commaStyleButton.IsEnabled = isIdle;");
        source.Should().Contain("_increaseDecimalButton.IsEnabled = isIdle;");
        source.Should().Contain("_decreaseDecimalButton.IsEnabled = isIdle;");
        source.Should().Contain("private void CurrencyFormatButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void PercentFormatButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void CommaStyleButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void IncreaseDecimalButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void DecreaseDecimalButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void ApplySelectedRangeCurrencyFormat()");
        source.Should().Contain("ApplySelectedRangeNumberFormat(CurrencyNumberFormat, \"Applied currency format to\", \"Currency format failed.\");");
        source.Should().Contain("private void ApplySelectedRangePercentFormat()");
        source.Should().Contain("ApplySelectedRangeNumberFormat(PercentNumberFormat, \"Applied percent format to\", \"Percent format failed.\");");
        source.Should().Contain("private void ApplySelectedRangeCommaStyle()");
        source.Should().Contain("ApplySelectedRangeNumberFormat(CommaNumberFormat, \"Applied comma style to\", \"Comma style failed.\");");
        source.Should().Contain("private void ApplySelectedRangeNumberFormat(string numberFormat, string successAction, string failureMessage)");
        source.Should().Contain("var result = _session.SetSelectedRangeNumberFormat(numberFormat);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? failureMessage);");
        source.Should().Contain("RefreshShell($\"{successAction} {rangeReference}\");");
        source.Should().Contain("private void IncreaseSelectedRangeDecimalPlaces()");
        source.Should().Contain("var result = _session.IncreaseSelectedRangeDecimalPlaces();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Increase Decimal failed.\");");
        source.Should().Contain("RefreshShell($\"Increased decimals for {rangeReference}\");");
        source.Should().Contain("private void DecreaseSelectedRangeDecimalPlaces()");
        source.Should().Contain("var result = _session.DecreaseSelectedRangeDecimalPlaces();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Decrease Decimal failed.\");");
        source.Should().Contain("RefreshShell($\"Decreased decimals for {rangeReference}\");");
        source.Should().Contain("cell.DisplayText,");
    }

    [Fact]
    public void MainWindow_WiresWrapTextThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private readonly ToggleButton _wrapTextButton = new();");
        source.Should().Contain("_wrapTextButton.Content = \"Wrap\";");
        source.Should().Contain("_wrapTextButton.Click += WrapTextButton_Click;");
        source.Should().Contain("_wrapTextButton.IsChecked = _session.IsSelectedRangeStartWrapText;");
        source.Should().Contain("_wrapTextButton.IsEnabled = isIdle;");
        source.Should().Contain("private void WrapTextButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeWrapText(_wrapTextButton.IsChecked == true);");
        source.Should().Contain("private void ToggleSelectedRangeWrapText()");
        source.Should().Contain("ApplySelectedRangeWrapText(!_session.IsSelectedRangeStartWrapText);");
        source.Should().Contain("private void ApplySelectedRangeWrapText(bool enabled)");
        source.Should().Contain("var result = _session.SetSelectedRangeWrapText(enabled);");
        source.Should().Contain("_wrapTextButton.IsChecked = _session.IsSelectedRangeStartWrapText;");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Wrap Text failed.\");");
        source.Should().Contain("RefreshShell($\"{(enabled ? \"Wrapped\" : \"Unwrapped\")} {rangeReference}\");");
        source.Should().Contain("var textWrapping = style?.WrapText == true ? TextWrapping.Wrap : TextWrapping.NoWrap;");
        source.Should().Contain("var effectiveTextWrapping = textRotation == 255 ? TextWrapping.NoWrap : textWrapping;");
        source.Should().Contain("TextWrapping = effectiveTextWrapping,");
        source.Should().Contain("TextTrimming = effectiveTextWrapping == TextWrapping.Wrap || textRotation == 255");
        source.Should().Contain("? TextTrimming.None");
        source.Should().Contain(": TextTrimming.CharacterEllipsis");
    }

    [Fact]
    public void MainWindow_WiresIndentThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private const double CellIndentLevelWidth = 12;");
        source.Should().Contain("private readonly Button _decreaseIndentButton = new();");
        source.Should().Contain("private readonly Button _increaseIndentButton = new();");
        source.Should().Contain("_decreaseIndentButton.Content = \"Out\";");
        source.Should().Contain("_increaseIndentButton.Content = \"In\";");
        source.Should().Contain("_decreaseIndentButton.Click += DecreaseIndentButton_Click;");
        source.Should().Contain("_increaseIndentButton.Click += IncreaseIndentButton_Click;");
        source.Should().Contain("_decreaseIndentButton.IsEnabled = isIdle;");
        source.Should().Contain("_increaseIndentButton.IsEnabled = isIdle;");
        source.Should().Contain("private void DecreaseIndentButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void IncreaseIndentButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("private void DecreaseSelectedRangeIndent()");
        source.Should().Contain("var result = _session.DecreaseSelectedRangeIndent();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Decrease Indent failed.\");");
        source.Should().Contain("RefreshShell($\"Decreased indent for {rangeReference}\");");
        source.Should().Contain("private void IncreaseSelectedRangeIndent()");
        source.Should().Contain("var result = _session.IncreaseSelectedRangeIndent();");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Increase Indent failed.\");");
        source.Should().Contain("RefreshShell($\"Increased indent for {rangeReference}\");");
        source.Should().Contain("var indentPadding = GetCellIndentPadding(style);");
        source.Should().Contain("private static double GetCellIndentPadding(CellStyle? style)");
        source.Should().Contain("Math.Clamp(style.IndentLevel, 0, 15) * CellIndentLevelWidth;");
        source.Should().Contain("Padding = new Thickness(8 + indentPadding, 0, 8, 0),");
    }

    [Fact]
    public void MainWindow_WiresVerticalAlignmentThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("using CellVAlign = FreeX.Core.Model.VerticalAlignment;");
        source.Should().Contain("private readonly ToggleButton _alignTopButton = new();");
        source.Should().Contain("private readonly ToggleButton _alignMiddleButton = new();");
        source.Should().Contain("private readonly ToggleButton _alignBottomButton = new();");
        source.Should().Contain("_alignTopButton.Content = \"Top\";");
        source.Should().Contain("_alignMiddleButton.Content = \"Mid\";");
        source.Should().Contain("_alignBottomButton.Content = \"Bot\";");
        source.Should().Contain("_alignTopButton.Click += AlignTopButton_Click;");
        source.Should().Contain("_alignMiddleButton.Click += AlignMiddleButton_Click;");
        source.Should().Contain("_alignBottomButton.Click += AlignBottomButton_Click;");
        source.Should().Contain("_alignTopButton.IsChecked = _session.SelectedRangeStartVerticalAlignment == CellVAlign.Top;");
        source.Should().Contain("_alignMiddleButton.IsChecked = _session.SelectedRangeStartVerticalAlignment == CellVAlign.Center;");
        source.Should().Contain("_alignBottomButton.IsChecked = _session.SelectedRangeStartVerticalAlignment == CellVAlign.Bottom;");
        source.Should().Contain("_alignTopButton.IsEnabled = isIdle;");
        source.Should().Contain("_alignMiddleButton.IsEnabled = isIdle;");
        source.Should().Contain("_alignBottomButton.IsEnabled = isIdle;");
        source.Should().Contain("private void AlignTopButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeVerticalAlignment(CellVAlign.Top);");
        source.Should().Contain("private void AlignMiddleButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeVerticalAlignment(CellVAlign.Center);");
        source.Should().Contain("private void AlignBottomButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeVerticalAlignment(CellVAlign.Bottom);");
        source.Should().Contain("private void ApplySelectedRangeVerticalAlignment(CellVAlign alignment)");
        source.Should().Contain("var result = _session.SetSelectedRangeVerticalAlignment(alignment);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Vertical alignment failed.\");");
        source.Should().Contain("RefreshShell($\"Aligned {rangeReference} {FormatVerticalAlignmentStatus(alignment)}\");");
        source.Should().Contain("var verticalAlignment = MapCellVerticalAlignment(style?.VerticalAlignment ?? CellVAlign.Bottom);");
        source.Should().Contain("private static AvaloniaVerticalAlignment MapCellVerticalAlignment(CellVAlign verticalAlignment)");
        source.Should().Contain("CellVAlign.Top => AvaloniaVerticalAlignment.Top,");
        source.Should().Contain("CellVAlign.Bottom => AvaloniaVerticalAlignment.Bottom,");
        source.Should().Contain("_ => AvaloniaVerticalAlignment.Center");
        source.Should().Contain("VerticalAlignment = verticalAlignment,");
        source.Should().Contain("private static string FormatVerticalAlignmentStatus(CellVAlign alignment)");
    }

    [Fact]
    public void MainWindow_WiresHorizontalAlignmentThroughSharedWorkbookSession()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("using CellHAlign = FreeX.Core.Model.HorizontalAlignment;");
        source.Should().Contain("private readonly ToggleButton _alignLeftButton = new();");
        source.Should().Contain("private readonly ToggleButton _alignCenterButton = new();");
        source.Should().Contain("private readonly ToggleButton _alignRightButton = new();");
        source.Should().Contain("_alignLeftButton.Content = \"L\";");
        source.Should().Contain("_alignCenterButton.Content = \"C\";");
        source.Should().Contain("_alignRightButton.Content = \"R\";");
        source.Should().Contain("_alignLeftButton.Click += AlignLeftButton_Click;");
        source.Should().Contain("_alignCenterButton.Click += AlignCenterButton_Click;");
        source.Should().Contain("_alignRightButton.Click += AlignRightButton_Click;");
        source.Should().Contain("_alignLeftButton.IsChecked = _session.SelectedRangeStartHorizontalAlignment == CellHAlign.Left;");
        source.Should().Contain("_alignCenterButton.IsChecked = _session.SelectedRangeStartHorizontalAlignment == CellHAlign.Center;");
        source.Should().Contain("_alignRightButton.IsChecked = _session.SelectedRangeStartHorizontalAlignment == CellHAlign.Right;");
        source.Should().Contain("_alignLeftButton.IsEnabled = isIdle;");
        source.Should().Contain("_alignCenterButton.IsEnabled = isIdle;");
        source.Should().Contain("_alignRightButton.IsEnabled = isIdle;");
        source.Should().Contain("private void AlignLeftButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeHorizontalAlignment(CellHAlign.Left);");
        source.Should().Contain("private void AlignCenterButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeHorizontalAlignment(CellHAlign.Center);");
        source.Should().Contain("private void AlignRightButton_Click(object? sender, RoutedEventArgs e)");
        source.Should().Contain("ApplySelectedRangeHorizontalAlignment(CellHAlign.Right);");
        source.Should().Contain("private void ApplySelectedRangeHorizontalAlignment(CellHAlign alignment)");
        source.Should().Contain("var result = _session.SetSelectedRangeHorizontalAlignment(alignment);");
        source.Should().Contain("ShowEditIssue(result.ErrorMessage ?? \"Alignment failed.\");");
        source.Should().Contain("RefreshShell($\"Aligned {rangeReference} {FormatHorizontalAlignmentStatus(alignment)}\");");
        source.Should().Contain("MapCellTextAlignment(");
        source.Should().Contain("style?.HorizontalAlignment ?? CellHAlign.General");
        source.Should().Contain("private static TextAlignment MapCellTextAlignment(CellHAlign horizontalAlignment, bool isNumericOrDate)");
        source.Should().Contain("CellHAlign.Center or CellHAlign.Justify or CellHAlign.Distributed => TextAlignment.Center,");
        source.Should().Contain("CellHAlign.General when isNumericOrDate => TextAlignment.Right,");
        source.Should().Contain("private static string FormatHorizontalAlignmentStatus(CellHAlign alignment)");
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
