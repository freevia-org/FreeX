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
        script.Should().Contain("osx-arm64=macos-15");
        script.Should().Contain("osx-x64=macos-15-intel");
        script.Should().NotContain("runner: macos-latest");
        script.Should().Contain("distribution_candidate:");
        script.Should().Contain("artifact_channel=\"distribution-candidate\"");
        script.Should().Contain("Distribution-candidate macOS app runs require Developer ID signing secrets");
        script.Should().Contain("Distribution-candidate macOS app runs require notarization secrets");
        script.Should().Contain("distribution_readiness=internal_preview_not_for_distribution");
        script.Should().Contain("distribution_readiness=distribution_candidate_ready");
        script.Should().Contain("/usr/sbin/spctl --assess --type execute --verbose=4 \"$app_path\"");
        script.Should().Contain("gatekeeper_assessment_subject=unzipped_app_bundle");
        script.Should().Contain("gatekeeper_assessment_status=accepted");
        script.Should().Contain("gatekeeper_assessment_source=Notarized Developer ID");
        script.Should().Contain("Distribution-candidate run requires accepted Gatekeeper assessment from Notarized Developer ID.");
        script.Should().Contain("publish-distribution-candidate:");
        script.Should().Contain("actions/download-artifact@v7");
        script.Should().Contain("Test portable PDF macOS route");
        script.Should().Contain("dotnet test tests/FreeX.App.Services.Tests/FreeX.App.Services.Tests.csproj");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfDocumentExporterTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfExportPlannerTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfPageContentPlannerTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfTextCapabilityPlannerTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.WorkbookExportPrintPlannerTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.WorkbookShareActionPlannerTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.WorkbookViewportScrollPlannerTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.OpenRecentWorkbookMenuPlannerTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AppServicesPortabilityGuardTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaProjectPortabilityGuardTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.ApplicationDataPathGuardTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AppStoragePathPlannerTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AppOptionsStoreTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AtomicFileWriterTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaShellSourceTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.MacOsLaunchSmokeReportKeyDriftGuardTests");
        script.Should().Contain("dotnet test tests/FreeX.Core.Model.Tests/FreeX.Core.Model.Tests.csproj");
        script.Should().Contain("FullyQualifiedName~FreeX.Core.Model.Tests.ExportPathPlannerTests");
        script.Should().Contain("freex-${{ matrix.runtime }}-portable-pdf-exporter-tests.trx");
        script.Should().Contain("freex-${{ matrix.runtime }}-export-path-tests.trx");
        script.Should().Contain("--results-directory artifacts");
        script.Should().Contain("FreeX-latest-macos-arm64.zip");
        script.Should().Contain("FreeX-latest-macos-x64.zip");
        script.Should().Contain("FreeX-latest-macos-distribution-candidate-manifest.json");
        script.Should().Contain("FreeX-latest-$assetLabel-default-open-launch-smoke.txt");
        script.Should().Contain("distribution_candidate_required_markers");
        script.Should().Contain("default_open_launch_smoke_report");
        script.Should().Contain("gh release create");
        script.Should().Contain("gh release upload");
        script.Should().Contain("--framework net10.0");
        script.Should().Contain("--output \"$app/Contents/MacOS\"");
        script.Should().Contain("native_fill_color_swatch_count=69");
        script.Should().Contain("native_font_color_swatch_count=69");
        script.Should().Contain("toolbar_format_painter_button=true");
        script.Should().Contain("toolbar_fill_cells_button=true");
        script.Should().Contain("toolbar_fill_down_menu_item=true");
        script.Should().Contain("toolbar_fill_right_menu_item=true");
        script.Should().Contain("toolbar_fill_up_menu_item=true");
        script.Should().Contain("toolbar_fill_left_menu_item=true");
        script.Should().Contain("toolbar_clear_button=true");
        script.Should().Contain("toolbar_clear_all_menu_item=true");
        script.Should().Contain("toolbar_clear_formats_menu_item=true");
        script.Should().Contain("toolbar_clear_contents_menu_item=true");
        script.Should().Contain("toolbar_clear_comments_menu_item=true");
        script.Should().Contain("toolbar_clear_hyperlinks_menu_item=true");
        script.Should().Contain("toolbar_borders_button=true");
        script.Should().Contain("toolbar_merge_and_center_button=true");
        script.Should().Contain("native_format_painter_menu_item=true");
        script.Should().Contain("native_borders_menu_item=true");
        script.Should().Contain("native_borders_preset_count=8");
        script.Should().Contain("native_merge_and_center_menu_item=true");
        script.Should().Contain("native_unmerge_cells_menu_item=true");
        script.Should().Contain("native_cell_styles_menu_item=true");
        script.Should().Contain("native_cell_styles_preset_count=33");
        script.Should().Contain("open_with_report=\"$artifact_root/freex-$runtime-macos-open-with-launch-smoke.txt\"");
        script.Should().Contain("open_with_smoke_file=\"$RUNNER_TEMP/freex-$runtime-open-with.csv\"");
        script.Should().Contain("app_path=\"$unzip_root/FreeX.app\"");
        script.Should().Contain("open -W -n -a \"$app_path\" \"$open_with_smoke_file\" --args --macos-launch-smoke \"$open_with_report\"");
        script.Should().Contain("--macos-launch-smoke-diagnostics-dir \"$app_diagnostics_dir\"");
        script.Should().Contain("app_diagnostics_directory_configured=true");
        script.Should().Contain("app_diagnostics_events_path=\"$app_diagnostics_dir/events.jsonl\"");
        script.Should().Contain("app_diagnostics_crash_reports_dir=\"$app_diagnostics_dir/CrashReports\"");
        script.Should().Contain("app_diagnostics_crash_count=0");
        script.Should().Contain("app_diagnostics_artifact=freex-`$runtime-macos-app-diagnostics");
        script.Should().Contain("app_diagnostics_events_jsonl=true");
        script.Should().Contain("opened_source_path=.*freex-$runtime-open-with.csv");
        script.Should().Contain("freex-${{ matrix.runtime }}-macos-open-with-launch-smoke.txt");
        script.Should().Contain("default_open_report=\"$artifact_root/freex-$runtime-macos-default-open-launch-smoke.txt\"");
        script.Should().Contain("default_open_smoke_file=\"$RUNNER_TEMP/freex-$runtime-default-open.fxl\"");
        script.Should().Contain("\"FileFormat\": \"FreeX.NativeJsonWorkbook\"");
        script.Should().Contain("open -W -n \"$default_open_smoke_file\" --args --macos-launch-smoke \"$default_open_report\"");
        script.Should().Contain("opened_source_path=.*freex-$runtime-default-open.fxl");
        script.Should().Contain("launchservices_default_open_app_override=false");
        script.Should().Contain("launchservices_default_open_document_extension=fxl");
        script.Should().Contain("src\\FreeX.App.Services\\PortablePdfDocumentExporter.cs");
        script.Should().Contain("src\\FreeX.App.Services\\WorkbookShareActionPlanner.cs");
        script.Should().Contain("public static WorkbookShareActionSurface MacOsPreview");
        script.Should().Contain("surface.CanShowShareSheet || surface.CanOpenContainingFolder");
        script.Should().Contain("src\\FreeX.App.Services\\WorkbookViewportScrollPlanner.cs");
        script.Should().Contain("public static WorkbookViewportScrollState Create(Sheet sheet, ViewportModel viewport)");
        script.Should().Contain("public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(");
        script.Should().Contain("WorkbookViewportScrollPlanner.Create(_session.ActiveSheet, _session.Viewport)");
        script.Should().Contain("WorkbookViewportScrollPlanner.CalculateViewportOrigin(");
        script.Should().Contain("src\\FreeX.App.Services\\LocalFilePath.cs");
        script.Should().Contain("public static bool TryNormalize(string? candidate, out string normalizedPath)");
        script.Should().Contain("TryCreateExplicitUri(path, out var uri)");
        script.Should().Contain("src\\FreeX.App.Services\\OpenRecentWorkbookMenuPlanner.cs");
        script.Should().Contain("OpenRecentWorkbookMenuPlanner.Create(");
        script.Should().Contain("public const int DefaultMaximumItems = 10;");
        script.Should().Contain("Func<string, string?> resolveOpenWorkbookPath");
        script.Should().Contain("PlatformPathIdentityComparer.Current");
        script.Should().Contain("/Encoding /WinAnsiEncoding");
        script.Should().Contain("EncodeWinAnsiHexText(normalized)");
        script.Should().Contain("private static byte EncodeWinAnsiByte(char ch)");
        script.Should().Contain("built-in Helvetica/WinAnsi set");
        script.Should().Contain("ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, exportPathPlan, File.Exists)");
        script.Should().Contain("private async Task<bool> ConfirmNormalizedPdfOverwriteAsync(string normalizedPath)");
        script.Should().Contain("IsCancel = true,");
        script.Should().Contain("dialog.Opened += (_, _) => cancelButton.Focus();");
        script.Should().Contain("PdfExportOverwriteReplaceButton");
        script.Should().Contain("PdfExportOverwriteCancelButton");
        script.Should().Contain("launchservices_default_open_boundary=ci_open_document_without_app_override_not_finder_double_click");
        script.Should().Contain("freex-${{ matrix.runtime }}-macos-default-open-launch-smoke.txt");
        script.Should().Contain("cmd_find_direct_route_source_guard=true");
        script.Should().Contain("cmd_page_up_direct_route_source_guard=true");
        script.Should().Contain("cmd_page_down_direct_route_source_guard=true");
        script.Should().Contain("external_image_clipboard_paste_required=false");
        script.Should().Contain("live_command_key_smoke_required=false");
        script.Should().Contain("live_command_key_smoke=not_required");
        script.Should().Contain("native_new_workbook_menu_item=true");
        script.Should().Contain("native_open_recent_menu_item=true");
        script.Should().Contain("native_open_recent_item_count=[1-9]");
        script.Should().Contain("native_export_pdf_menu_item=true");
        script.Should().Contain("native_close_workbook_menu_item=true");
        script.Should().Contain("native_select_all_menu_item=true");
        script.Should().Contain("native_find_menu_item=true");
        script.Should().Contain("native_find_next_menu_item=true");
        script.Should().Contain("native_replace_menu_item=true");
        script.Should().Contain("native_go_to_menu_item=true");
        script.Should().Contain("native_go_to_special_menu_item=true");
        script.Should().Contain("native_data_menu=true");
        script.Should().Contain("native_flash_fill_menu_item=true");
        script.Should().Contain("native_review_menu=true");
        script.Should().Contain("native_sort_ascending_menu_item=true");
        script.Should().Contain("native_sort_descending_menu_item=true");
        script.Should().Contain("native_advanced_filter_menu_item=true");
        script.Should().Contain("native_remove_duplicates_menu_item=true");
        script.Should().Contain("native_subtotal_menu_item=true");
        script.Should().Contain("native_data_validation_preview_menu_item=true");
        script.Should().Contain("native_data_validation_menu_item=true");
        script.Should().Contain("native_what_if_analysis_menu_item=true");
        script.Should().Contain("native_goal_seek_menu_item=true");
        script.Should().Contain("native_data_table_menu_item=true");
        script.Should().Contain("native_scenario_manager_menu_item=true");
        script.Should().Contain("native_forecast_sheet_menu_item=true");
        script.Should().Contain("native_review_summary_menu_item=true");
        script.Should().Contain("native_check_accessibility_menu_item=true");
        script.Should().Contain("native_next_note_menu_item=true");
        script.Should().Contain("native_previous_note_menu_item=true");
        script.Should().Contain("native_next_comment_menu_item=true");
        script.Should().Contain("native_previous_comment_menu_item=true");
        script.Should().Contain("native_format_cells_menu_item=true");
        script.Should().Contain("macos_dialog_smoke=passed");
        script.Should().Contain("macos_dialog_smoke_attempted=true");
        script.Should().Contain("macos_dialog_smoke_status=passed");
        script.Should().Contain("macos_dialog_activation_completed=true");
        script.Should().Contain("find_dialog=true");
        script.Should().Contain("find_dialog_text_box=true");
        script.Should().Contain("find_dialog_action_buttons=true");
        script.Should().Contain("find_dialog_options=true");
        script.Should().Contain("find_dialog_format_controls=true");
        script.Should().Contain("find_dialog_compact_layout=true");
        script.Should().Contain("find_dialog_result_closed_without_accept=true");
        script.Should().Contain("replace_dialog=true");
        script.Should().Contain("replace_dialog_text_boxes=true");
        script.Should().Contain("replace_dialog_action_buttons=true");
        script.Should().Contain("replace_dialog_options=true");
        script.Should().Contain("replace_dialog_format_controls=true");
        script.Should().Contain("replace_dialog_compact_layout=true");
        script.Should().Contain("replace_dialog_result_closed_without_accept=true");
        script.Should().Contain("go_to_dialog=true");
        script.Should().Contain("go_to_dialog_reference_controls=true");
        script.Should().Contain("go_to_dialog_compact_layout=true");
        script.Should().Contain("go_to_dialog_result_closed_without_accept=true");
        script.Should().Contain("go_to_special_dialog=true");
        script.Should().Contain("go_to_special_dialog_kind_controls=true");
        script.Should().Contain("go_to_special_dialog_value_type_controls=true");
        script.Should().Contain("go_to_special_dialog_compact_layout=true");
        script.Should().Contain("go_to_special_dialog_result_closed_without_accept=true");
        script.Should().Contain("format_cells_dialog=true");
        script.Should().Contain("format_cells_dialog_tab_strip=true");
        script.Should().Contain("format_cells_dialog_default_number_tab=true");
        script.Should().Contain("format_cells_dialog_number_controls=true");
        script.Should().Contain("format_cells_dialog_action_buttons=true");
        script.Should().Contain("format_cells_dialog_compact_layout=true");
        script.Should().Contain("format_cells_dialog_result_closed_without_accept=true");
        script.Should().Contain("native_fill_cells_menu_item=true");
        script.Should().Contain("native_fill_down_menu_item=true");
        script.Should().Contain("native_fill_right_menu_item=true");
        script.Should().Contain("native_fill_up_menu_item=true");
        script.Should().Contain("native_fill_left_menu_item=true");
        script.Should().Contain("native_clear_menu_item=true");
        script.Should().Contain("native_clear_all_menu_item=true");
        script.Should().Contain("native_clear_formats_menu_item=true");
        script.Should().Contain("native_clear_contents_menu_item=true");
        script.Should().Contain("native_clear_comments_menu_item=true");
        script.Should().Contain("native_clear_hyperlinks_menu_item=true");
        script.Should().Contain("native_bold_menu_item=true");
        script.Should().Contain("native_italic_menu_item=true");
        script.Should().Contain("native_underline_menu_item=true");
        script.Should().Contain("native_double_underline_menu_item=true");
        script.Should().Contain("native_strikethrough_menu_item=true");
        script.Should().Contain("native_increase_font_size_menu_item=true");
        script.Should().Contain("native_decrease_font_size_menu_item=true");
        script.Should().Contain("native_fill_color_menu_item=true");
        script.Should().Contain("native_clear_fill_menu_item=true");
        script.Should().Contain("native_font_color_menu_item=true");
        script.Should().Contain("native_fill_color_swatch_count=69");
        script.Should().Contain("native_font_color_swatch_count=69");
        script.Should().Contain("native_currency_format_menu_item=true");
        script.Should().Contain("native_percent_format_menu_item=true");
        script.Should().Contain("native_comma_style_menu_item=true");
        script.Should().Contain("native_increase_decimal_menu_item=true");
        script.Should().Contain("native_decrease_decimal_menu_item=true");
        script.Should().Contain("native_align_top_menu_item=true");
        script.Should().Contain("native_align_middle_menu_item=true");
        script.Should().Contain("native_align_bottom_menu_item=true");
        script.Should().Contain("toolbar_wrap_text_button=true");
        script.Should().Contain("native_wrap_text_menu_item=true");
        script.Should().Contain("native_decrease_indent_menu_item=true");
        script.Should().Contain("native_increase_indent_menu_item=true");
        script.Should().Contain("native_align_left_menu_item=true");
        script.Should().Contain("native_align_center_menu_item=true");
        script.Should().Contain("native_align_right_menu_item=true");
        script.Should().Contain("native_minimize_window_menu_item=true");
        script.Should().Contain("native_zoom_window_menu_item=true");
        script.Should().Contain("native_bring_all_to_front_menu_item=true");
        script.Should().Contain("native_quit_menu_item=true");
        script.Should().Contain("new_sheet_button=true");
        script.Should().Contain("native_sheet_menu=true");
        script.Should().Contain("native_window_menu=true");
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
        script.Should().Contain("HasNativeFindMenuItem &&");
        script.Should().Contain("HasNativeFindNextMenuItem &&");
        script.Should().Contain("HasNativeReplaceMenuItem &&");
        script.Should().Contain("HasNativeGoToMenuItem &&");
        script.Should().Contain("HasNativeGoToSpecialMenuItem &&");
        script.Should().Contain("HasNativeDataMenu &&");
        script.Should().Contain("HasNativeReviewMenu &&");
        script.Should().Contain("HasNativeSortAscendingMenuItem &&");
        script.Should().Contain("HasNativeSortDescendingMenuItem &&");
        script.Should().Contain("HasNativeAdvancedFilterMenuItem &&");
        script.Should().Contain("HasNativeRemoveDuplicatesMenuItem &&");
        script.Should().Contain("HasNativeDataValidationPreviewMenuItem &&");
        script.Should().Contain("HasNativeDataValidationMenuItem &&");
        script.Should().Contain("HasNativeWhatIfAnalysisMenuItem &&");
        script.Should().Contain("HasNativeGoalSeekMenuItem &&");
        script.Should().Contain("HasNativeDataTableMenuItem &&");
        script.Should().Contain("HasNativeScenarioManagerMenuItem &&");
        script.Should().Contain("HasNativeForecastSheetMenuItem &&");
        script.Should().Contain("HasNativeReviewSummaryMenuItem &&");
        script.Should().Contain("HasNativeCheckAccessibilityMenuItem &&");
        script.Should().Contain("HasNativeNextNoteMenuItem &&");
        script.Should().Contain("HasNativePreviousNoteMenuItem &&");
        script.Should().Contain("HasNativeNextCommentMenuItem &&");
        script.Should().Contain("HasNativePreviousCommentMenuItem &&");
        script.Should().Contain("HasNativeFormatCellsMenuItem &&");
        script.Should().Contain("HasNativeFormatCellsMenuItem:");
        script.Should().Contain("private readonly NativeMenuItem _sortAscendingMenuItem = new();");
        script.Should().Contain("_sortAscendingMenuItem.Header = `\"Sort A to Z`\";");
        script.Should().Contain("_sortDescendingMenuItem.Header = `\"Sort Z to A`\";");
        script.Should().Contain("var dataMenu = new NativeMenu();");
        script.Should().Contain("dataMenu.Items.Add(_sortAscendingMenuItem);");
        script.Should().Contain("Header = `\"Data`\",");
        script.Should().Contain("var hasNativeDataMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>");
        script.Should().Contain("HasNativeDataMenu: hasNativeDataMenu");
        script.Should().Contain("HasNativeReviewMenu: hasNativeReviewMenu");
        script.Should().Contain("private readonly NativeMenuItem _flashFillMenuItem = new();");
        script.Should().Contain("_flashFillMenuItem.Header = `\"Flash Fill`\";");
        script.Should().Contain("_flashFillMenuItem.Gesture = new KeyGesture(Key.E, KeyModifiers.Control);");
        script.Should().Contain("_flashFillMenuItem.Click += (_, _) => FlashFillSelectedRange();");
        script.Should().Contain("dataMenu.Items.Add(_flashFillMenuItem);");
        script.Should().Contain("_flashFillMenuItem.IsEnabled = isIdle;");
        script.Should().Contain("e.Key == Key.E && HasOnlyControlModifier(e.KeyModifiers)");
        script.Should().Contain("private void FlashFillSelectedRange()");
        script.Should().Contain("_session.FlashFillSelectedRange()");
        script.Should().Contain("HasNativeFlashFillMenuItem: HasNativeMenuItem(_flashFillMenuItem, `\"Flash Fill`\")");
        script.Should().Contain("HasNativeFlashFillMenuItem &&");
        script.Should().Contain("native_flash_fill_menu_item=");
        script.Should().Contain("_sortAscendingMenuItem.IsEnabled = isIdle && _session.CanSortSelectedRange;");
        script.Should().Contain("_session.SortSelectedRange(ascending)");
        script.Should().Contain("HasNativeSortAscendingMenuItem: HasNativeMenuItem(_sortAscendingMenuItem, `\"Sort A to Z`\", requireGesture: false)");
        script.Should().Contain("HasNativeSortDescendingMenuItem: HasNativeMenuItem(_sortDescendingMenuItem, `\"Sort Z to A`\", requireGesture: false)");
        script.Should().Contain("HasNativeAdvancedFilterMenuItem: HasNativeMenuItem(_advancedFilterMenuItem, `\"Advanced Filter...`\", requireGesture: false)");
        script.Should().Contain("_removeDuplicatesMenuItem.Header = `\"Remove Duplicates...`\";");
        script.Should().Contain("_removeDuplicatesMenuItem.Click += async (_, _) => await ShowRemoveDuplicatesDialogAsync();");
        script.Should().Contain("dataMenu.Items.Add(_removeDuplicatesMenuItem);");
        script.Should().Contain("_removeDuplicatesMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1;");
        script.Should().Contain("HasNativeRemoveDuplicatesMenuItem: HasNativeMenuItem(_removeDuplicatesMenuItem, `\"Remove Duplicates...`\", requireGesture: false)");
        script.Should().Contain("native_remove_duplicates_menu_item=");
        script.Should().Contain("private readonly NativeMenuItem _subtotalMenuItem = new();");
        script.Should().Contain("_subtotalMenuItem.Header = `\"Subtotal...`\";");
        script.Should().Contain("_subtotalMenuItem.Click += async (_, _) => await ShowSubtotalDialogAsync();");
        script.Should().Contain("dataMenu.Items.Add(_subtotalMenuItem);");
        script.Should().Contain("_subtotalMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1 && _session.SelectedRange.ColCount > 1;");
        script.Should().Contain("private async Task ShowSubtotalDialogAsync()");
        script.Should().Contain("private async Task<SubtotalDialogResult?> ShowSubtotalInputDialogAsync()");
        script.Should().Contain("_session.ExecuteSubtotalOptions(selection.Options!)");
        script.Should().Contain("_session.RemoveSelectedRangeSubtotals()");
        script.Should().Contain("new SubtotalInputOptions(");
        script.Should().Contain("AutomationProperties.SetAutomationId(dialog, `\"SubtotalCompactDialog`\");");
        script.Should().Contain("AutomationProperties.SetAutomationId(groupColumnBox, `\"SubtotalGroupColumnBox`\");");
        script.Should().Contain("AutomationProperties.SetAutomationId(functionBox, `\"SubtotalFunctionBox`\");");
        script.Should().Contain("AutomationProperties.SetAutomationId(columnsPanel, `\"SubtotalColumnsPanel`\");");
        script.Should().Contain("AutomationProperties.SetAutomationId(removeAllButton, `\"SubtotalRemoveAllButton`\");");
        script.Should().Contain("HasNativeSubtotalMenuItem: HasNativeMenuItem(_subtotalMenuItem, `\"Subtotal...`\", requireGesture: false)");
        script.Should().Contain("HasNativeSubtotalMenuItem &&");
        script.Should().Contain("native_subtotal_menu_item=");
        script.Should().Contain("HasNativeDataValidationPreviewMenuItem: HasNativeMenuItem(_dataValidationPreviewMenuItem, `\"Data Validation Preview...`\", requireGesture: false)");
        script.Should().Contain("HasNativeDataValidationMenuItem: HasNativeMenuItem(_dataValidationMenuItem, `\"Data Validation...`\", requireGesture: false)");
        script.Should().Contain("HasNativeWhatIfAnalysisMenuItem: HasNativeMenuItem(_whatIfAnalysisMenuItem, `\"What-If Analysis`\", requireGesture: false)");
        script.Should().Contain("HasNativeGoalSeekMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, `\"Goal Seek...`\")");
        script.Should().Contain("HasNativeDataTableMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, `\"Data Table...`\")");
        script.Should().Contain("HasNativeScenarioManagerMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, `\"Scenario Manager...`\")");
        script.Should().Contain("HasNativeForecastSheetMenuItem: HasNativeMenuItem(_forecastSheetMenuItem, `\"Forecast Sheet...`\", requireGesture: false)");
        script.Should().Contain("HasNativeReviewSummaryMenuItem: HasNativeMenuItem(_reviewSummaryMenuItem, `\"Review Summary...`\", requireGesture: false)");
        script.Should().Contain("HasNativeCheckAccessibilityMenuItem: HasNativeMenuItem(_checkAccessibilityMenuItem, `\"Check Accessibility...`\", requireGesture: false)");
        script.Should().Contain("HasNativeNextNoteMenuItem: HasNativeMenuItem(_nextNoteMenuItem, `\"Next Note`\", requireGesture: false)");
        script.Should().Contain("HasNativePreviousNoteMenuItem: HasNativeMenuItem(_previousNoteMenuItem, `\"Previous Note`\", requireGesture: false)");
        script.Should().Contain("HasNativeNextCommentMenuItem: HasNativeMenuItem(_nextCommentMenuItem, `\"Next Comment`\", requireGesture: false)");
        script.Should().Contain("HasNativePreviousCommentMenuItem: HasNativeMenuItem(_previousCommentMenuItem, `\"Previous Comment`\", requireGesture: false)");
        script.Should().Contain("public WorkbookCellEditResult SortSelectedRange(bool ascending)");
        script.Should().Contain("new SortCommand(sheetId, sheetRange, sortByColOffset: 0, ascending)");
        script.Should().Contain("public WorkbookCellEditResult FlashFillSelectedRange()");
        script.Should().Contain("var plan = FlashFillRangePlanner.Plan(sheet, sheetRange);");
        script.Should().Contain("FlashFillRangePlanner.HasFillTargets(sheet, plan)");
        script.Should().Contain("commands.Add(plan.CreateCommand(sheetId));");
        script.Should().Contain("public WorkbookCellEditResult ExecuteSubtotalOptions(SubtotalInputOptions options)");
        script.Should().Contain("public WorkbookCellEditResult RemoveSelectedRangeSubtotals()");
        script.Should().Contain("new SubtotalCommand(");
        script.Should().Contain("new RemoveSubtotalRowsCommand(sheetId, sheetRange)");
        script.Should().Contain("public FlashFillCommand CreateCommand(SheetId sheetId)");
        script.Should().Contain("new FlashFillCommand(sheetId, FillColumn, SourceColumn, StartRow, EndRow)");
        script.Should().Contain("HasFormatCellsDialog &&");
        script.Should().Contain("HasFormatCellsDialogTabStrip &&");
        script.Should().Contain("HasFormatCellsDialogDefaultNumberTab &&");
        script.Should().Contain("HasFormatCellsDialogNumberControls &&");
        script.Should().Contain("HasFormatCellsDialogActionButtons &&");
        script.Should().Contain("HasFormatCellsDialogCompactLayout &&");
        script.Should().Contain("HasFormatCellsDialogClosedWithoutAccept");
        script.Should().Contain("HasNativeCloseWorkbookMenuItem &&");
        script.Should().Contain("HasNativeRenameSheetMenuItem &&");
        script.Should().Contain("HasNativeTabColorMenuItem &&");
        script.Should().Contain("HasFormatPainterButton &&");
        script.Should().Contain("HasFillCellsButton &&");
        script.Should().Contain("HasFillDownMenuItem &&");
        script.Should().Contain("HasFillRightMenuItem &&");
        script.Should().Contain("HasFillUpMenuItem &&");
        script.Should().Contain("HasFillLeftMenuItem &&");
        script.Should().Contain("HasClearButton &&");
        script.Should().Contain("HasClearAllMenuItem &&");
        script.Should().Contain("HasClearFormatsMenuItem &&");
        script.Should().Contain("HasClearContentsMenuItem &&");
        script.Should().Contain("HasClearCommentsMenuItem &&");
        script.Should().Contain("HasClearHyperlinksMenuItem &&");
        script.Should().Contain("HasBordersButton &&");
        script.Should().Contain("HasMergeAndCenterButton &&");
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
        script.Should().Contain("HasNativeFormatPainterMenuItem &&");
        script.Should().Contain("HasNativeFillCellsMenuItem &&");
        script.Should().Contain("HasNativeFillDownMenuItem &&");
        script.Should().Contain("HasNativeFillRightMenuItem &&");
        script.Should().Contain("HasNativeFillUpMenuItem &&");
        script.Should().Contain("HasNativeFillLeftMenuItem &&");
        script.Should().Contain("HasNativeClearMenuItem &&");
        script.Should().Contain("HasNativeClearAllMenuItem &&");
        script.Should().Contain("HasNativeClearFormatsMenuItem &&");
        script.Should().Contain("HasNativeClearContentsMenuItem &&");
        script.Should().Contain("HasNativeClearCommentsMenuItem &&");
        script.Should().Contain("HasNativeClearHyperlinksMenuItem &&");
        script.Should().Contain("HasNativeBordersMenuItem &&");
        script.Should().Contain("NativeBordersPresetCount == Enum.GetValues<CellBorderPreset>().Length");
        script.Should().Contain("HasNativeMergeAndCenterMenuItem &&");
        script.Should().Contain("HasNativeUnmergeCellsMenuItem &&");
        script.Should().Contain("private readonly NativeMenuItem _workbookStatisticsMenuItem = new();");
        script.Should().Contain("_workbookStatisticsMenuItem.Header = `\"Workbook Statistics...`\";");
        script.Should().Contain("_workbookStatisticsMenuItem.Gesture = new KeyGesture(Key.G, KeyModifiers.Control | KeyModifiers.Shift);");
        script.Should().Contain("_workbookStatisticsMenuItem.Click += async (_, _) => await ShowWorkbookStatisticsDialogAsync();");
        script.Should().Contain("fileMenu.Items.Add(_workbookStatisticsMenuItem);");
        script.Should().Contain("_workbookStatisticsMenuItem.IsEnabled = isIdle;");
        script.Should().Contain("e.Key == Key.G && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)");
        script.Should().Contain("private async Task ShowWorkbookStatisticsDialogAsync()");
        script.Should().Contain("WorkbookStatisticsService.GetStatistics(_session.Workbook)");
        script.Should().Contain("AutomationProperties.SetAutomationId(dialog, `\"WorkbookStatisticsDialog`\");");
        script.Should().Contain("AutomationProperties.SetAutomationId(okButton, `\"WorkbookStatisticsOkButton`\");");
        script.Should().Contain("AutomationProperties.SetAutomationId(statisticsBlock, `\"WorkbookStatisticsSummary`\");");
        script.Should().Contain("private static string FormatWorkbookStatistics(WorkbookStatistics statistics)");
        script.Should().Contain("Cells with data: {statistics.CellCount}");
        script.Should().Contain("Shapes and text boxes: {statistics.ShapeCount}");
        script.Should().Contain("Named ranges: {statistics.NamedRangeCount}");
        script.Should().Contain("toolbar_format_painter_button=");
        script.Should().Contain("toolbar_fill_cells_button=");
        script.Should().Contain("toolbar_fill_down_menu_item=");
        script.Should().Contain("toolbar_fill_right_menu_item=");
        script.Should().Contain("toolbar_fill_up_menu_item=");
        script.Should().Contain("toolbar_fill_left_menu_item=");
        script.Should().Contain("toolbar_clear_button=");
        script.Should().Contain("toolbar_clear_all_menu_item=");
        script.Should().Contain("toolbar_clear_formats_menu_item=");
        script.Should().Contain("toolbar_clear_contents_menu_item=");
        script.Should().Contain("toolbar_clear_comments_menu_item=");
        script.Should().Contain("toolbar_clear_hyperlinks_menu_item=");
        script.Should().Contain("toolbar_borders_button=");
        script.Should().Contain("toolbar_merge_and_center_button=");
        script.Should().Contain("native_format_painter_menu_item=");
        script.Should().Contain("native_fill_cells_menu_item=");
        script.Should().Contain("native_fill_down_menu_item=");
        script.Should().Contain("native_fill_right_menu_item=");
        script.Should().Contain("native_fill_up_menu_item=");
        script.Should().Contain("native_fill_left_menu_item=");
        script.Should().Contain("native_clear_menu_item=");
        script.Should().Contain("native_clear_all_menu_item=");
        script.Should().Contain("native_clear_formats_menu_item=");
        script.Should().Contain("native_clear_contents_menu_item=");
        script.Should().Contain("native_clear_comments_menu_item=");
        script.Should().Contain("native_clear_hyperlinks_menu_item=");
        script.Should().Contain("native_borders_menu_item=");
        script.Should().Contain("native_borders_preset_count=");
        script.Should().Contain("native_merge_and_center_menu_item=");
        script.Should().Contain("native_unmerge_cells_menu_item=");
        script.Should().Contain("native_find_menu_item=");
        script.Should().Contain("native_find_next_menu_item=");
        script.Should().Contain("native_replace_menu_item=");
        script.Should().Contain("native_go_to_menu_item=");
        script.Should().Contain("native_go_to_special_menu_item=");
        script.Should().Contain("native_data_menu=");
        script.Should().Contain("native_flash_fill_menu_item=");
        script.Should().Contain("native_sort_ascending_menu_item=");
        script.Should().Contain("native_sort_descending_menu_item=");
        script.Should().Contain("native_format_cells_menu_item=");
        script.Should().Contain("macos_dialog_smoke=");
        script.Should().Contain("macos_dialog_smoke_attempted=");
        script.Should().Contain("macos_dialog_smoke_status=");
        script.Should().Contain("macos_dialog_activation_completed=");
        script.Should().Contain("find_dialog=");
        script.Should().Contain("find_dialog_text_box=");
        script.Should().Contain("find_dialog_action_buttons=");
        script.Should().Contain("find_dialog_options=");
        script.Should().Contain("find_dialog_format_controls=");
        script.Should().Contain("find_dialog_compact_layout=");
        script.Should().Contain("find_dialog_result_closed_without_accept=");
        script.Should().Contain("replace_dialog=");
        script.Should().Contain("replace_dialog_text_boxes=");
        script.Should().Contain("replace_dialog_action_buttons=");
        script.Should().Contain("replace_dialog_options=");
        script.Should().Contain("replace_dialog_format_controls=");
        script.Should().Contain("replace_dialog_compact_layout=");
        script.Should().Contain("replace_dialog_result_closed_without_accept=");
        script.Should().Contain("go_to_dialog=");
        script.Should().Contain("go_to_dialog_reference_controls=");
        script.Should().Contain("go_to_dialog_compact_layout=");
        script.Should().Contain("go_to_dialog_result_closed_without_accept=");
        script.Should().Contain("go_to_special_dialog=");
        script.Should().Contain("go_to_special_dialog_kind_controls=");
        script.Should().Contain("go_to_special_dialog_value_type_controls=");
        script.Should().Contain("go_to_special_dialog_compact_layout=");
        script.Should().Contain("go_to_special_dialog_result_closed_without_accept=");
        script.Should().Contain("format_cells_dialog=");
        script.Should().Contain("format_cells_dialog_tab_strip=");
        script.Should().Contain("format_cells_dialog_default_number_tab=");
        script.Should().Contain("format_cells_dialog_number_controls=");
        script.Should().Contain("format_cells_dialog_action_buttons=");
        script.Should().Contain("format_cells_dialog_compact_layout=");
        script.Should().Contain("format_cells_dialog_result_closed_without_accept=");
        script.Should().Contain("native_help_menu=true");
        script.Should().Contain("native_help_online_menu_item=true");
        script.Should().Contain("native_legal_notices_menu_item=");
        script.Should().Contain("drawing_object_previews=3");
        script.Should().Contain("roundtrip_drawing_object_previews=3");
        script.Should().Contain("format_cells_style_roundtrip=true");
        script.Should().Contain("format_cells_style_roundtrip_count");
        script.Should().Contain("test \"$format_cells_style_roundtrip_count\" -ge 2");
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
        script.Should().Contain("private readonly NativeMenuItem _formatCellsMenuItem = new();");
        script.Should().Contain("_formatCellsMenuItem.Header = `\"Format Cells...`\"");
        script.Should().Contain("FormatCellsCompactPlanner.TryPlan");
        script.Should().Contain("_session.ApplySelectedRangeCompactFormat(");
        script.Should().Contain("selection.Request.MergeCells");
        script.Should().Contain("`\"FormatCellsCompactDialog`\"");
        script.Should().Contain("`\"FormatCellsNumberFormatBox`\"");
        script.Should().Contain("new(`\"Justify`\", CellHAlign.Justify)");
        script.Should().Contain("new(`\"Distributed`\", CellHAlign.Distributed)");
        script.Should().Contain("new(`\"Justify`\", CellVAlign.Justify)");
        script.Should().Contain("new(`\"Distributed`\", CellVAlign.Distributed)");
        script.Should().Contain("`\"FormatCellsMergeCellsBox`\"");
        script.Should().Contain("MergeCells: ReadChangedFormatCellsBool(currentMergeCells, mergeCellsBox)");
        script.Should().Contain("bool? mergeCells = null");
        script.Should().Contain("CreateFormatCellsMergeCommands(range, shouldMerge)");
        script.Should().Contain("CellMergePlanner.CreateMergeCommands(");
        script.Should().Contain("bool? MergeCells = null");
        script.Should().Contain("`\"FormatCellsFillPatternStyleBox`\"");
        script.Should().Contain("`\"FormatCellsFillPatternColorBox`\"");
        script.Should().Contain("`\"FormatCellsNormalFontBox`\"");
        script.Should().Contain("`\"FormatCellsProtectionExplanationText`\"");
        script.Should().Contain("Locking cells or hiding formulas has no effect until you protect the worksheet.");
        script.Should().Contain("var normalStyle = CellStyle.Default;");
        script.Should().Contain("Bold: normalFont ? normalStyle.Bold : ReadChangedFormatCellsBool(_session.IsSelectedRangeStartBold, boldBox)");
        script.Should().Contain("FontName: normalFont ? normalStyle.FontName : ReadChangedFormatCellsText(currentFontName, fontNameBox)");
        script.Should().Contain("FontColor: normalFont ? normalStyle.FontColor : (fontColorBox.SelectedItem as FormatCellsColorChoice)?.Color");
        script.Should().Contain("SelectFormatCellsColor(fontColorBox, normal.FontColor)");
        script.Should().Contain("FillPatternStyle: clearFill ? null : ReadChangedFormatCellsValue(currentFillPatternStyle, fillPatternStyleBox)");
        script.Should().Contain("FillPatternColor: clearFill ? null : (fillPatternColorBox.SelectedItem as FormatCellsColorChoice)?.Color");
        script.Should().Contain("CellFillPatternStyle? FillPatternStyle = null");
        script.Should().Contain("CellColor? FillPatternColor = null");
        script.Should().Contain("FillPatternStyle: request.ClearFill ? null : request.FillPatternStyle");
        script.Should().Contain("FillPatternColor: request.ClearFill ? null : request.FillPatternColor");
        script.Should().Contain("private enum FindDialogAction");
        script.Should().Contain("private sealed record FindDialogResult(");
        script.Should().Contain("FindOptions Options,");
        script.Should().Contain("bool MatchCase,");
        script.Should().Contain("bool MatchEntireCell);");
        script.Should().Contain("`\"FindAllButton`\"");
        script.Should().Contain("CreateFindOptionsControls(`\"Find`\", defaultLookInIndex: 0)");
        script.Should().Contain("{automationPrefix}WithinBox");
        script.Should().Contain("{automationPrefix}LookInBox");
        script.Should().Contain("`\"FindAllResultsStatusText`\"");
        script.Should().Contain("`\"FindAllResultsList`\"");
        script.Should().Contain("_session.FindAll(search.FindText, search.Options, search.MatchCase, search.MatchEntireCell)");
        script.Should().Contain("public WorkbookFindAllResult FindAll(");
        script.Should().Contain("private WorkbookFindAllMatch CreateFindAllMatch(FindResult result)");
        script.Should().Contain("private enum ReplaceDialogAction");
        script.Should().Contain("private sealed record ReplaceDialogResult(");
        script.Should().Contain("ReplaceDialogAction Action,");
        script.Should().Contain("StyleDiff? ReplacementFormat);");
        script.Should().Contain("private sealed record FindOptionsControls(");
        script.Should().Contain("`\"ReplaceButton`\"");
        script.Should().Contain("CreateFindOptionsControls(`\"Replace`\", defaultLookInIndex: 1)");
        script.Should().Contain("CreateFindReplaceFormatButton(`\"FindChooseFormatFromCellButton`\", `\"Choose From Cell`\")");
        script.Should().Contain("CreateFindReplaceFormatButton(`\"FindClearFormatButton`\", `\"Clear Format`\")");
        script.Should().Contain("CreateFindReplaceFormatButton(`\"ReplaceFindChooseFormatFromCellButton`\", `\"Choose From Cell`\")");
        script.Should().Contain("CreateFindReplaceFormatButton(`\"ReplaceFindClearFormatButton`\", `\"Clear Format`\")");
        script.Should().Contain("CreateFindReplaceFormatButton(`\"ReplaceWithChooseFormatFromCellButton`\", `\"Choose From Cell`\")");
        script.Should().Contain("CreateFindReplaceFormatButton(`\"ReplaceWithClearFormatButton`\", `\"Clear Format`\")");
        script.Should().Contain("CreateFindReplaceFormatRow(`\"Find format`\",");
        script.Should().Contain("CreateFindReplaceFormatRow(`\"Replace format`\",");
        script.Should().Contain("CreateFindOptions(optionsControls, findFormat)");
        script.Should().Contain("RequiredFormat: requiredFormat);");
        script.Should().Contain("replacement.ReplacementFormat");
        script.Should().Contain("_session.ReplaceNextValue(");
        script.Should().Contain("replacement.Options,");
        script.Should().Contain("public WorkbookReplaceResult ReplaceNextValue(");
        script.Should().Contain("public StyleDiff? CreateFormatDiffFromActiveCell()");
        script.Should().Contain("public StyleDiff? CreateFormatDiffFromCell(CellAddress address)");
        script.Should().Contain("StyleDiff? replacementFormat = null");
        script.Should().Contain("replacementFormat is not null");
        script.Should().Contain("new GridRange(edit.Address, edit.Address)");
        script.Should().Contain("new GridRange(match.Address, match.Address)");
        script.Should().Contain("GetReplaceTargetIndex(matches, effectiveOptions.SearchOrder, sameSearch)");
        script.Should().Contain("FindLookIn.Formulas => cell.FormulaText");
        script.Should().Contain("new SetCommentCommand(");
        script.Should().Contain("new UpdateThreadedCommentTextCommand(");
        script.Should().Contain("public enum FindResultTarget");
        script.Should().Contain("ThreadedCommentReply");
        script.Should().Contain("FindResultTarget Target = FindResultTarget.Cell,");
        script.Should().Contain("int? ReplyIndex = null);");
        script.Should().Contain("public readonly record struct SearchText(");
        script.Should().Contain("comment.Replies[replyIndex].Text");
        script.Should().Contain("FindResultTarget.ThreadedCommentReply,");
        script.Should().Contain("match.Target == FindResultTarget.ThreadedCommentReply");
        script.Should().Contain("match.ReplyIndex is { } replyIndex");
        script.Should().Contain("new UpdateThreadedCommentReplyCommand(");
        script.Should().Contain("private static bool IsValidThreadedCommentReplyIndex(ThreadedComment comment, int replyIndex)");
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
        script.Should().Contain("VerifyLiveCommandKeysArgument");
        script.Should().Contain("await mainWindow.TryPasteLaunchSmokeClipboardImageAsync();");
        script.Should().Contain("BeginLaunchSmokeLiveCommandKeyProbe");
        script.Should().Contain("live_command_key_smoke_required=");
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
        script.Should().Contain("DrawingObjectRenderPlanner.Plan(viewport)");
        script.Should().Contain("CreateSelectableDrawingObjectVisual(renderPlan, width, height)");
        script.Should().Contain("AutomationProperties.SetItemStatus(container, selected ? `\"Selected`\" : `\"Not selected`\")");
        script.Should().Contain("CreateDrawingObjectVisual(renderPlan, width, height)");
        script.Should().Contain("CreateDrawingCellRangeSnapshotVisual(renderPlan, width, height)");
        script.Should().Contain("CreateDrawingImageSourceRect(crop)");
        script.Should().Contain("TryCreateDrawingBitmap(imageBytes, out var bitmap)");
        script.Should().Contain("private static bool HasVisibleCellBorder(CellStyle? style)");
        script.Should().Contain("private readonly RecentFilesStore _recentFiles = RecentFilesStore.Load();");
        script.Should().Contain("_newWorkbookMenuItem.Click += (_, _) => CreateNewWorkbook();");
        script.Should().Contain("_openRecentMenuItem.Header = `\"Open Recent`\";");
        script.Should().Contain("_selectAllMenuItem.Header = `\"Select All`\";");
        script.Should().Contain("_fillCellsButton.Content = `\"Fill Cells`\";");
        script.Should().Contain("_fillDownMenuItem.Gesture = new KeyGesture(Key.D, KeyModifiers.Control);");
        script.Should().Contain("_fillRightMenuItem.Gesture = new KeyGesture(Key.R, KeyModifiers.Control);");
        script.Should().Contain("private void FillSelectedRange(FillCellsDirection direction)");
        script.Should().Contain("_session.FillSelectedRange(direction)");
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
        script.Should().Contain("public bool CanFillSelectedRange(FillCellsDirection direction)");
        script.Should().Contain("public WorkbookCellEditResult FillSelectedRange(FillCellsDirection direction)");
        script.Should().Contain("new FillCellsCommand(sheetId, sheetRange, direction)");
        script.Should().Contain("CreateBorderPresetCommand(range, preset)");
        script.Should().Contain("CellBorderPresetPlanner.Plan(preset, range, range.Start, borderStyle, borderColor)");
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
    public void MacOsAppReadinessPreflight_FailsForMovingHostedRunnerLabel()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path, workflowArm64Runner: "macos-latest");

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("macOS workflow runtime runner matrix must include 'osx-arm64=macos-15'");
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
        string workflowArm64Runner = "macos-15",
        string workflowX64Runner = "macos-15-intel",
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
            on:
              workflow_dispatch:
                inputs:
                  distribution_candidate:
                    description: Require Developer ID signing, accepted notarization, stapled ticket, and Gatekeeper assessment evidence.
                    type: boolean
                    default: false
            jobs:
              macos-app:
                runs-on: ${"{{"} matrix.runner {"}}"}
                strategy:
                  matrix:
                    include:
                      - runtime: osx-arm64
                        runner: {workflowArm64Runner}
                      - runtime: osx-x64
                        runner: {workflowX64Runner}
                      {FormatWorkflowRuntimeEntry(workflowExtraRuntime)}
                steps:
                  - uses: actions/setup-dotnet@v5
                    with:
                      dotnet-version: 10.0.x
                  - name: Capture runner toolchain evidence
                    run: echo runner
                  - name: Test portable PDF macOS route
                    shell: bash
                    run: |
                      dotnet test tests/FreeX.App.Services.Tests/FreeX.App.Services.Tests.csproj \
                        --configuration Release \
                        --filter 'FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfDocumentExporterTests|FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfExportPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfPageContentPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfTextCapabilityPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.WorkbookExportPrintPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.WorkbookShareActionPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.WorkbookViewportScrollPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.OpenRecentWorkbookMenuPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.AppServicesPortabilityGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaProjectPortabilityGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.ApplicationDataPathGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.AppStoragePathPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.AppOptionsStoreTests|FullyQualifiedName~FreeX.App.Services.Tests.AtomicFileWriterTests|FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaShellSourceTests|FullyQualifiedName~FreeX.App.Services.Tests.MacOsLaunchSmokeReportKeyDriftGuardTests' \
                        --logger "trx;LogFileName=freex-${"{{"} matrix.runtime {"}}"}-portable-pdf-exporter-tests.trx" \
                        --results-directory artifacts
                      dotnet test tests/FreeX.Core.Model.Tests/FreeX.Core.Model.Tests.csproj \
                        --configuration Release \
                        --filter 'FullyQualifiedName~FreeX.Core.Model.Tests.ExportPathPlannerTests' \
                        --logger "trx;LogFileName=freex-${"{{"} matrix.runtime {"}}"}-export-path-tests.trx" \
                        --results-directory artifacts
                  - name: Build app project
                    run: dotnet build src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj --configuration Release
                  - shell: bash
                    env:
                      FREEX_RUNTIME: ${"{{"} matrix.runtime {"}}"}
                      FREEX_DISTRIBUTION_CANDIDATE: ${"{{"} github.event_name == 'workflow_dispatch' && inputs.distribution_candidate == true {"}}"}
                    run: |
                      app="$RUNNER_TEMP/FreeX.app"
                      artifact_root="$GITHUB_WORKSPACE/artifacts"
                      smoke_log="$artifact_root/smoke.log"
                      runtime="$FREEX_RUNTIME"
                      zip_name="freex-$runtime-macos-app.zip"
                      zip_path="$artifact_root/$zip_name"
                      unzip_root="$RUNNER_TEMP/freex-$runtime-unzip"
                      app_path="$unzip_root/FreeX.app"
                      open_with_report="$artifact_root/freex-$runtime-macos-open-with-launch-smoke.txt"
                      default_open_report="$artifact_root/freex-$runtime-macos-default-open-launch-smoke.txt"
                      app_diagnostics_dir="$artifact_root/freex-$runtime-macos-app-diagnostics"
                      distribution_candidate="$FREEX_DISTRIBUTION_CANDIDATE"
                      artifact_channel="internal-preview"
                      distribution_contract="internal_preview_not_for_distribution_notarization_optional"
                      artifact_channel="distribution-candidate"
                      distribution_contract="distribution_candidate_requires_developer_id_notarization_stapling"
                      echo "artifact_channel=$artifact_channel"
                      echo "distribution_candidate=$distribution_candidate"
                      echo "distribution_readiness=$distribution_readiness"
                      echo "Distribution-candidate macOS app runs require Developer ID signing secrets: MACOS_CODESIGN_CERTIFICATE_P12, MACOS_CODESIGN_CERTIFICATE_PASSWORD, and MACOS_DEVELOPER_ID_APPLICATION."
                      echo "Distribution-candidate macOS app runs require notarization secrets: MACOS_NOTARY_APPLE_ID, MACOS_NOTARY_TEAM_ID, and MACOS_NOTARY_PASSWORD."
                      echo "Distribution-candidate run requires codesign_mode=developer-id, notarization_status=accepted, and stapler_validated=true."
                      gatekeeper_assessment_required="$distribution_candidate"
                      /usr/sbin/spctl --assess --type execute --verbose=4 "$app_path"
                      echo "gatekeeper_assessment_attempted=true"
                      echo "gatekeeper_assessment_required=$gatekeeper_assessment_required"
                      echo "gatekeeper_assessment_subject=unzipped_app_bundle"
                      echo "gatekeeper_assessment_type=execute"
                      echo "gatekeeper_assessment_exit_code=$gatekeeper_assessment_exit_code"
                      echo "gatekeeper_assessment_status=$gatekeeper_assessment_status"
                      echo "gatekeeper_assessment_source=$gatekeeper_assessment_source"
                      echo "gatekeeper_assessment_output=$gatekeeper_line"
                      echo "distribution_readiness=distribution_candidate_blocked_gatekeeper_assessment"
                      echo "Distribution-candidate run requires accepted Gatekeeper assessment from Notarized Developer ID."
                      test 'gatekeeper_assessment_source" != "Notarized Developer ID"'
                      echo "gatekeeper_assessment_required=true"
                      echo "gatekeeper_assessment_exit_code=0"
                      echo "gatekeeper_assessment_status=accepted"
                      echo "gatekeeper_assessment_source=Notarized Developer ID"
                      echo "distribution_readiness=internal_preview_not_for_distribution"
                      echo "distribution_readiness=distribution_candidate_ready"
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
                      ditto -x -k "$zip_path" "$unzip_root"
                      (cd "$artifact_root" && shasum -a 256 "$zip_name" > "$zip_name.sha256")
                      test -x "$unzip_root/FreeX.app/Contents/MacOS/FreeX"
                      test -f "$unzip_root/FreeX.app/Contents/MacOS/FreeX.dll"
                      xcrun notarytool submit "$zip_path"
                      xcrun stapler validate "$app" | tee -a "$notary_log"
                      tester_instructions_path="$artifact_root/freex-$runtime-macos-tester-instructions.md"
                      shasum -a 256 -c "$zip_name.sha256"
                      zip_sha256="$(cut -d ' ' -f 1 "$artifact_root/$zip_name.sha256")"
                      echo "zip_sha256=$zip_sha256"
                      cat > "$tester_instructions_path" <<EOF
                      This artifact is a macOS port validation build. Internal-preview artifacts are not a public release channel; distribution-candidate artifacts must show Developer ID signing, accepted notarization, stapler validation, and accepted Gatekeeper assessment in evidence.
                      Use osx-arm64 for Apple Silicon Macs and osx-x64 for Intel Macs.
                      Unzip the GitHub Actions artifact wrapper first; these files are inside it.
                      ditto -x -k $zip_name .
                      If artifact_channel=internal-preview, ad-hoc signed or non-notarized previews may require Control-click or right-click > Open for trusted internal testing.
                      EOF
                      "$unzip_root/FreeX.app/Contents/MacOS/FreeX" --packaging-smoke | tee "$smoke_log"
                      grep -q "macOS Preview Workbook" "$smoke_log"
                      grep -q "drawing_object_previews=3" "$smoke_log"
                      grep -q "roundtrip_drawing_object_previews=3" "$smoke_log"
                      grep -q "format_cells_style_roundtrip=true" "$smoke_log"
                      "$unzip_root/FreeX.app/Contents/MacOS/FreeX" --packaging-smoke "$RUNNER_TEMP/smoke.csv" | tee -a "$smoke_log"
                      grep -q "Packaging smoke opened" "$smoke_log"
                      grep -q "edited, saved, and reopened" "$smoke_log"
                      format_cells_style_roundtrip_count="$(grep -c "format_cells_style_roundtrip=true" "$smoke_log")"
                      test "$format_cells_style_roundtrip_count" -ge 2
                      echo "format_cells_style_roundtrip=true"
                      echo "format_cells_style_roundtrip_count=$format_cells_style_roundtrip_count"
                      /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$unzip_root/FreeX.app"
                      open -W -n -b io.github.tony-xmelon.freex "$RUNNER_TEMP/launch.csv" --args --macos-launch-smoke "$artifact_root/launch.txt" --macos-launch-smoke-diagnostics-dir "$app_diagnostics_dir"
                      osascript -e 'tell application id "io.github.tony-xmelon.freex" to quit' || true
                      open_with_smoke_file="$RUNNER_TEMP/freex-$runtime-open-with.csv"
                      open -W -n -a "$app_path" "$open_with_smoke_file" --args --macos-launch-smoke "$open_with_report" --macos-launch-smoke-diagnostics-dir "$app_diagnostics_dir"
                      grep -q "macos_launch_smoke=passed" "$open_with_report"
                      grep -q "app_diagnostics_directory_configured=true" "$open_with_report"
                      grep -q "window_shown=true" "$open_with_report"
                      grep -q "opened_source_path=.*freex-$runtime-open-with.csv" "$open_with_report"
                      grep -q "viewport_rows=[1-9]" "$open_with_report"
                      grep -q "viewport_columns=[1-9]" "$open_with_report"
                      grep -q "native_open_recent_menu_item=true" "$open_with_report"
                      grep -q "native_open_recent_item_count=[1-9]" "$open_with_report"
                      default_open_smoke_file="$RUNNER_TEMP/freex-$runtime-default-open.fxl"
                      cat > "$default_open_smoke_file" <<'JSON'
                      {"{"} "FileFormat": "FreeX.NativeJsonWorkbook" {"}"}
                      JSON
                      open -W -n "$default_open_smoke_file" --args --macos-launch-smoke "$default_open_report" --macos-launch-smoke-diagnostics-dir "$app_diagnostics_dir"
                      grep -q "app_diagnostics_directory_configured=true" "$default_open_report"
                      launchservices_default_open_app_override=false
                      launchservices_default_open_document_extension=fxl
                      launchservices_default_open_boundary=ci_open_document_without_app_override_not_finder_double_click
                      grep -q "opened_source_path=.*freex-$runtime-default-open.fxl" "$default_open_report"
                      grep -q "launchservices_default_open_app_override=false" "$default_open_report"
                      grep -q "launchservices_default_open_document_extension=fxl" "$default_open_report"
                      grep -q "launchservices_default_open_boundary=ci_open_document_without_app_override_not_finder_double_click" "$default_open_report"
                      app_diagnostics_events_path="$app_diagnostics_dir/events.jsonl"
                      app_diagnostics_crash_reports_dir="$app_diagnostics_dir/CrashReports"
                      if [[ -d "$app_diagnostics_crash_reports_dir" ]]; then
                        app_diagnostics_crash_count="$(find "$app_diagnostics_crash_reports_dir" -type f -name '*.json' | wc -l | tr -d ' ')"
                      else
                        app_diagnostics_crash_count=0
                      fi
                      echo "app_diagnostics_artifact=freex-$runtime-macos-app-diagnostics"
                      echo "app_diagnostics_events_jsonl=true"
                      echo "app_diagnostics_crash_report_count=$app_diagnostics_crash_count"
                      test -f "$app_diagnostics_events_path"
                      grep -q '"eventName":"app_start"' "$app_diagnostics_events_path"
                      grep -q '"eventName":"app_ready"' "$app_diagnostics_events_path"
                      grep -q '"eventName":"macos_launch_smoke"' "$app_diagnostics_events_path"
                      grep -q "external_image_clipboard_paste_required=false" "$artifact_root/launch.txt"
                      grep -q "live_command_key_smoke_required=false" "$artifact_root/launch.txt"
                      grep -q "live_command_key_smoke=not_required" "$artifact_root/launch.txt"
                      grep -q "cmd_find_direct_route_source_guard=true" "$artifact_root/launch.txt"
                      grep -q "cmd_page_up_direct_route_source_guard=true" "$artifact_root/launch.txt"
                      grep -q "cmd_page_down_direct_route_source_guard=true" "$artifact_root/launch.txt"
                      grep -q "new_sheet_button=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_format_painter_button=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_autosum_button=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_autosum_sum_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_autosum_average_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_autosum_count_numbers_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_autosum_count_all_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_autosum_max_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_autosum_min_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_fill_cells_button=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_fill_down_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_fill_right_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_fill_up_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_fill_left_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_clear_button=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_clear_all_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_clear_formats_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_clear_contents_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_clear_comments_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_clear_hyperlinks_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_borders_button=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_wrap_text_button=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_merge_and_center_button=true" "$artifact_root/launch.txt"
                      grep -q "native_file_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_new_workbook_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_open_recent_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_open_recent_item_count=[1-9]" "$artifact_root/launch.txt"
                      grep -q "native_workbook_statistics_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_export_pdf_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_edit_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_close_workbook_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_data_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_flash_fill_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_review_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_format_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_view_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_sheet_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_window_menu=true" "$artifact_root/launch.txt"
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
                      grep -q "native_format_painter_menu_item=true" "$artifact_root/launch.txt"
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
                      grep -q "native_find_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_find_next_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_replace_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_go_to_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_go_to_special_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_sort_ascending_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_sort_descending_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_advanced_filter_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_remove_duplicates_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_subtotal_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_data_validation_preview_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_data_validation_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_what_if_analysis_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_goal_seek_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_data_table_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_scenario_manager_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_forecast_sheet_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_review_summary_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_check_accessibility_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_next_note_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_previous_note_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_next_comment_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_previous_comment_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_format_cells_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "macos_dialog_smoke=passed" "$artifact_root/launch.txt"
                      grep -q "macos_dialog_smoke_attempted=true" "$artifact_root/launch.txt"
                      grep -q "macos_dialog_smoke_status=passed" "$artifact_root/launch.txt"
                      grep -q "macos_dialog_activation_completed=true" "$artifact_root/launch.txt"
                      grep -q "find_dialog=true" "$artifact_root/launch.txt"
                      grep -q "find_dialog_text_box=true" "$artifact_root/launch.txt"
                      grep -q "find_dialog_action_buttons=true" "$artifact_root/launch.txt"
                      grep -q "find_dialog_options=true" "$artifact_root/launch.txt"
                      grep -q "find_dialog_format_controls=true" "$artifact_root/launch.txt"
                      grep -q "find_dialog_compact_layout=true" "$artifact_root/launch.txt"
                      grep -q "find_dialog_result_closed_without_accept=true" "$artifact_root/launch.txt"
                      grep -q "replace_dialog=true" "$artifact_root/launch.txt"
                      grep -q "replace_dialog_text_boxes=true" "$artifact_root/launch.txt"
                      grep -q "replace_dialog_action_buttons=true" "$artifact_root/launch.txt"
                      grep -q "replace_dialog_options=true" "$artifact_root/launch.txt"
                      grep -q "replace_dialog_format_controls=true" "$artifact_root/launch.txt"
                      grep -q "replace_dialog_compact_layout=true" "$artifact_root/launch.txt"
                      grep -q "replace_dialog_result_closed_without_accept=true" "$artifact_root/launch.txt"
                      grep -q "go_to_dialog=true" "$artifact_root/launch.txt"
                      grep -q "go_to_dialog_reference_controls=true" "$artifact_root/launch.txt"
                      grep -q "go_to_dialog_compact_layout=true" "$artifact_root/launch.txt"
                      grep -q "go_to_dialog_result_closed_without_accept=true" "$artifact_root/launch.txt"
                      grep -q "go_to_special_dialog=true" "$artifact_root/launch.txt"
                      grep -q "go_to_special_dialog_kind_controls=true" "$artifact_root/launch.txt"
                      grep -q "go_to_special_dialog_value_type_controls=true" "$artifact_root/launch.txt"
                      grep -q "go_to_special_dialog_compact_layout=true" "$artifact_root/launch.txt"
                      grep -q "go_to_special_dialog_result_closed_without_accept=true" "$artifact_root/launch.txt"
                      grep -q "format_cells_dialog=true" "$artifact_root/launch.txt"
                      grep -q "format_cells_dialog_tab_strip=true" "$artifact_root/launch.txt"
                      grep -q "format_cells_dialog_default_number_tab=true" "$artifact_root/launch.txt"
                      grep -q "format_cells_dialog_number_controls=true" "$artifact_root/launch.txt"
                      grep -q "format_cells_dialog_action_buttons=true" "$artifact_root/launch.txt"
                      grep -q "format_cells_dialog_compact_layout=true" "$artifact_root/launch.txt"
                      grep -q "format_cells_dialog_result_closed_without_accept=true" "$artifact_root/launch.txt"
                      grep -q "native_autosum_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_autosum_sum_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_autosum_average_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_autosum_count_numbers_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_autosum_count_all_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_autosum_max_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_autosum_min_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_fill_cells_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_fill_down_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_fill_right_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_fill_up_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_fill_left_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_clear_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_clear_all_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_clear_formats_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_clear_contents_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_clear_comments_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_clear_hyperlinks_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_bold_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_italic_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_underline_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_double_underline_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_strikethrough_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_increase_font_size_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_decrease_font_size_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_fill_color_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_clear_fill_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_font_color_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_fill_color_swatch_count=69" "$artifact_root/launch.txt"
                      grep -q "native_font_color_swatch_count=69" "$artifact_root/launch.txt"
                      grep -q "native_borders_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_borders_preset_count=8" "$artifact_root/launch.txt"
                      grep -q "native_merge_and_center_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_unmerge_cells_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_cell_styles_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_cell_styles_preset_count=33" "$artifact_root/launch.txt"
                      grep -q "native_horizontal_text_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_angle_counterclockwise_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_angle_clockwise_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_vertical_text_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_rotate_text_up_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_rotate_text_down_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_currency_format_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_percent_format_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_comma_style_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_increase_decimal_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_decrease_decimal_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_align_top_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_align_middle_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_align_bottom_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "toolbar_wrap_text_button=true" "$artifact_root/launch.txt"
                      grep -q "native_wrap_text_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_decrease_indent_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_increase_indent_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_align_left_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_align_center_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_align_right_menu_item=true" "$artifact_root/launch.txt"
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
                      grep -q "native_minimize_window_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_zoom_window_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_bring_all_to_front_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_help_online_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_send_feedback_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_check_for_updates_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_about_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_legal_notices_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_quit_menu_item=true" "$artifact_root/launch.txt"
                      echo "bundle_icon=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$app/Contents/Info.plist")"
                  - name: Upload app artifact
                    uses: actions/upload-artifact@v7
                    with:
                      if-no-files-found: error
                      path: |
                        artifacts/freex-osx-arm64-macos-tester-instructions.md
                        artifacts/freex-${"{{"} matrix.runtime {"}}"}-macos-open-with-launch-smoke.txt
                        artifacts/freex-${"{{"} matrix.runtime {"}}"}-macos-default-open-launch-smoke.txt
                  - name: Upload app diagnostics
                    if: always()
                    uses: actions/upload-artifact@v7
                    with:
                      name: freex-${"{{"} github.run_id {"}}"}-${"{{"} github.run_attempt {"}}"}-${"{{"} matrix.runtime {"}}"}-macos-diagnostics
                      if-no-files-found: warn
                      path: |
                        artifacts/freex-osx-arm64-macos-evidence.txt
                        artifacts/freex-${"{{"} matrix.runtime {"}}"}-macos-open-with-launch-smoke.txt
                        artifacts/freex-${"{{"} matrix.runtime {"}}"}-macos-default-open-launch-smoke.txt
                        artifacts/freex-${"{{"} matrix.runtime {"}}"}-portable-pdf-exporter-tests.trx
                        artifacts/freex-${"{{"} matrix.runtime {"}}"}-export-path-tests.trx
                        artifacts/freex-${"{{"} matrix.runtime {"}}"}-macos-app-diagnostics/**
              publish-distribution-candidate:
                name: Publish macOS distribution candidate
                needs: macos-app
                if: ${"{{"} github.event_name == 'workflow_dispatch' && inputs.distribution_candidate == true {"}}"}
                runs-on: ubuntu-latest
                timeout-minutes: 30
                permissions:
                  actions: read
                  contents: write
                concurrency:
                  group: macos-distribution-candidate-release
                  cancel-in-progress: false
                steps:
                  - uses: actions/download-artifact@v7
                    with:
                      pattern: freex-${"{{"} github.run_id {"}}"}-${"{{"} github.run_attempt {"}}"}-*-macos-app
                      merge-multiple: true
                  - name: Prepare release-channel assets
                    shell: pwsh
                    run: |
                      FreeX-latest-macos-arm64.zip
                      FreeX-latest-macos-x64.zip
                      FreeX-latest-macos-distribution-candidate-manifest.json
                      FreeX-latest-macos-distribution-candidate-instructions.md
                      FreeX-latest-$assetLabel-default-open-launch-smoke.txt
                      source_artifact_pattern
                      distribution_candidate_required_markers
                      default_open_launch_smoke_report
                  - name: Upload release-channel prepared assets
                    uses: actions/upload-artifact@v7
                    with:
                      if-no-files-found: error
                  - name: Create or update GitHub release
                    shell: pwsh
                    run: |
                      gh release create
                      gh release upload
                      --draft=false
                      --prerelease
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
                    var diagnostics = AvaloniaAppDiagnostics.Create(launchSmokeOptions?.DiagnosticsDirectory);
                    diagnostics.RegisterUnhandledExceptionHandlers();
                    diagnostics.RecordEvent("app_start");
                    App.StartupArguments = startupArguments;
                    App.LaunchSmokeOptions = launchSmokeOptions;
                    App.Diagnostics = diagnostics;
                    try
                    {
                        BuildAvaloniaApp().StartWithClassicDesktopLifetime(startupArguments);
                        diagnostics.RecordEvent("app_exit");
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        diagnostics.RecordCrash(ex, "avalonia_startup");
                        throw;
                    }
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
                internal static AvaloniaAppDiagnostics? Diagnostics { get; set; }

                private static async Task ActivatedAsync(MainWindow mainWindow, ActivatedEventArgs args)
                {
                    Diagnostics?.RecordEvent("app_ready");
                    this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime;
                    if (args is not FileActivatedEventArgs fileArgs || fileArgs.Kind != ActivationKind.File)
                        return;

                    await mainWindow.OpenActivatedFilesAsync(fileArgs.Files);
                    MacOsLaunchSmokeCoordinator.Start(mainWindow, launchSmokeOptions, Diagnostics);
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/AvaloniaAppDiagnostics.cs",
            """
            namespace FreeX.App.Avalonia;

            internal sealed class AvaloniaAppDiagnostics
            {
                public static AvaloniaAppDiagnostics Create(string? diagnosticsDirectory = null)
                {
                    AppDiagnosticsOptions.CreateDefault();
                    new AppDiagnosticsFileStore(options);
                    AppDiagnosticsMetadata.Create("Version Test");
                    return new();
                }

                public void RegisterUnhandledExceptionHandlers()
                {
                    AppDomain.CurrentDomain.UnhandledException += (_, args) => { };
                    TaskScheduler.UnobservedTaskException += (_, args) => { };
                }

                public void RecordEvent(string eventName, IReadOnlyDictionary<string, string?>? properties = null)
                {
                    AppDiagnosticsFileStore.SanitizeProperties(properties);
                }

                public string RecordCrash(Exception exception, string source) => "";
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
                /*
                private readonly ScrollBar _verticalWorksheetScrollBar = new();
                private readonly ScrollBar _horizontalWorksheetScrollBar = new();
                private bool _isUpdatingWorksheetScrollBars;
                root.Children.Add(BuildWorksheetViewportChrome());
                _sheetScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                _sheetScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                _verticalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;
                _horizontalWorksheetScrollBar.ValueChanged += WorksheetScrollBar_ValueChanged;
                WorkbookViewportScrollPlanner.Create(_session.ActiveSheet, _session.Viewport)
                ApplyWorksheetScrollAxis(_verticalWorksheetScrollBar, state.Vertical);
                ApplyWorksheetScrollAxis(_horizontalWorksheetScrollBar, state.Horizontal);
                WorkbookViewportScrollPlanner.CalculateViewportOrigin(
                _session.SetViewportOrigin(topRow, leftCol)
                */
                public async Task OpenActivatedFilesAsync(IReadOnlyList<IStorageItem> files) => await Task.CompletedTask;
                private static void RenderCell(CellStyle? style)
                {
                    CreateColorPaletteFlyout(ColorPaletteTarget.Fill, includeClearFill: true);
                    _formatPainterButton.Content = "Format Painter";
                    AutomationProperties.SetAutomationId(_formatPainterButton, "HomeFormatPainterButton");
                    AutomationProperties.SetHelpText(_formatPainterButton, "Copy formatting from the selection and apply it to another range.");
                    _formatPainterMenuItem.Header = "Format Painter";
                    _formatPainterMenuItem.Click += (_, _) => CaptureFormatPainterSource(persistent: false);
                    editMenu.Items.Add(_formatPainterMenuItem);
                    _formatPainterButton.IsEnabled = isIdle;
                    _formatPainterMenuItem.IsEnabled = _formatPainterButton.IsEnabled;
                    _autoSumButton.Content = "AutoSum";
                    _autoSumButton.Flyout = CreateAutoSumFlyout();
                    AutomationProperties.SetAutomationId(_autoSumButton, "HomeAutoSumButton");
                    AutomationProperties.SetHelpText(_autoSumButton, "Insert a formula using nearby numeric cells.");
                    _autoSumSumFlyoutItem.Click += (_, _) => InsertAutoSumFormula("SUM");
                    _autoSumAverageFlyoutItem.Click += (_, _) => InsertAutoSumFormula("AVERAGE");
                    _autoSumCountNumbersFlyoutItem.Click += (_, _) => InsertAutoSumFormula("COUNT");
                    _autoSumCountAllFlyoutItem.Click += (_, _) => InsertAutoSumFormula("COUNTA");
                    _autoSumMaxFlyoutItem.Click += (_, _) => InsertAutoSumFormula("MAX");
                    _autoSumMinFlyoutItem.Click += (_, _) => InsertAutoSumFormula("MIN");
                    _autoSumMenuItem.Header = "AutoSum";
                    _autoSumMenuItem.Menu = CreateNativeAutoSumMenu();
                    _autoSumSumMenuItem.Gesture = new KeyGesture(Key.OemPlus, KeyModifiers.Alt);
                    editMenu.Items.Add(_autoSumMenuItem);
                    _autoSumButton.IsEnabled = isIdle;
                    _autoSumMenuItem.IsEnabled = _autoSumButton.IsEnabled;
                    private MenuFlyout CreateAutoSumFlyout()
                    private NativeMenu CreateNativeAutoSumMenu()
                    private void InsertAutoSumFormula(string functionName)
                    _session.InsertAutoSumFormula(functionName)
                    private static bool IsAutoSumShortcut(KeyEventArgs args)
                    HasAutoSumButton: _autoSumButton.Content?.ToString() == "AutoSum"
                    HasNativeAutoSumMenuItem: HasNativeMenuItem(_autoSumMenuItem, "AutoSum", requireGesture: false)
                    _fillCellsButton.Content = "Fill Cells";
                    _fillCellsButton.Flyout = CreateFillCellsFlyout();
                    AutomationProperties.SetAutomationId(_fillCellsButton, "HomeFillCellsButton");
                    AutomationProperties.SetHelpText(_fillCellsButton, "Copy the edge cells across the selected range.");
                    _fillDownFlyoutItem.Header = "Down";
                    _fillDownFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Down);
                    _fillRightFlyoutItem.Header = "Right";
                    _fillRightFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Right);
                    _fillUpFlyoutItem.Header = "Up";
                    _fillUpFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Up);
                    _fillLeftFlyoutItem.Header = "Left";
                    _fillLeftFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Left);
                    _fillCellsMenuItem.Header = "Fill";
                    _fillCellsMenuItem.Menu = CreateNativeFillCellsMenu();
                    _fillDownMenuItem.Gesture = new KeyGesture(Key.D, KeyModifiers.Control);
                    _fillRightMenuItem.Gesture = new KeyGesture(Key.R, KeyModifiers.Control);
                    editMenu.Items.Add(_fillCellsMenuItem);
                    _fillDownFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Down);
                    _fillRightFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Right);
                    _fillUpFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Up);
                    _fillLeftFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Left);
                    _fillCellsMenuItem.IsEnabled = _fillCellsButton.IsEnabled;
                    _clearButton.Content = "Clear";
                    AutomationProperties.SetAutomationId(_clearButton, "HomeClearButton");
                    AutomationProperties.SetHelpText(_clearButton, "Clear contents, formatting, comments, hyperlinks, or all cell state from the selected range.");
                    _clearButton.Flyout = CreateClearFlyout();
                    _clearAllFlyoutItem.Header = "Clear All";
                    _clearFormatsFlyoutItem.Header = "Clear Formats";
                    _clearContentsFlyoutItem.Header = "Clear Contents";
                    _clearCommentsFlyoutItem.Header = "Clear Comments and Notes";
                    _clearHyperlinksFlyoutItem.Header = "Clear Hyperlinks";
                    _clearMenuItem.Header = "Clear";
                    _clearMenuItem.Menu = CreateNativeClearMenu();
                    _clearAllMenuItem.Header = "Clear All";
                    _clearAllMenuItem.Click += (_, _) => ClearSelectedRangeAll();
                    _clearFormatsMenuItem.Header = "Clear Formats";
                    _clearFormatsMenuItem.Click += (_, _) => ClearSelectedRangeFormats();
                    _clearContentsMenuItem.Header = "Clear Contents";
                    _clearContentsMenuItem.Click += (_, _) => ClearSelectedRangeContents();
                    _clearCommentsMenuItem.Header = "Clear Comments and Notes";
                    _clearCommentsMenuItem.Click += (_, _) => ClearSelectedRangeComments();
                    _clearHyperlinksMenuItem.Header = "Clear Hyperlinks";
                    _clearHyperlinksMenuItem.Click += (_, _) => ClearSelectedRangeHyperlinks();
                    editMenu.Items.Add(_clearMenuItem);
                    _clearButton.IsEnabled = isIdle;
                    _clearMenuItem.IsEnabled = _clearButton.IsEnabled;
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
                    DrawingObjectRenderPlanner.Plan(viewport);
                    CreateSelectableDrawingObjectVisual(renderPlan, width, height);
                    AutomationProperties.SetAutomationId(container, $"DrawingObject{drawingObject.Kind}{drawingObject.Id:N}");
                    AutomationProperties.SetHelpText(container, "Selects this drawing object preview in the workbook viewport.");
                    AutomationProperties.SetItemStatus(container, selected ? "Selected" : "Not selected");
                    container.PointerPressed += (_, args) => { };
                    if (args.Key is Key.Enter or Key.Space) { }
                    CreateSelectedDrawingObjectAdorner();
                    ClearSelectedDrawingObject();
                    CreateDrawingObjectVisual(renderPlan, width, height);
                    CreateDrawingCellRangeSnapshotVisual(renderPlan, width, height);
                    CreateDrawingImageSourceRect(crop);
                    TryCreateDrawingBitmap(imageBytes, out var bitmap);
                    AddStyledCellBorderOverlay(content, style);
                    private readonly RecentFilesStore _recentFiles = RecentFilesStore.Load();
                    _newWorkbookMenuItem.Click += (_, _) => CreateNewWorkbook();
                    _openRecentMenuItem.Header = "Open Recent";
                    _openRecentMenuItem.Menu = CreateNativeOpenRecentMenu(isIdle: true);
                    fileMenu.Items.Add(_openRecentMenuItem);
                    RefreshNativeOpenRecentMenu(isIdle);
                    LocalFilePath.TryNormalize(candidate, out var normalizedCandidate)
                    Directory.Exists(normalizedCandidate)
                    File.Exists(normalizedCandidate)
                    _session.TryResolveOpenTarget(normalizedCandidate, out var target, out unsupportedMessage)
                    path = target!.Path;
                    private readonly NativeMenuItem _workbookStatisticsMenuItem = new();
                    private readonly NativeMenuItem _exportPdfMenuItem = new();
                    _exportPdfMenuItem.Header = "Export to PDF...";
                    _exportPdfMenuItem.Click += async (_, _) => await ExportActiveSheetPdfAsync();
                    fileMenu.Items.Add(_exportPdfMenuItem);
                    _exportPdfMenuItem.IsEnabled = isIdle && StorageProvider.CanSave;
                    HasNativeExportPdfMenuItem: HasNativeMenuItem(_exportPdfMenuItem, "Export to PDF...", requireGesture: false)
                    private async Task ExportActiveSheetPdfAsync()
                    var exportPathPlan = ExportPathPlanner.Plan(requestedPath, ExportFileFormat.Pdf);
                    ExportPathPlanner.ShouldPromptForNormalizedOverwrite(requestedPath, exportPathPlan, File.Exists)
                    !await ConfirmNormalizedPdfOverwriteAsync(exportPathPlan.Path)
                    path = exportPathPlan.Path;
                    private async Task<bool> ConfirmNormalizedPdfOverwriteAsync(string normalizedPath)
                    IsCancel = true,
                    dialog.Opened += (_, _) => cancelButton.Focus();
                    AutomationProperties.SetAutomationId(replaceButton, "PdfExportOverwriteReplaceButton");
                    AutomationProperties.SetAutomationId(cancelButton, "PdfExportOverwriteCancelButton");
                    PortablePdfDocumentExporter.Save(_session.Workbook, exportPlan, path)
                    _workbookStatisticsMenuItem.Header = "Workbook Statistics...";
                    _workbookStatisticsMenuItem.Gesture = new KeyGesture(Key.G, KeyModifiers.Control | KeyModifiers.Shift);
                    _workbookStatisticsMenuItem.Click += async (_, _) => await ShowWorkbookStatisticsDialogAsync();
                    fileMenu.Items.Add(_workbookStatisticsMenuItem);
                    _workbookStatisticsMenuItem.IsEnabled = isIdle;
                    HasNativeWorkbookStatisticsMenuItem: HasNativeMenuItem(_workbookStatisticsMenuItem, "Workbook Statistics...")
                    e.Key == Key.G && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)
                    private async Task ShowWorkbookStatisticsDialogAsync()
                    WorkbookStatisticsService.GetStatistics(_session.Workbook)
                    AutomationProperties.SetAutomationId(dialog, "WorkbookStatisticsDialog");
                    AutomationProperties.SetAutomationId(okButton, "WorkbookStatisticsOkButton");
                    AutomationProperties.SetAutomationId(statisticsBlock, "WorkbookStatisticsSummary");
                    private static string FormatWorkbookStatistics(WorkbookStatistics statistics)
                    Cells with data: {statistics.CellCount}
                    Shapes and text boxes: {statistics.ShapeCount}
                    Named ranges: {statistics.NamedRangeCount}
                    _selectAllMenuItem.Header = "Select All";
                    _selectAllMenuItem.Gesture = new KeyGesture(Key.A, KeyModifiers.Meta);
                    _selectAllMenuItem.Click += (_, _) => SelectCurrentRegionOrAll();
                    editMenu.Items.Add(_selectAllMenuItem);
                    _selectAllMenuItem.IsEnabled = isIdle;
                    private readonly NativeMenuItem _findMenuItem = new();
                    private readonly NativeMenuItem _findNextMenuItem = new();
                    private readonly NativeMenuItem _replaceMenuItem = new();
                    private readonly NativeMenuItem _goToMenuItem = new();
                    private readonly NativeMenuItem _goToSpecialMenuItem = new();
                    private readonly NativeMenuItem _sortAscendingMenuItem = new();
                    private readonly NativeMenuItem _sortDescendingMenuItem = new();
                    private readonly NativeMenuItem _flashFillMenuItem = new();
                    private enum FindDialogAction
                    private sealed record FindDialogResult(
                        string FindText,
                        FindDialogAction Action,
                        FindOptions Options,
                        bool MatchCase,
                        bool MatchEntireCell);
                    private enum ReplaceDialogAction
                    private sealed record ReplaceDialogResult(
                        string FindText,
                        string ReplaceText,
                        ReplaceDialogAction Action,
                        FindOptions Options,
                        bool MatchCase,
                        bool MatchEntireCell,
                        StyleDiff? ReplacementFormat);
                    private sealed record FindOptionsControls(
                        ComboBox WithinBox,
                        ComboBox SearchBox,
                        ComboBox LookInBox,
                        CheckBox MatchCaseBox,
                        CheckBox MatchEntireCellBox,
                        Control Panel);
                    private sealed record GoToSpecialDialogResult(GoToSpecialKind Kind, GoToSpecialOptions Options);
                    private sealed record GoToSpecialChoice(GoToSpecialKind Kind, string Label)
                    _findMenuItem.Header = "Find...";
                    _findMenuItem.Gesture = new KeyGesture(Key.F, KeyModifiers.Meta);
                    _findMenuItem.Click += async (_, _) => await ShowFindDialogAsync();
                    _findNextMenuItem.Header = "Find Next";
                    _findNextMenuItem.Gesture = new KeyGesture(Key.G, KeyModifiers.Meta);
                    _findNextMenuItem.Click += (_, _) => FindNext();
                    _replaceMenuItem.Header = "Replace...";
                    _replaceMenuItem.Gesture = new KeyGesture(Key.H, KeyModifiers.Control);
                    _replaceMenuItem.Click += async (_, _) => await ShowReplaceDialogAsync();
                    _goToMenuItem.Header = "Go To...";
                    _goToMenuItem.Gesture = new KeyGesture(Key.G, KeyModifiers.Control);
                    _goToMenuItem.Click += async (_, _) => await ShowGoToDialogAsync();
                    _goToSpecialMenuItem.Header = "Go To Special...";
                    _goToSpecialMenuItem.Click += async (_, _) => await ShowGoToSpecialDialogAsync();
                    _sortAscendingMenuItem.Header = "Sort A to Z";
                    _sortAscendingMenuItem.Click += (_, _) => SortSelectedRange(ascending: true);
                    _sortDescendingMenuItem.Header = "Sort Z to A";
                    _sortDescendingMenuItem.Click += (_, _) => SortSelectedRange(ascending: false);
                    _flashFillMenuItem.Header = "Flash Fill";
                    _flashFillMenuItem.Gesture = new KeyGesture(Key.E, KeyModifiers.Control);
                    _flashFillMenuItem.Click += (_, _) => FlashFillSelectedRange();
                    var dataMenu = new NativeMenu();
                    dataMenu.Items.Add(_sortAscendingMenuItem);
                    dataMenu.Items.Add(_sortDescendingMenuItem);
                    dataMenu.Items.Add(_flashFillMenuItem);
                    Header = "Data",
                    Menu = dataMenu,
                    editMenu.Items.Add(_findMenuItem);
                    editMenu.Items.Add(_findNextMenuItem);
                    editMenu.Items.Add(_replaceMenuItem);
                    editMenu.Items.Add(_goToMenuItem);
                    editMenu.Items.Add(_goToSpecialMenuItem);
                    _findMenuItem.IsEnabled = isIdle;
                    _findNextMenuItem.IsEnabled = isIdle && !string.IsNullOrWhiteSpace(_session.LastFindText);
                    _replaceMenuItem.IsEnabled = isIdle;
                    _goToMenuItem.IsEnabled = isIdle;
                    _goToSpecialMenuItem.IsEnabled = isIdle;
                    _sortAscendingMenuItem.IsEnabled = isIdle && _session.CanSortSelectedRange;
                    _sortDescendingMenuItem.IsEnabled = isIdle && _session.CanSortSelectedRange;
                    _flashFillMenuItem.IsEnabled = isIdle;
                    e.Key == Key.E && HasOnlyControlModifier(e.KeyModifiers)
                    private void SortSelectedRange(bool ascending)
                    _session.SortSelectedRange(ascending)
                    private void FlashFillSelectedRange()
                    _session.FlashFillSelectedRange()
                    var hasNativeDataMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
                    string.Equals(item.Header?.ToString(), "Data", StringComparison.Ordinal)
                    HasNativeDataMenu: hasNativeDataMenu
                    HasNativeFindMenuItem: HasNativeMenuItem(_findMenuItem, "Find...");
                    HasNativeFindNextMenuItem: HasNativeMenuItem(_findNextMenuItem, "Find Next");
                    HasNativeReplaceMenuItem: HasNativeMenuItem(_replaceMenuItem, "Replace...");
                    HasNativeGoToMenuItem: HasNativeMenuItem(_goToMenuItem, "Go To...");
                    HasNativeSortAscendingMenuItem: HasNativeMenuItem(_sortAscendingMenuItem, "Sort A to Z", requireGesture: false)
                    HasNativeSortDescendingMenuItem: HasNativeMenuItem(_sortDescendingMenuItem, "Sort Z to A", requireGesture: false)
                    HasNativeFlashFillMenuItem: HasNativeMenuItem(_flashFillMenuItem, "Flash Fill")
                    HasNativeFormatCellsMenuItem: HasNativeMenuItem(_formatCellsMenuItem, "Format Cells...", requireGesture: false);
                    private readonly NativeMenuItem _advancedFilterMenuItem = new();
                    private readonly NativeMenuItem _removeDuplicatesMenuItem = new();
                    private readonly NativeMenuItem _subtotalMenuItem = new();
                    private readonly NativeMenuItem _dataValidationMenuItem = new();
                    private readonly NativeMenuItem _whatIfAnalysisMenuItem = new();
                    private readonly NativeMenuItem _goalSeekMenuItem = new();
                    private readonly NativeMenuItem _dataTableMenuItem = new();
                    private readonly NativeMenuItem _scenarioManagerMenuItem = new();
                    private readonly NativeMenuItem _forecastSheetMenuItem = new();
                    private readonly NativeMenuItem _reviewSummaryMenuItem = new();
                    private readonly NativeMenuItem _checkAccessibilityMenuItem = new();
                    private readonly NativeMenuItem _nextNoteMenuItem = new();
                    private readonly NativeMenuItem _previousNoteMenuItem = new();
                    private readonly NativeMenuItem _nextCommentMenuItem = new();
                    private readonly NativeMenuItem _previousCommentMenuItem = new();
                    _advancedFilterMenuItem.Header = "Advanced Filter...";
                    _advancedFilterMenuItem.Click += async (_, _) => await ShowAdvancedFilterDialogAsync();
                    _dataValidationMenuItem.Header = "Data Validation...";
                    _dataValidationMenuItem.Click += async (_, _) => await ShowDataValidationDialogAsync();
                    _whatIfAnalysisMenuItem.Header = "What-If Analysis";
                    _whatIfAnalysisMenuItem.Menu = CreateNativeWhatIfAnalysisMenu();
                    _goalSeekMenuItem.Header = "Goal Seek...";
                    _scenarioManagerMenuItem.Header = "Scenario Manager...";
                    _dataTableMenuItem.Header = "Data Table...";
                    _forecastSheetMenuItem.Header = "Forecast Sheet...";
                    _reviewSummaryMenuItem.Header = "Review Summary...";
                    _checkAccessibilityMenuItem.Header = "Check Accessibility...";
                    _nextNoteMenuItem.Header = "Next Note";
                    _previousNoteMenuItem.Header = "Previous Note";
                    _nextCommentMenuItem.Header = "Next Comment";
                    _previousCommentMenuItem.Header = "Previous Comment";
                    _removeDuplicatesMenuItem.Header = "Remove Duplicates...";
                    _removeDuplicatesMenuItem.Click += async (_, _) => await ShowRemoveDuplicatesDialogAsync();
                    _subtotalMenuItem.Header = "Subtotal...";
                    _subtotalMenuItem.Click += async (_, _) => await ShowSubtotalDialogAsync();
                    dataMenu.Items.Add(_advancedFilterMenuItem);
                    dataMenu.Items.Add(_removeDuplicatesMenuItem);
                    dataMenu.Items.Add(_subtotalMenuItem);
                    dataMenu.Items.Add(_dataValidationMenuItem);
                    dataMenu.Items.Add(_whatIfAnalysisMenuItem);
                    dataMenu.Items.Add(_forecastSheetMenuItem);
                    var reviewMenu = new NativeMenu();
                    reviewMenu.Items.Add(_reviewSummaryMenuItem);
                    reviewMenu.Items.Add(_checkAccessibilityMenuItem);
                    reviewMenu.Items.Add(_nextNoteMenuItem);
                    reviewMenu.Items.Add(_previousNoteMenuItem);
                    reviewMenu.Items.Add(_nextCommentMenuItem);
                    reviewMenu.Items.Add(_previousCommentMenuItem);
                    Header = "Review",
                    Menu = reviewMenu,
                    var hasNativeReviewMenu = _nativeMenu?.Items.OfType<NativeMenuItem>().Any(item =>
                    string.Equals(item.Header?.ToString(), "Review", StringComparison.Ordinal)
                    HasNativeReviewMenu: hasNativeReviewMenu
                    _advancedFilterMenuItem.IsEnabled = isIdle;
                    _removeDuplicatesMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1;
                    _subtotalMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1 && _session.SelectedRange.ColCount > 1;
                    _dataValidationMenuItem.IsEnabled = isIdle;
                    _whatIfAnalysisMenuItem.IsEnabled = isIdle;
                    _goalSeekMenuItem.IsEnabled = isIdle;
                    _dataTableMenuItem.IsEnabled = isIdle && _session.SelectedRange.RowCount > 1 && _session.SelectedRange.ColCount > 1;
                    _scenarioManagerMenuItem.IsEnabled = isIdle;
                    _forecastSheetMenuItem.IsEnabled = isIdle;
                    _reviewSummaryMenuItem.IsEnabled = isIdle;
                    _checkAccessibilityMenuItem.IsEnabled = isIdle;
                    _nextNoteMenuItem.IsEnabled = isIdle;
                    _previousNoteMenuItem.IsEnabled = isIdle;
                    _nextCommentMenuItem.IsEnabled = isIdle;
                    _previousCommentMenuItem.IsEnabled = isIdle;
                    private NativeMenu CreateNativeWhatIfAnalysisMenu()
                    menu.Items.Add(_goalSeekMenuItem);
                    menu.Items.Add(_scenarioManagerMenuItem);
                    menu.Items.Add(_dataTableMenuItem);
                    private async Task ShowSubtotalDialogAsync()
                    private async Task<SubtotalDialogResult?> ShowSubtotalInputDialogAsync()
                    _session.ExecuteSubtotalOptions(selection.Options!)
                    _session.RemoveSelectedRangeSubtotals()
                    new SubtotalInputOptions(
                    AutomationProperties.SetAutomationId(dialog, "SubtotalCompactDialog");
                    AutomationProperties.SetAutomationId(groupColumnBox, "SubtotalGroupColumnBox");
                    AutomationProperties.SetAutomationId(functionBox, "SubtotalFunctionBox");
                    AutomationProperties.SetAutomationId(columnsPanel, "SubtotalColumnsPanel");
                    AutomationProperties.SetAutomationId(removeAllButton, "SubtotalRemoveAllButton");
                    HasNativeAdvancedFilterMenuItem: HasNativeMenuItem(_advancedFilterMenuItem, "Advanced Filter...", requireGesture: false)
                    HasNativeRemoveDuplicatesMenuItem: HasNativeMenuItem(_removeDuplicatesMenuItem, "Remove Duplicates...", requireGesture: false)
                    HasNativeSubtotalMenuItem: HasNativeMenuItem(_subtotalMenuItem, "Subtotal...", requireGesture: false)
                    HasNativeDataValidationPreviewMenuItem: HasNativeMenuItem(_dataValidationPreviewMenuItem, "Data Validation Preview...", requireGesture: false)
                    HasNativeDataValidationMenuItem: HasNativeMenuItem(_dataValidationMenuItem, "Data Validation...", requireGesture: false)
                    HasNativeWhatIfAnalysisMenuItem: HasNativeMenuItem(_whatIfAnalysisMenuItem, "What-If Analysis", requireGesture: false)
                    HasNativeGoalSeekMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, "Goal Seek...")
                    HasNativeDataTableMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, "Data Table...")
                    HasNativeScenarioManagerMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, "Scenario Manager...")
                    HasNativeForecastSheetMenuItem: HasNativeMenuItem(_forecastSheetMenuItem, "Forecast Sheet...", requireGesture: false)
                    HasNativeReviewSummaryMenuItem: HasNativeMenuItem(_reviewSummaryMenuItem, "Review Summary...", requireGesture: false)
                    HasNativeCheckAccessibilityMenuItem: HasNativeMenuItem(_checkAccessibilityMenuItem, "Check Accessibility...", requireGesture: false)
                    HasNativeNextNoteMenuItem: HasNativeMenuItem(_nextNoteMenuItem, "Next Note", requireGesture: false)
                    HasNativePreviousNoteMenuItem: HasNativeMenuItem(_previousNoteMenuItem, "Previous Note", requireGesture: false)
                    HasNativeNextCommentMenuItem: HasNativeMenuItem(_nextCommentMenuItem, "Next Comment", requireGesture: false)
                    HasNativePreviousCommentMenuItem: HasNativeMenuItem(_previousCommentMenuItem, "Previous Comment", requireGesture: false)
                    private async Task ShowFindDialogAsync()
                    private async Task<FindDialogResult?> ShowFindInputDialogAsync(Action<FindDialogSmokeProbe>? launchSmokeProbe = null)
                    private async Task ShowFindAllResultsDialogAsync(string searchText, IReadOnlyList<WorkbookFindAllMatch> matches)
                    private void NavigateToFindAllMatch(WorkbookFindAllMatch match)
                    FindOptions? options = null,
                    private async Task ShowReplaceDialogAsync()
                    private async Task<ReplaceDialogResult?> ShowReplaceInputDialogAsync(Action<ReplaceDialogSmokeProbe>? launchSmokeProbe = null)
                    private async Task ShowGoToDialogAsync()
                    private async Task ShowGoToSpecialDialogAsync()
                    private async Task<GoToSpecialDialogResult?> ShowGoToSpecialInputDialogAsync(Action<GoToSpecialDialogSmokeProbe>? launchSmokeProbe = null)
                    private static GoToSpecialChoice[] CreateGoToSpecialChoices()
                    private bool SelectGoToSpecial(GoToSpecialKind kind, GoToSpecialOptions? options = null)
                    private async Task<string?> ShowSingleInputDialogAsync(
                    "FindTextBox"
                    "FindNextButton"
                    "FindAllButton"
                    CreateFindOptionsControls("Find", defaultLookInIndex: 0)
                    StyleDiff? findFormat = null;
                    CreateFindReplaceFormatButton("FindChooseFormatFromCellButton", "Choose From Cell")
                    CreateFindReplaceFormatButton("FindClearFormatButton", "Clear Format")
                    _session.CreateFormatDiffFromActiveCell()
                    CreateFindReplaceFormatRow("Find format", chooseFormatButton, clearFormatButton)
                    {automationPrefix}WithinBox
                    {automationPrefix}SearchBox
                    {automationPrefix}LookInBox
                    {automationPrefix}MatchCaseBox
                    {automationPrefix}MatchEntireCellBox
                    "FindAllResultsStatusText"
                    "FindAllResultsList"
                    "FindAllCloseButton"
                    "ReplaceFindTextBox"
                    "ReplaceWithTextBox"
                    "ReplaceButton"
                    "ReplaceAllButton"
                    CreateFindOptionsControls("Replace", defaultLookInIndex: 1)
                    StyleDiff? replacementFormat = null;
                    CreateFindReplaceFormatButton("ReplaceFindChooseFormatFromCellButton", "Choose From Cell")
                    CreateFindReplaceFormatButton("ReplaceFindClearFormatButton", "Clear Format")
                    CreateFindReplaceFormatButton("ReplaceWithChooseFormatFromCellButton", "Choose From Cell")
                    CreateFindReplaceFormatButton("ReplaceWithClearFormatButton", "Clear Format")
                    CreateFindReplaceFormatRow("Replace format", chooseReplaceFormatButton, clearReplaceFormatButton)
                    "GoToReferenceBox"
                    "GoToSpecialKindBox"
                    "GoToSpecialNumbersBox"
                    "GoToSpecialTextBox"
                    "GoToSpecialLogicalsBox"
                    "GoToSpecialErrorsBox"
                    "GoToSpecialOkButton"
                    private FindOptions CreateFindOptions(FindOptionsControls controls, StyleDiff? requiredFormat = null)
                    CreateFindOptions(optionsControls, findFormat)
                    RequiredFormat: requiredFormat);
                    private static FindOptionsControls CreateFindOptionsControls(string automationPrefix, int defaultLookInIndex)
                    private static Button CreateFindReplaceFormatButton(string automationId, string content)
                    private static StackPanel CreateFindReplaceFormatRow(string label, Button chooseButton, Button clearButton)
                    private static void UpdateFindReplaceFormatState(StyleDiff? format, Button chooseButton, Button clearButton)
                    FindLookIn.Formulas
                    FindLookIn.Notes
                    FindLookIn.Comments
                    var result = _session.FindNext(searchText, options, matchCase, matchEntireCell);
                    var result = _session.FindAll(search.FindText, search.Options, search.MatchCase, search.MatchEntireCell);
                    await ShowFindAllResultsDialogAsync(search.FindText, result.Matches);
                    var result = _session.GoToCell(match.Address);
                    replacement.Action == ReplaceDialogAction.ReplaceAll
                    replacement.Options,
                    replacement.MatchCase,
                    replacement.MatchEntireCell
                    replacement.ReplacementFormat
                    _session.ReplaceNextValue(
                    _session.ReplaceAllValues(
                    var result = _session.GoToReference(reference);
                    var result = _session.GoToSpecial(kind, options);
                    result.SelectedRanges.Count == 1
                    e.Key == Key.F5;
                    args.Key == Key.Oem1 && args.KeyModifiers == KeyModifiers.Alt;
                    SelectGoToSpecial(GoToSpecialKind.VisibleCellsOnly);
                    e.Key == Key.F && HasOnlyCommandModifier(e.KeyModifiers);
                    e.Key == Key.G && e.KeyModifiers == KeyModifiers.Meta;
                    e.Key == Key.H && HasOnlyControlModifier(e.KeyModifiers);
                    e.Key == Key.G && HasOnlyControlModifier(e.KeyModifiers);
                    e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A or Key.B or Key.D or Key.E or Key.I or Key.R or Key.U;
                    else if (e.Key == Key.A && HasOnlyCommandModifier(e.KeyModifiers)) { }
                    else if (e.Key == Key.D && HasOnlyControlModifier(e.KeyModifiers)) { }
                    else if (e.Key == Key.R && HasOnlyControlModifier(e.KeyModifiers)) { }
                    Header = "(No Recent Workbooks)";
                    OpenRecentWorkbookMenuPlanner.Create(
                    _recentFiles.Entries
                    File.Exists
                    path => _session.TryResolveOpenTarget(path, out var target, out _) ? target!.Path : null
                    plan.ItemCount == 0
                    foreach (var entry in plan.Items)
                    Header = entry.Header
                    await OpenWorkbookPathAsync(target.Path);
                    _recentFiles.AddOrUpdate(target.Path);
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
                    _minimizeWindowMenuItem.Gesture = new KeyGesture(Key.M, KeyModifiers.Meta);
                    _minimizeWindowMenuItem.Click += (_, _) => WindowState = WindowState.Minimized;
                    _zoomWindowMenuItem.Header = "Zoom";
                    _bringAllToFrontMenuItem.Header = "Bring All to Front";
                    var windowItem = new NativeMenuItem { Header = "Window" };
                    var item = new NativeMenuItem { Header = "Help" };
                    TopLevel.GetTopLevel(this)?.Launcher.ToString();
                    AppHelpInfo.BuildAboutText(versionText, PlatformAboutSummary);
                    LegalNoticeProvider.GetDocuments().Select(document => document.Title);
                    HasFocusableSheetTab: HasSheetTabButton(button => button.Focusable);
                    HasFocusableActiveSheetTab: FindSheetTabButton(_session.ActiveSheet.Id)?.Focusable == true;
                    HasShellFocusCycleTargets: _sheetGridHost.Focusable &&;
                    HasNativeWindowMenu: hasNativeWindowMenu;
                    HasNativeMinimizeWindowMenuItem: HasNativeMenuItem(_minimizeWindowMenuItem, "Minimize");
                    HasNativeZoomWindowMenuItem: HasNativeMenuItem(_zoomWindowMenuItem, "Zoom", requireGesture: false);
                    HasNativeBringAllToFrontMenuItem: HasNativeMenuItem(_bringAllToFrontMenuItem, "Bring All to Front", requireGesture: false);
                    GetToolbarFocusTargets().Any(control => control.Focusable) &&;
                    _formulaBox.Focusable &&;
                    _zoomText.Focusable;
                    HasFormatPainterButton: _formatPainterButton.Content?.ToString() == "Format Painter";
                    HasNativeFormatPainterMenuItem: HasNativeMenuItem(_formatPainterMenuItem, "Format Painter", requireGesture: false);
                    private readonly NativeMenuItem _formatCellsMenuItem = new();
                    _formatCellsMenuItem.Header = "Format Cells...";
                    _formatCellsMenuItem.Gesture = new KeyGesture(Key.D1, KeyModifiers.Meta);
                    _formatCellsMenuItem.Click += async (_, _) => await ShowFormatCellsDialogAsync();
                    formatMenu.Items.Add(_formatCellsMenuItem);
                    _formatCellsMenuItem.IsEnabled = isIdle;
                    Key.D1;
                    HasOnlyCommandModifier(e.KeyModifiers);
                    await ShowFormatCellsDialogAsync();
                    private async Task ShowFormatCellsDialogAsync()
                    FormatCellsCompactPlanner.TryPlan(selection.Request, out var diff, out var errorMessage);
                    _session.ApplySelectedRangeCompactFormat(
                        diff,
                        selection.BorderPreset,
                        selection.BorderStyle,
                        selection.BorderColor,
                        selection.Request.MergeCells);
                    "FormatCellsCompactDialog"
                    "FormatCellsNumberFormatBox"
                    "FormatCellsHorizontalAlignmentBox"
                    "FormatCellsVerticalAlignmentBox"
                    new("Justify", CellHAlign.Justify)
                    new("Distributed", CellHAlign.Distributed)
                    new("Justify", CellVAlign.Justify)
                    new("Distributed", CellVAlign.Distributed)
                    "FormatCellsWrapTextBox"
                    "FormatCellsMergeCellsBox"
                    "FormatCellsFontSizeBox"
                    "FormatCellsFontColorBox"
                    "FormatCellsFillColorBox"
                    "FormatCellsFillPatternStyleBox"
                    "FormatCellsFillPatternColorBox"
                    "FormatCellsBorderPresetBox"
                    "FormatCellsBorderStyleBox"
                    "FormatCellsBorderColorBox"
                    "FormatCellsDoubleUnderlineBox"
                    "FormatCellsShrinkToFitBox"
                    "FormatCellsIndentLevelBox"
                    "FormatCellsTextRotationBox"
                    "FormatCellsFontNameBox"
                    "FormatCellsNormalFontBox"
                    "FormatCellsSuperscriptBox"
                    "FormatCellsSubscriptBox"
                    "FormatCellsLockedBox"
                    "FormatCellsHiddenBox"
                    "FormatCellsProtectionExplanationText"
                    Locking cells or hiding formulas has no effect until you protect the worksheet.
                    var currentMergeCells = _session.IsSelectedRangeMerged;
                    MergeCells: ReadChangedFormatCellsBool(currentMergeCells, mergeCellsBox)
                    var normalStyle = CellStyle.Default;
                    Bold: normalFont ? normalStyle.Bold : ReadChangedFormatCellsBool(_session.IsSelectedRangeStartBold, boldBox)
                    FontName: normalFont ? normalStyle.FontName : ReadChangedFormatCellsText(currentFontName, fontNameBox)
                    FontColor: normalFont ? normalStyle.FontColor : (fontColorBox.SelectedItem as FormatCellsColorChoice)?.Color
                    SelectFormatCellsColor(fontColorBox, normal.FontColor)
                    FillPatternStyle: clearFill ? null : ReadChangedFormatCellsValue(currentFillPatternStyle, fillPatternStyleBox)
                    FillPatternColor: clearFill ? null : (fillPatternColorBox.SelectedItem as FormatCellsColorChoice)?.Color
                    CreateFormatCellsField("Pattern style", fillPatternStyleBox)
                    CreateFormatCellsField("Pattern color", fillPatternColorBox)
                    private static IReadOnlyList<FormatCellsNullableChoice<CellFillPatternStyle>> CreateFormatCellsFillPatternStyleChoices()
                    CellFillPatternStyle.DarkTrellis
                    HasFillCellsButton: _fillCellsButton.Content?.ToString() == "Fill Cells";
                    HasFillDownMenuItem: HasToolbarMenuItem(_fillDownFlyoutItem, "Down");
                    HasFillRightMenuItem: HasToolbarMenuItem(_fillRightFlyoutItem, "Right");
                    HasFillUpMenuItem: HasToolbarMenuItem(_fillUpFlyoutItem, "Up");
                    HasFillLeftMenuItem: HasToolbarMenuItem(_fillLeftFlyoutItem, "Left");
                    HasNativeFillCellsMenuItem: HasNativeMenuItem(_fillCellsMenuItem, "Fill", requireGesture: false);
                    HasNativeFillDownMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, "Down");
                    HasNativeFillRightMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, "Right");
                    HasNativeFillUpMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, "Up");
                    HasNativeFillLeftMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, "Left");
                    HasClearButton: _clearButton.Content?.ToString() == "Clear";
                    HasClearAllMenuItem: HasToolbarMenuItem(_clearAllFlyoutItem, "Clear All");
                    HasNativeClearMenuItem: HasNativeMenuItem(_clearMenuItem, "Clear", requireGesture: false);
                    HasNativeClearHyperlinksMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, "Clear Hyperlinks");
                    HasBordersButton: _bordersButton.Content?.ToString() == "Borders";
                    HasNativeBordersMenuItem: HasNativeMenuItem(_bordersMenuItem, "Borders", requireGesture: false);
                    NativeBordersPresetCount: nativeBordersPresetCount;
                    _mergeAndCenterButton.Content = "Merge & Center";
                    AutomationProperties.SetAutomationId(_mergeAndCenterButton, "HomeMergeAndCenterButton");
                    AutomationProperties.SetHelpText(_mergeAndCenterButton, "Merge and center the selected cells.");
                    _mergeAndCenterMenuItem.Header = "Merge & Center";
                    _mergeAndCenterMenuItem.Click += (_, _) => MergeAndCenterSelectedRange();
                    _unmergeCellsMenuItem.Header = "Unmerge Cells";
                    _unmergeCellsMenuItem.Click += (_, _) => UnmergeSelectedRange();
                    formatMenu.Items.Add(_mergeAndCenterMenuItem);
                    formatMenu.Items.Add(_unmergeCellsMenuItem);
                    _mergeAndCenterButton.IsEnabled = isIdle;
                    _mergeAndCenterMenuItem.IsEnabled = _mergeAndCenterButton.IsEnabled;
                    _unmergeCellsMenuItem.IsEnabled = isIdle && _session.IsSelectedRangeMerged;
                    HasMergeAndCenterButton: _mergeAndCenterButton.Content?.ToString() == "Merge & Center";
                    HasNativeMergeAndCenterMenuItem: HasNativeMenuItem(_mergeAndCenterMenuItem, "Merge & Center", requireGesture: false);
                    HasNativeUnmergeCellsMenuItem: HasNativeMenuItem(_unmergeCellsMenuItem, "Unmerge Cells", requireGesture: false);
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
                private void CaptureFormatPainterSource(bool persistent)
                {
                    _session.CaptureFormatPainterSource(persistent);
                }
                private void ApplyFormatPainterAfterTargetSelection()
                {
                    _session.ApplyFormatPainterToSelectedRange();
                }
                private void CancelFormatPainter()
                {
                    _session.CancelFormatPainter();
                }
                private MenuFlyout CreateFillCellsFlyout() => new();
                private NativeMenu CreateNativeFillCellsMenu() => new();
                private void FillSelectedRange(FillCellsDirection direction)
                {
                    var result = _session.FillSelectedRange(direction);
                    FormatFillCellsAction(direction);
                }
                private static string FormatFillCellsAction(FillCellsDirection direction) => "";
                private MenuFlyout CreateClearFlyout() => new();
                private NativeMenu CreateNativeClearMenu() => new();
                private void ClearSelectedRangeAll()
                {
                    _session.ClearSelectedRangeAll();
                }
                private void ClearSelectedRangeFormats()
                {
                    _session.ClearSelectedRangeFormats();
                }
                private void ClearSelectedRangeComments()
                {
                    _session.ClearSelectedRangeComments();
                }
                private void ClearSelectedRangeHyperlinks()
                {
                    _session.ClearSelectedRangeHyperlinks();
                }
                private void ApplySelectedRangeBorderPreset(CellBorderPreset preset)
                {
                    var result = _session.SetSelectedRangeBorderPreset(preset);
                }
                private void MergeAndCenterSelectedRange()
                {
                    var result = _session.MergeAndCenterSelectedRange();
                }
                private void UnmergeSelectedRange()
                {
                    var result = _session.UnmergeSelectedRange();
                }
                private static bool HasVisibleCellBorder(CellStyle? style) => true;
                private NativeMenu CreateNativeOpenRecentMenu(bool isIdle) => new();
                private void SelectCurrentRegionOrAll()
                {
                    var range = _session.SelectCurrentRegionOrAll();
                }
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
                public const string DiagnosticsDirectoryArgument = "--macos-launch-smoke-diagnostics-dir";
                public const string VerifyImageClipboardPasteArgument = "--macos-launch-smoke-verify-image-clipboard";
                public const string VerifyLiveCommandKeysArgument = "--macos-launch-smoke-verify-live-command-keys";
                public string? DiagnosticsDirectory { get; }
                public bool VerifyImageClipboardPaste { get; }
                public static void Parse(List<string> filteredArguments, out string[] startupArguments)
                {
                    var reportPath = "";
                    var diagnosticsDirectory = "";
                    diagnosticsDirectory = args[++index];
                    var verifyImageClipboardPaste = true;
                    var verifyLiveCommandKeys = true;
                    new MacOsLaunchSmokeOptions(
                        reportPath,
                        verifyImageClipboardPaste,
                        verifyLiveCommandKeys,
                        diagnosticsDirectory);
                    startupArguments = filteredArguments.ToArray();
                }

                public static void Start(MainWindow mainWindow, MacOsLaunchSmokeOptions options, AvaloniaAppDiagnostics? diagnostics = null)
                {
                    RunAsync(mainWindow, options, diagnostics);
                }

                private static void RunAsync(MainWindow mainWindow, MacOsLaunchSmokeOptions options, AvaloniaAppDiagnostics? diagnostics)
                {
                    diagnostics?.RecordEvent("macos_launch_smoke");
                    diagnostics?.RecordCrash(ex, "macos_launch_smoke");
                    var reportMarker = "app_diagnostics_directory_configured={FormatBool(appDiagnosticsConfigured)}";
                }
            }

            internal sealed class MacOsLaunchSmokeCommandKeySnapshot
            {
                private bool HasFindDirectRouteSourceGuard { get; }
                private bool HasPageUpDirectRouteSourceGuard { get; }
                private bool HasPageDownDirectRouteSourceGuard { get; }

                public bool IsPassed =>
                    HasFindDirectRouteSourceGuard &&
                    HasPageUpDirectRouteSourceGuard &&
                    HasPageDownDirectRouteSourceGuard;

                private static bool HasMainWindowDirectCommandRouteSourceSupport(params string[] requiredMethodNames) =>
                    requiredMethodNames.Length > 0;
            }

            internal sealed class MacOsLaunchSmokeSnapshot
            {
                public bool IsPassed =>
                    HasNativeFileMenu &&
                    HasNativeEditMenu &&
                    HasNativeDataMenu &&
                    HasNativeReviewMenu &&
                    HasNativeFormatMenu &&
                    HasNativeViewMenu &&
                    HasNativeSheetMenu &&
                    HasNativeWindowMenu &&
                    HasNativeHelpMenu &&
                    HasNativeNewWorkbookMenuItem &&
                    HasNativeOpenRecentMenuItem &&
                    NativeOpenRecentItemCount > 0 &&
                    HasNativeExportPdfMenuItem &&
                    HasNativeWorkbookStatisticsMenuItem &&
                    HasNativeSelectAllMenuItem &&
                    HasNativeFindMenuItem &&
                    HasNativeFindNextMenuItem &&
                    HasNativeReplaceMenuItem &&
                    HasNativeGoToMenuItem &&
                    HasNativeGoToSpecialMenuItem &&
                    HasNativeFlashFillMenuItem &&
                    HasNativeSortAscendingMenuItem &&
                    HasNativeSortDescendingMenuItem &&
                    HasNativeAdvancedFilterMenuItem &&
                    HasNativeRemoveDuplicatesMenuItem &&
                    HasNativeSubtotalMenuItem &&
                    HasNativeDataValidationPreviewMenuItem &&
                    HasNativeDataValidationMenuItem &&
                    HasNativeWhatIfAnalysisMenuItem &&
                    HasNativeGoalSeekMenuItem &&
                    HasNativeDataTableMenuItem &&
                    HasNativeScenarioManagerMenuItem &&
                    HasNativeForecastSheetMenuItem &&
                    HasNativeReviewSummaryMenuItem &&
                    HasNativeCheckAccessibilityMenuItem &&
                    HasNativeNextNoteMenuItem &&
                    HasNativePreviousNoteMenuItem &&
                    HasNativeNextCommentMenuItem &&
                    HasNativePreviousCommentMenuItem &&
                    HasNativeFormatCellsMenuItem &&
                    HasFormatCellsDialog &&
                    HasFormatCellsDialogTabStrip &&
                    HasFormatCellsDialogDefaultNumberTab &&
                    HasFormatCellsDialogNumberControls &&
                    HasFormatCellsDialogActionButtons &&
                    HasFormatCellsDialogCompactLayout &&
                    HasFormatCellsDialogClosedWithoutAccept &&
                    HasNativeCloseWorkbookMenuItem &&
                    HasNativeRenameSheetMenuItem &&
                    HasNativeMoveSheetLeftMenuItem &&
                    HasNativeMoveSheetRightMenuItem &&
                    HasNativeTabColorMenuItem &&
                    HasNativeClearTabColorMenuItem &&
                    NativeTabColorSwatchCount == CellColorPalettePlanner.BuildDefaultSwatches().Count &&
                    HasFormatPainterButton &&
                    HasAutoSumButton &&
                    HasAutoSumSumMenuItem &&
                    HasAutoSumAverageMenuItem &&
                    HasAutoSumCountNumbersMenuItem &&
                    HasAutoSumCountAllMenuItem &&
                    HasAutoSumMaxMenuItem &&
                    HasAutoSumMinMenuItem &&
                    HasFillCellsButton &&
                    HasFillDownMenuItem &&
                    HasFillRightMenuItem &&
                    HasFillUpMenuItem &&
                    HasFillLeftMenuItem &&
                    HasClearButton &&
                    HasClearAllMenuItem &&
                    HasClearFormatsMenuItem &&
                    HasClearContentsMenuItem &&
                    HasClearCommentsMenuItem &&
                    HasClearHyperlinksMenuItem &&
                    HasBordersButton &&
                    HasWrapTextButton &&
                    HasMergeAndCenterButton &&
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
                    HasNativeMinimizeWindowMenuItem &&
                    HasNativeZoomWindowMenuItem &&
                    HasNativeBringAllToFrontMenuItem &&
                    HasNativeFormatPainterMenuItem &&
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
                    HasNativeGoToSpecialMenuItem &&
                    HasNativeAutoSumMenuItem &&
                    HasNativeAutoSumSumMenuItem &&
                    HasNativeAutoSumAverageMenuItem &&
                    HasNativeAutoSumCountNumbersMenuItem &&
                    HasNativeAutoSumCountAllMenuItem &&
                    HasNativeAutoSumMaxMenuItem &&
                    HasNativeAutoSumMinMenuItem &&
                    HasNativeFillCellsMenuItem &&
                    HasNativeFillDownMenuItem &&
                    HasNativeFillRightMenuItem &&
                    HasNativeFillUpMenuItem &&
                    HasNativeFillLeftMenuItem &&
                    HasNativeClearMenuItem &&
                    HasNativeClearAllMenuItem &&
                    HasNativeClearFormatsMenuItem &&
                    HasNativeClearContentsMenuItem &&
                    HasNativeClearCommentsMenuItem &&
                    HasNativeClearHyperlinksMenuItem &&
                    HasNativeBordersMenuItem &&
                    NativeBordersPresetCount == Enum.GetValues<CellBorderPreset>().Length &&
                    HasNativeMergeAndCenterMenuItem &&
                    HasNativeUnmergeCellsMenuItem &&
                    HasNativeCellStylesMenuItem &&
                    HasNativeCopyMenuItem;
                private bool HasNativeFileMenu { get; }
                private bool HasNativeEditMenu { get; }
                private bool HasNativeDataMenu { get; }
                private bool HasNativeFormatMenu { get; }
                private bool HasNativeViewMenu { get; }
                private bool HasNativeSheetMenu { get; }
                private bool HasNativeWindowMenu { get; }
                private bool HasNativeHelpMenu { get; }
                private bool HasNativeNewWorkbookMenuItem { get; }
                private bool HasNativeOpenRecentMenuItem { get; }
                private int NativeOpenRecentItemCount { get; }
                private bool HasNativeExportPdfMenuItem { get; }
                private bool HasNativeWorkbookStatisticsMenuItem { get; }
                private bool HasNativeSelectAllMenuItem { get; }
                private bool HasNativeFindMenuItem { get; }
                private bool HasNativeFindNextMenuItem { get; }
                private bool HasNativeReplaceMenuItem { get; }
                private bool HasNativeGoToMenuItem { get; }
                private bool HasNativeGoToSpecialMenuItem { get; }
                private bool HasNativeFlashFillMenuItem { get; }
                private bool HasNativeSortAscendingMenuItem { get; }
                private bool HasNativeSortDescendingMenuItem { get; }
                private bool HasNativeFormatCellsMenuItem { get; }
                private bool HasFormatCellsDialog { get; }
                private bool HasFormatCellsDialogTabStrip { get; }
                private bool HasFormatCellsDialogDefaultNumberTab { get; }
                private bool HasFormatCellsDialogNumberControls { get; }
                private bool HasFormatCellsDialogActionButtons { get; }
                private bool HasFormatCellsDialogCompactLayout { get; }
                private bool HasFormatCellsDialogClosedWithoutAccept { get; }
                private bool HasNativeCloseWorkbookMenuItem { get; }
                private bool HasNativeRenameSheetMenuItem { get; }
                private bool HasNativeMoveSheetLeftMenuItem { get; }
                private bool HasNativeMoveSheetRightMenuItem { get; }
                private bool HasNativeTabColorMenuItem { get; }
                private bool HasNativeClearTabColorMenuItem { get; }
                private int NativeTabColorSwatchCount { get; }
                private bool HasFormatPainterButton { get; }
                private bool HasAutoSumButton { get; }
                private bool HasAutoSumSumMenuItem { get; }
                private bool HasAutoSumAverageMenuItem { get; }
                private bool HasAutoSumCountNumbersMenuItem { get; }
                private bool HasAutoSumCountAllMenuItem { get; }
                private bool HasAutoSumMaxMenuItem { get; }
                private bool HasAutoSumMinMenuItem { get; }
                private bool HasFillCellsButton { get; }
                private bool HasFillDownMenuItem { get; }
                private bool HasFillRightMenuItem { get; }
                private bool HasFillUpMenuItem { get; }
                private bool HasFillLeftMenuItem { get; }
                private bool HasClearButton { get; }
                private bool HasClearAllMenuItem { get; }
                private bool HasClearFormatsMenuItem { get; }
                private bool HasClearContentsMenuItem { get; }
                private bool HasClearCommentsMenuItem { get; }
                private bool HasClearHyperlinksMenuItem { get; }
                private bool HasBordersButton { get; }
                private bool HasWrapTextButton { get; }
                private bool HasMergeAndCenterButton { get; }
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
                private bool HasNativeFormatPainterMenuItem { get; }
                private bool HasNativeAutoSumMenuItem { get; }
                private bool HasNativeAutoSumSumMenuItem { get; }
                private bool HasNativeAutoSumAverageMenuItem { get; }
                private bool HasNativeAutoSumCountNumbersMenuItem { get; }
                private bool HasNativeAutoSumCountAllMenuItem { get; }
                private bool HasNativeAutoSumMaxMenuItem { get; }
                private bool HasNativeAutoSumMinMenuItem { get; }
                private bool HasNativeFillCellsMenuItem { get; }
                private bool HasNativeFillDownMenuItem { get; }
                private bool HasNativeFillRightMenuItem { get; }
                private bool HasNativeFillUpMenuItem { get; }
                private bool HasNativeFillLeftMenuItem { get; }
                private bool HasNativeClearMenuItem { get; }
                private bool HasNativeClearAllMenuItem { get; }
                private bool HasNativeClearFormatsMenuItem { get; }
                private bool HasNativeClearContentsMenuItem { get; }
                private bool HasNativeClearCommentsMenuItem { get; }
                private bool HasNativeClearHyperlinksMenuItem { get; }
                private bool HasNativeBordersMenuItem { get; }
                private bool HasNativeMergeAndCenterMenuItem { get; }
                private bool HasNativeUnmergeCellsMenuItem { get; }
                private bool HasNativeMinimizeWindowMenuItem { get; }
                private bool HasNativeZoomWindowMenuItem { get; }
                private bool HasNativeBringAllToFrontMenuItem { get; }
                public int ExternalImageClipboardPictureCount { get; }
                public int ExternalImageClipboardPicturePngByteCount { get; }
                public int NativeBordersPresetCount { get; }
                public int NativeCellStylesPresetCount { get; }
                public string DialogReport => "macos_dialog_smoke= macos_dialog_smoke_attempted= macos_dialog_smoke_status= macos_dialog_activation_completed= find_dialog= find_dialog_text_box= find_dialog_action_buttons= find_dialog_options= find_dialog_format_controls= find_dialog_compact_layout= find_dialog_result_closed_without_accept= replace_dialog= replace_dialog_text_boxes= replace_dialog_action_buttons= replace_dialog_options= replace_dialog_format_controls= replace_dialog_compact_layout= replace_dialog_result_closed_without_accept= go_to_dialog= go_to_dialog_reference_controls= go_to_dialog_compact_layout= go_to_dialog_result_closed_without_accept= go_to_special_dialog= go_to_special_dialog_kind_controls= go_to_special_dialog_value_type_controls= go_to_special_dialog_compact_layout= go_to_special_dialog_result_closed_without_accept= format_cells_dialog= format_cells_dialog_tab_strip= format_cells_dialog_default_number_tab= format_cells_dialog_number_controls= format_cells_dialog_action_buttons= format_cells_dialog_compact_layout= format_cells_dialog_result_closed_without_accept=";
                public string NewRouteReport => "native_flash_fill_menu_item= native_review_menu= native_advanced_filter_menu_item= native_remove_duplicates_menu_item= native_subtotal_menu_item= native_data_validation_preview_menu_item= native_data_validation_menu_item= native_what_if_analysis_menu_item= native_goal_seek_menu_item= native_data_table_menu_item= native_scenario_manager_menu_item= native_forecast_sheet_menu_item= native_review_summary_menu_item= native_check_accessibility_menu_item= native_next_note_menu_item= native_previous_note_menu_item= native_next_comment_menu_item= native_previous_comment_menu_item=";
                public string Report => "live_command_key_smoke_required= live_command_key_smoke= live_command_key_smoke_attempted= live_command_key_smoke_ready= cmd_find_direct_route_source_guard= cmd_page_up_direct_route_source_guard= cmd_page_down_direct_route_source_guard= live_cmd_select_all_received= live_cmd_select_all_state_changed= live_cmd_bold_received= live_cmd_bold_state_changed= live_cmd_italic_received= live_cmd_italic_state_changed= live_cmd_underline_received= live_cmd_underline_state_changed= external_image_clipboard_paste_required= external_image_clipboard_paste= external_image_clipboard_picture_count= external_image_clipboard_picture_png_bytes= native_new_workbook_menu_item= native_open_recent_menu_item= native_open_recent_item_count= native_export_pdf_menu_item= native_workbook_statistics_menu_item= native_close_workbook_menu_item= new_sheet_button= toolbar_format_painter_button= toolbar_autosum_button= toolbar_autosum_sum_menu_item= toolbar_autosum_average_menu_item= toolbar_autosum_count_numbers_menu_item= toolbar_autosum_count_all_menu_item= toolbar_autosum_max_menu_item= toolbar_autosum_min_menu_item= toolbar_fill_cells_button= toolbar_fill_down_menu_item= toolbar_fill_right_menu_item= toolbar_fill_up_menu_item= toolbar_fill_left_menu_item= toolbar_clear_button= toolbar_clear_all_menu_item= toolbar_clear_formats_menu_item= toolbar_clear_contents_menu_item= toolbar_clear_comments_menu_item= toolbar_clear_hyperlinks_menu_item= toolbar_borders_button= toolbar_wrap_text_button= toolbar_merge_and_center_button= focusable_sheet_tab= focusable_active_sheet_tab= shell_focus_cycle_targets= sheet_tab_context_keyboard_help= sheet_tab_context_rename_menu_item= sheet_tab_context_tab_color_menu_item= sheet_tab_context_no_color_menu_item= sheet_tab_context_select_all_sheets_menu_item= sheet_tab_context_ungroup_sheets_menu_item= native_data_menu= native_flash_fill_menu_item= native_remove_duplicates_menu_item= native_subtotal_menu_item= native_data_validation_preview_menu_item= native_view_menu= native_sheet_menu= native_window_menu= native_new_sheet_menu_item= native_rename_sheet_menu_item= native_duplicate_sheet_menu_item= native_move_sheet_left_menu_item= native_move_sheet_right_menu_item= native_tab_color_menu_item= native_tab_color_clear_item= native_tab_color_swatch_count= native_select_all_sheets_menu_item= native_ungroup_sheets_menu_item= native_hide_sheet_menu_item= native_unhide_sheet_menu_item= native_delete_sheet_menu_item= native_cut_menu_item= native_copy_menu_item= native_paste_special_menu_item= native_format_painter_menu_item= native_paste_special_comments_menu_item= native_paste_special_validation_menu_item= native_paste_special_all_except_borders_menu_item= native_paste_special_all_merging_conditional_formats_menu_item= native_paste_special_column_widths_menu_item= native_paste_special_formulas_and_number_formats_menu_item= native_paste_special_values_and_number_formats_menu_item= native_paste_special_values_and_source_formatting_menu_item= native_paste_special_keep_source_column_widths_menu_item= native_paste_special_paste_link_menu_item= native_paste_special_text_menu_item= native_paste_special_unicode_text_menu_item= native_paste_special_picture_menu_item= native_paste_special_linked_picture_menu_item= native_select_all_menu_item= native_find_menu_item= native_find_next_menu_item= native_replace_menu_item= native_go_to_menu_item= native_go_to_special_menu_item= native_sort_ascending_menu_item= native_sort_descending_menu_item= native_format_cells_menu_item= native_autosum_menu_item= native_autosum_sum_menu_item= native_autosum_average_menu_item= native_autosum_count_numbers_menu_item= native_autosum_count_all_menu_item= native_autosum_max_menu_item= native_autosum_min_menu_item= native_fill_cells_menu_item= native_fill_down_menu_item= native_fill_right_menu_item= native_fill_up_menu_item= native_fill_left_menu_item= native_clear_menu_item= native_clear_all_menu_item= native_clear_formats_menu_item= native_clear_contents_menu_item= native_clear_comments_menu_item= native_clear_hyperlinks_menu_item= native_bold_menu_item= native_fill_color_swatch_count= native_font_color_swatch_count= native_borders_menu_item= native_borders_preset_count= native_merge_and_center_menu_item= native_unmerge_cells_menu_item= native_cell_styles_menu_item= native_cell_styles_preset_count= native_horizontal_text_menu_item= native_angle_counterclockwise_menu_item= native_angle_clockwise_menu_item= native_vertical_text_menu_item= native_rotate_text_up_menu_item= native_rotate_text_down_menu_item= native_show_gridlines_menu_item= native_show_headings_menu_item= native_zoom_in_menu_item= native_zoom_out_menu_item= native_zoom_100_menu_item= native_zoom_to_selection_menu_item= native_freeze_panes_menu_item= native_freeze_top_row_menu_item= native_freeze_first_column_menu_item= native_unfreeze_panes_menu_item= native_show_formulas_menu_item= native_minimize_window_menu_item= native_zoom_window_menu_item= native_bring_all_to_front_menu_item= native_help_menu= native_help_online_menu_item= native_send_feedback_menu_item= native_check_for_updates_menu_item= native_about_menu_item= native_legal_notices_menu_item=";
            }

            internal sealed class MacOsLaunchSmokeCommandKeySnapshot
            {
                private bool HasFindDirectRouteSourceGuard { get; }
                private bool HasPageUpDirectRouteSourceGuard { get; }
                private bool HasPageDownDirectRouteSourceGuard { get; }

                public bool IsPassed =>
                    HasFindDirectRouteSourceGuard &&
                    HasPageUpDirectRouteSourceGuard &&
                    HasPageDownDirectRouteSourceGuard;
            }

            internal sealed class MacOsLaunchSmokeCoordinator
            {
                private static async Task RunAsync(MainWindow mainWindow, MacOsLaunchSmokeOptions options)
                {
                    var snapshot = mainWindow.CreateLaunchSmokeSnapshot();
                    var initialExternalImageClipboardPictureCount = snapshot.ExternalImageClipboardPictureCount;
                    var liveCommandKeyEvidence = mainWindow.BeginLaunchSmokeLiveCommandKeyProbe();
                    liveCommandKeyEvidence.IsPassed.ToString();
                    await mainWindow.TryPasteLaunchSmokeClipboardImageAsync();
                    IsPassed(snapshot, options, initialExternalImageClipboardPictureCount).ToString();
                    HasExternalImageClipboardPasteEvidence(snapshot, initialExternalImageClipboardPictureCount).ToString();
                }

                private static bool HasMainWindowDirectCommandRouteSourceSupport(params string[] requiredMarkers) => true;
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
            "src/FreeX.App.Services/CellMergePlanner.cs",
            """
            namespace FreeX.App.Services;

            public static class CellMergePlanner
            {
                public static bool IsSelectionMerged(Sheet sheet, GridRange range) =>
                    sheet.MergedRegions.Any(region => region.Overlaps(range));

                public static IReadOnlyList<IWorkbookCommand> CreateMergeAndCenterCommands(SheetId sheetId, GridRange range)
                {
                    new MergeCellsCommand(sheetId, range).ToString();
                    new ApplyStyleCommand(sheetId, range, new StyleDiff(HAlign: HorizontalAlignment.Center)).ToString();
                    return [];
                }

                public static IReadOnlyList<IWorkbookCommand> CreateMergeCommands(Sheet sheet, SheetId sheetId, GridRange range, bool mergeCells)
                {
                    new MergeCellsCommand(sheetId, range).ToString();
                    return CreateUnmergeCommands(sheet, sheetId, range);
                }

                public static IReadOnlyList<IWorkbookCommand> CreateUnmergeCommands(Sheet sheet, SheetId sheetId, GridRange range)
                {
                    new UnmergeCellsCommand(sheetId, region).ToString();
                    return [];
                }
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
            "src/FreeX.Core.Commands/FindReplaceService.cs",
            """
            namespace FreeX.Core.Commands;

            /*
            public enum FindResultTarget
            ThreadedCommentReply
            FindResultTarget Target = FindResultTarget.Cell,
            int? ReplyIndex = null);
            */
            """);

        WriteFile(
            root,
            "src/FreeX.Core.Commands/FindReplaceSearchPlanner.cs",
            """
            namespace FreeX.Core.Commands;

            /*
            public readonly record struct SearchText(
            comment.Replies[replyIndex].Text
            FindResultTarget.ThreadedCommentReply,
            */
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
                public bool IsFormatPainterActive =>
                public bool CaptureFormatPainterSource(bool persistent = false)
                public void CancelFormatPainter()
                public WorkbookCellEditResult ApplyFormatPainterToSelectedRange()
                CreateFormatPainterCommand(sourceSheet, sourceRange, targetRange)
                private IWorkbookCommand CreateFormatPainterCommand(Sheet sourceSheet, GridRange sourceRange, GridRange targetRange)
                FormatPainterCommandFactory.Create(
                public WorkbookCellEditResult ClearSelectedRangeAll()
                public WorkbookCellEditResult ClearSelectedRangeFormats()
                public WorkbookCellEditResult ClearSelectedRangeComments()
                public WorkbookCellEditResult ClearSelectedRangeHyperlinks()
                private IWorkbookCommand CreateClearAllCommand(GridRange range)
                new ClearContentsCommand(sheetId, sheetRange)
                CellStyleDiffPlanner.ClearFormatsDiff()
                new ClearConditionalFormatsCommand(sheetId, sheetRange)
                new ClearDataValidationCommand(sheetId, sheetRange)
                new ClearCommentsCommand(sheetId, sheetRange)
                new ClearHyperlinksCommand(sheetId, sheetRange)
                public WorkbookCellEditResult InsertAutoSumFormula(string functionName)
                AutoSumFormulaPlanner.BuildFormula(ActiveSheet, functionName, target)
                CreateEditCellsCommand([(target, Cell.FromFormula(formula))])
                SelectCell(GetNextAutoSumCell(target));
                public bool CanFillSelectedRange(FillCellsDirection direction)
                public WorkbookCellEditResult FillSelectedRange(FillCellsDirection direction)
                new FillCellsCommand(sheetId, sheetRange, direction)
                public WorkbookCellEditResult FlashFillSelectedRange()
                var plan = FlashFillRangePlanner.Plan(sheet, sheetRange);
                FlashFillRangePlanner.HasFillTargets(sheet, plan)
                commands.Add(plan.CreateCommand(sheetId));
                public WorkbookCellEditResult ExecuteSubtotalOptions(SubtotalInputOptions options)
                public WorkbookCellEditResult RemoveSelectedRangeSubtotals()
                new SubtotalCommand(
                new RemoveSubtotalRowsCommand(sheetId, sheetRange)
                private static string GetFillCellsTitle(FillCellsDirection direction)
                FillCellsDirection.Down => "Fill Down"
                FillCellsDirection.Right => "Fill Right"
                FillCellsDirection.Up => "Fill Up"
                FillCellsDirection.Left => "Fill Left"
                public bool CanSortSelectedRange => SelectedRange.RowCount > 1;
                public WorkbookCellEditResult SortSelectedRange(bool ascending)
                new SortCommand(sheetId, sheetRange, sortByColOffset: 0, ascending)
                "Select at least two rows to sort."
                public WorkbookCellEditResult SetSelectedRangeBorderPreset(CellBorderPreset preset)
                CreateBorderPresetCommand(range, preset)
                CellBorderPresetPlanner.Plan(preset, range, range.Start, borderStyle, borderColor)
                CellBorderPresetPlanner.RequiresPerCellPlanning(preset)
                BorderShortcutService.HasBorderChanges(diff)
                GroupedApplyStyleCommand(targetSheetIds, sourceRange, diff)
                public WorkbookCellEditResult ApplySelectedRangeCompactFormat(
                    bool? mergeCells = null)
                CreateFormatCellsMergeCommands(range, shouldMerge)
                public bool IsSelectedRangeMerged => CellMergePlanner.IsSelectionMerged(ActiveSheet, SelectedRange);
                public WorkbookCellEditResult MergeAndCenterSelectedRange()
                CreateMergeAndCenterCommand(range)
                public WorkbookCellEditResult UnmergeSelectedRange()
                CreateUnmergeCommands(range)
                private IWorkbookCommand CreateMergeAndCenterCommand(GridRange range)
                CellMergePlanner.CreateMergeAndCenterCommands(sheetId, sheetRange)
                private IReadOnlyList<IWorkbookCommand> CreateFormatCellsMergeCommands(GridRange range, bool mergeCells)
                CellMergePlanner.CreateMergeCommands(
                private IReadOnlyList<IWorkbookCommand> CreateUnmergeCommands(GridRange range)
                CellMergePlanner.CreateUnmergeCommands(sheet, sheetId, RemapRangeToSheet(range, sheetId))
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
                public string LastFindText => _lastFindText ??
                public StyleDiff? CreateFormatDiffFromActiveCell()
                public StyleDiff? CreateFormatDiffFromCell(CellAddress address)
                public IReadOnlyList<GridRange> SelectedRanges { get; private set; } = [];
                public WorkbookFindAllResult FindAll(
                return WorkbookFindAllResult.Found(results.Select(CreateFindAllMatch).ToList());
                private WorkbookFindAllMatch CreateFindAllMatch(FindResult result)
                private string FindNameForAddress(CellAddress address)
                public WorkbookReplaceResult ReplaceAllValues(
                public WorkbookReplaceResult ReplaceNextValue(
                FindOptions? options,
                StyleDiff? replacementFormat = null
                replacementFormat is not null
                new GridRange(edit.Address, edit.Address)
                private static bool TryCreateReplacementCommand(
                new CompositeWorkbookCommand(
                new ApplyStyleCommand(
                new GridRange(match.Address, match.Address)
                var effectiveOptions = ResolveFindOptions(options, FindLookIn.Values);
                GetReplaceTargetIndex(matches, effectiveOptions.SearchOrder, sameSearch)
                commands.Add(new EditCellsCommand(sheetId, edits));
                var editCommand = new EditCellsCommand(sheet.Id, [(match.Address, newCell)]);
                effectiveOptions.LookIn,
                FindLookIn.Formulas => cell.FormulaText
                FindLookIn.Values => cell.HasFormula ? null : GetReplaceableDisplayText(cell.Value)
                newCell = cell.Clone();
                FindLookIn.Notes when
                match.Target == FindResultTarget.Note
                sheet.Comments.TryGetValue(match.Address, out var note) => note
                new SetCommentCommand(
                new UpdateThreadedCommentTextCommand(
                match.Target == FindResultTarget.ThreadedCommentReply
                match.ReplyIndex is { } replyIndex
                new UpdateThreadedCommentReplyCommand(
                private static bool IsValidThreadedCommentReplyIndex(ThreadedComment comment, int replyIndex)
                return WorkbookReplaceResult.Replaced(1, replacedRange, index + 1, matches.Count);
                public WorkbookNavigationResult GoToReference(string reference)
                public WorkbookGoToSpecialResult GoToSpecial(GoToSpecialKind kind, GoToSpecialOptions? options = null)
                GoToSpecialService.Find(Workbook, ActiveSheet, SelectedRange, kind, ActiveCell, options)
                SelectionRangeService.CompressAddresses(matches)
                SelectRanges(selectedRange, ranges);
                WorkbookReferenceNavigator.TryParseReferenceRange(
                public WorkbookNavigationResult FindNext(
                FindReplaceService.Find(Workbook, text, effectiveOptions, matchCase, matchEntireCell)
                return WorkbookNavigationResult.Found(
                private WorkbookNavigationResult NavigateToRange(GridRange range)
                SelectSheet(range.Start.Sheet);
                private int GetNextFindResultIndex(
                private int CompareFindOrder(CellAddress left, CellAddress right, FindSearchOrder searchOrder)
                private SheetId? ResolveSheetIdByName(string sheetName)
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
            "src/FreeX.App.Services/FlashFillRangePlanner.cs",
            """
            namespace FreeX.App.Services;

            public readonly record struct FlashFillCommandPlan(
                uint FillColumn,
                uint SourceColumn,
                uint StartRow,
                uint EndRow)
            {
                public FlashFillCommand CreateCommand(SheetId sheetId) =>
                    new FlashFillCommand(sheetId, FillColumn, SourceColumn, StartRow, EndRow);
            }

            public static class FlashFillRangePlanner
            {
                public static bool HasFillTargets(Sheet sheet, FlashFillCommandPlan plan) => true;
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/WorkbookReferenceNavigator.cs",
            """
            namespace FreeX.App.Services;

            public static class WorkbookReferenceNavigator
            {
                /*
                public static bool TryParseAddress(string text, SheetId sheetId, out CellAddress address)
                public static IReadOnlyList<string> BuildReferenceChoices(
                public static bool TryParseReference(
                public static bool TryParseReferenceRange(
                Func<string, SheetId?> resolveSheetId
                private static bool TryResolveReferenceSheet(
                private static string? NormalizeAbsoluteA1Reference(string input)
                private static bool TryParseAbsoluteR1C1CellReference(string input, SheetId sheetId, out CellAddress address)
                */
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/WorkbookShareActionPlanner.cs",
            """
            namespace FreeX.App.Services;

            public enum WorkbookShareActionPlanKind
            {
                ShareSheet,
                OpenContainingFolder,
                SaveAsBeforeShare,
                Deferred
            }

            public enum WorkbookShareActionUnavailableReason
            {
                None,
                ShareSheetUnavailable,
                ContainingFolderUnavailable
            }

            public sealed record WorkbookShareActionSurface(
                string ShareSheetLabel,
                bool CanShowShareSheet,
                bool CanOpenContainingFolder = false,
                string OpenContainingFolderLabel = "Open Containing Folder")
            {
                public static WorkbookShareActionSurface MacOsPreview { get; } =
                    new("macOS Share Sheet", CanShowShareSheet: false);
            }

            public sealed record WorkbookShareActionPlan(
                WorkbookShareActionPlanKind Kind,
                string? Path,
                string? ContainingFolderPath = null,
                WorkbookShareReadinessSaveAsReason SaveAsReason = WorkbookShareReadinessSaveAsReason.None,
                string? CandidatePath = null,
                WorkbookShareActionUnavailableReason UnavailableReason = WorkbookShareActionUnavailableReason.None,
                WorkbookShareActionSurface? Surface = null);

            public static class WorkbookShareActionPlanner
            {
                public static WorkbookShareActionPlan CreatePlan(
                    string? currentFilePath,
                    Func<string, bool>? fileExists = null) =>
                    CreatePlan(currentFilePath, WorkbookShareActionSurface.MacOsPreview, fileExists);

                public static WorkbookShareActionPlan CreatePlan(
                    string? currentFilePath,
                    WorkbookShareActionSurface surface,
                    Func<string, bool>? fileExists = null)
                {
                    var readiness = WorkbookShareReadinessPlanner.CreatePlan(
                        currentFilePath,
                        new WorkbookShareSurface(surface.ShareSheetLabel),
                        fileExists);
                    var hasNativeAction = surface.CanShowShareSheet || surface.CanOpenContainingFolder;
                    if (readiness.Kind != WorkbookShareReadinessPlanKind.ShareExistingFile)
                        return new WorkbookShareActionPlan(
                            hasNativeAction ? WorkbookShareActionPlanKind.SaveAsBeforeShare : WorkbookShareActionPlanKind.Deferred,
                            null);

                    if (surface.CanOpenContainingFolder &&
                        TryGetContainingFolderPath(readiness.Path, out var containingFolderPath))
                        return new WorkbookShareActionPlan(
                            WorkbookShareActionPlanKind.OpenContainingFolder,
                            readiness.Path,
                            containingFolderPath,
                            UnavailableReason: WorkbookShareActionUnavailableReason.ShareSheetUnavailable,
                            Surface: surface);

                    return new WorkbookShareActionPlan(WorkbookShareActionPlanKind.ShareSheet, readiness.Path);
                }

                private static bool TryGetContainingFolderPath(string? filePath, out string containingFolderPath)
                {
                    containingFolderPath = "";
                    return false;
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/LocalFilePath.cs",
            """
            namespace FreeX.App.Services;

            public static class LocalFilePath
            {
                public static bool TryNormalize(string? candidate, out string normalizedPath)
                {
                    normalizedPath = "";
                    var path = candidate!.Trim();
                    if (TryCreateExplicitUri(path, out var uri))
                    {
                        if (!uri.IsFile)
                            return false;

                        path = uri.LocalPath;
                    }

                    path.Contains('\0', StringComparison.Ordinal);
                    IsUnixAbsolutePath(path);
                    Path.GetFullPath(path);
                    return true;
                }

                private static bool TryCreateExplicitUri(string candidate, out Uri uri)
                {
                    Uri.TryCreate(candidate, UriKind.Absolute, out var parsed);
                    IsWindowsDrivePath(candidate, parsed.Scheme);
                    uri = parsed;
                    return true;
                }

                private static bool IsWindowsDrivePath(string candidate, string scheme) =>
                    char.IsAsciiLetter(candidate[0]);

                private static bool IsUnixAbsolutePath(string path) => true;
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/OpenRecentWorkbookMenuPlanner.cs",
            """
            namespace FreeX.App.Services;

            public sealed record OpenRecentWorkbookMenuItemPlan(
                string Path,
                string Header,
                DateTimeOffset LastOpened);

            public sealed record OpenRecentWorkbookMenuPlan(IReadOnlyList<OpenRecentWorkbookMenuItemPlan> Items)
            {
                public int ItemCount => Items.Count;
            }

            public static class OpenRecentWorkbookMenuPlanner
            {
                public const int DefaultMaximumItems = 10;

                public static OpenRecentWorkbookMenuPlan Create(
                    IEnumerable<RecentFileEntry> entries,
                    Func<string, bool> fileExists,
                    Func<string, bool> canOpenWorkbook,
                    int maximumItems = DefaultMaximumItems)
                {
                    return Create(
                        entries,
                        fileExists,
                        path => canOpenWorkbook(path) ? path : null,
                        maximumItems);
                }

                public static OpenRecentWorkbookMenuPlan Create(
                    IEnumerable<RecentFileEntry> entries,
                    Func<string, bool> fileExists,
                    Func<string, string?> resolveOpenWorkbookPath,
                    int maximumItems = DefaultMaximumItems)
                {
                    if (maximumItems < 1)
                        return new OpenRecentWorkbookMenuPlan([]);

                    var seenPaths = new HashSet<string>(PlatformPathIdentityComparer.Current);
                    return new OpenRecentWorkbookMenuPlan(
                        entries
                            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
                            .OrderByDescending(entry => entry.LastOpened)
                            .Select(entry => (Entry: entry, Path: resolveOpenWorkbookPath(entry.Path)))
                            .Where(item => !string.IsNullOrWhiteSpace(item.Path) && fileExists(item.Path))
                            .Where(item => seenPaths.Add(item.Path!))
                            .Take(maximumItems)
                            .Select(item => new OpenRecentWorkbookMenuItemPlan(
                                item.Path!,
                                FormatHeader(item.Path!),
                                item.Entry.LastOpened))
                            .ToList());
                }

                public static string FormatHeader(string path)
                {
                    Path.GetFileName(path);
                    Path.GetDirectoryName(path);
                    return path;
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/WorkbookViewportScrollPlanner.cs",
            """
            namespace FreeX.App.Services;

            public readonly record struct WorkbookViewportScrollAxis(
                double Minimum,
                double Maximum,
                double Value,
                double ViewportSize,
                double SmallChange,
                double LargeChange,
                bool IsEnabled);

            public readonly record struct WorkbookViewportScrollState(
                WorkbookViewportScrollAxis Vertical,
                WorkbookViewportScrollAxis Horizontal);

            public static class WorkbookViewportScrollPlanner
            {
                private const double MinimumScrollValue = 1;

                public static WorkbookViewportScrollState Create(Sheet sheet, ViewportModel viewport)
                {
                    CountScrollableRows(viewport.RowMetrics, sheet.FrozenRows);
                    CountScrollableColumns(viewport.ColMetrics, sheet.FrozenCols);
                    return default;
                }

                public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(
                    Sheet sheet,
                    double verticalScrollValue,
                    double horizontalScrollValue) =>
                    (
                        ScrollbarValueToWorksheetIndex(verticalScrollValue, sheet.FrozenRows, CellAddress.MaxRow),
                        ScrollbarValueToWorksheetIndex(horizontalScrollValue, sheet.FrozenCols, CellAddress.MaxCol));

                public static uint ScrollbarValueToWorksheetIndex(double value, uint frozenCount, uint limit) => 1;
                public static uint WorksheetIndexToScrollbarValue(uint worksheetIndex, uint frozenCount) => 1;
                public static uint CalculateScrollableLimit(uint absoluteLimit, uint frozenCount) => 1;
                public static uint CalculateMaximumViewportOrigin(uint absoluteLimit, uint visibleSpan) => 1;

                private static WorkbookViewportScrollAxis CreateAxis(uint visibleSpan, double maximum) =>
                    new(1, maximum, 1, visibleSpan, SmallChange: 1, LargeChange: 1, IsEnabled: maximum > MinimumScrollValue);

                private static uint CountScrollableRows(IReadOnlyList<RowMetric> rows, uint frozenRows) => 1;
                private static uint CountScrollableColumns(IReadOnlyList<ColMetric> columns, uint frozenColumns) => 1;
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/FormatCellsCompactPlanner.cs",
            """
            namespace FreeX.App.Services;

            public sealed record FormatCellsCompactRequest(
                CellColor? FillColor = null,
                bool ClearFill = false,
                bool? MergeCells = null,
                bool? DoubleUnderline = null,
                bool? ShrinkToFit = null,
                int? IndentLevel = null,
                int? TextRotation = null,
                string? FontName = null,
                bool? Superscript = null,
                bool? Subscript = null,
                bool? Locked = null,
                bool? Hidden = null,
                CellFillPatternStyle? FillPatternStyle = null,
                CellColor? FillPatternColor = null);

            public static class FormatCellsCompactPlanner
            {
                public static StyleDiff Plan(FormatCellsCompactRequest request) =>
                    new(
                        DoubleUnderline: request.DoubleUnderline,
                        ShrinkToFit: request.ShrinkToFit,
                        IndentLevel: NormalizeIndentLevel(request.IndentLevel),
                        TextRotation: NormalizeTextRotation(request.TextRotation),
                        FontName: NormalizeFontName(request.FontName),
                        Superscript: request.Superscript,
                        Subscript: request.Subscript,
                        Locked: request.Locked,
                        Hidden: request.Hidden,
                        FillPatternStyle: request.ClearFill ? null : request.FillPatternStyle,
                        FillPatternColor: request.ClearFill ? null : request.FillPatternColor);
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
                    ApplyFormatCellsStartupSmokeStyle();
                    VerifyFormatCellsStartupSmokeStyle();
                    var result = $"Packaging smoke opened; drawing_object_previews={drawingObjectPreviewCount}; edited, saved, and reopened after applying compact Format Cells style to B2; format_cells_style_roundtrip=true; roundtrip_drawing_object_previews={roundTripDrawingObjectPreviewCount}.";
                }

                private void ApplyFormatCellsStartupSmokeStyle()
                {
                }

                private void VerifyFormatCellsStartupSmokeStyle()
                {
                }
            }

            public static class PackagingSmokeCommand
            {
                public const string Argument = "--packaging-smoke";
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/PortablePdfDocumentExporter.cs",
            """
            namespace FreeX.App.Services;

            public static class PortablePdfDocumentExporter
            {
                private static void Export()
                {
                    PortablePdfPageContentPlanner.CreatePlan(workbook, request);
                    "/Encoding /WinAnsiEncoding".ToString();
                    EncodeWinAnsiHexText(normalized);
                    _ = "built-in Helvetica/WinAnsi set";
                }

                private static byte EncodeWinAnsiByte(char ch) => 0;
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

    private static string FormatWorkflowRuntimeEntry(string runtime)
    {
        return string.IsNullOrWhiteSpace(runtime)
            ? ""
            : $"- runtime: {runtime}{Environment.NewLine}                        runner: macos-15";
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
