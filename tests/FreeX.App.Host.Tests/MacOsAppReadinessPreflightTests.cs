using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class MacOsAppReadinessPreflightTests
{
    [Fact]
    public void MacOsAppReadinessPreflight_DeclaresMacOsBundleWorkflowAndSourceContracts()
    {
        var script = File.ReadAllText(WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1"));

        script.Should().Contain("Avalonia app TargetFramework must be net10.0");
        script.Should().Contain("Avalonia app RuntimeIdentifiers");
        script.Should().Contain("ApplicationTitle");
        script.Should().Contain("CFBundleName");
        script.Should().Contain("CFBundleIconFile");
        script.Should().Contain("FreeX.icns");
        script.Should().Contain("Test-MacOsIcon");
        script.Should().Contain("NSHighResolutionCapable");
        script.Should().Contain("dotnet-version: 10.0.x");
        script.Should().Contain("--framework net10.0");
        script.Should().Contain("--output \"$app/Contents/MacOS\"");
        script.Should().Contain("native_fill_color_swatch_count=69");
        script.Should().Contain("native_font_color_swatch_count=69");
        script.Should().Contain("toolbar_borders_button=true");
        script.Should().Contain("native_borders_menu_item=true");
        script.Should().Contain("native_borders_preset_count=8");
        script.Should().Contain("native_cell_styles_menu_item=true");
        script.Should().Contain("native_cell_styles_preset_count=33");
        script.Should().Contain("--macos-launch-smoke-verify-image-clipboard");
        script.Should().Contain("launch_clipboard_image=\"$RUNNER_TEMP/freex-$runtime-clipboard.png\"");
        script.Should().Contain("/usr/bin/swift - \"$launch_clipboard_image\"");
        script.Should().Contain("NSPasteboard.general");
        script.Should().Contain("external_image_clipboard_paste_required=true");
        script.Should().Contain("external_image_clipboard_paste=true");
        script.Should().Contain("external_image_clipboard_picture_count=[1-9]");
        script.Should().Contain("external_image_clipboard_picture_png_bytes=[1-9]");
        script.Should().Contain("native_new_workbook_menu_item=true");
        script.Should().Contain("native_open_recent_menu_item=true");
        script.Should().Contain("native_open_recent_item_count=[1-9]");
        script.Should().Contain("native_close_workbook_menu_item=true");
        script.Should().Contain("native_select_all_menu_item=true");
        script.Should().Contain("new_sheet_button=true");
        script.Should().Contain("native_sheet_menu=true");
        script.Should().Contain("native_new_sheet_menu_item=true");
        script.Should().Contain("native_rename_sheet_menu_item=true");
        script.Should().Contain("native_duplicate_sheet_menu_item=true");
        script.Should().Contain("native_tab_color_menu_item=true");
        script.Should().Contain("native_tab_color_clear_item=true");
        script.Should().Contain("native_tab_color_swatch_count=69");
        script.Should().Contain("focusable_sheet_tab=true");
        script.Should().Contain("focusable_active_sheet_tab=true");
        script.Should().Contain("shell_focus_cycle_targets=true");
        script.Should().Contain("sheet_tab_context_keyboard_help=true");
        script.Should().Contain("sheet_tab_context_rename_menu_item=true");
        script.Should().Contain("sheet_tab_context_tab_color_menu_item=true");
        script.Should().Contain("sheet_tab_context_no_color_menu_item=true");
        script.Should().Contain("sheet_tab_context_select_all_sheets_menu_item=true");
        script.Should().Contain("sheet_tab_context_ungroup_sheets_menu_item=true");
        script.Should().Contain("native_select_all_sheets_menu_item=true");
        script.Should().Contain("native_ungroup_sheets_menu_item=true");
        script.Should().Contain("native_delete_sheet_menu_item=true");
        script.Should().Contain("HasNativeNewWorkbookMenuItem &&");
        script.Should().Contain("HasNativeOpenRecentMenuItem &&");
        script.Should().Contain("NativeOpenRecentItemCount > 0 &&");
        script.Should().Contain("HasNativeSelectAllMenuItem &&");
        script.Should().Contain("HasNativeCloseWorkbookMenuItem &&");
        script.Should().Contain("HasNativeRenameSheetMenuItem &&");
        script.Should().Contain("HasNativeTabColorMenuItem &&");
        script.Should().Contain("HasBordersButton &&");
        script.Should().Contain("HasFocusableSheetTab &&");
        script.Should().Contain("HasFocusableActiveSheetTab &&");
        script.Should().Contain("HasShellFocusCycleTargets &&");
        script.Should().Contain("HasSheetTabContextKeyboardHelp &&");
        script.Should().Contain("HasSheetTabContextRenameMenuItem &&");
        script.Should().Contain("HasSheetTabContextTabColorMenuItem &&");
        script.Should().Contain("HasSheetTabContextNoColorMenuItem &&");
        script.Should().Contain("HasSheetTabContextSelectAllSheetsMenuItem &&");
        script.Should().Contain("HasSheetTabContextUngroupSheetsMenuItem &&");
        script.Should().Contain("HasNativeSelectAllSheetsMenuItem &&");
        script.Should().Contain("HasNativeUngroupSheetsMenuItem &&");
        script.Should().Contain("HasNativeDeleteSheetMenuItem &&");
        script.Should().Contain("HasNativeBordersMenuItem &&");
        script.Should().Contain("NativeBordersPresetCount == Enum.GetValues<CellBorderPreset>().Length");
        script.Should().Contain("toolbar_borders_button=");
        script.Should().Contain("native_borders_menu_item=");
        script.Should().Contain("native_borders_preset_count=");
        script.Should().Contain("native_help_menu=true");
        script.Should().Contain("native_help_online_menu_item=true");
        script.Should().Contain("native_legal_notices_menu_item=");
        script.Should().Contain("drawing_object_previews=3");
        script.Should().Contain("roundtrip_drawing_object_previews=3");
        script.Should().Contain("shasum -a 256 -c \"$zip_name.sha256\"");
        script.Should().Contain("zip_sha256=$zip_sha256");
        script.Should().Contain("freex-$runtime-macos-tester-instructions.md");
        script.Should().Contain("Upload app diagnostics");
        script.Should().Contain("if: always()");
        script.Should().Contain("freex-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.runtime }}-macos-diagnostics");
        script.Should().Contain("if-no-files-found: warn");
        script.Should().Contain("native_horizontal_text_menu_item=true");
        script.Should().Contain("native_rotate_text_down_menu_item=");
        script.Should().Contain("native_show_gridlines_menu_item=true");
        script.Should().Contain("native_show_headings_menu_item=true");
        script.Should().Contain("native_zoom_in_menu_item=true");
        script.Should().Contain("native_zoom_out_menu_item=true");
        script.Should().Contain("native_zoom_100_menu_item=true");
        script.Should().Contain("native_zoom_to_selection_menu_item=true");
        script.Should().Contain("native_freeze_panes_menu_item=true");
        script.Should().Contain("native_freeze_top_row_menu_item=true");
        script.Should().Contain("native_freeze_first_column_menu_item=true");
        script.Should().Contain("native_unfreeze_panes_menu_item=true");
        script.Should().Contain("PackagingSmokeCommand.TryRun(args, Console.Out, Console.Error, out var smokeExitCode)");
        script.Should().Contain("PortPreviewWorkbookFactory.PreviewShapeName");
        script.Should().Contain("_sessionFactory.Create(source, SmokeViewportHeight, SmokeViewportWidth, includeObjects: true)");
        script.Should().Contain("StartWithClassicDesktopLifetime(startupArguments)");
        script.Should().Contain("IActivatableLifetime");
        script.Should().Contain("OpenActivatedFilesAsync");
        script.Should().Contain("using FreeX.Core.Calc;");
        script.Should().Contain("AddGridChild(grid, CreateCell(cell, row, col, zoomFactor, colWidth, rowHeight)");
        script.Should().Contain("CellTextOrientationLayoutPlanner.HasTextOrientation(textRotation)");
        script.Should().Contain("CellTextOrientationLayoutPlanner.CalculateLayout(");
        script.Should().Contain("CreateTextRotationTransform(layout.TransformAngle)");
        script.Should().Contain("Canvas.SetLeft(textBlock, layout.TextPoint.X);");
        script.Should().Contain("Canvas.SetTop(textBlock, layout.TextPoint.Y);");
        script.Should().Contain("public static class CellTextOrientationLayoutPlanner");
        script.Should().Contain("public static bool ShouldClip(");
        script.Should().Contain("CreateNativePasteSpecialMenu()");
        script.Should().Contain("_bordersButton.Flyout = CreateBorderPresetFlyout();");
        script.Should().Contain("_bordersMenuItem.Menu = CreateNativeBorderPresetMenu();");
        script.Should().Contain("PasteSpecialClipboardAtActiveCell(text, mode, options)");
        script.Should().Contain("CreatePasteSpecialTextMenuItem(`\"Text`\")");
        script.Should().Contain("CreateNativePasteSpecialTextMenuItem(`\"Unicode Text`\")");
        script.Should().Contain("_session.PasteClipboardTextAtActiveCell(text, preserveText: true)");
        script.Should().Contain("CreatePastePictureMenuItem(`\"Picture`\", linkedPicture: false)");
        script.Should().Contain("CreateNativePastePictureMenuItem(`\"Linked Picture`\", linkedPicture: true)");
        script.Should().Contain("private enum ShellFocusRegion");
        script.Should().Contain("private static readonly ShellFocusRegion[] ShellFocusCycle");
        script.Should().Contain("private static bool IsShellFocusCycleKey(KeyEventArgs args)");
        script.Should().Contain("CycleShellFocus(reverse: e.KeyModifiers == KeyModifiers.Shift);");
        script.Should().Contain("private void CycleShellFocus(bool reverse)");
        script.Should().Contain("private static ShellFocusRegion GetNextShellFocusRegion(ShellFocusRegion current, bool reverse)");
        script.Should().Contain("private ShellFocusRegion GetCurrentShellFocusRegion()");
        script.Should().Contain("private bool FocusShellRegion(ShellFocusRegion region)");
        script.Should().Contain("private bool FocusFirstEnabledToolbarControl()");
        script.Should().Contain("private IReadOnlyList<Control> GetToolbarFocusTargets()");
        script.Should().Contain("private static bool FocusControl(Control control)");
        script.Should().Contain("private void NavigateSheetTabFromKeyboard(SheetId sheetId, KeyEventArgs args)");
        script.Should().Contain("private bool SelectAdjacentVisibleSheetFromKeyboard(int direction, bool selectRange)");
        script.Should().Contain("Math.Clamp(targetIndex, 0, _session.SheetTabs.Count - 1)");
        script.Should().Contain("_session.ShouldPreferExternalClipboardImage(text)");
        script.Should().Contain("private async Task<bool> TryPasteClipboardImageAsync(IClipboard clipboard, CellAddress destination)");
        script.Should().Contain("await clipboard.TryGetBitmapAsync()");
        script.Should().Contain("bitmap.Save(stream)");
        script.Should().Contain("_session.PasteClipboardImageAtActiveCell(pngBytes, pixelWidth, pixelHeight)");
        script.Should().Contain("internal async Task<bool> TryPasteLaunchSmokeClipboardImageAsync()");
        script.Should().Contain("return await TryPasteClipboardImageAsync(clipboard, _session.ActiveCell);");
        script.Should().Contain("ExternalImageClipboardPictureCount: externalImageClipboardPictures.Length");
        script.Should().Contain("ExternalImageClipboardPicturePngByteCount: externalImageClipboardPictures.Sum(static picture => picture.ImageBytes!.Length)");
        script.Should().Contain("VerifyImageClipboardPasteArgument");
        script.Should().Contain("await mainWindow.TryPasteLaunchSmokeClipboardImageAsync();");
        script.Should().Contain("external_image_clipboard_paste_required=");
        script.Should().Contain("external_image_clipboard_picture_png_bytes=");
        script.Should().Contain("_session.PastePictureFromClipboardAtActiveCell(text, linkedPicture)");
        script.Should().Contain("public WorkbookCellEditResult PasteClipboardImageAtActiveCell(");
        script.Should().Contain("ClipboardPictureService.CreateInsertCommand(");
        script.Should().Contain("native_paste_special_text_menu_item=true");
        script.Should().Contain("native_paste_special_unicode_text_menu_item=true");
        script.Should().Contain("native_paste_special_picture_menu_item=true");
        script.Should().Contain("native_paste_special_linked_picture_menu_item=true");
        script.Should().Contain("AddStyledCellBorderOverlay(content, style);");
        script.Should().Contain("CreateSelectableDrawingObjectVisual(drawingObject, width, height)");
        script.Should().Contain("AutomationProperties.SetItemStatus(container, selected ? `\"Selected`\" : `\"Not selected`\")");
        script.Should().Contain("CreateDrawingObjectVisual(drawingObject, width, height)");
        script.Should().Contain("TryCreateDrawingBitmap(imageBytes, out var bitmap)");
        script.Should().Contain("private static bool HasVisibleCellBorder(CellStyle? style)");
        script.Should().Contain("private readonly RecentFilesStore _recentFiles = RecentFilesStore.Load();");
        script.Should().Contain("_newWorkbookMenuItem.Click += (_, _) => CreateNewWorkbook();");
        script.Should().Contain("_openRecentMenuItem.Header = `\"Open Recent`\";");
        script.Should().Contain("_selectAllMenuItem.Header = `\"Select All`\";");
        script.Should().Contain("private void SelectCurrentRegionOrAll()");
        script.Should().Contain("private NativeMenu CreateNativeOpenRecentMenu(bool isIdle)");
        script.Should().Contain("private void RecordRecentWorkbook(string path)");
        script.Should().Contain("_closeWorkbookMenuItem.Click += async (_, _) => await CloseWorkbookAsync();");
        script.Should().Contain("_sessionFactory.CreateNew(viewportHeight, viewportWidth, includeObjects: true)");
        script.Should().Contain("RefreshViewportSizeForZoom();");
        script.Should().Contain("private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)");
        script.Should().Contain("private async Task<bool> ConfirmDirtyWorkbookCloseAsync(string title, string discardButtonText)");
        script.Should().Contain("AutomationProperties.SetAutomationId(saveButton, `\"DirtyWorkbookSaveButton`\");");
        script.Should().Contain("public WorkbookSession CreateNew(");
        script.Should().Contain("WorkbookFactory.Create(options)");
        script.Should().Contain("`\"Created new workbook.`\"");
        script.Should().Contain("var result = _session.AddSheet();");
        script.Should().Contain("var result = _session.RenameActiveSheet(newName);");
        script.Should().Contain("private async Task<string?> ShowRenameSheetDialogAsync(string currentName)");
        script.Should().Contain("AutomationProperties.SetAutomationId(nameBox, `\"RenameSheetNameBox`\");");
        script.Should().Contain("var validationError = _session.Workbook.ValidateSheetName(proposedName, _session.ActiveSheet.Id);");
        script.Should().Contain("button.PointerPressed += (_, args) => SelectSheetFromPointer(tab.Id, args);");
        script.Should().Contain("private void SelectSheetFromPointer(SheetId sheetId, PointerPressedEventArgs args)");
        script.Should().Contain("if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)");
        script.Should().Contain("var selectRange = modifiers.HasFlag(KeyModifiers.Shift);");
        script.Should().Contain("var toggle = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);");
        script.Should().Contain("args.Handled = true;");
        script.Should().Contain("_session.SelectSheetFromTab(sheetId, selectRange, toggle)");
        script.Should().Contain("var result = _session.DuplicateActiveSheet();");
        script.Should().Contain("var result = _session.SetActiveSheetTabColor(color);");
        script.Should().Contain("var result = _session.DeleteActiveSheet();");
        script.Should().Contain("_showGridlinesMenuItem.Header = `\"Gridlines`\";");
        script.Should().Contain("_showHeadingsMenuItem.Header = `\"Headings`\";");
        script.Should().Contain("viewMenu.Items.Add(_showGridlinesMenuItem);");
        script.Should().Contain("var result = _session.SetShowGridlines(showGridlines);");
        script.Should().Contain("var result = _session.SetShowHeadings(showHeadings);");
        script.Should().Contain("_zoomInMenuItem.Header = `\"Zoom In`\";");
        script.Should().Contain("_zoomOutMenuItem.Header = `\"Zoom Out`\";");
        script.Should().Contain("_zoom100MenuItem.Header = `\"100%`\";");
        script.Should().Contain("_zoomToSelectionMenuItem.Header = `\"Zoom to Selection`\";");
        script.Should().Contain("viewMenu.Items.Add(_zoomInMenuItem);");
        script.Should().Contain("var result = _session.SetZoomPercent(zoomPercent);");
        script.Should().Contain("_zoomText.Text = FormatZoomPercent(_session.ZoomPercent);");
        script.Should().Contain("CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor)");
        script.Should().Contain("displayHeight / zoomFactor");
        script.Should().Contain("showGridlines ? GridLine : Brushes.Transparent");
        script.Should().Contain("_freezePanesMenuItem.Header = `\"Freeze Panes`\";");
        script.Should().Contain("_freezePanesMenuItem.Click += (_, _) => FreezePanesAtActiveCell();");
        script.Should().Contain("viewMenu.Items.Add(_freezePanesMenuItem);");
        script.Should().Contain("private void ApplyFreezePaneCommand(Func<WorkbookCellEditResult> execute, string successAction, string failureMessage)");
        script.Should().Contain("_session.FreezePanesAtActiveCell");
        script.Should().Contain("public WorkbookCellEditResult FreezePanesAtActiveCell()");
        script.Should().Contain("public WorkbookCellEditResult FreezeTopRow()");
        script.Should().Contain("public WorkbookCellEditResult FreezeFirstColumn()");
        script.Should().Contain("public WorkbookCellEditResult UnfreezePanes()");
        script.Should().Contain("new SetFreezePanesCommand(ActiveSheet.Id, frozenRows, frozenCols)");
        script.Should().Contain("public WorkbookCellEditResult SetShowGridlines(bool showGridlines)");
        script.Should().Contain("public WorkbookCellEditResult SetShowHeadings(bool showHeadings)");
        script.Should().Contain("new SetWorksheetViewOptionsCommand(ActiveSheet.Id, showGridlines, showHeadings, showRulers)");
        script.Should().Contain("public WorkbookCellEditResult SetSelectedRangeBorderPreset(CellBorderPreset preset)");
        script.Should().Contain("CreateBorderPresetCommand(range, preset)");
        script.Should().Contain("CellBorderPresetPlanner.Plan(preset, range, range.Start)");
        script.Should().Contain("CellBorderPresetPlanner.RequiresPerCellPlanning(preset)");
        script.Should().Contain("BorderShortcutService.HasBorderChanges(diff)");
        script.Should().Contain("GroupedApplyStyleCommand(targetSheetIds, sourceRange, diff)");
        script.Should().Contain("public enum CellBorderPreset");
        script.Should().Contain("CellBorderPreset.All");
        script.Should().Contain("CellBorderPreset.Outside");
        script.Should().Contain("CellBorderPreset.Inside");
        script.Should().Contain("CellBorderPreset.NoBorder");
        script.Should().Contain("public static StyleDiff Plan(");
        script.Should().Contain("public static bool RequiresPerCellPlanning(CellBorderPreset preset)");
        script.Should().Contain("public int ZoomPercent => ActiveSheet.ZoomPercent;");
        script.Should().Contain("public WorkbookCellEditResult SetZoomPercent(int zoomPercent)");
        script.Should().Contain("new SetWorksheetZoomCommand(ActiveSheet.Id, zoomPercent)");
        script.Should().Contain("public WorkbookCellEditResult SetActiveSheetTabColor(CellColor? color)");
        script.Should().Contain("new SetSheetTabColorCommand(ActiveSheet.Id, color)");
        script.Should().Contain("public WorkbookCellEditResult AddSheet()");
        script.Should().Contain("public WorkbookCellEditResult RenameActiveSheet(string? name)");
        script.Should().Contain("new RenameSheetCommand(ActiveSheet.Id, newName)");
        script.Should().Contain("ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id)");
        script.Should().Contain("new DuplicateSheetCommand(sourceSheetId)");
        script.Should().Contain("public WorkbookCellEditResult DeleteActiveSheet()");
        script.Should().Contain("new RemoveSheetCommand(sheetId)");
        script.Should().Contain("public GridRange SelectCurrentRegionOrAll()");
        script.Should().Contain("OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl");
        script.Should().Contain("AppHelpInfo.BuildAboutText(versionText, PlatformAboutSummary)");
        script.Should().Contain("LegalNoticeProvider.GetDocuments().Select(document =>");
        script.Should().Contain("public sealed class RecentFilesStore");
        script.Should().Contain("public static class AtomicFileWriter");
        script.Should().Contain("Portable macOS source contains forbidden token");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_PassesFromOutsideRepositoryWorkingDirectory()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path);

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().Be(0, result.Error);
        result.Output.Should().Contain("Validated macOS app source wiring markers.");
        result.Output.Should().Contain("Validated portable macOS source hygiene");
        result.Output.Should().Contain("macOS app readiness preflight passed.");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_FailsForWindowsSpecificAvaloniaTargetFramework()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path, targetFramework: "net10.0-windows");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("Avalonia app TargetFramework must be net10.0");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_FailsForUnexpectedWorkflowRuntime()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path, workflowExtraRuntime: "osx-ppc");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("macOS workflow runtime markers must not include unexpected value 'osx-ppc'");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_FailsForForbiddenPortableSourceToken()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(
            temp.Path,
            extraAvaloniaSource: """
            namespace FreeX.App.Avalonia;

            internal static class WindowsOnlyLeak
            {
                private const string Token = "System.Windows";
            }
            """);

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        var combinedOutput = result.Output + result.Error;
        combinedOutput.Should().Contain("Portable macOS source contains forbidden token 'System.Windows'");
        combinedOutput.Should().Contain("src/FreeX.App.Avalonia/WindowsOnlyLeak.cs");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_FailsForMalformedMacOsIcon()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path);
        File.WriteAllText(
            Path.Combine(temp.Path, "src", "FreeX.App.Avalonia", "Packaging", "macos", "FreeX.icns"),
            "not-an-icns");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("macOS app icon must start with the icns magic header");
    }

    private static PowerShellResult RunScriptFromTemporaryWorkingDirectory(string scriptPath, string arguments)
    {
        using var workingDirectory = new TestTemporaryDirectory();
        return PowerShellScriptRunner.Run(scriptPath, workingDirectory.Path, arguments);
    }

    private static void CreateMinimalMacOsReadinessRepo(
        string root,
        string targetFramework = "net10.0",
        string workflowExtraRuntime = "",
        string extraAvaloniaSource = "")
    {
        WriteFile(
            root,
            "src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\FreeX.App.Services\FreeX.App.Services.csproj" />
                <ProjectReference Include="..\FreeX.Core.Calc\FreeX.Core.Calc.csproj" />
                <ProjectReference Include="..\FreeX.Core.Commands\FreeX.Core.Commands.csproj" />
                <ProjectReference Include="..\FreeX.Core.IO\FreeX.Core.IO.csproj" />
                <ProjectReference Include="..\FreeX.Core.Model\FreeX.Core.Model.csproj" />
              </ItemGroup>
              <ItemGroup>
                <PackageReference Include="Avalonia" Version="12.0.4" />
                <PackageReference Include="Avalonia.Desktop" Version="12.0.4" />
                <PackageReference Include="Avalonia.Fonts.Inter" Version="12.0.4" />
                <PackageReference Include="Avalonia.Themes.Fluent" Version="12.0.4" />
              </ItemGroup>
              <ItemGroup>
                <Content Include="Packaging\macos\FreeX.icns" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
              </ItemGroup>
              <PropertyGroup>
                <AssemblyName>FreeX</AssemblyName>
                <ApplicationTitle>FreeX</ApplicationTitle>
                <OutputType>Exe</OutputType>
                <RuntimeIdentifiers>osx-arm64;osx-x64</RuntimeIdentifiers>
                <TargetFramework>{{TargetFramework}}</TargetFramework>
              </PropertyGroup>
            </Project>
            """.Replace("{{TargetFramework}}", targetFramework));

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/Packaging/macos/Info.plist",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
            <dict>
              <key>CFBundleDisplayName</key>
              <string>FreeX</string>
              <key>CFBundleDocumentTypes</key>
              <array>
                <dict>
                  <key>CFBundleTypeExtensions</key>
                  <array>
                    <string>fxl</string>
                  </array>
                  <key>CFBundleTypeName</key>
                  <string>FreeX Workbook</string>
                  <key>CFBundleTypeRole</key>
                  <string>Editor</string>
                  <key>LSHandlerRank</key>
                  <string>Owner</string>
                </dict>
                <dict>
                  <key>CFBundleTypeExtensions</key>
                  <array>
                    <string>xlsx</string>
                    <string>xlsm</string>
                    <string>xltx</string>
                    <string>xltm</string>
                    <string>xls</string>
                    <string>xlsb</string>
                    <string>xlt</string>
                    <string>csv</string>
                    <string>tsv</string>
                    <string>tab</string>
                  </array>
                  <key>CFBundleTypeName</key>
                  <string>Spreadsheet Workbooks</string>
                  <key>CFBundleTypeRole</key>
                  <string>Viewer</string>
                  <key>LSHandlerRank</key>
                  <string>Alternate</string>
                </dict>
              </array>
              <key>CFBundleExecutable</key>
              <string>FreeX</string>
              <key>CFBundleIdentifier</key>
              <string>io.github.tony-xmelon.freex</string>
              <key>CFBundleIconFile</key>
              <string>FreeX.icns</string>
              <key>CFBundleName</key>
              <string>FreeX</string>
              <key>CFBundlePackageType</key>
              <string>APPL</string>
              <key>LSMinimumSystemVersion</key>
              <string>12.0</string>
              <key>NSHighResolutionCapable</key>
              <true/>
            </dict>
            </plist>
            """);

        WriteFile(
            root,
            ".github/workflows/macos-app.yml",
            $"""
            name: macOS App Preview
            jobs:
              macos-app:
                runs-on: macos-latest
                strategy:
                  matrix:
                    runtime:
                      - osx-arm64
                      - osx-x64
                      {FormatWorkflowRuntimeLine(workflowExtraRuntime)}
                steps:
                  - uses: actions/setup-dotnet@v5
                    with:
                      dotnet-version: 10.0.x
                  - run: dotnet build src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj --configuration Release
                  - shell: bash
                    run: |
                      app="$RUNNER_TEMP/FreeX.app"
                      artifact_root="$GITHUB_WORKSPACE/artifacts"
                      runtime="osx-arm64"
                      zip_name="freex-$runtime-macos-app.zip"
                      zip_path="$artifact_root/$zip_name"
                      unzip_root="$RUNNER_TEMP/freex-$runtime-unzip"
                      echo "Developer ID signing is disabled for pull_request events; using ad-hoc signing."
                      dotnet publish src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj \
                        --configuration Release \
                        --framework net10.0 \
                        --runtime "$runtime" \
                        --self-contained true \
                        -p:UseAppHost=true \
                        -p:PublishReadyToRun=false \
                        -p:PublishSingleFile=false \
                        --output "$app/Contents/MacOS"
                      cp src/FreeX.App.Avalonia/Packaging/macos/Info.plist "$app/Contents/Info.plist"
                      cp src/FreeX.App.Avalonia/Packaging/macos/FreeX.icns "$app/Contents/Resources/FreeX.icns"
                      plutil -lint "$app/Contents/Info.plist"
                      test -f "$app/Contents/MacOS/FreeX"
                      test -x "$app/Contents/MacOS/FreeX"
                      test -f "$app/Contents/MacOS/FreeX.dll"
                      test -f "$app/Contents/Resources/FreeX.icns"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:0:CFBundleTypeExtensions:0' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:1:CFBundleTypeExtensions:0' "$app/Contents/Info.plist"
                      lipo -archs "$app/Contents/MacOS/FreeX"
                      codesign --verify --deep --strict "$app"
                      ditto -c -k --sequesterRsrc --keepParent "$app" "$zip_path"
                      (cd "$artifact_root" && shasum -a 256 "$zip_name" > "$zip_name.sha256")
                      test -x "$unzip_root/FreeX.app/Contents/MacOS/FreeX"
                      test -f "$unzip_root/FreeX.app/Contents/MacOS/FreeX.dll"
                      xcrun notarytool submit "$zip_path"
                      xcrun stapler validate "$app"
                      tester_instructions_path="$artifact_root/freex-$runtime-macos-tester-instructions.md"
                      shasum -a 256 -c "$zip_name.sha256"
                      zip_sha256="$(cut -d ' ' -f 1 "$artifact_root/$zip_name.sha256")"
                      echo "zip_sha256=$zip_sha256"
                      cat > "$tester_instructions_path" <<EOF
                      This artifact is a preview build for macOS port validation. It is not a public release channel.
                      Use osx-arm64 for Apple Silicon Macs and osx-x64 for Intel Macs.
                      Unzip the GitHub Actions artifact wrapper first; these files are inside it.
                      Ad-hoc signed or non-notarized previews may require Control-click or right-click > Open for trusted internal testing.
                      EOF
                      "$unzip_root/FreeX.app/Contents/MacOS/FreeX" --packaging-smoke | tee "$artifact_root/smoke.log"
                      grep -q "macOS Preview Workbook" "$artifact_root/smoke.log"
                      grep -q "drawing_object_previews=3" "$artifact_root/smoke.log"
                      grep -q "roundtrip_drawing_object_previews=3" "$artifact_root/smoke.log"
                      "$unzip_root/FreeX.app/Contents/MacOS/FreeX" --packaging-smoke "$RUNNER_TEMP/smoke.csv" | tee -a "$artifact_root/smoke.log"
                      grep -q "Packaging smoke opened" "$artifact_root/smoke.log"
                      grep -q "edited, saved, and reopened" "$artifact_root/smoke.log"
                      /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$unzip_root/FreeX.app"
                      launch_clipboard_image="$RUNNER_TEMP/freex-$runtime-clipboard.png"
                      base64 -D > "$launch_clipboard_image"
                      /usr/bin/swift - "$launch_clipboard_image" <<'SWIFT'
                      NSPasteboard.general
                      pasteboard.clearContents()
                      pasteboard.writeObjects([image])
                      SWIFT
                      open -W -n -b io.github.tony-xmelon.freex "$RUNNER_TEMP/launch.csv" --args --macos-launch-smoke "$artifact_root/launch.txt" --macos-launch-smoke-verify-image-clipboard
                      osascript -e 'tell application id "io.github.tony-xmelon.freex" to quit' || true
                      grep -q "external_image_clipboard_paste_required=true" "$artifact_root/launch.txt"
                      grep -q "external_image_clipboard_paste=true" "$artifact_root/launch.txt"
                      grep -q "external_image_clipboard_picture_count=[1-9]" "$artifact_root/launch.txt"
                      grep -q "external_image_clipboard_picture_png_bytes=[1-9]" "$artifact_root/launch.txt"
                      grep -q "new_sheet_button=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_borders_button=true" "$artifact_root/launch.txt"
                      grep -q "native_file_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_new_workbook_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_open_recent_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_open_recent_item_count=[1-9]" "$artifact_root/launch.txt"
                      grep -q "native_edit_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_close_workbook_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_format_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_view_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_sheet_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_help_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_new_sheet_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_rename_sheet_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_duplicate_sheet_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_move_sheet_left_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_move_sheet_right_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_tab_color_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_tab_color_clear_item=true" "$artifact_root/launch.txt"
                      grep -q "native_tab_color_swatch_count=69" "$artifact_root/launch.txt"
                      grep -q "focusable_sheet_tab=true" "$artifact_root/launch.txt"
                      grep -q "focusable_active_sheet_tab=true" "$artifact_root/launch.txt"
                      grep -q "shell_focus_cycle_targets=true" "$artifact_root/launch.txt"
                      grep -q "sheet_tab_context_keyboard_help=true" "$artifact_root/launch.txt"
                      grep -q "sheet_tab_context_rename_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "sheet_tab_context_tab_color_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "sheet_tab_context_no_color_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "sheet_tab_context_select_all_sheets_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "sheet_tab_context_ungroup_sheets_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_select_all_sheets_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_ungroup_sheets_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_hide_sheet_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_unhide_sheet_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_delete_sheet_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_cut_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_copy_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_comments_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_validation_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_all_except_borders_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_all_merging_conditional_formats_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_column_widths_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_formulas_and_number_formats_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_values_and_number_formats_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_values_and_source_formatting_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_keep_source_column_widths_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_paste_link_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_text_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_unicode_text_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_picture_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_paste_special_linked_picture_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_select_all_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_clear_contents_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_bold_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_fill_color_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_font_color_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_fill_color_swatch_count=69" "$artifact_root/launch.txt"
                      grep -q "native_font_color_swatch_count=69" "$artifact_root/launch.txt"
                      grep -q "native_borders_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_borders_preset_count=8" "$artifact_root/launch.txt"
                      grep -q "native_cell_styles_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_cell_styles_preset_count=33" "$artifact_root/launch.txt"
                      grep -q "native_horizontal_text_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_angle_counterclockwise_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_angle_clockwise_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_vertical_text_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_rotate_text_up_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_rotate_text_down_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_show_gridlines_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_show_headings_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_zoom_in_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_zoom_out_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_zoom_100_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_zoom_to_selection_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_freeze_panes_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_freeze_top_row_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_freeze_first_column_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_unfreeze_panes_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_show_formulas_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_help_online_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_send_feedback_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_check_for_updates_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_about_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_legal_notices_menu_item=true" "$artifact_root/launch.txt"
                      echo "bundle_icon=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$app/Contents/Info.plist")"
                  - name: Upload app artifact
                    uses: actions/upload-artifact@v7
                    with:
                      if-no-files-found: error
                      path: artifacts/freex-osx-arm64-macos-tester-instructions.md
                  - name: Upload app diagnostics
                    if: always()
                    uses: actions/upload-artifact@v7
                    with:
                      name: freex-${"{{"} github.run_id {"}}"}-${"{{"} github.run_attempt {"}}"}-${"{{"} matrix.runtime {"}}"}-macos-diagnostics
                      if-no-files-found: warn
                      path: artifacts/freex-osx-arm64-macos-evidence.txt
            """);

        WriteMinimalIcns(root, "src/FreeX.App.Avalonia/Packaging/macos/FreeX.icns");

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/Program.cs",
            """
            namespace FreeX.App.Avalonia;

            internal static class Program
            {
                public static int Main(string[] args)
                {
                    if (PackagingSmokeCommand.TryRun(args, Console.Out, Console.Error, out var smokeExitCode))
                        return smokeExitCode;

                    MacOsLaunchSmokeOptions.TryParse(args, out var launchSmokeOptions, out var startupArguments, out var launchSmokeError);
                    App.StartupArguments = startupArguments;
                    App.LaunchSmokeOptions = launchSmokeOptions;
                    BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments);
                    return 0;
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/App.cs",
            """
            namespace FreeX.App.Avalonia;

            public sealed class App
            {
                private static async Task ActivatedAsync(MainWindow mainWindow, ActivatedEventArgs args)
                {
                    this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime;
                    if (args is not FileActivatedEventArgs fileArgs || fileArgs.Kind != ActivationKind.File)
                        return;

                    await mainWindow.OpenActivatedFilesAsync(fileArgs.Files);
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/MainWindow.cs",
            """
            using FreeX.Core.Calc;

            namespace FreeX.App.Avalonia;

            public sealed class MainWindow
            {
                private const string NativeWorkbookExtension = ".fxl";
                private enum ShellFocusRegion { Worksheet, Toolbar, FormulaBar, SheetTabs, StatusBar }
                private static readonly ShellFocusRegion[] ShellFocusCycle =
                [
                    ShellFocusRegion.Worksheet,
                    ShellFocusRegion.Toolbar,
                    ShellFocusRegion.FormulaBar,
                    ShellFocusRegion.SheetTabs,
                    ShellFocusRegion.StatusBar
                ];
                public async Task OpenActivatedFilesAsync(IReadOnlyList<IStorageItem> files) => await Task.CompletedTask;
                private static void RenderCell(CellStyle? style)
                {
                    CreateColorPaletteFlyout(ColorPaletteTarget.Fill, includeClearFill: true);
                    _bordersButton.Flyout = CreateBorderPresetFlyout();
                    AutomationProperties.SetAutomationId(_bordersButton, "HomeBordersButton");
                    AutomationProperties.SetHelpText(_bordersButton, "Apply or change borders on the selected cells.");
                    _bordersMenuItem.Header = "Borders";
                    _bordersMenuItem.Menu = CreateNativeBorderPresetMenu();
                    formatMenu.Items.Add(_bordersMenuItem);
                    _bordersButton.IsEnabled = isIdle;
                    _bordersMenuItem.IsEnabled = _bordersButton.IsEnabled;
                    CreateNativePasteSpecialMenu();
                    PasteSpecialClipboardAtActiveCell(text, mode, options);
                    /*
                    CreatePasteCommentsMenuItem("Comments and Notes")
                    CreatePasteDataValidationMenuItem("Validation")
                    CreatePasteSpecialMenuItem("All Except Borders", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllExceptBorders))
                    CreatePasteSpecialMenuItem("All Merging Conditional Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats))
                    CreatePasteColumnWidthsMenuItem("Column Widths")
                    CreatePasteSpecialMenuItem("Formulas and Number Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats))
                    CreatePasteSpecialMenuItem("Values and Number Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats))
                    CreatePasteSpecialMenuItem("Values and Source Formatting", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting))
                    CreatePasteSpecialMenuItem("Keep Source Column Widths", PasteCellsMode.All, default, keepSourceColumnWidths: true)
                    CreatePasteLinkMenuItem("Paste Link")
                    CreateNativePasteCommentsMenuItem("Comments and Notes")
                    CreateNativePasteDataValidationMenuItem("Validation")
                    CreateNativePasteSpecialMenuItem("All Except Borders", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllExceptBorders))
                    CreateNativePasteSpecialMenuItem("All Merging Conditional Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats))
                    CreateNativePasteColumnWidthsMenuItem("Column Widths")
                    CreateNativePasteSpecialMenuItem("Formulas and Number Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats))
                    CreateNativePasteSpecialMenuItem("Values and Number Formats", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats))
                    CreateNativePasteSpecialMenuItem("Values and Source Formatting", PasteCellsMode.All, new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting))
                    CreateNativePasteSpecialMenuItem("Keep Source Column Widths", PasteCellsMode.All, default, keepSourceColumnWidths: true)
                    CreateNativePasteLinkMenuItem("Paste Link")
                    private async Task PasteColumnWidthsFromClipboardAsync(string label)
                    _session.PasteColumnWidthsFromClipboardAtActiveCell(text)
                    private async Task PasteCommentsFromClipboardAsync(string label)
                    _session.PasteCommentsFromClipboardAtActiveCell(text)
                    private async Task PasteDataValidationFromClipboardAsync(string label)
                    _session.PasteDataValidationFromClipboardAtActiveCell(text)
                    private async Task PasteLinkFromClipboardAsync(string label)
                    _session.PasteLinkFromClipboardAtActiveCell(text)
                    HasNativePasteSpecialCommentsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Comments and Notes")
                    HasNativePasteSpecialValidationMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Validation")
                    HasNativePasteSpecialAllExceptBordersMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "All Except Borders")
                    HasNativePasteSpecialAllMergingConditionalFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "All Merging Conditional Formats")
                    HasNativePasteSpecialColumnWidthsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Column Widths")
                    HasNativePasteSpecialFormulasAndNumberFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Formulas and Number Formats")
                    HasNativePasteSpecialValuesAndNumberFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Values and Number Formats")
                    HasNativePasteSpecialValuesAndSourceFormattingMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Values and Source Formatting")
                    HasNativePasteSpecialKeepSourceColumnWidthsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Keep Source Column Widths")
                    HasNativePasteSpecialPasteLinkMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Paste Link")
                    private static bool HasNativeSubmenuItem(NativeMenu? menu, string expectedHeader)
                    */
                    CreatePasteSpecialTextMenuItem("Text");
                    CreatePasteSpecialTextMenuItem("Unicode Text");
                    CreatePastePictureMenuItem("Picture", linkedPicture: false);
                    CreatePastePictureMenuItem("Linked Picture", linkedPicture: true);
                    CreateNativePasteSpecialTextMenuItem("Text");
                    CreateNativePasteSpecialTextMenuItem("Unicode Text");
                    CreateNativePastePictureMenuItem("Picture", linkedPicture: false);
                    CreateNativePastePictureMenuItem("Linked Picture", linkedPicture: true);
                    _session.PasteClipboardTextAtActiveCell(text, preserveText: true);
                    _session.ShouldPreferExternalClipboardImage(text);
                    private async Task<bool> TryPasteClipboardImageAsync(IClipboard clipboard, CellAddress destination)
                    await clipboard.TryGetBitmapAsync()
                    bitmap.Save(stream)
                    _session.PasteClipboardImageAtActiveCell(pngBytes, pixelWidth, pixelHeight);
                    internal async Task<bool> TryPasteLaunchSmokeClipboardImageAsync()
                    return await TryPasteClipboardImageAsync(clipboard, _session.ActiveCell);
                    private async Task PastePictureFromClipboardAsync(string label, bool linkedPicture)
                    _session.PastePictureFromClipboardAtActiveCell(text, linkedPicture);
                    HasNativePasteSpecialTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Text");
                    HasNativePasteSpecialUnicodeTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Unicode Text");
                    HasNativePasteSpecialPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Picture");
                    HasNativePasteSpecialLinkedPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Linked Picture");
                    CellColorPalettePlanner.BuildDefaultSwatches();
                    CreateSelectableDrawingObjectVisual(drawingObject, width, height);
                    AutomationProperties.SetAutomationId(container, $"DrawingObject{drawingObject.Kind}{drawingObject.Id:N}");
                    AutomationProperties.SetHelpText(container, "Selects this drawing object preview in the workbook viewport.");
                    AutomationProperties.SetItemStatus(container, selected ? "Selected" : "Not selected");
                    container.PointerPressed += (_, args) => { };
                    if (args.Key is Key.Enter or Key.Space) { }
                    CreateSelectedDrawingObjectAdorner();
                    ClearSelectedDrawingObject();
                    CreateDrawingObjectVisual(drawingObject, width, height);
                    TryCreateDrawingBitmap(imageBytes, out var bitmap);
                    AddStyledCellBorderOverlay(content, style);
                    private readonly RecentFilesStore _recentFiles = RecentFilesStore.Load();
                    _newWorkbookMenuItem.Click += (_, _) => CreateNewWorkbook();
                    _openRecentMenuItem.Header = "Open Recent";
                    _openRecentMenuItem.Menu = CreateNativeOpenRecentMenu(isIdle: true);
                    fileMenu.Items.Add(_openRecentMenuItem);
                    RefreshNativeOpenRecentMenu(isIdle);
                    _selectAllMenuItem.Header = "Select All";
                    _selectAllMenuItem.Gesture = new KeyGesture(Key.A, KeyModifiers.Meta);
                    _selectAllMenuItem.Click += (_, _) => SelectCurrentRegionOrAll();
                    editMenu.Items.Add(_selectAllMenuItem);
                    _selectAllMenuItem.IsEnabled = isIdle;
                    e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A;
                    else if (e.Key == Key.A && HasOnlyCommandModifier(e.KeyModifiers)) { }
                    Header = "(No Recent Workbooks)";
                    entries.Sort(static (left, right) => right.LastOpened.CompareTo(left.LastOpened));
                    _recentFiles.AddOrUpdate(path);
                    RecordRecentWorkbook(target.Path);
                    _closeWorkbookMenuItem.Click += async (_, _) => await CloseWorkbookAsync();
                    fileMenu.Items.Add(_newWorkbookMenuItem);
                    fileMenu.Items.Add(_closeWorkbookMenuItem);
                    _sessionFactory.CreateNew(viewportHeight, viewportWidth, includeObjects: true);
                    RefreshViewportSizeForZoom();
                    Closing += MainWindow_Closing;
                    ConfirmDirtyWorkbookCloseAsync("Close Workbook", "Discard and Close").ToString();
                    ResetToNewWorkbook("Closed workbook.");
                    ConfirmDirtyWorkbookCloseAsync("Close FreeX", "Discard and Close").ToString();
                    TryQuitApplicationAsync().ToString();
                    ConfirmDirtyWorkbookCloseAsync("Quit FreeX", "Discard and Quit").ToString();
                    _allowCloseWithoutDirtyPrompt = true;
                    SaveCurrentWorkbookAsync().ToString();
                    AutomationProperties.SetAutomationId(saveButton, "DirtyWorkbookSaveButton");
                    AutomationProperties.SetAutomationId(discardButton, "DirtyWorkbookDiscardButton");
                    AutomationProperties.SetAutomationId(cancelButton, "DirtyWorkbookCancelButton");
                    _newSheetButton.Click += (_, _) => AddNewSheet();
                    _newSheetMenuItem.Click += (_, _) => AddNewSheet();
                    _renameSheetMenuItem.Click += async (_, _) => await RenameActiveSheetAsync();
                    _duplicateSheetMenuItem.Click += (_, _) => DuplicateActiveSheet();
                    _moveSheetLeftMenuItem.Click += (_, _) => MoveActiveSheetLeft();
                    _moveSheetRightMenuItem.Click += (_, _) => MoveActiveSheetRight();
                    _tabColorMenuItem.Header = "Tab Color";
                    _tabColorMenuItem.Menu = CreateNativeSheetTabColorMenu();
                    _selectAllSheetsMenuItem.Header = "Select All Sheets";
                    _selectAllSheetsMenuItem.Click += (_, _) => SelectAllVisibleSheets();
                    _ungroupSheetsMenuItem.Header = "Ungroup Sheets";
                    _ungroupSheetsMenuItem.Click += (_, _) => UngroupSheets();
                    sheetMenu.Items.Add(_tabColorMenuItem);
                    sheetMenu.Items.Add(_selectAllSheetsMenuItem);
                    sheetMenu.Items.Add(_ungroupSheetsMenuItem);
                    _tabColorMenuItem.IsEnabled = isIdle;
                    _selectAllSheetsMenuItem.IsEnabled = isIdle && _session.SheetTabs.Count > 1;
                    _ungroupSheetsMenuItem.IsEnabled = isIdle && _session.IsWorkbookGrouped;
                    private string FormatWindowWorkbookTitle()
                    ? $"{_session.DisplayName} [Group]"
                    var isGroupedTab = tab.IsGrouped && _session.IsWorkbookGrouped;
                    tab.TabColor is { } tabColor ? Brush(tabColor) : Brushes.Transparent;
                    var clearColorItem = new NativeMenuItem { Header = "No Color" };
                    clearColorItem.Click += (_, _) => ApplyActiveSheetTabColor(null);
                    ApplyActiveSheetTabColor(swatch.Color);
                    var result = _session.SetActiveSheetTabColor(color);
                    var changed = _session.SelectAllVisibleSheets();
                    var changed = _session.UngroupSheets();
                    _hideSheetMenuItem.Click += (_, _) => HideActiveSheet();
                    _unhideSheetMenuItem.Click += async (_, _) => await UnhideSheetAsync();
                    _deleteSheetMenuItem.Click += (_, _) => DeleteActiveSheet();
                    _showGridlinesMenuItem.Header = "Gridlines";
                    _showGridlinesMenuItem.ToggleType = MenuItemToggleType.CheckBox;
                    _showGridlinesMenuItem.Click += (_, _) => ToggleShowGridlines();
                    _showHeadingsMenuItem.Header = "Headings";
                    _showHeadingsMenuItem.ToggleType = MenuItemToggleType.CheckBox;
                    _showHeadingsMenuItem.Click += (_, _) => ToggleShowHeadings();
                    _zoomInMenuItem.Header = "Zoom In";
                    _zoomOutMenuItem.Header = "Zoom Out";
                    _zoom100MenuItem.Header = "100%";
                    _zoomToSelectionMenuItem.Header = "Zoom to Selection";
                    _zoomInMenuItem.Click += (_, _) => ZoomIn();
                    _zoomOutMenuItem.Click += (_, _) => ZoomOut();
                    _zoom100MenuItem.Click += (_, _) => ZoomTo100Percent();
                    _zoomToSelectionMenuItem.Click += (_, _) => ZoomToSelection();
                    viewMenu.Items.Add(_showGridlinesMenuItem);
                    viewMenu.Items.Add(_showHeadingsMenuItem);
                    viewMenu.Items.Add(_zoomInMenuItem);
                    viewMenu.Items.Add(_zoomOutMenuItem);
                    viewMenu.Items.Add(_zoom100MenuItem);
                    viewMenu.Items.Add(_zoomToSelectionMenuItem);
                    _freezePanesMenuItem.Header = "Freeze Panes";
                    _freezePanesMenuItem.Click += (_, _) => FreezePanesAtActiveCell();
                    _freezeTopRowMenuItem.Header = "Freeze Top Row";
                    _freezeFirstColumnMenuItem.Header = "Freeze First Column";
                    _unfreezePanesMenuItem.Header = "Unfreeze Panes";
                    viewMenu.Items.Add(_freezePanesMenuItem);
                    viewMenu.Items.Add(_freezeTopRowMenuItem);
                    viewMenu.Items.Add(_freezeFirstColumnMenuItem);
                    viewMenu.Items.Add(_unfreezePanesMenuItem);
                    _showFormulasMenuItem.ToggleType = MenuItemToggleType.CheckBox;
                    _showFormulasMenuItem.Click += (_, _) => ToggleShowFormulas();
                    Header = "View";
                    var sheetItem = new NativeMenuItem { Header = "Sheet" };
                    var result = _session.AddSheet();
                    var result = _session.RenameActiveSheet(newName);
                    ShowRenameSheetDialogAsync(currentName).ToString();
                    AutomationProperties.SetAutomationId(nameBox, "RenameSheetNameBox");
                    var validationError = _session.Workbook.ValidateSheetName(proposedName, _session.ActiveSheet.Id);
                    private const string SheetTabContextHelpText = "Selects this sheet. Press F6 repeatedly to reach sheet tabs, use arrow keys to switch sheets, or right-click/press Shift+F10 for sheet tab options.";
                    _sheetGridHost.Focusable = true;
                    AutomationProperties.SetName(_sheetGridHost, "Worksheet");
                    _zoomText.Focusable = true;
                    AutomationProperties.SetName(_zoomText, "Zoom");
                    Focusable = true,
                    Tag = tab.Id,
                    button.ContextMenu = CreateSheetTabContextMenu(tab);
                    button.DoubleTapped += async (_, args) => await RenameSheetFromTabAsync(tab.Id, args);
                    button.KeyDown += (_, args) => HandleSheetTabKeyDown(tab.Id, button, args);
                    AutomationProperties.SetName(button, tab.Name);
                    AutomationProperties.SetHelpText(button, SheetTabContextHelpText);
                    ItemsSource = CreateSheetTabContextMenuItems(tab, isIdle, sheetTabIndex).ToArray();
                    CreateSheetTabContextMenuItem(tab, "Rename...", async () => await RenameActiveSheetAsync(), isIdle);
                    CreateSheetTabContextMenuItem(tab, "Insert Sheet", AddNewSheet, isIdle);
                    CreateSheetTabContextMenuItem(tab, "Duplicate", DuplicateActiveSheet, isIdle);
                    CreateSheetTabContextMenuItem(tab, "Delete Sheet", DeleteActiveSheet, isIdle);
                    CreateSheetTabContextMenuItem(tab, "Hide", HideActiveSheet, isIdle && _session.SheetTabs.Count > 1);
                    CreateSheetTabContextMenuItem(tab, "Unhide...", async () => await UnhideSheetAsync(), isIdle && _session.HiddenSheets.Count > 0);
                    CreateSheetTabColorContextMenuItem(tab, isIdle);
                    CreateSheetTabContextMenuItem(tab, "Select All Sheets", SelectAllVisibleSheets, isIdle && _session.SheetTabs.Count > 1);
                    CreateSheetTabContextMenuItem(tab, "Ungroup Sheets", UngroupSheets, isIdle && _session.IsWorkbookGrouped);
                    CreateSheetTabContextMenuItem(tab, "Move Left", MoveActiveSheetLeft, isIdle && sheetTabIndex > 0);
                    button.PointerPressed += (_, args) => SelectSheetFromPointer(tab.Id, args);
                    args.Key == Key.Apps;
                    args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.Shift;
                    contextMenu.Opened -= SheetTabContextMenu_Opened;
                    contextMenu.Opened += SheetTabContextMenu_Opened;
                    contextMenu.Open(button);
                    NavigateSheetTabFromKeyboard(sheetId, args);
                    if (args.KeyModifiers != KeyModifiers.None) { }
                    Key.Left => GetAdjacentSheetTabId(sheetId, direction: -1);
                    Key.Right => GetAdjacentSheetTabId(sheetId, direction: 1);
                    Key.Home => GetEdgeSheetTabId(first: true);
                    Key.End => GetEdgeSheetTabId(first: false);
                    Math.Clamp(targetIndex, 0, _session.SheetTabs.Count - 1);
                    FirstOrDefault(item => item.IsEnabled)?.Focus();
                    if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed);
                    var selectRange = modifiers.HasFlag(KeyModifiers.Shift);
                    var toggle = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
                    args.Handled = true;
                    _session.SelectSheetFromTab(sheetId, selectRange, toggle);
                    var result = _session.DuplicateActiveSheet();
                    var result = _session.MoveActiveSheetLeft();
                    var result = _session.MoveActiveSheetRight();
                    var result = _session.HideActiveSheet();
                    UnhideSheetAsync().ToString();
                    ShowUnhideSheetDialogAsync(_session.HiddenSheets).ToString();
                    AutomationProperties.SetAutomationId(sheetBox, "UnhideSheetList");
                    var result = _session.UnhideSheet(sheet.Id);
                    var result = _session.DeleteActiveSheet();
                    ToggleShowGridlines();
                    var result = _session.SetShowGridlines(showGridlines);
                    ToggleShowHeadings();
                    var result = _session.SetShowHeadings(showHeadings);
                    ZoomIn();
                    ApplyZoomPercent(_session.ZoomPercent + ZoomStepPercent, "Zoom In failed.");
                    ZoomOut();
                    ApplyZoomPercent(_session.ZoomPercent - ZoomStepPercent, "Zoom Out failed.");
                    ZoomTo100Percent();
                    ApplyZoomPercent(100, "100% Zoom failed.");
                    ZoomToSelection();
                    ApplyZoomPercent(zoomPercent, "Zoom to Selection failed.");
                    var result = _session.SetZoomPercent(zoomPercent);
                    CalculateZoomAxisFitPercent(viewportWidth, range.ColCount, ZoomToSelectionDefaultColumnWidth);
                    _zoomText.Text = FormatZoomPercent(_session.ZoomPercent);
                    var showHeadings = _session.ActiveSheet.ShowHeadings;
                    var zoomFactor = GetActiveZoomFactor();
                    showGridlines ? GridLine : Brushes.Transparent;
                    CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor);
                    CalculateDisplayedGridHeight(viewport, showHeadings, zoomFactor);
                    fontSize * zoomFactor;
                    displayHeight / zoomFactor;
                    AddGridChild(grid, CreateCell(cell, row, col, zoomFactor, colWidth, rowHeight));
                    CellTextOrientationLayoutPlanner.HasTextOrientation(textRotation);
                    CreateOrientedCellContent();
                    var layout = CellTextOrientationLayoutPlanner.CalculateLayout();
                    CreateTextRotationTransform(layout.TransformAngle);
                    textBlock.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
                    textBlock.RenderTransform = transform;
                    Canvas.SetLeft(textBlock, layout.TextPoint.X);
                    Canvas.SetTop(textBlock, layout.TextPoint.Y);
                    CellTextOrientationLayoutPlanner.PrepareDisplayText(text, textRotation);
                    CellTextOrientationLayoutPlanner.NormalizeRotationForDisplay(textRotation);
                    private static RotateTransform? CreateTextRotationTransform(double transformAngle)
                    return Math.Abs(transformAngle) <= 0.001 ? null : new RotateTransform(transformAngle);
                    FreezePanesAtActiveCell();
                    FreezeTopRow();
                    FreezeFirstColumn();
                    UnfreezePanes();
                    ApplyFreezePaneCommand(_session.FreezePanesAtActiveCell, "Froze panes at", "Freeze Panes failed.");
                    ToggleShowFormulas();
                    var result = _session.SetShowFormulas(showFormulas);
                    if (e.Key == Key.F11 && e.KeyModifiers == KeyModifiers.Shift) { }
                    if (IsShellFocusCycleKey(e)) { }
                    CycleShellFocus(reverse: e.KeyModifiers == KeyModifiers.Shift);
                    args.Key == Key.F6 && args.KeyModifiers == KeyModifiers.None;
                    if (e.Key == Key.PageUp && HasCommandAndShiftModifiers(e.KeyModifiers)) { SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: true); }
                    if (e.Key == Key.PageDown && HasCommandAndShiftModifiers(e.KeyModifiers)) { SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: true); }
                    if (e.Key == Key.PageUp && HasOnlyCommandModifier(e.KeyModifiers)) { SelectAdjacentVisibleSheetFromKeyboard(direction: -1, selectRange: false); }
                    if (e.Key == Key.PageDown && HasOnlyCommandModifier(e.KeyModifiers)) { SelectAdjacentVisibleSheetFromKeyboard(direction: 1, selectRange: false); }
                    _helpOnlineMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, "Help Online");
                    _sendFeedbackMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.FeedbackUrl, "Send Feedback");
                    _checkForUpdatesMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.LatestReleaseUrl, "Check for Updates");
                    _aboutMenuItem.Click += async (_, _) => await ShowAboutDialogAsync();
                    _legalNoticesMenuItem.Click += async (_, _) => await ShowLegalNoticesDialogAsync();
                    var item = new NativeMenuItem { Header = "Help" };
                    TopLevel.GetTopLevel(this)?.Launcher.ToString();
                    AppHelpInfo.BuildAboutText(versionText, PlatformAboutSummary);
                    LegalNoticeProvider.GetDocuments().Select(document => document.Title);
                    HasFocusableSheetTab: HasSheetTabButton(button => button.Focusable);
                    HasFocusableActiveSheetTab: FindSheetTabButton(_session.ActiveSheet.Id)?.Focusable == true;
                    HasShellFocusCycleTargets: _sheetGridHost.Focusable &&;
                    GetToolbarFocusTargets().Any(control => control.Focusable) &&;
                    _formulaBox.Focusable &&;
                    _zoomText.Focusable;
                    HasBordersButton: _bordersButton.Content?.ToString() == "Borders";
                    HasNativeBordersMenuItem: HasNativeMenuItem(_bordersMenuItem, "Borders", requireGesture: false);
                    NativeBordersPresetCount: nativeBordersPresetCount;
                    HasSheetTabContextKeyboardHelp: HasSheetTabButton(button =>;
                    string.Equals(AutomationProperties.GetHelpText(button), SheetTabContextHelpText, StringComparison.Ordinal));
                    HasSheetTabContextRenameMenuItem: HasSheetTabContextMenuItem("Rename...");
                    HasSheetTabContextTabColorMenuItem: HasSheetTabContextMenuItem("Tab Color");
                    HasSheetTabContextNoColorMenuItem: HasSheetTabContextSubmenuItem("Tab Color", "No Color");
                    HasSheetTabContextSelectAllSheetsMenuItem: HasSheetTabContextMenuItem("Select All Sheets");
                    HasSheetTabContextUngroupSheetsMenuItem: HasSheetTabContextMenuItem("Ungroup Sheets");
                }
                private MenuFlyout CreateBorderPresetFlyout() => new();
                private MenuItem CreateBorderPresetMenuItem(CellBorderPreset preset)
                {
                    AutomationProperties.SetAutomationId(menuItem, $"HomeBorders{preset}MenuItem");
                    return new();
                }
                private NativeMenu CreateNativeBorderPresetMenu() => new();
                private NativeMenuItem CreateNativeBorderPresetMenuItem(CellBorderPreset preset) => new();
                private void ApplySelectedRangeBorderPreset(CellBorderPreset preset)
                {
                    var result = _session.SetSelectedRangeBorderPreset(preset);
                }
                private static bool HasVisibleCellBorder(CellStyle? style) => true;
                private NativeMenu CreateNativeOpenRecentMenu(bool isIdle) => new();
                private void SelectCurrentRegionOrAll()
                {
                    var range = _session.SelectCurrentRegionOrAll();
                }
                private List<RecentFileEntry> GetOpenableRecentWorkbookEntries() => new();
                private async Task OpenRecentWorkbookAsync(string path) => await Task.CompletedTask;
                private void RecordStartupRecentWorkbook(StartupWorkbookLoadResult source) { }
                private void RecordRecentWorkbook(string path) { }
                private void CreateNewWorkbook() { }
                private async Task CloseWorkbookAsync() => await Task.CompletedTask;
                private void ResetToNewWorkbook(string status) { }
                private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e) => await Task.CompletedTask;
                private async Task TryQuitApplicationAsync() => await Task.CompletedTask;
                private async Task<bool> ConfirmDirtyWorkbookCloseAsync(string title, string discardButtonText) => await Task.FromResult(true);
                private async Task<DirtyWorkbookCloseChoice> ShowDirtyWorkbookCloseDialogAsync(string title, string discardButtonText) => await Task.FromResult(DirtyWorkbookCloseChoice.Cancel);
                private async Task SaveDirtyWorkbookBeforeCloseAsync() => await SaveCurrentWorkbookAsync();
                private async Task SaveCurrentWorkbookAsync() => await Task.CompletedTask;
                private async Task RenameActiveSheetAsync() => await Task.CompletedTask;
                private async Task<string?> ShowRenameSheetDialogAsync(string currentName) => await Task.FromResult<string?>(currentName);
                private async Task PasteSpecialExternalTextFromClipboardAsync(string label) => await Task.CompletedTask;
                private async Task UnhideSheetAsync() => await Task.CompletedTask;
                private async Task<WorkbookHiddenSheet?> ShowUnhideSheetDialogAsync(IReadOnlyList<WorkbookHiddenSheet> hiddenSheets) => await Task.FromResult<WorkbookHiddenSheet?>(null);
                private ContextMenu CreateSheetTabContextMenu(WorkbookSheetTab tab) => new();
                private IEnumerable<Control> CreateSheetTabContextMenuItems(WorkbookSheetTab tab, bool isIdle, int sheetTabIndex) => [];
                private MenuItem CreateSheetTabContextMenuItem(WorkbookSheetTab tab, string header, Action action, bool isEnabled) => new();
                private bool SelectSheetForContextCommand(SheetId sheetId) => true;
                private async Task RenameSheetFromTabAsync(SheetId sheetId, TappedEventArgs args) => await RenameActiveSheetAsync();
                private void HandleSheetTabKeyDown(SheetId sheetId, Button button, KeyEventArgs args) { }
                private void OpenSheetTabContextMenuFromKeyboard(SheetId sheetId, Button button, KeyEventArgs args) { }
                private static bool IsSheetTabContextMenuKey(KeyEventArgs args) => true;
                private void NavigateSheetTabFromKeyboard(SheetId sheetId, KeyEventArgs args) { }
                private bool SelectAdjacentVisibleSheetFromKeyboard(int direction, bool selectRange) => true;
                private void SelectSheetTabFromKeyboard(SheetId sheetId, bool selectRange) { }
                private SheetId? GetAdjacentSheetTabId(SheetId sheetId, int direction) => null;
                private SheetId? GetEdgeSheetTabId(bool first) => null;
                private bool FocusActiveSheetTab() => true;
                private bool FocusSheetTab(SheetId sheetId) => true;
                private static void SheetTabContextMenu_Opened(object? sender, RoutedEventArgs args) { }
                private Button? FindSheetTabButton(SheetId sheetId) => button.Tag is SheetId tag && tag == sheetId ? new() : null;
                private bool HasSheetTabButton(Func<Button, bool> predicate) => true;
                private void SelectSheetFromPointer(SheetId sheetId, PointerPressedEventArgs args) { }
                private NativeMenu CreateNativeSheetTabColorMenu() => new();
                private NativeMenuItem CreateNativeSheetTabColorSwatchMenuItem(CellColorSwatch swatch) => new();
                private void ApplyActiveSheetTabColor(CellColor? color) { }
                private void SelectAllVisibleSheets() { }
                private void UngroupSheets() { }
                private void ToggleShowGridlines() { }
                private void ToggleShowHeadings() { }
                private void ZoomIn() => ApplyZoomPercent(_session.ZoomPercent + ZoomStepPercent, "Zoom In failed.");
                private void ZoomOut() => ApplyZoomPercent(_session.ZoomPercent - ZoomStepPercent, "Zoom Out failed.");
                private void ZoomTo100Percent() => ApplyZoomPercent(100, "100% Zoom failed.");
                private void ZoomToSelection() { }
                private void ApplyZoomPercent(int zoomPercent, string errorMessage) { }
                private int CalculateZoomToSelectionPercent() => 100;
                private double GetActiveZoomFactor() => 1;
                private void FreezePanesAtActiveCell() { }
                private void FreezeTopRow() { }
                private void FreezeFirstColumn() { }
                private void UnfreezePanes() { }
                private void ApplyFreezePaneCommand(Func<WorkbookCellEditResult> execute, string successAction, string failureMessage) { }
                private void ToggleShowFormulas() { }
                private static bool HasCommandAndShiftModifiers(KeyModifiers modifiers) => true;
                private static bool IsShellFocusCycleKey(KeyEventArgs args) => true;
                private void CycleShellFocus(bool reverse) { }
                private static ShellFocusRegion GetNextShellFocusRegion(ShellFocusRegion current, bool reverse) => current;
                private ShellFocusRegion GetCurrentShellFocusRegion() => ShellFocusRegion.Worksheet;
                private bool FocusShellRegion(ShellFocusRegion region) => region switch
                {
                    ShellFocusRegion.Toolbar => FocusFirstEnabledToolbarControl(),
                    ShellFocusRegion.FormulaBar => FocusControl(_formulaBox),
                    ShellFocusRegion.SheetTabs => FocusActiveSheetTab(),
                    ShellFocusRegion.StatusBar => FocusControl(_zoomText),
                    _ => FocusControl(_sheetGridHost)
                };
                private bool FocusFirstEnabledToolbarControl() => true;
                private IReadOnlyList<Control> GetToolbarFocusTargets() =>
                [
                    _openButton,
                    _alignRightButton
                ];
                private bool IsAnyToolbarControlFocused() => true;
                private bool IsAnySheetTabFocused() => true;
                private static bool FocusControl(Control control) => true;
                internal MacOsLaunchSmokeSnapshot CreateLaunchSmokeSnapshot()
                {
                    ExternalImageClipboardPictureCount: externalImageClipboardPictures.Length;
                    ExternalImageClipboardPicturePngByteCount: externalImageClipboardPictures.Sum(static picture => picture.ImageBytes!.Length);
                    return new();
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.Core.Calc/CellTextOrientationLayoutPlanner.cs",
            """
            namespace FreeX.Core.Calc;

            public readonly record struct CellTextLayoutPoint(double X, double Y);
            public readonly record struct CellTextLayoutRect(double Left, double Top, double Width, double Height);
            public readonly record struct CellTextOrientationLayout(CellTextLayoutPoint TextPoint, CellTextLayoutRect Bounds, double TransformAngle);

            public static class CellTextOrientationLayoutPlanner
            {
                public static bool HasTextOrientation(int textRotation) => true;
                public static bool IsStackedTextRotation(int textRotation) => textRotation == 255;
                public static int NormalizeRotationForDisplay(int textRotation) => textRotation;
                public static string PrepareDisplayText(string text, int textRotation) => text;
                public static CellTextOrientationLayout CalculateLayout() => new();
                public static bool ShouldClip() => false;
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/MacOsLaunchSmoke.cs",
            """
            namespace FreeX.App.Avalonia;

            internal sealed class MacOsLaunchSmokeOptions
            {
                public const string Argument = "--macos-launch-smoke";
                public const string VerifyImageClipboardPasteArgument = "--macos-launch-smoke-verify-image-clipboard";
                public bool VerifyImageClipboardPaste { get; }
                public static void Parse(List<string> filteredArguments, out string[] startupArguments)
                {
                    var reportPath = "";
                    var verifyImageClipboardPaste = true;
                    new MacOsLaunchSmokeOptions(reportPath, verifyImageClipboardPaste).ToString();
                    startupArguments = filteredArguments.ToArray();
                }
            }

            internal sealed class MacOsLaunchSmokeSnapshot
            {
                public bool IsPassed =>
                    HasNativeFileMenu &&
                    HasNativeEditMenu &&
                    HasNativeFormatMenu &&
                    HasNativeViewMenu &&
                    HasNativeSheetMenu &&
                    HasNativeHelpMenu &&
                    HasNativeNewWorkbookMenuItem &&
                    HasNativeOpenRecentMenuItem &&
                    NativeOpenRecentItemCount > 0 &&
                    HasNativeSelectAllMenuItem &&
                    HasNativeCloseWorkbookMenuItem &&
                    HasNativeRenameSheetMenuItem &&
                    HasNativeMoveSheetLeftMenuItem &&
                    HasNativeMoveSheetRightMenuItem &&
                    HasNativeTabColorMenuItem &&
                    HasNativeClearTabColorMenuItem &&
                    NativeTabColorSwatchCount == CellColorPalettePlanner.BuildDefaultSwatches().Count &&
                    HasBordersButton &&
                    HasFocusableSheetTab &&
                    HasFocusableActiveSheetTab &&
                    HasShellFocusCycleTargets &&
                    HasSheetTabContextKeyboardHelp &&
                    HasSheetTabContextRenameMenuItem &&
                    HasSheetTabContextTabColorMenuItem &&
                    HasSheetTabContextNoColorMenuItem &&
                    HasSheetTabContextSelectAllSheetsMenuItem &&
                    HasSheetTabContextUngroupSheetsMenuItem &&
                    HasNativeSelectAllSheetsMenuItem &&
                    HasNativeUngroupSheetsMenuItem &&
                    HasNativeHideSheetMenuItem &&
                    HasNativeUnhideSheetMenuItem &&
                    HasNativeDeleteSheetMenuItem &&
                    HasNativeShowGridlinesMenuItem &&
                    HasNativeShowHeadingsMenuItem &&
                    HasNativeZoomInMenuItem &&
                    HasNativeZoomOutMenuItem &&
                    HasNativeZoom100MenuItem &&
                    HasNativeZoomToSelectionMenuItem &&
                    HasNativeFreezePanesMenuItem &&
                    HasNativeFreezeTopRowMenuItem &&
                    HasNativeFreezeFirstColumnMenuItem &&
                    HasNativeUnfreezePanesMenuItem &&
                    HasNativeShowFormulasMenuItem &&
                    HasNativePasteSpecialCommentsMenuItem &&
                    HasNativePasteSpecialValidationMenuItem &&
                    HasNativePasteSpecialAllExceptBordersMenuItem &&
                    HasNativePasteSpecialAllMergingConditionalFormatsMenuItem &&
                    HasNativePasteSpecialColumnWidthsMenuItem &&
                    HasNativePasteSpecialFormulasAndNumberFormatsMenuItem &&
                    HasNativePasteSpecialValuesAndNumberFormatsMenuItem &&
                    HasNativePasteSpecialValuesAndSourceFormattingMenuItem &&
                    HasNativePasteSpecialKeepSourceColumnWidthsMenuItem &&
                    HasNativePasteSpecialPasteLinkMenuItem &&
                    HasNativePasteSpecialTextMenuItem &&
                    HasNativePasteSpecialUnicodeTextMenuItem &&
                    HasNativePasteSpecialPictureMenuItem &&
                    HasNativePasteSpecialLinkedPictureMenuItem &&
                    HasNativeBordersMenuItem &&
                    NativeBordersPresetCount == Enum.GetValues<CellBorderPreset>().Length &&
                    HasNativeCellStylesMenuItem &&
                    HasNativeCopyMenuItem;
                private bool HasNativeFileMenu { get; }
                private bool HasNativeEditMenu { get; }
                private bool HasNativeFormatMenu { get; }
                private bool HasNativeViewMenu { get; }
                private bool HasNativeSheetMenu { get; }
                private bool HasNativeHelpMenu { get; }
                private bool HasNativeNewWorkbookMenuItem { get; }
                private bool HasNativeOpenRecentMenuItem { get; }
                private int NativeOpenRecentItemCount { get; }
                private bool HasNativeSelectAllMenuItem { get; }
                private bool HasNativeCloseWorkbookMenuItem { get; }
                private bool HasNativeRenameSheetMenuItem { get; }
                private bool HasNativeMoveSheetLeftMenuItem { get; }
                private bool HasNativeMoveSheetRightMenuItem { get; }
                private bool HasNativeTabColorMenuItem { get; }
                private bool HasNativeClearTabColorMenuItem { get; }
                private int NativeTabColorSwatchCount { get; }
                private bool HasBordersButton { get; }
                private bool HasFocusableSheetTab { get; }
                private bool HasFocusableActiveSheetTab { get; }
                private bool HasShellFocusCycleTargets { get; }
                private bool HasSheetTabContextKeyboardHelp { get; }
                private bool HasSheetTabContextRenameMenuItem { get; }
                private bool HasSheetTabContextTabColorMenuItem { get; }
                private bool HasSheetTabContextNoColorMenuItem { get; }
                private bool HasSheetTabContextSelectAllSheetsMenuItem { get; }
                private bool HasSheetTabContextUngroupSheetsMenuItem { get; }
                private bool HasNativeSelectAllSheetsMenuItem { get; }
                private bool HasNativeUngroupSheetsMenuItem { get; }
                private bool HasNativeHideSheetMenuItem { get; }
                private bool HasNativeUnhideSheetMenuItem { get; }
                private bool HasNativeDeleteSheetMenuItem { get; }
                private bool HasNativeShowGridlinesMenuItem { get; }
                private bool HasNativeShowHeadingsMenuItem { get; }
                private bool HasNativeZoomInMenuItem { get; }
                private bool HasNativeZoomOutMenuItem { get; }
                private bool HasNativeZoom100MenuItem { get; }
                private bool HasNativeZoomToSelectionMenuItem { get; }
                private bool HasNativeFreezePanesMenuItem { get; }
                private bool HasNativeFreezeTopRowMenuItem { get; }
                private bool HasNativeFreezeFirstColumnMenuItem { get; }
                private bool HasNativeUnfreezePanesMenuItem { get; }
                private bool HasNativeShowFormulasMenuItem { get; }
                private bool HasNativePasteSpecialCommentsMenuItem { get; }
                private bool HasNativePasteSpecialValidationMenuItem { get; }
                private bool HasNativePasteSpecialAllExceptBordersMenuItem { get; }
                private bool HasNativePasteSpecialAllMergingConditionalFormatsMenuItem { get; }
                private bool HasNativePasteSpecialColumnWidthsMenuItem { get; }
                private bool HasNativePasteSpecialFormulasAndNumberFormatsMenuItem { get; }
                private bool HasNativePasteSpecialValuesAndNumberFormatsMenuItem { get; }
                private bool HasNativePasteSpecialValuesAndSourceFormattingMenuItem { get; }
                private bool HasNativePasteSpecialKeepSourceColumnWidthsMenuItem { get; }
                private bool HasNativePasteSpecialPasteLinkMenuItem { get; }
                private bool HasNativeCellStylesMenuItem { get; }
                private bool HasNativeCopyMenuItem { get; }
                private bool HasNativePasteSpecialTextMenuItem { get; }
                private bool HasNativePasteSpecialUnicodeTextMenuItem { get; }
                private bool HasNativePasteSpecialPictureMenuItem { get; }
                private bool HasNativePasteSpecialLinkedPictureMenuItem { get; }
                private bool HasNativeBordersMenuItem { get; }
                public int ExternalImageClipboardPictureCount { get; }
                public int ExternalImageClipboardPicturePngByteCount { get; }
                public int NativeBordersPresetCount { get; }
                public int NativeCellStylesPresetCount { get; }
                public string Report => "external_image_clipboard_paste_required= external_image_clipboard_paste= external_image_clipboard_picture_count= external_image_clipboard_picture_png_bytes= native_new_workbook_menu_item= native_open_recent_menu_item= native_open_recent_item_count= native_close_workbook_menu_item= new_sheet_button= toolbar_borders_button= focusable_sheet_tab= focusable_active_sheet_tab= shell_focus_cycle_targets= sheet_tab_context_keyboard_help= sheet_tab_context_rename_menu_item= sheet_tab_context_tab_color_menu_item= sheet_tab_context_no_color_menu_item= sheet_tab_context_select_all_sheets_menu_item= sheet_tab_context_ungroup_sheets_menu_item= native_view_menu= native_sheet_menu= native_new_sheet_menu_item= native_rename_sheet_menu_item= native_duplicate_sheet_menu_item= native_move_sheet_left_menu_item= native_move_sheet_right_menu_item= native_tab_color_menu_item= native_tab_color_clear_item= native_tab_color_swatch_count= native_select_all_sheets_menu_item= native_ungroup_sheets_menu_item= native_hide_sheet_menu_item= native_unhide_sheet_menu_item= native_delete_sheet_menu_item= native_cut_menu_item= native_copy_menu_item= native_paste_special_menu_item= native_paste_special_comments_menu_item= native_paste_special_validation_menu_item= native_paste_special_all_except_borders_menu_item= native_paste_special_all_merging_conditional_formats_menu_item= native_paste_special_column_widths_menu_item= native_paste_special_formulas_and_number_formats_menu_item= native_paste_special_values_and_number_formats_menu_item= native_paste_special_values_and_source_formatting_menu_item= native_paste_special_keep_source_column_widths_menu_item= native_paste_special_paste_link_menu_item= native_paste_special_text_menu_item= native_paste_special_unicode_text_menu_item= native_paste_special_picture_menu_item= native_paste_special_linked_picture_menu_item= native_select_all_menu_item= native_clear_contents_menu_item= native_bold_menu_item= native_fill_color_swatch_count= native_font_color_swatch_count= native_borders_menu_item= native_borders_preset_count= native_cell_styles_menu_item= native_cell_styles_preset_count= native_horizontal_text_menu_item= native_angle_counterclockwise_menu_item= native_angle_clockwise_menu_item= native_vertical_text_menu_item= native_rotate_text_up_menu_item= native_rotate_text_down_menu_item= native_show_gridlines_menu_item= native_show_headings_menu_item= native_zoom_in_menu_item= native_zoom_out_menu_item= native_zoom_100_menu_item= native_zoom_to_selection_menu_item= native_freeze_panes_menu_item= native_freeze_top_row_menu_item= native_freeze_first_column_menu_item= native_unfreeze_panes_menu_item= native_show_formulas_menu_item= native_help_menu= native_help_online_menu_item= native_send_feedback_menu_item= native_check_for_updates_menu_item= native_about_menu_item= native_legal_notices_menu_item=";
            }

            internal sealed class MacOsLaunchSmokeCoordinator
            {
                private static async Task RunAsync(MainWindow mainWindow, MacOsLaunchSmokeOptions options)
                {
                    var snapshot = mainWindow.CreateLaunchSmokeSnapshot();
                    var initialExternalImageClipboardPictureCount = snapshot.ExternalImageClipboardPictureCount;
                    await mainWindow.TryPasteLaunchSmokeClipboardImageAsync();
                    IsPassed(snapshot, options, initialExternalImageClipboardPictureCount).ToString();
                    HasExternalImageClipboardPasteEvidence(snapshot, initialExternalImageClipboardPictureCount).ToString();
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/CellBorderPresetPlanner.cs",
            """
            namespace FreeX.App.Services;

            public enum CellBorderPreset
            {
                All,
                Outside,
                Inside,
                NoBorder,
                Top,
                Right,
                Bottom,
                Left
            }

            public static class CellBorderPresetPlanner
            {
                public static StyleDiff Plan(
                    CellBorderPreset preset,
                    GridRange range,
                    CellAddress address,
                    BorderStyle style = BorderStyle.Thin,
                    CellColor? color = null)
                {
                    var borderColor = color ?? CellColor.Black;
                    CellBorderPreset.All.ToString();
                    CellBorderPreset.Outside.ToString();
                    CellBorderPreset.Inside.ToString();
                    CellBorderPreset.NoBorder.ToString();
                    BorderShortcutService.GetAllBorderDiff(style, borderColor);
                    BorderShortcutService.GetOutlineBorderDiff(range, address, style, borderColor);
                    BorderShortcutService.GetInsideBorderDiff(range, address, style, borderColor);
                    BorderShortcutService.GetClearBorderDiff();
                    return new();
                }

                public static string GetDisplayName(CellBorderPreset preset) => "";
                public static bool RequiresPerCellPlanning(CellBorderPreset preset) => true;
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/RecentFilesStore.cs",
            """
            namespace FreeX.App.Services;

            public sealed class RecentFileEntry { }

            public sealed class RecentFilesStore
            {
                private Func<DateTimeOffset> _clock;
                public static RecentFilesStore Load() => Load(DefaultStorePath);
                public static RecentFilesStore Load(string storePath, Func<DateTimeOffset>? clock = null) => new();
                private static string DefaultStorePath => "recent.json";
                private void SetClock(Func<DateTimeOffset>? clock) { _clock = clock ?? (() => DateTimeOffset.UtcNow); }
                private void AddOrUpdate() { LastOpened = _clock(); }
                private DateTimeOffset LastOpened { get; set; }
                private void Save() => AtomicFileWriter.WriteAllText(_storePath, JsonSerializer.Serialize(Entries));
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/AtomicFileWriter.cs",
            """
            namespace FreeX.App.Services;

            public static class AtomicFileWriter
            {
                public static void WriteAllText(string path, string content)
                {
                    File.WriteAllText(tempPath, content);
                    File.Move(tempPath, path, overwrite: true);
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/WorkbookSessionFactory.cs",
            """
            namespace FreeX.App.Services;

            public sealed class WorkbookSessionFactory
            {
                public WorkbookSession CreateNew()
                {
                    var workbook = WorkbookFactory.Create(options);
                    var source = new StartupWorkbookLoadResult(
                        workbook,
                        workbook.Name,
                        "Created new workbook.",
                        IsFallback: false);
                    return Create(source);
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/WorkbookSession.cs",
            """
            namespace FreeX.App.Services;

            public sealed class WorkbookSession
            {
                /*
                public IReadOnlyList<WorkbookHiddenSheet> HiddenSheets =>
                public bool CanHideActiveSheet =>
                public bool IsWorkbookGrouped =>
                public WorkbookCellEditResult SetActiveSheetTabColor(CellColor? color)
                new SetSheetTabColorCommand(ActiveSheet.Id, color)
                public WorkbookCellEditResult SetSelectedRangeBorderPreset(CellBorderPreset preset)
                CreateBorderPresetCommand(range, preset)
                CellBorderPresetPlanner.Plan(preset, range, range.Start)
                CellBorderPresetPlanner.RequiresPerCellPlanning(preset)
                BorderShortcutService.HasBorderChanges(diff)
                GroupedApplyStyleCommand(targetSheetIds, sourceRange, diff)
                public bool SelectSheetFromTab(SheetId sheetId, bool selectRange, bool toggle)
                SheetGroupSelectionService.SelectRange(
                SheetGroupSelectionService.Toggle(sheetId, _groupedSheetIds)
                public bool SelectAllVisibleSheets()
                SheetGroupSelectionService.SelectAll(GetSelectableSheetIds())
                public bool UngroupSheets()
                public WorkbookCellEditResult HideActiveSheet()
                new SetSheetHiddenCommand(sheetId, hidden: true)
                public WorkbookCellEditResult UnhideSheet(SheetId sheetId)
                new SetSheetHiddenCommand(sheetId, hidden: false)
                public bool IsShowingFormulas => ActiveSheet.ShowFormulas;
                public WorkbookCellEditResult SetShowFormulas(bool showFormulas)
                new SetWorksheetShowFormulasCommand(ActiveSheet.Id, showFormulas)
                public bool IsShowingGridlines => ActiveSheet.ShowGridlines;
                public bool IsShowingHeadings => ActiveSheet.ShowHeadings;
                public WorkbookCellEditResult SetShowGridlines(bool showGridlines)
                public WorkbookCellEditResult SetShowHeadings(bool showHeadings)
                new SetWorksheetViewOptionsCommand(ActiveSheet.Id, showGridlines, showHeadings, showRulers)
                public int ZoomPercent => ActiveSheet.ZoomPercent;
                public WorkbookCellEditResult SetZoomPercent(int zoomPercent)
                new SetWorksheetZoomCommand(ActiveSheet.Id, zoomPercent)
                public WorkbookCellEditResult FreezePanesAtActiveCell()
                public WorkbookCellEditResult FreezeTopRow()
                public WorkbookCellEditResult FreezeFirstColumn()
                public WorkbookCellEditResult UnfreezePanes()
                new SetFreezePanesCommand(ActiveSheet.Id, frozenRows, frozenCols)
                public WorkbookCellEditResult PasteColumnWidthsFromClipboardAtActiveCell(string? text)
                public WorkbookCellEditResult PasteCommentsFromClipboardAtActiveCell(string? text, bool transpose = false)
                new PasteCommentsCommand(
                public WorkbookCellEditResult PasteDataValidationFromClipboardAtActiveCell(string? text, bool transpose = false)
                new PasteDataValidationCommand(
                public WorkbookCellEditResult PasteLinkFromClipboardAtActiveCell(
                PasteLinkService.CreateLinkedCells(
                public WorkbookCellEditResult PastePictureFromClipboardAtActiveCell(
                new PasteRangeAsPictureCommand(
                public bool ShouldPreferExternalClipboardImage(string? text)
                public WorkbookCellEditResult PasteClipboardImageAtActiveCell(
                ClipboardPictureService.CreateInsertCommand(
                private static string FormatPictureCellText(ScalarValue value)
                new PasteColumnWidthsCommand(
                private IWorkbookCommand CreatePasteLinkCommand(
                var sheetDestination = RemapAddressToSheet(destination, sheetId)
                IWorkbookCommand command = new EditCellsCommand(sheetId, linkedCells)
                private IWorkbookCommand CreateGroupedSheetCommand(
                Func<SheetId, IWorkbookCommand> createCommand
                bool keepSourceColumnWidths = false
                if (keepSourceColumnWidths)
                */
                public WorkbookCellEditResult AddSheet()
                {
                    var result = _cellEditService.ExecuteEditCommand(
                        Workbook,
                        new AddSheetCommand(WorkbookSheetNameGenerator.GenerateUniqueSheetName(Workbook)));
                    ApplySuccessfulWorkbookStructureResult(Workbook.Sheets[^1].Id);
                    return result;
                }

                public WorkbookCellEditResult RenameActiveSheet(string? name)
                {
                    var newName = (name ?? "").Trim();
                    var result = _cellEditService.ExecuteEditCommand(
                        Workbook,
                        new RenameSheetCommand(ActiveSheet.Id, newName));
                    ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
                    return result;
                }

                public WorkbookCellEditResult DuplicateActiveSheet()
                {
                    var sourceSheetId = ActiveSheet.Id;
                    var result = _cellEditService.ExecuteEditCommand(
                        Workbook,
                        new DuplicateSheetCommand(sourceSheetId));
                    return result;
                }

                public WorkbookCellEditResult DeleteActiveSheet()
                {
                    var sheetId = ActiveSheet.Id;
                    var result = _cellEditService.ExecuteEditCommand(
                        Workbook,
                        new RemoveSheetCommand(sheetId));
                    return result;
                }

                public GridRange SelectCurrentRegionOrAll()
                {
                    if (SelectionRangeService.GetCurrentRegion(ActiveSheet, ActiveCell) is { } currentRegion &&
                        SelectedRange != currentRegion)
                    {
                        return currentRegion;
                    }

                    return new GridRange(
                        new CellAddress(ActiveSheet.Id, 1, 1),
                        new CellAddress(ActiveSheet.Id, CellAddress.MaxRow, CellAddress.MaxCol));
                }

                public WorkbookCellEditResult UndoLastEdit()
                {
                    ApplySuccessfulHistoryResult(result, sheetIdsBefore);
                    return result;
                }

                private void ApplySuccessfulWorkbookStructureResult(SheetId preferredSheetId) { }
                private void ApplySuccessfulWorkbookMetadataResult(SheetId preferredSheetId) { }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/PortPreviewWorkbookFactory.cs",
            """
            namespace FreeX.App.Services;

            public static class PortPreviewWorkbookFactory
            {
                public const string PreviewShapeName = "Port readiness shape";
                public const string PreviewTextBoxName = "Port preview note";
                public const string PreviewPictureName = "Port preview logo";

                private static void CreatePreview()
                {
                    AddPreviewDrawingObjects(sheet);
                    sheet.DrawingShapes.Add(shape);
                    sheet.TextBoxes.Add(textBox);
                    sheet.Pictures.Add(picture);
                    sheet.DrawingObjectZOrder.AddRange();
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/WorkbookStartupSmokeService.cs",
            """
            namespace FreeX.App.Services;

            internal sealed class WorkbookStartupSmokeService
            {
                private const string RoundTripExtension = ".fxl";
                private void Smoke()
                {
                    _sessionFactory.Create(source, SmokeViewportHeight, SmokeViewportWidth, includeObjects: true);
                    VerifyDrawingObjectPreviews();
                    PortPreviewWorkbookFactory.PreviewShapeName.ToString();
                    var result = $"Packaging smoke opened; drawing_object_previews={drawingObjectPreviewCount}; edited, saved, and reopened; roundtrip_drawing_object_previews={roundTripDrawingObjectPreviewCount}.";
                }
            }

            public static class PackagingSmokeCommand
            {
                public const string Argument = "--packaging-smoke";
            }
            """);

        WriteFile(
            root,
            "src/FreeX.Core.IO/NativeJsonAdapter.cs",
            """
            namespace FreeX.Core.IO;

            public sealed class NativeJsonAdapter
            {
                public string Extension => ".fxl";
                public string FormatName => "FreeX Workbook";
            }
            """);

        if (!string.IsNullOrWhiteSpace(extraAvaloniaSource))
        {
            WriteFile(root, "src/FreeX.App.Avalonia/WindowsOnlyLeak.cs", extraAvaloniaSource);
        }
    }

    private static string FormatWorkflowRuntimeLine(string runtime)
    {
        return string.IsNullOrWhiteSpace(runtime)
            ? ""
            : $"- {runtime}";
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteMinimalIcns(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(
            path,
            [
                (byte)'i', (byte)'c', (byte)'n', (byte)'s',
                0, 0, 0, 32,
                (byte)'i', (byte)'c', (byte)'p', (byte)'4',
                0, 0, 0, 8,
                (byte)'i', (byte)'c', (byte)'p', (byte)'5',
                0, 0, 0, 8,
                (byte)'i', (byte)'c', (byte)'0', (byte)'8',
                0, 0, 0, 8
            ]);
    }
}
