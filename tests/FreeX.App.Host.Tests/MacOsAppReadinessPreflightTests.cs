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
        script.Should().Contain("EnableMacOsTargetFramework");
        script.Should().Contain("net10.0-macos");
        script.Should().Contain("SupportedOSPlatformVersion");
        script.Should().Contain("MacOs\\**\\*.cs");
        script.Should().Contain("FREEX_MACOS_SHARE_SHEET");
        script.Should().Contain("Avalonia app RuntimeIdentifiers");
        script.Should().Contain("ApplicationTitle");
        script.Should().Contain("CFBundleName");
        script.Should().Contain("Name = ApplicationTitle;");
        script.Should().Contain("NativeDock.SetMenu(app, menu);");
        script.Should().Contain("Path = \"tools\\FreeX.Validation.Avalonia\\RendererHost\\MainWindow.RendererValidationAccess.cs\"");
        script.Should().Contain("internal NativeMenu? NativeDockMenu =>");
        script.Should().Contain("NativeDock.GetMenu(app)");
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
        script.Should().Contain("artifact_bundle_metadata_subject=unzipped_app_bundle");
        script.Should().Contain("bundle_identifier=$(/usr/libexec/PlistBuddy -c ''Print :CFBundleIdentifier'' \"$app_info_plist\")");
        script.Should().Contain("bundle_package_type=$(/usr/libexec/PlistBuddy -c ''Print :CFBundlePackageType'' \"$app_info_plist\")");
        script.Should().Contain("bundle_minimum_system_version=$(/usr/libexec/PlistBuddy -c ''Print :LSMinimumSystemVersion'' \"$app_info_plist\")");
        script.Should().Contain("bundle_high_resolution_capable=$(/usr/libexec/PlistBuddy -c ''Print :NSHighResolutionCapable'' \"$app_info_plist\")");
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
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AppDiagnosticsFileStoreTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AppServicesPortabilityGuardTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaProjectPortabilityGuardTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.ApplicationDataPathGuardTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AppStoragePathPlannerTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AppOptionsStoreTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AtomicFileWriterTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaShellSourceTests");
        script.Should().Contain("FullyQualifiedName~FreeX.App.Services.Tests.MacOsLaunchSmokeReportKeyDriftGuardTests");
        script.Should().Contain("LocalAppDiagnostics.Create(");
        script.Should().Contain("Path = \"shared\\Free.Shared.AppServices\\LocalAppDiagnostics.cs\"");
        script.Should().Contain("string.IsNullOrWhiteSpace(diagnosticsDirectory)");
        script.Should().Contain("? defaults.DiagnosticsDirectory");
        script.Should().Contain(": diagnosticsDirectory,");
        script.Should().Contain("Path = \"shared\\Free.Shared.AppServices\\AppCrashHandlers.cs\"");
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
        script.Should().Contain("smoke_status=passed");
        script.Should().Contain("Assert-ContainsRequiredText -Text $smokeReportText -Needle \"macos_launch_smoke=passed\"");
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
        script.Should().Contain("native_borders_preset_count=14");
        script.Should().Contain("native_merge_and_center_menu_item=true");
        script.Should().Contain("native_unmerge_cells_menu_item=true");
        script.Should().Contain("native_cell_styles_menu_item=true");
        script.Should().Contain("native_cell_styles_preset_count=33");
        script.Should().Contain("launchservices_smoke_timeout_seconds=60");
        script.Should().Contain("Run-PackagedProductLaunchProbe.sh");
        script.Should().Contain("--executable \"$unzip_root/FreeX.app/Contents/MacOS/FreeX\"");
        script.Should().Contain("macOS workflow must exercise the executable inside the extracted app bundle before recording smoke_status=passed.");
        script.Should().Contain("launchservices_cleanup_timeout_seconds=10");
        script.Should().Contain("run_bounded_launchservices_smoke \"bundle_id\" \"$launch_smoke_report\"");
        script.Should().Contain("run_bounded_launchservices_smoke \"open_with\" \"$open_with_report\"");
        script.Should().Contain("run_bounded_launchservices_smoke \"default_open\" \"$default_open_report\"");
        script.Should().Contain("launchservices_smoke_cleanup_timeout=true");
        script.Should().Contain("macOS workflow must route all three hosted LaunchServices launch smoke paths through run_bounded_launchservices_smoke.");
        script.Should().Contain("Require hosted smoke before app artifact upload");
        script.Should().Contain("smoke_status=skipped_host_arch_mismatch");
        script.Should().Contain("app_artifact_upload_blocked=host_arch_mismatch");
        script.Should().Contain("Host/runtime architecture mismatch for $runtime on $host_arch cannot publish a macOS app artifact.");
        script.Should().Contain("macOS workflow must require successful hosted smoke before uploading the app artifact.");
        script.Should().Contain("open_with_report=\"$artifact_root/freex-$runtime-macos-open-with-launch-smoke.txt\"");
        script.Should().Contain("open_with_smoke_file=\"$RUNNER_TEMP/freex-$runtime-open-with.csv\"");
        script.Should().Contain("app_path=\"$unzip_root/FreeX.app\"");
        script.Should().Contain("run_launchservices_with_validation \"$open_with_report\" \"$open_with_smoke_file\"");
        script.Should().Contain("open -W -n -a \"$app_path\" \"$open_with_smoke_file\"");
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
        script.Should().Contain("run_launchservices_with_validation \"$default_open_report\" \"$default_open_smoke_file\"");
        script.Should().Contain("open -W -n \"$default_open_smoke_file\"");
        script.Should().Contain("opened_source_path=.*freex-$runtime-default-open.fxl");
        script.Should().Contain("launchservices_default_open_app_override=false");
        script.Should().Contain("launchservices_default_open_document_extension=fxl");
        script.Should().Contain("src\\FreeX.App.Services\\PortablePdfDocumentExporter.cs");
        script.Should().Contain("shared\\Free.Shared.AppServices\\WorkbookShareActionPlanner.cs");
        script.Should().Contain("public static WorkbookShareActionSurface MacOsPreview");
        script.Should().Contain("surface.CanShowShareSheet || surface.CanOpenContainingFolder");
        script.Should().Contain("src\\FreeX.App.Services\\WorkbookViewportScrollPlanner.cs");
        script.Should().Contain("public static WorkbookViewportScrollState Create(Sheet sheet, ViewportModel viewport)");
        script.Should().Contain("public static (uint TopRow, uint LeftCol) CalculateViewportOrigin(");
        script.Should().Contain("WorkbookViewportScrollPlanner.Create(_session.ActiveSheet, _session.Viewport)");
        script.Should().Contain("WorkbookViewportScrollPlanner.CalculateViewportOrigin(");
        script.Should().Contain("shared\\Free.Shared.AppServices\\LocalFilePath.cs");
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
        script.Should().Contain("WorkbookExportInteractionPlanner.CreateRequestPlan(");
        script.Should().Contain("private async Task<bool> ConfirmNormalizedOverwriteAsync(");
        script.Should().Contain("NormalizedOverwriteTargetKind.Pdf");
        script.Should().Contain("IsCancel = true,");
        script.Should().Contain("dialog.Opened += (_, _) => cancelButton.Focus();");
        script.Should().Contain("prompt.ReplaceButtonAutomationId");
        script.Should().Contain("prompt.CancelButtonAutomationId");
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
        script.Should().Contain("sort_dialog=true");
        script.Should().Contain("sort_dialog_sort_on_controls=true");
        script.Should().Contain("sort_dialog_color_controls=true");
        script.Should().Contain("sort_dialog_action_buttons=true");
        script.Should().Contain("sort_dialog_compact_layout=true");
        script.Should().Contain("sort_dialog_result_closed_without_accept=true");
        script.Should().Contain("data_validation_dropdown_control=true");
        script.Should().Contain("data_validation_dropdown_items=true");
        script.Should().Contain("data_validation_dialog=true");
        script.Should().Contain("data_validation_dialog_criteria_controls=true");
        script.Should().Contain("data_validation_dialog_message_controls=true");
        script.Should().Contain("data_validation_dialog_action_buttons=true");
        script.Should().Contain("data_validation_dialog_compact_layout=true");
        script.Should().Contain("data_validation_dialog_result_closed_without_accept=true");
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
        script.Should().Contain("NativeMenuItemId.SortAscending => _sortAscendingMenuItem,");
        script.Should().Contain("NativeMenuItemId.SortDescending => _sortDescendingMenuItem,");
        script.Should().Contain("var dataMenu = CreateNativeMenu(NativeMenuTopLevelId.Data);");
        script.Should().Contain("[NativeMenuTopLevelId.Data] = dataMenu,");
        script.Should().Contain("[NativeMenuTopLevelId.Review] = reviewMenu,");
        script.Should().Contain("var hasNativeDataMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Data);");
        script.Should().Contain("var hasNativeReviewMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Review);");
        script.Should().Contain("HasNativeDataMenu: hasNativeDataMenu");
        script.Should().Contain("HasNativeReviewMenu: hasNativeReviewMenu");
        script.Should().Contain("private readonly NativeMenuItem _flashFillMenuItem = new();");
        script.Should().Contain("NativeMenuItemId.FlashFill => _flashFillMenuItem,");
        script.Should().Contain("_flashFillMenuItem.Click += (_, _) => FlashFillSelectedRange();");
        script.Should().Contain("NativeMenuCatalog.PlanMenuAvailability(");
        script.Should().Contain("WorkbookApplicationCommandIntent.FlashFill =>");
        script.Should().Contain("private void FlashFillSelectedRange()");
        script.Should().Contain("_session.FlashFillSelectedRange()");
        script.Should().Contain("HasNativeFlashFillMenuItem: HasNativeMenuItem(_flashFillMenuItem, NativeMenuItemId.FlashFill)");
        script.Should().Contain("HasNativeFlashFillMenuItem &&");
        script.Should().Contain("native_flash_fill_menu_item=");
        script.Should().Contain("new NativeMenuAvailabilityContext(");
        script.Should().Contain("private void SortSelectedRange(bool ascending)");
        script.Should().Contain("HasNativeSortAscendingMenuItem: HasNativeMenuItem(_sortAscendingMenuItem, NativeMenuItemId.SortAscending)");
        script.Should().Contain("HasNativeSortDescendingMenuItem: HasNativeMenuItem(_sortDescendingMenuItem, NativeMenuItemId.SortDescending)");
        script.Should().Contain("HasNativeAdvancedFilterMenuItem: HasNativeMenuItem(_advancedFilterMenuItem, NativeMenuItemId.AdvancedFilter)");
        script.Should().Contain("NativeMenuItemId.RemoveDuplicates => _removeDuplicatesMenuItem,");
        script.Should().Contain("_removeDuplicatesMenuItem.Click += async (_, _) => await ShowRemoveDuplicatesDialogAsync();");
        script.Should().Contain("HasNativeRemoveDuplicatesMenuItem: HasNativeMenuItem(_removeDuplicatesMenuItem, NativeMenuItemId.RemoveDuplicates)");
        script.Should().Contain("native_remove_duplicates_menu_item=");
        script.Should().Contain("private readonly NativeMenuItem _subtotalMenuItem = new();");
        script.Should().Contain("NativeMenuItemId.Subtotal => _subtotalMenuItem,");
        script.Should().Contain("_subtotalMenuItem.Click += async (_, _) => await ShowSubtotalDialogAsync();");
        script.Should().Contain("private async Task ShowSubtotalDialogAsync()");
        script.Should().Contain("private async Task<SubtotalDialogPlanResult?> ShowSubtotalInputDialogAsync(");
        script.Should().Contain("_session.ExecuteSubtotalOptions(selection.ToInputOptions())");
        script.Should().Contain("_session.RemoveSelectedRangeSubtotals()");
        script.Should().Contain("SubtotalDialogPlanner.TryCreateResult(");
        script.Should().Contain("AutomationProperties.SetAutomationId(dialog, `\"SubtotalCompactDialog`\");");
        script.Should().Contain("AutomationProperties.SetAutomationId(groupColumnBox, `\"SubtotalGroupColumnBox`\");");
        script.Should().Contain("AutomationProperties.SetAutomationId(functionBox, `\"SubtotalFunctionBox`\");");
        script.Should().Contain("AutomationProperties.SetAutomationId(columnsList, `\"SubtotalColumnsPanel`\");");
        script.Should().Contain("AutomationProperties.SetAutomationId(removeAllButton, `\"SubtotalRemoveAllButton`\");");
        script.Should().Contain("HasNativeSubtotalMenuItem: HasNativeMenuItem(_subtotalMenuItem, NativeMenuItemId.Subtotal)");
        script.Should().Contain("HasNativeSubtotalMenuItem &&");
        script.Should().Contain("native_subtotal_menu_item=");
        script.Should().Contain("HasNativeDataValidationPreviewMenuItem: HasNativeMenuItem(_dataValidationPreviewMenuItem, NativeMenuItemId.DataValidationPreview)");
        script.Should().Contain("HasNativeDataValidationMenuItem: HasNativeMenuItem(_dataValidationMenuItem, NativeMenuItemId.DataValidation)");
        script.Should().Contain("HasNativeWhatIfAnalysisMenuItem: HasNativeMenuItem(_whatIfAnalysisMenuItem, NativeMenuItemId.WhatIfAnalysis)");
        script.Should().Contain("HasNativeGoalSeekMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, NativeMenuItemId.GoalSeek)");
        script.Should().Contain("HasNativeDataTableMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, NativeMenuItemId.DataTable)");
        script.Should().Contain("HasNativeScenarioManagerMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, NativeMenuItemId.ScenarioManager)");
        script.Should().Contain("HasNativeForecastSheetMenuItem: HasNativeMenuItem(_forecastSheetMenuItem, NativeMenuItemId.ForecastSheet)");
        script.Should().Contain("HasNativeReviewSummaryMenuItem: HasNativeMenuItem(_reviewSummaryMenuItem, NativeMenuItemId.ReviewSummary)");
        script.Should().Contain("HasNativeCheckAccessibilityMenuItem: HasNativeMenuItem(_checkAccessibilityMenuItem, NativeMenuItemId.CheckAccessibility)");
        script.Should().Contain("HasNativeNextNoteMenuItem: HasNativeMenuItem(_nextNoteMenuItem, NativeMenuItemId.NextNote)");
        script.Should().Contain("HasNativePreviousNoteMenuItem: HasNativeMenuItem(_previousNoteMenuItem, NativeMenuItemId.PreviousNote)");
        script.Should().Contain("HasNativeNextCommentMenuItem: HasNativeMenuItem(_nextCommentMenuItem, NativeMenuItemId.NextComment)");
        script.Should().Contain("HasNativePreviousCommentMenuItem: HasNativeMenuItem(_previousCommentMenuItem, NativeMenuItemId.PreviousComment)");
        script.Should().Contain("public WorkbookCellEditResult SortSelectedRange(bool ascending)");
        script.Should().Contain("QuickSortRangePlanner.Create(ActiveSheet, range, ActiveCell)");
        script.Should().Contain("sortPlan.SortByColOffset");
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
        script.Should().Contain("ConfigureNativeFileMenuItem(_workbookStatisticsMenuItem, NativeFileMenuItemId.WorkbookStatistics);");
        script.Should().Contain("_workbookStatisticsMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.WorkbookStatistics);");
        script.Should().Contain("ApplyNativeFileMenuAvailability(isIdle);");
        script.Should().Contain("src\\FreeX.App.Presentation\\Shell\\NativeMenuCatalog.cs");
        script.Should().Contain("new(NativeMenuTopLevelId.View, `\"View`\")");
        script.Should().Contain("new(NativeMenuTopLevelId.Sheet, `\"Sheet`\")");
        script.Should().Contain("new(NativeMenuTopLevelId.Window, `\"Window`\")");
        script.Should().Contain("new(NativeMenuTopLevelId.Help, `\"Help`\")");
        script.Should().Contain("FileItem(NativeFileMenuItemId.WorkbookStatistics)");
        script.Should().Contain("NativeMenuGesture(WorkbookShortcutRoute.WorkbookStatistics)");
        script.Should().Contain("WorkbookApplicationCommandIntent.WorkbookStatistics =>");
        script.Should().Contain("private async Task ShowWorkbookStatisticsDialogAsync()");
        script.Should().Contain("WorkbookStatisticsService.GetStatistics(_session.Workbook)");
        script.Should().Contain("AutomationProperties.SetAutomationId(dialog, `\"WorkbookStatisticsDialog`\");");
        script.Should().Contain("AutomationProperties.SetAutomationId(okButton, `\"WorkbookStatisticsOkButton`\");");
        script.Should().Contain("FreeXAutomationIdCatalog.WorkbookStatisticsSummary");
        script.Should().Contain("private static string FormatWorkbookStatistics(WorkbookStatistics statistics)");
        script.Should().Contain("WorkbookStatisticsFormatter.Format(statistics)");
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
        script.Should().Contain("sort_dialog=");
        script.Should().Contain("sort_dialog_sort_on_controls=");
        script.Should().Contain("sort_dialog_color_controls=");
        script.Should().Contain("sort_dialog_action_buttons=");
        script.Should().Contain("sort_dialog_compact_layout=");
        script.Should().Contain("sort_dialog_result_closed_without_accept=");
        script.Should().Contain("data_validation_dropdown_control=");
        script.Should().Contain("data_validation_dropdown_items=");
        script.Should().Contain("data_validation_dialog=");
        script.Should().Contain("data_validation_dialog_criteria_controls=");
        script.Should().Contain("data_validation_dialog_message_controls=");
        script.Should().Contain("data_validation_dialog_action_buttons=");
        script.Should().Contain("data_validation_dialog_compact_layout=");
        script.Should().Contain("data_validation_dialog_result_closed_without_accept=");
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
        script.Should().Contain("PackagingSmokeCommand.TryRun");
        script.Should().Contain("ValidationHostCommandRouteExecutor.Run(");
        script.Should().Contain("PortPreviewWorkbookFactory.PreviewShapeName");
        script.Should().Contain("_sessionFactory.Create(source, SmokeViewportHeight, SmokeViewportWidth, includeObjects: true)");
        script.Should().Contain("StartWithClassicDesktopLifetime(arguments)");
        script.Should().Contain("IActivatableLifetime");
        script.Should().Contain("OpenActivatedFilesAsync");
        script.Should().Contain("using FreeX.Core.Calc;");
        script.Should().Contain("var cellControl = CreateCell(cell, row, col, zoomFactor, colWidth, rowHeight, mergeRegion)");
        script.Should().Contain("AddGridChild(grid, cellControl, rowIndex + headerOffset, colIndex + headerOffset)");
        script.Should().Contain("CellTextOrientationLayoutPlanner.HasTextOrientation(textRotation)");
        script.Should().Contain("CellTextOrientationLayoutPlanner.CalculateLayout(");
        script.Should().Contain("CreateTextRotationTransform(layout.TransformAngle)");
        script.Should().Contain("Canvas.SetLeft(textBlock, layout.TextPoint.X);");
        script.Should().Contain("Canvas.SetTop(textBlock, layout.TextPoint.Y);");
        script.Should().Contain("public static class CellTextOrientationLayoutPlanner");
        script.Should().Contain("public static bool ShouldClip(");
        script.Should().Contain("CreateNativePasteSpecialMenu()");
        script.Should().Contain("private readonly NativeMenuItem _formatCellsMenuItem = new();");
        script.Should().Contain("NativeMenuItemId.FormatCells => _formatCellsMenuItem,");
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
        script.Should().Contain("new FormatCellsCompactDialogInput(");
        script.Should().Contain("FormatCellsDialogPlanner.TryCreateCompactPlan(plannerInput");
        script.Should().Contain("public static bool TryCreateCompactPlan(");
        script.Should().Contain("FormatCellsInputParser.TryParseFontSize(input.FontSizeText");
        script.Should().Contain("MergeCells: Changed(input.InitialMergeCells, input.MergeCells)");
        script.Should().Contain("bool? mergeCells = null");
        script.Should().Contain("CreateFormatCellsMergeCommands(area, shouldMerge, mergeContentResolution)");
        script.Should().Contain("CellMergePlanner.CreateMergeCommands(");
        script.Should().Contain("bool? MergeCells = null");
        script.Should().Contain("`\"FormatCellsFillPatternStyleBox`\"");
        script.Should().Contain("`\"FormatCellsFillPatternColorBox`\"");
        script.Should().Contain("`\"FormatCellsNormalFontBox`\"");
        script.Should().Contain("`\"FormatCellsProtectionExplanationText`\"");
        // The protection explanation was localized; the preflight now declares the contract via the
        // UiText resource key rather than the inline English string.
        script.Should().Contain("FormatCells_ProtectionExplanation");
        script.Should().Contain("UseNormalFont: normalFont");
        script.Should().Contain("FontNameText: fontNameBox.Text");
        script.Should().Contain("FontColor: (fontColorBox.SelectedItem as FormatCellsColorChoice)?.Color");
        script.Should().Contain("SelectFormatCellsColor(fontColorBox, normal.FontColor)");
        script.Should().Contain("FillPatternStyle: SelectedFormatCellsValue(currentFillStyle.FillPatternStyle, fillPatternStyleBox)");
        script.Should().Contain("FillPatternColorText: fillEditor.PatternColorTextBox.Text");
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
        script.Should().Contain("`\"FindReplaceResultsList`\"");
        script.Should().Contain("_session.FindAll(search.FindText, search.Options, search.MatchCase, search.MatchEntireCell)");
        script.Should().Contain("public WorkbookFindAllResult FindAll(");
        script.Should().Contain("private WorkbookFindAllMatch CreateFindAllMatch(FindResult result)");
        script.Should().Contain("private enum ReplaceDialogAction");
        script.Should().Contain("private sealed record ReplaceDialogResult(");
        script.Should().Contain("ReplaceDialogAction Action,");
        script.Should().Contain("StyleDiff? ReplacementFormat);");
        script.Should().Contain("internal sealed record FindOptionsControls(");
        script.Should().Contain("`\"ReplaceButton`\"");
        script.Should().Contain("CreateFindOptionsControls(`\"Replace`\", defaultLookInIndex: 1)");
        script.Should().Contain("`\"FindChooseFormatFromCellButton`\",");
        script.Should().Contain("`\"FindClearFormatButton`\",");
        script.Should().Contain("`\"ReplaceFindChooseFormatFromCellButton`\",");
        script.Should().Contain("`\"ReplaceFindClearFormatButton`\",");
        script.Should().Contain("`\"ReplaceWithChooseFormatFromCellButton`\",");
        script.Should().Contain("`\"ReplaceWithClearFormatButton`\",");
        script.Should().Contain("FindReplaceText(FindReplaceDialogText.ChooseFromCell));");
        script.Should().Contain("UiText.Get(`\"FindReplace_ClearFormat`\"));");
        script.Should().Contain("UiText.Get(`\"FindReplace_FindFormat`\"),");
        script.Should().Contain("UiText.Get(`\"FindReplace_ReplaceFormat`\"),");
        script.Should().Contain("CreateFindOptions(optionsControls, findFormat, selectionScopeAtOpen)");
        script.Should().Contain("FindReplaceDialogPlanner.CreateFindOptions(");
        script.Should().Contain("requiredFormat: requiredFormat,");
        script.Should().Contain("selectionScope: selectionScope);");
        script.Should().Contain("ShowFindReplaceTabbedDialogAsync(replaceMode: true)");
        script.Should().Contain("_session.ReplaceNextValue(");
        script.Should().Contain("public WorkbookReplaceResult ReplaceNextValue(");
        script.Should().Contain("public StyleDiff? CreateFormatDiffFromActiveCell()");
        script.Should().Contain("public StyleDiff? CreateFormatDiffFromCell(CellAddress address)");
        script.Should().Contain("StyleDiff? replacementFormat = null");
        script.Should().Contain("FindReplaceDialogPlanner.CreateFindOptions(");
        script.Should().Contain("new GridRange(match.Address, match.Address)");
        script.Should().Contain("public enum FindResultTarget");
        script.Should().Contain("ThreadedCommentReply");
        script.Should().Contain("FindResultTarget Target = FindResultTarget.Cell,");
        script.Should().Contain("int? ReplyIndex = null);");
        script.Should().Contain("public readonly record struct SearchText(");
        script.Should().Contain("comment.Replies[replyIndex].Text");
        script.Should().Contain("FindResultTarget.ThreadedCommentReply,");
        script.Should().Contain("_bordersButton.Flyout = CreateBorderPresetFlyout();");
        script.Should().Contain("_bordersMenuItem.Menu = CreateNativeBorderPresetMenu();");
        script.Should().Contain("PasteSpecialClipboardAtActiveCell(text, mode, options, clipboardReadFailed: clipboardReadFailed, html: html)");
        script.Should().Contain("CreatePasteSpecialTextMenuItem(`\"Text`\")");
        script.Should().Contain("CreateNativePasteSpecialTextMenuItem(`\"Unicode Text`\")");
        script.Should().Contain("_session.PasteClipboardTextAtActiveCell(text, preserveText: true, clipboardReadFailed: clipboardReadFailed, html: html)");
        script.Should().Contain("CreatePastePictureMenuItem(`\"Picture`\", linkedPicture: false)");
        script.Should().Contain("CreateNativePastePictureMenuItem(`\"Linked Picture`\", linkedPicture: true)");
        script.Should().Contain("ShellFocusTarget.Worksheet");
        script.Should().Contain("ShellFocusTarget.Ribbon");
        script.Should().Contain("ShellFocusTarget.TaskPane");
        script.Should().Contain("private static bool IsShellFocusCycleKey(KeyEventArgs args)");
        script.Should().Contain("CycleShellFocus(reverse: e.KeyModifiers == KeyModifiers.Shift);");
        script.Should().Contain("private void CycleShellFocus(bool reverse)");
        script.Should().Contain("ShellFocusCyclePlanner.TryFocusNextAvailable(");
        script.Should().Contain("private bool IsShellFocusTargetAvailable(ShellFocusTarget target)");
        script.Should().Contain("private ShellFocusTarget GetCurrentShellFocusTarget()");
        script.Should().Contain("private bool FocusShellRegion(ShellFocusTarget target)");
        script.Should().Contain("private bool FocusFirstEnabledToolbarControl()");
        script.Should().Contain("private IReadOnlyList<Control> GetToolbarFocusTargets()");
        script.Should().Contain("private static bool FocusControl(Control control)");
        script.Should().Contain("private void NavigateSheetTabFromKeyboard(SheetId sheetId, KeyEventArgs args)");
        script.Should().Contain("private bool SelectAdjacentVisibleSheetFromKeyboard(int direction, bool selectRange)");
        script.Should().Contain("SheetTabFocusPlanner.AdjacentTab(_session.SheetTabs, sheetId, direction, static tab => tab.Id)");
        script.Should().Contain("SheetTabFocusPlanner.EdgeTab(_session.SheetTabs, first, static tab => tab.Id)");
        script.Should().Contain("_session.ShouldPreferExternalClipboardImage(text)");
        script.Should().Contain("private async Task<bool> TryPasteClipboardImageAsync()");
        script.Should().Contain("await _platformClipboard.ReadImageAsync()");
        script.Should().Contain("image.PngBytes");
        script.Should().Contain("_session.PasteClipboardImageAtActiveCell(pngBytes, pixelWidth, pixelHeight)");
        script.Should().Contain("internal async Task<bool> TryPasteExternalClipboardImageAsync()");
        script.Should().Contain("return await TryPasteClipboardImageAsync();");
        script.Should().Contain("ExternalImageClipboardPictureCount: shell.ExternalImageClipboardPictureCount");
        script.Should().Contain("ExternalImageClipboardPicturePngByteCount: shell.ExternalImageClipboardPicturePngByteCount");
        script.Should().Contain("VerifyImageClipboardPasteArgument");
        script.Should().Contain("VerifyLiveCommandKeysArgument");
        script.Should().Contain("await access.TryPasteExternalClipboardImageAsync();");
        script.Should().Contain("access.BeginCommandObservation(observation =>");
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
        script.Should().Contain("AddStyledCellBorderOverlay(content, style, borderNeighbors, zoomFactor);");
        script.Should().Contain("DrawingObjectRenderPlanner.Plan(viewport)");
        script.Should().Contain("CreateSelectableDrawingObjectVisual(renderPlan, width, height)");
        script.Should().Contain("UiText.Get(selected ? `\"Automation_Selected`\" : `\"Automation_NotSelected`\"));");
        script.Should().Contain("CreateDrawingObjectVisual(renderPlan, width, height, _session.Workbook.Theme)");
        script.Should().Contain("CreateDrawingCellRangeSnapshotVisual(renderPlan, width, height, theme)");
        script.Should().Contain("CreateDrawingImageSourceRect(crop)");
        script.Should().Contain("TryCreateDrawingBitmap(imageBytes, out var bitmap)");
        script.Should().Contain("private static bool HasVisibleCellBorder(CellStyle? style)");
        script.Should().Contain("private readonly RecentFilesStore _recentFiles = RecentFilesStore.Load();");
        script.Should().Contain("_newWorkbookMenuItem.Click += async (_, _) => await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.New);");
        script.Should().Contain("ConfigureNativeFileMenuItem(_openRecentMenuItem, NativeFileMenuItemId.OpenRecent);");
        script.Should().Contain("NativeMenuItemId.SelectAll => _selectAllMenuItem,");
        script.Should().Contain("_fillCellsButton.Content = UiText.Get(`\"Toolbar_FillCells`\");");
        script.Should().Contain("NativeMenuItemId.FillDown => _fillDownMenuItem,");
        script.Should().Contain("NativeMenuItemId.FillRight => _fillRightMenuItem,");
        script.Should().Contain("private void FillSelectedRange(FillCellsDirection direction)");
        script.Should().Contain("_session.FillSelectedRange(direction)");
        script.Should().Contain("private void SelectCurrentRegionOrAll()");
        script.Should().Contain("private NativeMenu CreateNativeOpenRecentMenu(bool isIdle)");
        script.Should().Contain("private void RecordRecentWorkbook(string path, WorkbookFileAccessIdentity? fileAccessIdentity = null)");
        script.Should().Contain("WorkbookFileAccessServiceFactory.Create(App.Diagnostics)");
        script.Should().Contain("RecordFileAccessEvent(`\"workbook_file_access_identity`\", status, grantKind)");
        script.Should().Contain("RecordFileAccessEvent(`\"workbook_file_access_scope`\", status, grantKind)");
        script.Should().Contain("[\"payloadRedacted\"] = string.IsNullOrWhiteSpace(grantKind) ? null : \"true\"");
        script.Should().Contain("_closeWorkbookMenuItem.Click += async (_, _) => await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.Close);");
        script.Should().Contain("_sessionFactory.CreateNew(viewportHeight, viewportWidth, includeObjects: true)");
        script.Should().Contain("RefreshViewportSizeForZoom();");
        script.Should().Contain("private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)");
        script.Should().Contain("private async Task<bool> ConfirmBeforeDestructiveWorkbookActionAsync(string title, string discardButtonText)");
        script.Should().Contain("AutomationProperties.SetAutomationId(saveButton, `\"DirtyWorkbookSaveButton`\");");
        script.Should().Contain("public WorkbookSession CreateNew(");
        script.Should().Contain("WorkbookFactory.Create(options)");
        script.Should().Contain("`\"Created new workbook.`\"");
        script.Should().Contain("var result = _session.AddSheet(insertBeforeSheetId);");
        script.Should().Contain("var result = _session.RenameActiveSheet(newName);");
        script.Should().Contain("private async Task<string?> ShowRenameSheetDialogAsync(string currentName)");
        script.Should().Contain("AutomationProperties.SetAutomationId(nameBox, `\"RenameSheetNameBox`\");");
        script.Should().Contain("var validationError = _session.Workbook.ValidateSheetName(proposedName, _session.ActiveSheet.Id);");
        script.Should().Contain("(_, args) => BeginSheetTabPointer(tab.Id, args),");
        script.Should().Contain("private void BeginSheetTabPointer(SheetId sheetId, PointerPressedEventArgs args)");
        script.Should().Contain("if (!point.Properties.IsLeftButtonPressed)");
        script.Should().Contain("var selectRange = modifiers.HasFlag(KeyModifiers.Shift);");
        script.Should().Contain("var toggle = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);");
        script.Should().Contain("args.Handled = true;");
        script.Should().Contain("_session.SelectSheetFromTab(sheetId, selectRange, toggle)");
        script.Should().Contain("var result = _session.DuplicateSelectedSheets();");
        script.Should().Contain("var result = _session.SetActiveSheetTabColor(color);");
        script.Should().Contain("var result = _session.DeleteActiveSheet();");
        script.Should().Contain("NativeMenuItemId.ShowGridlines => _showGridlinesMenuItem,");
        script.Should().Contain("NativeMenuItemId.ShowHeadings => _showHeadingsMenuItem,");
        script.Should().Contain("var viewMenu = CreateNativeMenu(NativeMenuTopLevelId.View);");
        script.Should().Contain("var result = _session.SetShowGridlines(showGridlines);");
        script.Should().Contain("var result = _session.SetShowHeadings(showHeadings);");
        script.Should().Contain("NativeMenuItemId.ZoomIn => _zoomInMenuItem,");
        script.Should().Contain("NativeMenuItemId.ZoomOut => _zoomOutMenuItem,");
        script.Should().Contain("NativeMenuItemId.Zoom100 => _zoom100MenuItem,");
        script.Should().Contain("NativeMenuItemId.ZoomToSelection => _zoomToSelectionMenuItem,");
        script.Should().Contain("var result = _session.SetZoomPercent(zoomPercent);");
        script.Should().Contain("_zoomText.Text = StatusBarZoomSliderPlanner.FormatZoomPercent(_session.ZoomPercent);");
        script.Should().Contain("CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor)");
        script.Should().Contain("displayHeight / zoomFactor");
        script.Should().Contain("CellSurfaceGridlinePlanner.HasVisibleFill(");
        script.Should().Contain("BorderBrush = showGridlines ? defaultBorderBrush : Brushes.Transparent");
        script.Should().Contain("NativeMenuItemId.FreezePanes => _freezePanesMenuItem,");
        script.Should().Contain("_freezePanesMenuItem.Click += (_, _) => FreezePanesAtActiveCell();");
        script.Should().Contain("private void ApplyFreezePaneCommand(Func<WorkbookCellEditResult> execute, string successAction, string failureMessage)");
        script.Should().Contain("_session.FreezePanesAtActiveCell");
        script.Should().Contain("public WorkbookCellEditResult FreezePanesAtActiveCell()");
        script.Should().Contain("public WorkbookCellEditResult FreezeTopRow()");
        script.Should().Contain("public WorkbookCellEditResult FreezeFirstColumn()");
        script.Should().Contain("public WorkbookCellEditResult UnfreezePanes()");
        script.Should().Contain("new SetFreezePanesCommand(ActiveSheet.Id, frozenRows, frozenCols)");
        script.Should().Contain("public WorkbookCellEditResult SetShowGridlines(bool showGridlines)");
        script.Should().Contain("public WorkbookCellEditResult SetShowHeadings(bool showHeadings)");
        script.Should().Contain("return new SetWorksheetViewOptionsCommand(");
        script.Should().Contain("ExecuteGroupedWorksheetViewCommand(");
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
        script.Should().Contain("public WorkbookCellEditResult SetZoomPercent(int zoomPercent)");
        script.Should().Contain("sheetId => new SetWorksheetZoomCommand(sheetId, zoomPercent)");
        script.Should().Contain("public WorkbookCellEditResult SetActiveSheetTabColor(CellColor? color)");
        script.Should().Contain("public WorkbookCellEditResult SetSelectedSheetTabColor(CellColor? color)");
        script.Should().Contain("new SetSheetTabColorCommand(selectedSheetIds[0], color)");
        script.Should().Contain("public WorkbookCellEditResult AddSheet(SheetId? insertBeforeSheetId = null)");
        script.Should().Contain("public WorkbookCellEditResult RenameActiveSheet(string? name)");
        script.Should().Contain("new RenameSheetCommand(ActiveSheet.Id, newName)");
        script.Should().Contain("ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id)");
        script.Should().Contain("public WorkbookCellEditResult DuplicateSelectedSheets()");
        script.Should().Contain("new DuplicateSheetsCommand(");
        script.Should().Contain("public WorkbookCellEditResult DeleteActiveSheet()");
        script.Should().Contain("public WorkbookCellEditResult DeleteSelectedSheets()");
        script.Should().Contain("new RemoveSheetsCommand(selectedSheetIds)");
        script.Should().Contain("public GridRange SelectCurrentRegionOrAll()");
        script.Should().Contain("OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl");
        script.Should().Contain("FreeXAboutDialogPresentation.Create(typeof(AboutDialog).Assembly, `\"Avalonia`\")");
        script.Should().Contain("FreeXLegalNoticesPresentation.Create(LegalNoticeProvider.GetDocuments(), UiText.Get)");
        script.Should().Contain(
            "AutomationProperties.SetAutomationId(_tabControl, LegalNoticesDialogPresentation.SectionsAutomationId);");
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
    public void MacOsAppReadinessPreflight_FailsWhenCrashHandlerWrapperIsDisconnected()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path, disconnectedCrashHandlers: true);

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        var combinedOutput = result.Output + result.Error;
        combinedOutput.Should().Contain(
            "RegisterCrashHandlers' in shared\\Free.Shared.AppServices\\LocalAppDiagnostics.cs");
        combinedOutput.Should().Contain("AppCrashHandlers.Register('");
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
    public void MacOsAppReadinessPreflight_FailsWhenHostedSmokeGateIsRemovedBeforeAppUpload()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path);
        var workflowPath = Path.Combine(temp.Path, ".github", "workflows", "macos-app.yml");
        var workflow = File.ReadAllText(workflowPath);
        File.WriteAllText(
            workflowPath,
            workflow.Replace(
                "Require hosted smoke before app artifact upload",
                "Upload app artifact without hosted smoke gate",
                StringComparison.Ordinal));

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("macOS workflow is missing required readiness marker: Require hosted smoke before app artifact upload");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_FailsWhenBundleLaunchServicesSmokeIsUnbounded()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(temp.Path);
        var workflowPath = Path.Combine(temp.Path, ".github", "workflows", "macos-app.yml");
        var workflow = File.ReadAllText(workflowPath);
        File.WriteAllText(
            workflowPath,
            workflow.Replace(
                "run_bounded_launchservices_smoke \"bundle_id\" \"$launch_smoke_report\"",
                "run_unbounded_launchservices_smoke \"bundle_id\" \"$launch_smoke_report\"",
                StringComparison.Ordinal));

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        (result.Output + result.Error).Should().Contain("macOS workflow is missing required readiness marker: run_bounded_launchservices_smoke \"bundle_id\"");
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
    public void MacOsAppReadinessPreflight_FailsForNativeMacOsTokenOutsideMacOsSourceBoundary()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(
            temp.Path,
            extraAvaloniaSource: """
            using AppKit;

            namespace FreeX.App.Avalonia;

            internal static class NativeLeak
            {
                private static readonly object PickerType = typeof(NSSharingServicePicker);
            }
            """);

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        var combinedOutput = result.Output + result.Error;
        combinedOutput.Should().Contain("Portable macOS source contains native macOS token 'AppKit' outside src/FreeX.App.Avalonia/MacOs");
        combinedOutput.Should().Contain("src/FreeX.App.Avalonia/WindowsOnlyLeak.cs");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_FailsForWindowsTokenInsideMacOsSourceBoundary()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(
            temp.Path,
            extraAvaloniaSourcePath: "src/FreeX.App.Avalonia/MacOs/NativeWorkbookShareSheetService.cs",
            extraAvaloniaSource: """
            namespace FreeX.App.Avalonia;

            internal static class NativeWorkbookShareSheetService
            {
                private const string Token = "System.Windows";
            }
            """);

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().NotBe(0);
        var combinedOutput = result.Output + result.Error;
        combinedOutput.Should().Contain("Portable macOS source contains forbidden token 'System.Windows'");
        combinedOutput.Should().Contain("src/FreeX.App.Avalonia/MacOs/NativeWorkbookShareSheetService.cs");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_AllowsNativeLinuxTokensInsideLinuxSourceBoundary()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(
            temp.Path,
            extraAvaloniaSourcePath: "src/FreeX.App.Avalonia/Linux/X11WindowActivator.cs",
            extraAvaloniaSource: """
            using System.Runtime.InteropServices;

            namespace FreeX.App.Avalonia;

            internal static class X11WindowActivator
            {
                [DllImport("libX11.so.6")]
                private static extern nint XOpenDisplay(nint displayName);
            }
            """);

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().Be(0, result.Output + result.Error);
        result.Output.Should().Contain("macOS app readiness preflight passed.");
    }

    [Fact]
    public void MacOsAppReadinessPreflight_AllowsNativeMacOsTokensInsideMacOsSourceBoundary()
    {
        using var temp = new TestTemporaryDirectory();
        CreateMinimalMacOsReadinessRepo(
            temp.Path,
            extraAvaloniaSourcePath: "src/FreeX.App.Avalonia/MacOs/NativeWorkbookShareSheetService.cs",
            extraAvaloniaSource: """
            using AppKit;
            using Foundation;

            namespace FreeX.App.Avalonia;

            internal static class NativeWorkbookShareSheetService
            {
                private static readonly object PickerType = typeof(NSSharingServicePicker);
                private static readonly object UrlType = typeof(NSUrl);
            }
            """);

        var scriptPath = WorkspaceFileLocator.Find("tools", "Test-MacOsAppReadiness.ps1");
        var result = RunScriptFromTemporaryWorkingDirectory(scriptPath, $"-ProjectRoot \"{temp.Path}\"");

        result.ExitCode.Should().Be(0, result.Output + result.Error);
        (result.Output + result.Error).Should().Contain("macOS app readiness preflight passed.");
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
        string extraAvaloniaSourcePath = "src/FreeX.App.Avalonia/WindowsOnlyLeak.cs",
        string extraAvaloniaSource = "",
        bool disconnectedCrashHandlers = false)
    {
        WriteFile(
            root,
            "src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\FreeX.App.Presentation\FreeX.App.Presentation.csproj" />
                <ProjectReference Include="..\FreeX.App.Services\FreeX.App.Services.csproj" />
                <ProjectReference Include="..\FreeX.Core.Calc\FreeX.Core.Calc.csproj" />
                <ProjectReference Include="..\FreeX.Core.Commands\FreeX.Core.Commands.csproj" />
                <ProjectReference Include="..\FreeX.Core.IO\FreeX.Core.IO.csproj" />
                <ProjectReference Include="..\FreeX.Core.Model\FreeX.Core.Model.csproj" />
                <ProjectReference Include="..\..\shared\Free.Shared.Ribbon\Free.Shared.Ribbon.csproj" />
                <ProjectReference Include="..\..\shared\Free.Shared.Ribbon.Avalonia\Free.Shared.Ribbon.Avalonia.csproj" />
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
              <ItemGroup Condition="'$(TargetFramework)' != 'net10.0-macos'">
                <Compile Remove="MacOs\**\*.cs" />
              </ItemGroup>
              <PropertyGroup>
                <AssemblyName>FreeX</AssemblyName>
                <ApplicationTitle>FreeX</ApplicationTitle>
                <OutputType>Exe</OutputType>
                <RuntimeIdentifiers>osx-arm64;osx-x64</RuntimeIdentifiers>
                <SupportedOSPlatformVersion Condition="'$(TargetFramework)' == 'net10.0-macos'">12.0</SupportedOSPlatformVersion>
                <TargetFramework Condition="'$(EnableMacOsTargetFramework)' != 'true'">{{TargetFramework}}</TargetFramework>
                <TargetFrameworks Condition="'$(EnableMacOsTargetFramework)' == 'true'">net10.0;net10.0-macos</TargetFrameworks>
              </PropertyGroup>
              <PropertyGroup Condition="'$(TargetFramework)' == 'net10.0-macos'">
                <DefineConstants>$(DefineConstants);FREEX_MACOS_SHARE_SHEET</DefineConstants>
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
                        --filter 'FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfDocumentExporterTests|FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfExportPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfPageContentPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.PortablePdfTextCapabilityPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.WorkbookExportPrintPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.WorkbookShareActionPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.WorkbookViewportScrollPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.OpenRecentWorkbookMenuPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.AppDiagnosticsFileStoreTests|FullyQualifiedName~FreeX.App.Services.Tests.AppServicesPortabilityGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaProjectPortabilityGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.ApplicationDataPathGuardTests|FullyQualifiedName~FreeX.App.Services.Tests.AppStoragePathPlannerTests|FullyQualifiedName~FreeX.App.Services.Tests.AppOptionsStoreTests|FullyQualifiedName~FreeX.App.Services.Tests.AtomicFileWriterTests|FullyQualifiedName~FreeX.App.Services.Tests.AvaloniaShellSourceTests|FullyQualifiedName~FreeX.App.Services.Tests.MacOsLaunchSmokeReportKeyDriftGuardTests' \
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
                      evidence_path="$artifact_root/freex-$runtime-macos-evidence.txt"
                      zip_name="freex-$runtime-macos-app.zip"
                      zip_path="$artifact_root/$zip_name"
                      unzip_root="$RUNNER_TEMP/freex-$runtime-unzip"
                      app_path="$unzip_root/FreeX.app"
                      launch_smoke_report="$artifact_root/launch.txt"
                      open_with_report="$artifact_root/freex-$runtime-macos-open-with-launch-smoke.txt"
                      default_open_report="$artifact_root/freex-$runtime-macos-default-open-launch-smoke.txt"
                      app_diagnostics_dir="$artifact_root/freex-$runtime-macos-app-diagnostics"
                      validation_published="$RUNNER_TEMP/freex-$runtime-validation-publish"
                      validation_host="$validation_published/FreeX.Validation.Avalonia"
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
                      dotnet publish tools/FreeX.Validation.Avalonia/FreeX.Validation.Avalonia.csproj \
                        --configuration Release --framework net10.0 --runtime "$runtime" \
                        --self-contained true -p:UseAppHost=true -p:PublishReadyToRun=false \
                        -p:PublishSingleFile=false --output "$validation_published"
                      test -x "$validation_host"
                      cp src/FreeX.App.Avalonia/Packaging/macos/Info.plist "$app/Contents/Info.plist"
                      cp src/FreeX.App.Avalonia/Packaging/macos/FreeX.icns "$app/Contents/Resources/FreeX.icns"
                      plutil -lint "$app/Contents/Info.plist"
                      test -f "$app/Contents/MacOS/FreeX"
                      test -x "$app/Contents/MacOS/FreeX"
                      test -f "$app/Contents/MacOS/FreeX.dll"
                      test -f "$app/Contents/Resources/FreeX.icns"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundlePackageType' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :NSHighResolutionCapable' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:0:CFBundleTypeExtensions:0' "$app/Contents/Info.plist"
                      /usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:1:CFBundleTypeExtensions:0' "$app/Contents/Info.plist"
                      lipo -archs "$app/Contents/MacOS/FreeX"
                      codesign --verify --deep --strict "$app"
                      ditto -c -k --sequesterRsrc --keepParent "$app" "$zip_path"
                      ditto -x -k "$zip_path" "$unzip_root"
                      (cd "$artifact_root" && shasum -a 256 "$zip_name" > "$zip_name.sha256")
                      app_info_plist="$unzip_root/FreeX.app/Contents/Info.plist"
                      test -x "$unzip_root/FreeX.app/Contents/MacOS/FreeX"
                      test -f "$unzip_root/FreeX.app/Contents/MacOS/FreeX.dll"
                      xcrun notarytool submit "$zip_path"
                      xcrun stapler validate "$app" | tee -a "$notary_log"
                      tester_instructions_path="$artifact_root/freex-$runtime-macos-tester-instructions.md"
                      shasum -a 256 -c "$zip_name.sha256"
                      zip_sha256="$(cut -d ' ' -f 1 "$artifact_root/$zip_name.sha256")"
                      echo "zip_sha256=$zip_sha256"
                      echo "artifact_bundle_metadata_subject=unzipped_app_bundle"
                      echo "bundle_executable=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' "$app_info_plist")"
                      echo "bundle_icon=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIconFile' "$app_info_plist")"
                      echo "bundle_identifier=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$app_info_plist")"
                      echo "bundle_package_type=$(/usr/libexec/PlistBuddy -c 'Print :CFBundlePackageType' "$app_info_plist")"
                      echo "bundle_minimum_system_version=$(/usr/libexec/PlistBuddy -c 'Print :LSMinimumSystemVersion' "$app_info_plist")"
                      echo "bundle_high_resolution_capable=$(/usr/libexec/PlistBuddy -c 'Print :NSHighResolutionCapable' "$app_info_plist")"
                      echo "artifact_document_extensions_subject=unzipped_app_bundle"
                      echo "native_document_type=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:0:CFBundleTypeName' "$app_info_plist")"
                      echo "imported_document_type=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleDocumentTypes:1:CFBundleTypeName' "$app_info_plist")"
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
                      packaged_product_probe_home="$RUNNER_TEMP/freex-$runtime-packaged-product-home"
                      packaged_product_launch_report="$RUNNER_TEMP/freex-$runtime-packaged-product-launch.txt"
                      bash tools/Run-PackagedProductLaunchProbe.sh \
                        --executable "$unzip_root/FreeX.app/Contents/MacOS/FreeX" \
                        --readiness-root "$packaged_product_probe_home" \
                        --report "$packaged_product_launch_report"
                      grep -Fqx "packaged_product_launch_status=passed" "$packaged_product_launch_report"
                      grep -Fqx "packaged_product_executable=$unzip_root/FreeX.app/Contents/MacOS/FreeX" "$packaged_product_launch_report"
                      cat "$packaged_product_launch_report" >> "$evidence_path"
                      launchservices_smoke_timeout_seconds=60
                      launchservices_cleanup_timeout_seconds=10
                      append_launchservices_failure_diagnostics() {"{"}
                        echo "app_diagnostics_events_jsonl=true"
                      {"}"}
                      wait_for_bounded_launchservices_cleanup() {"{"}
                        local launchservices_pid="$1"
                        kill "$launchservices_pid" 2>/dev/null || true
                        kill -9 "$launchservices_pid" 2>/dev/null || true
                      {"}"}
                      run_bounded_launchservices_smoke() {"{"}
                        local smoke_name="$1"
                        local report_path="$2"
                        local timed_out=false
                        echo "launchservices_smoke_timed_out=$timed_out"
                        echo "launchservices_smoke_cleanup_timeout=true"
                        echo "launchservices_smoke_name=$smoke_name"
                        cat "$report_path" >> "$evidence_path"
                      {"}"}
                      run_launchservices_with_validation() {"{"}
                        local report_path="$1"
                        local source_path="$2"
                        shift 2
                        "$@" &
                        "$validation_host" --macos-launch-smoke "$report_path" --macos-launch-smoke-diagnostics-dir "$app_diagnostics_dir" "$source_path"
                      {"}"}
                      /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$unzip_root/FreeX.app"
                      run_bounded_launchservices_smoke "bundle_id" "$launch_smoke_report" \
                        run_launchservices_with_validation "$launch_smoke_report" "$RUNNER_TEMP/launch.csv" \
                          open -W -n -b io.github.tony-xmelon.freex "$RUNNER_TEMP/launch.csv"
                      osascript -e 'tell application id "io.github.tony-xmelon.freex" to quit' || true
                      open_with_smoke_file="$RUNNER_TEMP/freex-$runtime-open-with.csv"
                      run_bounded_launchservices_smoke "open_with" "$open_with_report" \
                        run_launchservices_with_validation "$open_with_report" "$open_with_smoke_file" \
                          open -W -n -a "$app_path" "$open_with_smoke_file"
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
                      run_bounded_launchservices_smoke "default_open" "$default_open_report" \
                        run_launchservices_with_validation "$default_open_report" "$default_open_smoke_file" \
                          open -W -n "$default_open_smoke_file"
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
                      echo "smoke_status=passed" >> "$evidence_path"
                      host_arch="$(uname -m)"
                      echo "smoke_status=skipped_host_arch_mismatch" >> "$evidence_path"
                      echo "macos_launch_smoke=skipped_host_arch_mismatch" > "$launch_smoke_report"
                      echo "macos_launch_smoke=skipped_host_arch_mismatch" > "$open_with_report"
                      echo "macos_launch_smoke=skipped_host_arch_mismatch" > "$default_open_report"
                      echo "app_artifact_upload_blocked=host_arch_mismatch" >> "$evidence_path"
                      rm -f "$zip_path" "$zip_path.sha256"
                      echo "Host/runtime architecture mismatch for $runtime on $host_arch cannot publish a macOS app artifact."
                      exit 1
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
                      grep -q "macos_accessibility_smoke=passed" "$artifact_root/launch.txt"
                      grep -q "a11y_formula_box_name=true" "$artifact_root/launch.txt"
                      grep -q "a11y_formula_box_help=true" "$artifact_root/launch.txt"
                      grep -q "a11y_formula_box_id=true" "$artifact_root/launch.txt"
                      grep -q "a11y_status_text_name=true" "$artifact_root/launch.txt"
                      grep -q "a11y_status_text_help=true" "$artifact_root/launch.txt"
                      grep -q "a11y_status_text_id=true" "$artifact_root/launch.txt"
                      grep -q "a11y_status_text_value=true" "$artifact_root/launch.txt"
                      grep -q "a11y_cell_address_name=true" "$artifact_root/launch.txt"
                      grep -q "a11y_cell_address_help=true" "$artifact_root/launch.txt"
                      grep -q "a11y_cell_address_id=true" "$artifact_root/launch.txt"
                      grep -q "a11y_selection_stats_name=true" "$artifact_root/launch.txt"
                      grep -q "a11y_selection_stats_help=true" "$artifact_root/launch.txt"
                      grep -q "a11y_selection_stats_id=true" "$artifact_root/launch.txt"
                      grep -q "native_file_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_new_workbook_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_open_recent_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_open_recent_item_count=[1-9]" "$artifact_root/launch.txt"
                      grep -q "native_workbook_statistics_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_export_pdf_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_share_workbook_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_top_level_menu_order=File|Home|Insert|Page Layout|Formulas|Data|Review|View|Sheet|Window|Help" "$artifact_root/launch.txt"
                      grep -q "native_dock_top_level_menu_order=File|Home|Insert|Page Layout|Formulas|Data|Review|View|Sheet|Window|Help" "$artifact_root/launch.txt"
                      grep -q "native_dock_menu_installed=true" "$artifact_root/launch.txt"
                      grep -q "native_dock_file_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_dock_file_menu_item_count=[1-9]" "$artifact_root/launch.txt"
                      grep -q "native_home_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_insert_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_page_layout_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_formulas_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_close_workbook_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_data_menu=true" "$artifact_root/launch.txt"
                      grep -q "native_flash_fill_menu_item=true" "$artifact_root/launch.txt"
                      grep -q "native_review_menu=true" "$artifact_root/launch.txt"
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
                      grep -q "go_to_dialog_history_controls=true" "$artifact_root/launch.txt"
                      grep -q "go_to_dialog_special_control=true" "$artifact_root/launch.txt"
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
                      grep -q "sort_dialog=true" "$artifact_root/launch.txt"
                      grep -q "sort_dialog_sort_on_controls=true" "$artifact_root/launch.txt"
                      grep -q "sort_dialog_color_controls=true" "$artifact_root/launch.txt"
                      grep -q "sort_dialog_action_buttons=true" "$artifact_root/launch.txt"
                      grep -q "sort_dialog_compact_layout=true" "$artifact_root/launch.txt"
                      grep -q "sort_dialog_result_closed_without_accept=true" "$artifact_root/launch.txt"
                      grep -q "data_validation_dropdown_control=true" "$artifact_root/launch.txt"
                      grep -q "data_validation_dropdown_items=true" "$artifact_root/launch.txt"
                      grep -q "data_validation_dialog=true" "$artifact_root/launch.txt"
                      grep -q "data_validation_dialog_criteria_controls=true" "$artifact_root/launch.txt"
                      grep -q "data_validation_dialog_message_controls=true" "$artifact_root/launch.txt"
                      grep -q "data_validation_dialog_action_buttons=true" "$artifact_root/launch.txt"
                      grep -q "data_validation_dialog_compact_layout=true" "$artifact_root/launch.txt"
                      grep -q "data_validation_dialog_result_closed_without_accept=true" "$artifact_root/launch.txt"
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
                      grep -q "native_borders_preset_count=14" "$artifact_root/launch.txt"
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
                  - name: Require hosted smoke before app artifact upload
                    shell: bash
                    env:
                      FREEX_RUNTIME: ${"{{"} matrix.runtime {"}}"}
                    run: |
                      runtime="$FREEX_RUNTIME"
                      artifact_root="$GITHUB_WORKSPACE/artifacts"
                      evidence_path="$artifact_root/freex-$runtime-macos-evidence.txt"
                      launch_smoke_report="$artifact_root/freex-$runtime-macos-launch-smoke.txt"
                      open_with_report="$artifact_root/freex-$runtime-macos-open-with-launch-smoke.txt"
                      default_open_report="$artifact_root/freex-$runtime-macos-default-open-launch-smoke.txt"
                      if grep -q "^smoke_status=skipped_host_arch_mismatch$" "$evidence_path"; then
                        echo "Host/runtime architecture mismatch for $runtime cannot publish a macOS app artifact."
                        exit 1
                      fi
                      grep -q "^smoke_status=passed$" "$evidence_path"
                      grep -q "^macos_launch_smoke=passed$" "$launch_smoke_report"
                      grep -q "^macos_launch_smoke=passed$" "$open_with_report"
                      grep -q "^macos_launch_smoke=passed$" "$default_open_report"
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
                needs: [macos-app, macos-preview-readiness]
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
                      smoke_status=passed
                      $packagingSmokeText = Get-Content -LiteralPath $packagingSmokePath -Raw
                      Assert-ContainsRequiredText -Text $smokeReportText -Needle "macos_launch_smoke=passed"
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

            internal static partial class Program
            {
                public static int Main(string[] args) =>
                    RunApplication(args, diagnosticsDirectory: null, externalStartupCoordinator: null);

                private static int RunApplication(
                    string[] startupArguments,
                    string? diagnosticsDirectory,
                    Action<MainWindow, LocalAppDiagnostics?>? externalStartupCoordinator)
                {
                    LocalAppDiagnostics? diagnostics = null;
                    return SisterAvaloniaProgramRunner.Run(
                        startupArguments,
                        new SisterAvaloniaProgramSpec(
                            FreeXApplicationStartupDescriptor.ProductIdentity,
                            SisterAvaloniaLaunchPreparation.Continue,
                            arguments => BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments))
                    {
                        CreateDiagnostics = () =>
                        {
                            diagnostics = LocalAppDiagnostics.Create(
                                AppHelpInfo.GetVersionText(typeof(Program).Assembly),
                                diagnosticsDirectory);
                            return new SisterAvaloniaProgramDiagnostics(
                                () => diagnostics.RegisterCrashHandlers(),
                                (exception, source) => diagnostics.RecordCrash(exception, source));
                        },
                        BeforeRun = () =>
                        {
                            var activeDiagnostics = diagnostics!;
                            activeDiagnostics.RecordEvent("app_start");
                            App.StartupArguments = startupArguments;
                            App.ExternalStartupCoordinator = externalStartupCoordinator;
                            App.Diagnostics = activeDiagnostics;
                        },
                        AfterRun = _ => diagnostics!.RecordEvent("app_exit"),
                        CompletedExitCode = 0
                    });
                }
            }
            """);

        WriteFile(
            root,
            "tools/FreeX.Validation.Avalonia/RendererHost/Program.ValidationHost.cs",
            """
            namespace FreeX.App.Avalonia;

            internal static partial class Program
            {
                internal static int RunValidationToolHost(
                    IReadOnlyList<string> startupArguments,
                    string? diagnosticsDirectory,
                    Action<MainWindow.RendererValidationAccess, LocalAppDiagnostics?> externalStartupCoordinator) =>
                    RunApplication(
                        startupArguments.ToArray(),
                        diagnosticsDirectory,
                        (window, diagnostics) =>
                            externalStartupCoordinator(window.CreateRendererValidationAccess(), diagnostics));
            }
            """);

        WriteFile(
            root,
            "tools/FreeX.Validation.Avalonia/RendererHost/MainWindow.RendererValidationAccess.cs",
            """
            namespace FreeX.App.Avalonia;

            public sealed partial class MainWindow
            {
                internal sealed class RendererValidationAccess
                {
                    internal NativeMenu? NativeDockMenu =>
                        global::Avalonia.Application.Current is { } app ? NativeDock.GetMenu(app) : null;
                }
            }
            """);

        WriteFile(
            root,
            "tools/FreeX.Validation.Avalonia/RendererHost/MainWindow.DialogInspectionAccess.cs",
            """
            namespace FreeX.App.Avalonia;

            public sealed partial class MainWindow
            {
                private async Task<FindDialogResult?> ShowFindInputDialogAsync(Action<FindDialogInspection> inspectionCallback) =>
                    await ShowFindInputDialogAsync();

                private async Task<ReplaceDialogResult?> ShowReplaceInputDialogAsync(Action<ReplaceDialogInspection> inspectionCallback) =>
                    await ShowReplaceInputDialogAsync();

                private async Task<GoToSpecialDialogResult?> ShowGoToSpecialInputDialogAsync(
                    Action<GoToSpecialDialogInspection> inspectionCallback) =>
                    await ShowGoToSpecialInputDialogAsync();
            }
            """);

        WriteFile(
            root,
            "shared/Free.Shared.Shell.Avalonia/SisterAvaloniaApplicationStartupRunner.cs",
            """
            namespace Free.Shared.Shell.Avalonia;

            internal static class SisterAvaloniaApplicationStartupRunner
            {
                internal static int Run(string[] startupArguments, dynamic spec)
                {
                    spec.RegisterUnhandledExceptionHandlers();
                    spec.RegisterRibbonCommandFaultHandler((Exception exception, string commandId) =>
                        spec.RecordCrash(exception, RibbonCommandCrashSourcePrefix + commandId));
                    spec.BeforeRun?.Invoke();
                    try
                    {
                        var lifetimeExitCode = spec.StartApplication(startupArguments);
                        spec.AfterRun?.Invoke(lifetimeExitCode);
                        return lifetimeExitCode;
                    }
                    catch (Exception ex)
                    {
                        spec.RecordCrash(ex, spec.StartupCrashSource);
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
                private const string ApplicationTitle = "FreeX";

                internal static LocalAppDiagnostics? Diagnostics { get; set; }

                private static async Task ActivatedAsync(MainWindow mainWindow, ActivatedEventArgs args)
                {
                    Name = ApplicationTitle;
                    Diagnostics?.RecordEvent("app_ready");
                    this.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime;
                    if (args is not FileActivatedEventArgs fileArgs || fileArgs.Kind != ActivationKind.File)
                        return;

                    await mainWindow.OpenActivatedFilesAsync(fileArgs.Files);
                    ExternalStartupCoordinator?.Invoke(mainWindow, Diagnostics);
                }
            }
            """);

        WriteFile(
            root,
            "tools/FreeX.Validation.Avalonia/Program.cs",
            """
            namespace FreeX.Validation.Avalonia;

            internal static class Program
            {
                public static int Main(string[] args)
                {
                    return ValidationHostCommandRouteExecutor.Run(
                        args,
                        Console.Error,
                        $"Expected {MacOsLaunchSmokeOptions.Argument}.",
                        ValidationHostCommandRouteExecutor.Immediate(
                            PackagingSmokeCommand.TryRun,
                            Console.Out,
                            Console.Error),
                        ValidationHostCommandRouteExecutor.Parsed<MacOsLaunchSmokeOptions>(
                            MacOsLaunchSmokeOptions.TryParse,
                            (options, startupArguments) =>
                                FreeX.App.Avalonia.Program.RunValidationToolHost(
                                    startupArguments,
                                    options.DiagnosticsDirectory,
                                    (window, diagnostics) => MacOsLaunchSmokeCoordinator.Start(window, options, diagnostics))));
                }
            }
            """);

        var localAppDiagnosticsRegisterCrashHandlers = disconnectedCrashHandlers
            ? """
                public void RegisterCrashHandlers() { }
                private void RegisterOtherCrashHandlers() => AppCrashHandlers.Register();
              """
            : """
                public void RegisterCrashHandlers(
                    Action<Action<Exception>>? subscribeDispatcher = null,
                    Action? onAfterFault = null) =>
                    AppCrashHandlers.Register(
                        (exception, source) => RecordCrash(exception, source),
                        subscribeDispatcher,
                        onAfterFault);
              """;

        WriteFile(
            root,
            "shared/Free.Shared.AppServices/LocalAppDiagnostics.cs",
            """
            namespace Free.Shared.AppServices;

            public class LocalAppDiagnostics
            {
                public static LocalAppDiagnostics Create(string appVersion, string? diagnosticsDirectory = null)
                {
                    var defaults = AppDiagnosticsOptions.CreateDefault();
                    var options = new AppDiagnosticsOptions(
                        string.IsNullOrWhiteSpace(diagnosticsDirectory)
                            ? defaults.DiagnosticsDirectory
                            : diagnosticsDirectory,
                        defaults.IsEnabled);
                    return new LocalAppDiagnostics(
                        new AppDiagnosticsFileStore(options),
                        AppDiagnosticsMetadata.Create(appVersion));
                }

            {{REGISTER_CRASH_HANDLERS}}
                public void RecordEvent(string eventName) { }
                public string RecordCrash(Exception exception, string source) => "";
            }
            """.Replace("{{REGISTER_CRASH_HANDLERS}}", localAppDiagnosticsRegisterCrashHandlers, StringComparison.Ordinal));

        WriteFile(
            root,
            "shared/Free.Shared.AppServices/AppCrashHandlers.cs",
            """
            namespace Free.Shared.AppServices;

            public static class AppCrashHandlers
            {
                public static void Register()
                {
                    AppDomain.CurrentDomain.UnhandledException += (_, args) => { };
                    TaskScheduler.UnobservedTaskException += (_, args) => { };
                }
            }
            """);

        WriteFile(
            root,
            "shared/Free.Shared.AppServices/AppDiagnosticsFileStore.cs",
            """
            namespace Free.Shared.AppServices;

            public sealed class AppDiagnosticsFileStore
            {
                private static readonly HashSet<string> AllowedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
                {
                    "grantKind",
                    "payloadRedacted"
                };

                public static IEnumerable<KeyValuePair<string, string?>> SanitizeProperties(
                    IReadOnlyDictionary<string, string?>? properties) => properties ?? new Dictionary<string, string?>();
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/WorkbookFileAccessService.cs",
            """
            namespace FreeX.App.Avalonia;

            internal interface IWorkbookFileAccessService { }

            internal sealed class WorkbookFileAccessScope
            {
                public static WorkbookFileAccessScope FromDisposable(IDisposable disposable, Action? onDispose = null) => new();
            }

            internal static class WorkbookFileAccessServiceFactory
            {
                public static IWorkbookFileAccessService Create(LocalAppDiagnostics? diagnostics = null) =>
                    new AvaloniaWorkbookFileAccessService(diagnostics);
            }

            internal sealed class AvaloniaWorkbookFileAccessService : IWorkbookFileAccessService
            {
                internal const string MacOsSecurityScopedBookmarkKind = "macos-security-scoped-bookmark";

                public AvaloniaWorkbookFileAccessService(LocalAppDiagnostics? diagnostics = null) { }

                private async Task BeginAsync(IStorageItem storageItem, IStorageProvider storageProvider, WorkbookFileAccessIdentity identity, string path)
                {
                    if (storageItem is { CanBookmark: true } && StorageItemMatchesPath(storageItem, path))
                    {
                        var bookmark = await storageItem.SaveBookmarkAsync();
                        RecordIdentityEvent("bookmark_created", grantKind: MacOsSecurityScopedBookmarkKind);
                        var storageFile = await storageProvider.OpenFileBookmarkAsync(bookmark);
                        PlatformPathIdentityComparer.Current.Equals(identity.LocalPath, resolvedPath);
                        RecordScopeEvent("scope_started", grantKind: MacOsSecurityScopedBookmarkKind);
                        WorkbookFileAccessScope.FromDisposable(
                            storageFile,
                            () => RecordScopeEvent("scope_ended", grantKind: MacOsSecurityScopedBookmarkKind));
                    }
                }

                private static bool StorageItemMatchesPath(IStorageItem storageItem, string path) => true;

                private void RecordIdentityEvent(string status, string? grantKind = null) =>
                    RecordFileAccessEvent("workbook_file_access_identity", status, grantKind);

                private void RecordScopeEvent(string status, string? grantKind = null) =>
                    RecordFileAccessEvent("workbook_file_access_scope", status, grantKind);

                private void RecordFileAccessEvent(string eventName, string status, string? grantKind)
                {
                    _diagnostics?.RecordEvent(eventName, new Dictionary<string, string?>
                    {
                        ["scope"] = "workbook_file_access",
                        ["grantKind"] = string.IsNullOrWhiteSpace(grantKind) ? null : grantKind,
                        ["payloadRedacted"] = string.IsNullOrWhiteSpace(grantKind) ? null : "true"
                    });
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
                private const ShellFocusTarget DefaultShellFocusTarget = ShellFocusTarget.Worksheet;
                private const ShellFocusTarget RibbonShellFocusTarget = ShellFocusTarget.Ribbon;
                private const ShellFocusTarget FormulaBarShellFocusTarget = ShellFocusTarget.FormulaBar;
                private const ShellFocusTarget SheetTabsShellFocusTarget = ShellFocusTarget.SheetTabs;
                private const ShellFocusTarget TaskPaneShellFocusTarget = ShellFocusTarget.TaskPane;
                private const ShellFocusTarget StatusBarShellFocusTarget = ShellFocusTarget.StatusBar;
                /*
                private readonly ScrollBar _verticalWorksheetScrollBar = new();
                private readonly ScrollBar _horizontalWorksheetScrollBar = new();
                private bool _isUpdatingWorksheetScrollBars;
                SisterAppClientFrameBuilder.Build(new SisterAppClientFrameSpec(
                WorkArea: BuildWorkbookWorkArea(),
                workArea.Children.Add(BuildWorksheetViewportChrome());
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
                private IWorkbookFileAccessService _fileAccess = WorkbookFileAccessServiceFactory.Create(App.Diagnostics);
                private void InstallNativeMenu(NativeMenu menu)
                NativeDock.SetMenu(app, menu);
                NativeMenu.SetMenu(this, menu);
                InstallNativeMenu(_nativeMenu);
                ConfigureNativeCatalogMenuItems();
                var homeMenu = CreateNativeMenu(NativeMenuTopLevelId.Home);
                var dataMenu = CreateNativeMenu(NativeMenuTopLevelId.Data);
                var reviewMenu = CreateNativeMenu(NativeMenuTopLevelId.Review);
                var viewMenu = CreateNativeMenu(NativeMenuTopLevelId.View);
                var sheetMenu = CreateNativeMenu(NativeMenuTopLevelId.Sheet);
                var windowMenu = CreateNativeMenu(NativeMenuTopLevelId.Window);
                var helpMenu = CreateNativeMenu(NativeMenuTopLevelId.Help);
                NativeMenuItemId.FormatPainter => _formatPainterMenuItem,
                NativeMenuItemId.FormatCells => _formatCellsMenuItem,
                NativeMenuItemId.FillCells => _fillCellsMenuItem,
                NativeMenuItemId.FillDown => _fillDownMenuItem,
                NativeMenuItemId.FillRight => _fillRightMenuItem,
                NativeMenuItemId.Clear => _clearMenuItem,
                NativeMenuItemId.ClearAll => _clearAllMenuItem,
                NativeMenuItemId.ClearFormats => _clearFormatsMenuItem,
                NativeMenuItemId.ClearContents => _clearContentsMenuItem,
                NativeMenuItemId.ClearComments => _clearCommentsMenuItem,
                NativeMenuItemId.ClearHyperlinks => _clearHyperlinksMenuItem,
                NativeMenuItemId.Borders => _bordersMenuItem,
                _borderPickerSession.Style,
                _borderPickerSession.Color);
                NativeMenuItemId.MergeAndCenter => _mergeAndCenterMenuItem,
                NativeMenuItemId.UnmergeCells => _unmergeCellsMenuItem,
                NativeMenuItemId.SelectAll => _selectAllMenuItem,
                NativeMenuItemId.Find => _findMenuItem,
                NativeMenuItemId.FindNext => _findNextMenuItem,
                NativeMenuItemId.Replace => _replaceMenuItem,
                NativeMenuItemId.GoTo => _goToMenuItem,
                NativeMenuItemId.GoToSpecial => _goToSpecialMenuItem,
                NativeMenuItemId.SortAscending => _sortAscendingMenuItem,
                NativeMenuItemId.SortDescending => _sortDescendingMenuItem,
                NativeMenuItemId.FlashFill => _flashFillMenuItem,
                NativeMenuItemId.AdvancedFilter => _advancedFilterMenuItem,
                NativeMenuItemId.RemoveDuplicates => _removeDuplicatesMenuItem,
                NativeMenuItemId.Subtotal => _subtotalMenuItem,
                NativeMenuItemId.DataValidation => _dataValidationMenuItem,
                NativeMenuItemId.WhatIfAnalysis => _whatIfAnalysisMenuItem,
                NativeMenuItemId.GoalSeek => _goalSeekMenuItem,
                NativeMenuItemId.ScenarioManager => _scenarioManagerMenuItem,
                NativeMenuItemId.DataTable => _dataTableMenuItem,
                NativeMenuItemId.ForecastSheet => _forecastSheetMenuItem,
                NativeMenuItemId.ReviewSummary => _reviewSummaryMenuItem,
                NativeMenuItemId.CheckAccessibility => _checkAccessibilityMenuItem,
                NativeMenuItemId.NextNote => _nextNoteMenuItem,
                NativeMenuItemId.PreviousNote => _previousNoteMenuItem,
                NativeMenuItemId.NextComment => _nextCommentMenuItem,
                NativeMenuItemId.PreviousComment => _previousCommentMenuItem,
                NativeMenuItemId.TabColor => _tabColorMenuItem,
                NativeMenuItemId.SelectAllSheets => _selectAllSheetsMenuItem,
                NativeMenuItemId.UngroupSheets => _ungroupSheetsMenuItem,
                NativeMenuItemId.ShowGridlines => _showGridlinesMenuItem,
                NativeMenuItemId.ShowHeadings => _showHeadingsMenuItem,
                NativeMenuItemId.ZoomIn => _zoomInMenuItem,
                NativeMenuItemId.ZoomOut => _zoomOutMenuItem,
                NativeMenuItemId.Zoom100 => _zoom100MenuItem,
                NativeMenuItemId.ZoomToSelection => _zoomToSelectionMenuItem,
                NativeMenuItemId.FreezePanes => _freezePanesMenuItem,
                NativeMenuItemId.FreezeTopRow => _freezeTopRowMenuItem,
                NativeMenuItemId.FreezeFirstColumn => _freezeFirstColumnMenuItem,
                NativeMenuItemId.UnfreezePanes => _unfreezePanesMenuItem,
                NativeMenuItemId.MinimizeWindow => _minimizeWindowMenuItem,
                NativeMenuItemId.ZoomWindow => _zoomWindowMenuItem,
                NativeMenuItemId.BringAllToFront => _bringAllToFrontMenuItem,
                ApplyNativeMenuAvailability(isIdle);
                NativeMenuCatalog.PlanMenuAvailability(
                new NativeMenuAvailabilityContext(
                GetNativeMenuItem(item.Id)
                => CreateNativeMenu(NativeMenuCatalog.FillCellsMenuEntries);
                => CreateNativeMenu(NativeMenuCatalog.ClearMenuEntries);
                HasNativeFormatPainterMenuItem: HasNativeMenuItem(_formatPainterMenuItem, NativeMenuItemId.FormatPainter)
                HasNativeBordersMenuItem: HasNativeMenuItem(_bordersMenuItem, NativeMenuItemId.Borders)
                HasNativeMergeAndCenterMenuItem: HasNativeMenuItem(_mergeAndCenterMenuItem, NativeMenuItemId.MergeAndCenter)
                HasNativeUnmergeCellsMenuItem: HasNativeMenuItem(_unmergeCellsMenuItem, NativeMenuItemId.UnmergeCells)
                HasNativeFindMenuItem: HasNativeMenuItem(_findMenuItem, NativeMenuItemId.Find)
                HasNativeFindNextMenuItem: HasNativeMenuItem(_findNextMenuItem, NativeMenuItemId.FindNext)
                HasNativeReplaceMenuItem: HasNativeMenuItem(_replaceMenuItem, NativeMenuItemId.Replace)
                HasNativeGoToMenuItem: HasNativeMenuItem(_goToMenuItem, NativeMenuItemId.GoTo)
                HasNativeSortAscendingMenuItem: HasNativeMenuItem(_sortAscendingMenuItem, NativeMenuItemId.SortAscending)
                HasNativeSortDescendingMenuItem: HasNativeMenuItem(_sortDescendingMenuItem, NativeMenuItemId.SortDescending)
                HasNativeFlashFillMenuItem: HasNativeMenuItem(_flashFillMenuItem, NativeMenuItemId.FlashFill)
                HasNativeAdvancedFilterMenuItem: HasNativeMenuItem(_advancedFilterMenuItem, NativeMenuItemId.AdvancedFilter)
                HasNativeRemoveDuplicatesMenuItem: HasNativeMenuItem(_removeDuplicatesMenuItem, NativeMenuItemId.RemoveDuplicates)
                HasNativeSubtotalMenuItem: HasNativeMenuItem(_subtotalMenuItem, NativeMenuItemId.Subtotal)
                HasNativeDataValidationPreviewMenuItem: HasNativeMenuItem(_dataValidationPreviewMenuItem, NativeMenuItemId.DataValidationPreview)
                HasNativeDataValidationMenuItem: HasNativeMenuItem(_dataValidationMenuItem, NativeMenuItemId.DataValidation)
                HasNativeWhatIfAnalysisMenuItem: HasNativeMenuItem(_whatIfAnalysisMenuItem, NativeMenuItemId.WhatIfAnalysis)
                HasNativeGoalSeekMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, NativeMenuItemId.GoalSeek)
                HasNativeDataTableMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, NativeMenuItemId.DataTable)
                HasNativeScenarioManagerMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, NativeMenuItemId.ScenarioManager)
                HasNativeForecastSheetMenuItem: HasNativeMenuItem(_forecastSheetMenuItem, NativeMenuItemId.ForecastSheet)
                HasNativeReviewSummaryMenuItem: HasNativeMenuItem(_reviewSummaryMenuItem, NativeMenuItemId.ReviewSummary)
                HasNativeCheckAccessibilityMenuItem: HasNativeMenuItem(_checkAccessibilityMenuItem, NativeMenuItemId.CheckAccessibility)
                HasNativeNextNoteMenuItem: HasNativeMenuItem(_nextNoteMenuItem, NativeMenuItemId.NextNote)
                HasNativePreviousNoteMenuItem: HasNativeMenuItem(_previousNoteMenuItem, NativeMenuItemId.PreviousNote)
                HasNativeNextCommentMenuItem: HasNativeMenuItem(_nextCommentMenuItem, NativeMenuItemId.NextComment)
                HasNativePreviousCommentMenuItem: HasNativeMenuItem(_previousCommentMenuItem, NativeMenuItemId.PreviousComment)
                HasNativeMinimizeWindowMenuItem: HasNativeMenuItem(_minimizeWindowMenuItem, NativeMenuItemId.MinimizeWindow)
                HasNativeZoomWindowMenuItem: HasNativeMenuItem(_zoomWindowMenuItem, NativeMenuItemId.ZoomWindow)
                HasNativeBringAllToFrontMenuItem: HasNativeMenuItem(_bringAllToFrontMenuItem, NativeMenuItemId.BringAllToFront)
                private static void RenderCell(CellStyle? style)
                {
                    CreateColorPaletteFlyout(ColorPaletteTarget.Fill, includeClearFill: true);
                    _formatPainterButton.Content = UiText.Get("MainWindow_TooltipTitle_FormatPainter");
                    AutomationProperties.SetAutomationId(_formatPainterButton, "HomeFormatPainterButton");
                    AutomationProperties.SetHelpText(
                        _formatPainterButton,
                        UiText.Get("MainWindow_TooltipDescription_CopyFormattingFromOnePlaceAndApplyItToAnother"));
                    _formatPainterMenuItem.Header = "Format Painter";
                    _formatPainterMenuItem.Click += (_, _) => CaptureFormatPainterSource(persistent: false);
                    homeMenu.Items.Add(_formatPainterMenuItem);
                    _formatPainterButton.IsEnabled = isIdle;
                    _formatPainterMenuItem.IsEnabled = _formatPainterButton.IsEnabled;
                    _autoSumButton.Content = UiText.Get("MainWindow_Content_AutoSum");
                    _autoSumButton.Flyout = CreateAutoSumFlyout();
                    AutomationProperties.SetAutomationId(_autoSumButton, "HomeAutoSumButton");
                    AutomationProperties.SetHelpText(_autoSumButton, UiText.Get("Toolbar_AutoSumHelpText"));
                    _autoSumSumFlyoutItem.Click += (_, _) => InsertAutoSumFormula("SUM");
                    _autoSumAverageFlyoutItem.Click += (_, _) => InsertAutoSumFormula("AVERAGE");
                    _autoSumCountNumbersFlyoutItem.Click += (_, _) => InsertAutoSumFormula("COUNT");
                    _autoSumCountAllFlyoutItem.Click += (_, _) => InsertAutoSumFormula("COUNTA");
                    _autoSumMaxFlyoutItem.Click += (_, _) => InsertAutoSumFormula("MAX");
                    _autoSumMinFlyoutItem.Click += (_, _) => InsertAutoSumFormula("MIN");
                    _autoSumMenuItem.Menu = CreateNativeAutoSumMenu();
                    => CreateNativeMenu(NativeMenuCatalog.AutoSumMenuEntries);
                    var formulasMenu = CreateNativeMenu(NativeMenuTopLevelId.Formulas);
                    _autoSumButton.IsEnabled = isIdle;
                    private MenuFlyout CreateAutoSumFlyout()
                    private NativeMenu CreateNativeAutoSumMenu()
                    private void InsertAutoSumFormula(string functionName)
                    _session.InsertAutoSumFormula(functionName)
                    private static bool IsAutoSumShortcut(KeyEventArgs args)
                    HasAutoSumButton: _autoSumButton.Content?.ToString() == "AutoSum"
                    HasNativeAutoSumMenuItem: HasNativeMenuItem(_autoSumMenuItem, NativeMenuItemId.AutoSum)
                    _fillCellsButton.Content = UiText.Get("Toolbar_FillCells");
                    _fillCellsButton.Flyout = CreateFillCellsFlyout();
                    AutomationProperties.SetAutomationId(_fillCellsButton, "HomeFillCellsButton");
                    AutomationProperties.SetHelpText(_fillCellsButton, UiText.Get("Toolbar_FillCellsHelpText"));
                    _fillDownFlyoutItem.Header = UiText.Get("MainWindow_Header_Down");
                    _fillDownFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Down);
                    _fillRightFlyoutItem.Header = UiText.Get("MainWindow_Header_Right");
                    _fillRightFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Right);
                    _fillUpFlyoutItem.Header = UiText.Get("MainWindow_Header_Up");
                    _fillUpFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Up);
                    _fillLeftFlyoutItem.Header = UiText.Get("MainWindow_Header_Left");
                    _fillLeftFlyoutItem.Click += (_, _) => FillSelectedRange(FillCellsDirection.Left);
                    _fillCellsMenuItem.Header = "Fill";
                    _fillCellsMenuItem.Menu = CreateNativeFillCellsMenu();
                    _fillDownMenuItem.Gesture = new KeyGesture(Key.D, KeyModifiers.Control);
                    _fillRightMenuItem.Gesture = new KeyGesture(Key.R, KeyModifiers.Control);
                    homeMenu.Items.Add(_fillCellsMenuItem);
                    _fillDownFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Down);
                    _fillRightFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Right);
                    _fillUpFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Up);
                    _fillLeftFlyoutItem.IsEnabled = isIdle && _session.CanFillSelectedRange(FillCellsDirection.Left);
                    _fillCellsMenuItem.IsEnabled = _fillCellsButton.IsEnabled;
                    WorksheetCommandPresentationCatalog.FormatFillStatus(direction, rangeReference)
                    _clearButton.Content = UiText.Get("Common_Clear");
                    AutomationProperties.SetAutomationId(_clearButton, "HomeClearButton");
                    AutomationProperties.SetHelpText(_clearButton, UiText.Get("Toolbar_ClearHelpText"));
                    _clearButton.Flyout = CreateClearFlyout();
                    _clearAllFlyoutItem.Header = UiText.Get("MainWindow_Header_ClearAll");
                    _clearFormatsFlyoutItem.Header = UiText.Get("MainWindow_Header_ClearFormats");
                    _clearContentsFlyoutItem.Header = UiText.Get("MainWindow_Header_ClearContents");
                    _clearCommentsFlyoutItem.Header = UiText.Get("MainWindow_Header_ClearCommentsAndNotes");
                    _clearHyperlinksFlyoutItem.Header = UiText.Get("MainWindow_Header_ClearHyperlinks");
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
                    _clearHyperlinksMenuItem.Click += (_, _) => RemoveSelectedRangeHyperlinks();
                    homeMenu.Items.Add(_clearMenuItem);
                    _clearButton.IsEnabled = isIdle;
                    _clearMenuItem.IsEnabled = _clearButton.IsEnabled;
                    _bordersButton.Flyout = CreateBorderPresetFlyout();
                    AutomationProperties.SetAutomationId(_bordersButton, "HomeBordersButton");
                    AutomationProperties.SetHelpText(_bordersButton, UiText.Get("MainWindow_TooltipDescription_ApplyOrChangeBordersOnTheSelectedCells"));
                    _bordersMenuItem.Header = "Borders";
                    _bordersMenuItem.Menu = CreateNativeBorderPresetMenu();
                    homeMenu.Items.Add(_bordersMenuItem);
                    _bordersButton.IsEnabled = isIdle;
                    _bordersMenuItem.IsEnabled = _bordersButton.IsEnabled;
                    CreateNativePasteSpecialMenu();
                    PasteSpecialClipboardAtActiveCell(text, mode, options, clipboardReadFailed: clipboardReadFailed, html: html);
                    _session.PasteClipboardTextAtActiveCell(text, preserveText: true, clipboardReadFailed: clipboardReadFailed, html: html);
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
                    private async Task<bool> TryPasteClipboardImageAsync()
                    await _platformClipboard.ReadImageAsync()
                    read.Value is not { PngBytes.Length: > 0 } image
                    var pngBytes = image.PngBytes;
                    _session.PasteClipboardImageAtActiveCell(pngBytes, pixelWidth, pixelHeight);
                    internal async Task<bool> TryPasteExternalClipboardImageAsync()
                    return await TryPasteClipboardImageAsync();
                    private async Task PastePictureFromClipboardAsync(string label, bool linkedPicture)
                    _session.PastePictureFromClipboardAtActiveCell(text, linkedPicture);
                    HasNativePasteSpecialTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Text");
                    HasNativePasteSpecialUnicodeTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Unicode Text");
                    HasNativePasteSpecialPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Picture");
                    HasNativePasteSpecialLinkedPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Linked Picture");
                    CellColorPalettePlanner.BuildDefaultSwatches(_session.Workbook.Theme);
                    DrawingObjectRenderPlanner.Plan(viewport);
                    CreateSelectableDrawingObjectVisual(renderPlan, width, height);
                    AutomationProperties.SetAutomationId(container, $"DrawingObject{drawingObject.Kind}{drawingObject.Id:N}");
                    AutomationProperties.SetHelpText(container, UiText.Get("DrawingObject_PreviewHelpText"));
                    AutomationProperties.SetItemStatus(
                        container,
                        UiText.Get(selected ? "Automation_Selected" : "Automation_NotSelected"));
                    container.PointerPressed += (_, args) => { };
                    if (args.Key is Key.Enter or Key.Space) { }
                    CreateDrawingObjectSelectionAdorner();
                    ClearSelectedDrawingObject();
                    CreateDrawingObjectVisual(renderPlan, width, height, _session.Workbook.Theme);
                    CreateDrawingCellRangeSnapshotVisual(renderPlan, width, height, theme);
                    CreateDrawingImageSourceRect(crop);
                    TryCreateDrawingBitmap(imageBytes, out var bitmap);
                    AddStyledCellBorderOverlay(content, style, borderNeighbors, zoomFactor);
                    private readonly RecentFilesStore _recentFiles = RecentFilesStore.Load();
                    _newWorkbookMenuItem.Click += async (_, _) => await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.New);
                    ConfigureNativeFileMenuItem(_openRecentMenuItem, NativeFileMenuItemId.OpenRecent);
                    _openRecentMenuItem.Menu = CreateNativeOpenRecentMenu(isIdle: true);
                    foreach (var entry in NativeMenuCatalog.FileMenuEntries)
                    menu.Items.Add(GetNativeFileMenuItem(entry.Item!.Id));
                    RefreshNativeOpenRecentMenu(isIdle);
                    WorkbookOpenIngressPlanner.SelectOpenableExistingLocalFile(
                    _fileWorkflow.TryResolveOpenTarget(candidatePath, out var target, out var unsupportedMessage)
                    WorkbookOpenIngressResolution.Resolved(target!.Path)
                    path = plan.Path;
                    private readonly NativeMenuItem _workbookStatisticsMenuItem = new();
                    private readonly NativeMenuItem _exportPdfMenuItem = new();
                    ConfigureNativeFileMenuItem(_exportPdfMenuItem, NativeFileMenuItemId.ExportPdf);
                    _exportPdfMenuItem.Click += async (_, _) => await ExportActiveSheetPdfAsync();
                    NativeFileMenuItemId.ExportPdf => _exportPdfMenuItem,
                    HasNativeExportPdfMenuItem: HasNativeFileMenuItem(_exportPdfMenuItem, NativeFileMenuItemId.ExportPdf)
                    private Task ExportActiveSheetPdfAsync() =>
                    ExportWorkbookPdfAsync(
                    var requestPlan = WorkbookExportInteractionPlanner.CreateRequestPlan(
                    requestPlan.ShouldConfirmNormalizedOverwrite
                    !await ConfirmNormalizedOverwriteAsync(
                    NormalizedOverwriteTargetKind.Pdf
                    WorkbookExportInteractionPlanner.CreateResultPlan(
                    private async Task<bool> ConfirmNormalizedOverwriteAsync(
                    IsCancel = true,
                    dialog.Opened += (_, _) => cancelButton.Focus();
                    AutomationProperties.SetAutomationId(replaceButton, prompt.ReplaceButtonAutomationId)
                    AutomationProperties.SetAutomationId(cancelButton, prompt.CancelButtonAutomationId)
                    var outcome = Pdf.AvaloniaPdfDocumentExporter.Save(
                    await File.WriteAllBytesAsync(
                    ConfigureNativeFileMenuItem(_workbookStatisticsMenuItem, NativeFileMenuItemId.WorkbookStatistics);
                    _workbookStatisticsMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.WorkbookStatistics);
                    ApplyNativeFileMenuAvailability(isIdle);
                    private readonly NativeMenuItem _optionsMenuItem = new();
                    ConfigureNativeFileMenuItem(_optionsMenuItem, NativeFileMenuItemId.Options);
                    _optionsMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.Options);
                    NativeFileMenuItemId.Options => _optionsMenuItem,
                    Text = UiText.Get("FormatCells_ProtectionExplanation"),
                    CreateFormatCellsField(UiText.Get("FormatCells_PatternStyle"), fillPatternStyleBox)
                    CreateFormatCellsField(UiText.Get("FormatCells_PatternColor"), fillPatternColorBox)
                    private readonly NativeMenuItem _backstageExportMenuItem = new();
                    private readonly NativeMenuItem _backstageInfoMenuItem = new();
                    private readonly NativeMenuItem _backstageAccountMenuItem = new();
                    ConfigureNativeFileMenuItem(_backstageInfoMenuItem, NativeFileMenuItemId.BackstageInfo);
                    _backstageInfoMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.BackstageInfo);
                    ConfigureNativeFileMenuItem(_backstageExportMenuItem, NativeFileMenuItemId.BackstageExport);
                    _backstageExportMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.BackstageExport);
                    ConfigureNativeFileMenuItem(_backstageAccountMenuItem, NativeFileMenuItemId.BackstageAccount);
                    _backstageAccountMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.BackstageAccount);
                    NativeFileMenuItemId.BackstageExport => _backstageExportMenuItem,
                    NativeFileMenuItemId.BackstageInfo => _backstageInfoMenuItem,
                    NativeFileMenuItemId.BackstageAccount => _backstageAccountMenuItem,
                    private readonly NativeMenuItem _printMenuItem = new();
                    ConfigureNativeFileMenuItem(_printMenuItem, NativeFileMenuItemId.Print);
                    _printMenuItem.Click += async (_, _) => await ShowPrintDialogAsync();
                    NativeFileMenuItemId.Print => _printMenuItem,
                    private readonly NativeMenuItem _printPreviewMenuItem = new();
                    ConfigureNativeFileMenuItem(_printPreviewMenuItem, NativeFileMenuItemId.PrintPreview);
                    _printPreviewMenuItem.Click += async (_, _) => await ExecuteOwnedNativeFileMenuItemAsync(NativeFileMenuItemId.PrintPreview);
                    HasNativeWorkbookStatisticsMenuItem: HasNativeFileMenuItem(_workbookStatisticsMenuItem, NativeFileMenuItemId.WorkbookStatistics)
                    case WorkbookApplicationCommandIntent.WorkbookStatistics:
                    private async Task ShowWorkbookStatisticsDialogAsync()
                    WorkbookStatisticsService.GetStatistics(_session.Workbook)
                    AutomationProperties.SetAutomationId(dialog, "WorkbookStatisticsDialog");
                    AutomationProperties.SetAutomationId(okButton, "WorkbookStatisticsOkButton");
                    AutomationProperties.SetAutomationId(statisticsBlock, FreeXAutomationIdCatalog.WorkbookStatisticsSummary);
                    private static string FormatWorkbookStatistics(WorkbookStatistics statistics)
                    WorkbookStatisticsFormatter.Format(statistics)
                    _selectAllMenuItem.Header = "Select All";
                    _selectAllMenuItem.Gesture = new KeyGesture(Key.A, KeyModifiers.Meta);
                    _selectAllMenuItem.Click += (_, _) => SelectCurrentRegionOrAll();
                    homeMenu.Items.Add(_selectAllMenuItem);
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
                    internal sealed record FindOptionsControls(
                        ComboBox WithinBox,
                        ComboBox SearchBox,
                        ComboBox LookInBox,
                        CheckBox MatchCaseBox,
                        CheckBox MatchEntireCellBox,
                        Control Panel);
                    private sealed record GoToSpecialDialogResult(GoToSpecialKind Kind, GoToSpecialOptions Options);
                    GoToDialogPlanner.BuildReferenceChoices(
                    GoToSpecialDialogPlanner.BuildChoices().ToArray()
                    GoToSpecialDialogPlanner.BuildOptions(choice.Kind, GetValueTypes())
                    private static AvaloniaGrid CreateGoToSpecialChoiceGrid(
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
                    [NativeMenuTopLevelId.Data] = dataMenu,
                    homeMenu.Items.Add(_findMenuItem);
                    homeMenu.Items.Add(_findNextMenuItem);
                    homeMenu.Items.Add(_replaceMenuItem);
                    homeMenu.Items.Add(_goToMenuItem);
                    homeMenu.Items.Add(_goToSpecialMenuItem);
                    _findMenuItem.IsEnabled = isIdle;
                    _findNextMenuItem.IsEnabled = isIdle && !string.IsNullOrWhiteSpace(_session.LastFindText);
                    _replaceMenuItem.IsEnabled = isIdle;
                    _goToMenuItem.IsEnabled = isIdle;
                    _goToSpecialMenuItem.IsEnabled = isIdle;
                    _sortAscendingMenuItem.IsEnabled = isIdle && _session.CanSortSelectedRange;
                    _sortDescendingMenuItem.IsEnabled = isIdle && _session.CanSortSelectedRange;
                    _flashFillMenuItem.IsEnabled = isIdle;
                    case WorkbookApplicationCommandIntent.FlashFill:
                    private void SortSelectedRange(bool ascending)
                    _session.SortSelectedRange(ascending)
                    private void FlashFillSelectedRange()
                    _session.FlashFillSelectedRange()
                    var hasNativeDataMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Data);
                    var hasNativeReviewMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Review);
                    HasNativeDataMenu: hasNativeDataMenu
                    HasNativeReviewMenu: hasNativeReviewMenu
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
                    [NativeMenuTopLevelId.Review] = reviewMenu,
                    var hasNativeReviewMenu = HasNativeTopLevelMenu(NativeMenuTopLevelId.Review);
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
                        => CreateNativeMenu(NativeMenuCatalog.WhatIfAnalysisMenuEntries);
                    private async Task ShowSubtotalDialogAsync()
                    private async Task<SubtotalDialogPlanResult?> ShowSubtotalInputDialogAsync()
                    _session.ExecuteSubtotalOptions(selection.ToInputOptions())
                    _session.RemoveSelectedRangeSubtotals()
                    SubtotalDialogPlanner.TryCreateResult(
                    AutomationProperties.SetAutomationId(dialog, "SubtotalCompactDialog");
                    AutomationProperties.SetAutomationId(groupColumnBox, "SubtotalGroupColumnBox");
                    AutomationProperties.SetAutomationId(functionBox, "SubtotalFunctionBox");
                    AutomationProperties.SetAutomationId(columnsList, "SubtotalColumnsPanel");
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
                    private void NavigateToFindAllMatch(WorkbookFindAllMatch match)
                    FindOptions? options = null,
                    private Task ShowReplaceDialogAsync()
                    {
                        return ShowFindReplaceTabbedDialogAsync(replaceMode: true);
                    }
                    private async Task ShowGoToDialogAsync()
                    private async Task ShowGoToSpecialDialogAsync()
                    private static AvaloniaGrid CreateGoToSpecialChoiceGrid(
                    private static GoToSpecialChoice[] CreateGoToSpecialChoices()
                    private bool SelectGoToSpecial(GoToSpecialKind kind, GoToSpecialOptions? options = null)
                    private async Task<string?> ShowSingleInputDialogAsync(
                    "FindTextBox"
                    "FindNextButton"
                    "FindAllButton"
                    CreateFindOptionsControls("Find", defaultLookInIndex: 0)
                    StyleDiff? findFormat = null;
                    CreateFindReplaceFormatButton(
                        "FindChooseFormatFromCellButton",
                        FindReplaceText(FindReplaceDialogText.ChooseFromCell));
                    CreateFindReplaceFormatButton(
                        "FindClearFormatButton",
                        UiText.Get("FindReplace_ClearFormat"));
                    _session.CreateFormatDiffFromActiveCell()
                    CreateFindReplaceFormatRow(
                        UiText.Get("FindReplace_FindFormat"),
                        chooseFormatButton,
                        clearFormatButton)
                    {automationPrefix}WithinBox
                    {automationPrefix}SearchBox
                    {automationPrefix}LookInBox
                    {automationPrefix}MatchCaseBox
                    {automationPrefix}MatchEntireCellBox
                    "FindReplaceResultsList"
                    "ReplaceFindTextBox"
                    "ReplaceWithTextBox"
                    "ReplaceButton"
                    "ReplaceAllButton"
                    CreateFindOptionsControls("Replace", defaultLookInIndex: 1)
                    StyleDiff? replacementFormat = null;
                    CreateFindReplaceFormatButton(
                        "ReplaceFindChooseFormatFromCellButton",
                        FindReplaceText(FindReplaceDialogText.ChooseFromCell));
                    CreateFindReplaceFormatButton(
                        "ReplaceFindClearFormatButton",
                        UiText.Get("FindReplace_ClearFormat"));
                    CreateFindReplaceFormatButton(
                        "ReplaceWithChooseFormatFromCellButton",
                        FindReplaceText(FindReplaceDialogText.ChooseFromCell));
                    CreateFindReplaceFormatButton(
                        "ReplaceWithClearFormatButton",
                        UiText.Get("FindReplace_ClearFormat"));
                    CreateFindReplaceFormatRow(
                        UiText.Get("FindReplace_ReplaceFormat"),
                        chooseReplaceFormatButton,
                        clearReplaceFormatButton)
                    "GoToReferenceBox"
                    "GoToSpecialKindBox"
                    "GoToSpecialNumbersBox"
                    "GoToSpecialTextBox"
                    "GoToSpecialLogicalsBox"
                    "GoToSpecialErrorsBox"
                    "GoToSpecialOkButton"
                    private FindOptions CreateFindOptions(
                    IReadOnlyList<GridRange>? selectionScope = null)
                    CreateFindOptions(optionsControls, findFormat, selectionScopeAtOpen)
                    FindReplaceDialogPlanner.CreateFindOptions(
                    requiredFormat: requiredFormat,
                    selectionScope: selectionScope);
                    private static FindOptionsControls CreateFindOptionsControls(string automationPrefix, int defaultLookInIndex)
                    private static Button CreateFindReplaceFormatButton(string automationId, string content)
                    private static StackPanel CreateFindReplaceFormatRow(string label, Button chooseButton, Button clearButton)
                    private static void UpdateFindReplaceFormatState(StyleDiff? format, Button chooseButton, Button clearButton)
                    FindLookIn.Formulas
                    FindLookIn.Notes
                    FindLookIn.Comments
                    var result = _session.FindNext(searchText, options, matchCase, matchEntireCell);
                    var result = _session.FindAll(search.FindText, search.Options, search.MatchCase, search.MatchEntireCell);
                    resultsList.ItemsSource = result.Matches;
                    var result = _session.GoToCell(match.Address);
                    _session.ReplaceNextValue(
                    _session.ReplaceAllValues(
                    var result = _session.GoToReference(reference);
                    var result = _session.GoToSpecial(kind, options);
                    result.SelectedRanges.Count == 1
                    e.Key == Key.F5;
                    args.Key == Key.Oem1 && args.KeyModifiers == KeyModifiers.Alt;
                    SelectGoToSpecial(GoToSpecialKind.VisibleCellsOnly);
                    case WorkbookApplicationCommandIntent.Find:
                    e.Key == Key.G && e.KeyModifiers == KeyModifiers.Meta;
                    case WorkbookApplicationCommandIntent.Replace:
                    case WorkbookApplicationCommandIntent.GoTo:
                    e.Key is Key.Z or Key.Y or Key.X or Key.C or Key.V or Key.A or Key.B or Key.D or Key.E or Key.I or Key.R or Key.U;
                    else if (e.Key == Key.A && HasOnlyCommandModifier(e.KeyModifiers)) { }
                    case WorkbookApplicationCommandIntent.FillDown:
                    case WorkbookApplicationCommandIntent.FillRight:
                    Header = UiText.Get("Backstage_Home_NoRecentWorkbooks"),
                    OpenRecentWorkbookMenuPlanner.Create(
                    _recentFiles.Snapshot()
                    File.Exists
                    path => _fileWorkflow.TryResolveOpenTarget(path, out var target, out _) ? target!.Path : null
                    plan.ItemCount == 0
                    foreach (var entry in plan.Items)
                    var fileAccessIdentity = entry.FileAccessIdentity;
                    Header = entry.Header
                    if (!_fileWorkflow.TryResolveOpenTarget(path, fileAccessIdentity, out var target, out _)
                    await OpenWorkbookPathAsync(target.Path, target.FileAccessIdentity);
                    _fileWorkflow.RegisterRecentFile(
                    new RecentFileRegistrationRequest(
                    FileAccessIdentity: fileAccessIdentity ?? target.FileAccessIdentity
                    _closeWorkbookMenuItem.Click += async (_, _) => await ExecuteBackstageCommandWorkflowAsync(FreeXBackstageCommandId.Close);
                    var fileMenu = CreateNativeFileMenu();
                    NativeFileMenuItemId.NewWorkbook => _newWorkbookMenuItem,
                    NativeFileMenuItemId.CloseWorkbook => _closeWorkbookMenuItem,
                    _sessionFactory.CreateNew(viewportHeight, viewportWidth, includeObjects: true);
                    RefreshViewportSizeForZoom();
                    Closing += MainWindow_Closing;
                    UiText.Get("DirtyWorkbook_CloseWorkbookTitle"),
                    UiText.Get("DirtyWorkbook_DiscardAndClose")))
                    ResetToNewWorkbook("Closed workbook.");
                    UiText.Get("DirtyWorkbook_CloseFreeXTitle"),
                    UiText.Get("DirtyWorkbook_DiscardAndClose")))
                    TryQuitApplicationAsync().ToString();
                    UiText.Get("DirtyWorkbook_QuitFreeXTitle"),
                    UiText.Get("DirtyWorkbook_DiscardAndQuit")))
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
                    WindowTitlePlanner.Compose(
                    applicationName: ApplicationTitle
                    groupSuffix: _session.IsWorkbookGrouped ? GroupTitleSuffix : ""
                    applicationPlacement: WindowTitleApplicationPlacement.DocumentThenApplication
                    Title = FormatWindowWorkbookTitle();
                    var isGroupedTab = tab.IsGrouped && _session.IsWorkbookGrouped;
                    tab.TabColor is { } tabColor ? Brush(tabColor) : Brushes.Transparent;
                    var clearColorItem = new NativeMenuItem { Header = UiText.Get("RibbonWire_TabColorNone") };
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
                    [NativeMenuTopLevelId.View] = viewMenu,
                    [NativeMenuTopLevelId.Sheet] = sheetMenu,
                    var result = _session.AddSheet(insertBeforeSheetId);
                    var result = _session.RenameActiveSheet(newName);
                    ShowRenameSheetDialogAsync(currentName).ToString();
                    AutomationProperties.SetAutomationId(nameBox, "RenameSheetNameBox");
                    var validationError = _session.Workbook.ValidateSheetName(proposedName, _session.ActiveSheet.Id);
                    _sheetGridHost.Focusable = true;
                    AutomationProperties.SetName(_sheetGridHost, UiText.Get("MainWindow_AutomationName_Worksheet"));
                    _zoomText.Focusable = true;
                    AutomationProperties.SetName(_zoomText, UiText.CreateAutomationName(UiText.Get("Common_Zoom")));
                    Focusable = true,
                    Tag = tab.Id,
                    button.ContextMenu = CreateSheetTabContextMenu(tab);
                    button.DoubleTapped += async (_, args) => await RenameSheetFromTabAsync(tab.Id, args);
                    button.KeyDown += (_, args) => HandleSheetTabKeyDown(tab.Id, button, args);
                    AutomationProperties.SetName(button, tab.Name);
                    AutomationProperties.SetHelpText(button, UiText.Get("SheetTabs_ContextHelpText"));
                    ItemsSource = CreateSheetTabContextMenuItems(tab, isIdle, sheetTabIndex).ToArray();
                    SheetTabContextMenuPlanner.BuildSheetTabCommands(
                    string Header(SheetTabContextMenuAction action) => UiText.Get(Common(action).ResourceKey);
                    bool Enabled(SheetTabContextMenuAction action) => isIdle && Common(action).IsEnabled;
                    CreateSheetTabContextMenuItem(tab, Header(SheetTabContextMenuAction.Rename), async () => await RenameActiveSheetAsync(), Enabled(SheetTabContextMenuAction.Rename));
                    CreateSheetTabColorContextMenuItem(tab, Header(SheetTabContextMenuAction.TabColor), Enabled(SheetTabContextMenuAction.TabColor));
                    CreateSheetTabContextMenuItem(tab, UiText.Get("MainWindow_Header_MoveLeft"), MoveActiveSheetLeft, isIdle && sheetTabIndex > 0);
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
                    SheetTabFocusPlanner.AdjacentTab(_session.SheetTabs, sheetId, direction, static tab => tab.Id);
                    SheetTabFocusPlanner.EdgeTab(_session.SheetTabs, first, static tab => tab.Id);
                    FocusFirstEnabledSheetTabMenuItem(items);
                    private static void FocusFirstEnabledSheetTabMenuItem(IEnumerable<Control> items);
                    foreach (var item in items);
                    item is MenuItem { IsEnabled: true } menuItem;
                    menuItem.Focus();
                    button.Tag is SheetId tag &&
                    tag == sheetId;
                    if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed);
                    var selectRange = modifiers.HasFlag(KeyModifiers.Shift);
                    var toggle = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
                    args.Handled = true;
                    _session.SelectSheetFromTab(sheetId, selectRange, toggle);
                    var result = _session.DuplicateSelectedSheets();
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
                    ApplyZoomPercent(_session.ZoomPercent + StatusBarZoomSliderPlanner.ZoomStepPercent, "Zoom In failed.");
                    ZoomOut();
                    ApplyZoomPercent(_session.ZoomPercent - StatusBarZoomSliderPlanner.ZoomStepPercent, "Zoom Out failed.");
                    ZoomTo100Percent();
                    ApplyZoomPercent(100, "100% Zoom failed.");
                    ZoomToSelection();
                    ApplyZoomPercent(zoomPercent, "Zoom to Selection failed.");
                    var result = _session.SetZoomPercent(zoomPercent);
                    CalculateZoomAxisFitPercent(viewportWidth, range.ColCount, ZoomToSelectionDefaultColumnWidth);
                    _zoomText.Text = StatusBarZoomSliderPlanner.FormatZoomPercent(_session.ZoomPercent);
                    var showHeadings = _session.ActiveSheet.ShowHeadings;
                    var zoomFactor = GetActiveZoomFactor();
                    CellSurfaceGridlinePlanner.HasVisibleFill(style, _session.Workbook.Theme);
                    BorderBrush = showGridlines ? defaultBorderBrush : Brushes.Transparent;
                    CalculateDisplayedGridWidth(viewport, showHeadings, zoomFactor);
                    CalculateDisplayedGridHeight(viewport, showHeadings, zoomFactor);
                    fontSize * zoomFactor;
                    displayHeight / zoomFactor;
                    var cellControl = CreateCell(cell, row, col, zoomFactor, colWidth, rowHeight, mergeRegion);
                    AddGridChild(grid, cellControl, rowIndex + headerOffset, colIndex + headerOffset);
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
                    if (IsPivotFieldPaneFocused())
                    case WorkbookApplicationCommandIntent.SelectPreviousSheetGroup:
                        SelectAdjacentVisibleSheetFromKeyboard(request.Direction, selectRange: true);
                    case WorkbookApplicationCommandIntent.SelectNextSheetGroup:
                        SelectAdjacentVisibleSheetFromKeyboard(request.Direction, selectRange: true);
                    case WorkbookApplicationCommandIntent.ActivatePreviousSheet:
                        SelectAdjacentVisibleSheetFromKeyboard(request.Direction, selectRange: false);
                    case WorkbookApplicationCommandIntent.ActivateNextSheet:
                        SelectAdjacentVisibleSheetFromKeyboard(request.Direction, selectRange: false);
                    _helpOnlineMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.HelpUrl, UiText.Get("MainWindow_Content_HelpOnline"));
                    _sendFeedbackMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.FeedbackUrl, UiText.Get("MainWindow_Content_Feedback"));
                    _checkForUpdatesMenuItem.Click += async (_, _) => await OpenExternalHelpLinkAsync(AppHelpInfo.LatestReleaseUrl, UiText.Get("MainWindow_Content_CheckForUpdates"));
                    _aboutMenuItem.Click += async (_, _) => await ShowAboutDialogAsync();
                    _legalNoticesMenuItem.Click += async (_, _) => await ShowLegalNoticesDialogAsync();
                    _minimizeWindowMenuItem.Gesture = new KeyGesture(Key.M, KeyModifiers.Meta);
                    _minimizeWindowMenuItem.Click += (_, _) => WindowState = WindowState.Minimized;
                    _zoomWindowMenuItem.Header = "Zoom";
                    _bringAllToFrontMenuItem.Header = "Bring All to Front";
                    [NativeMenuTopLevelId.Window] = windowMenu,
                    [NativeMenuTopLevelId.Help] = helpMenu,
                    TopLevel.GetTopLevel(this)?.Launcher.ToString();
                    AppHelpInfo.BuildAboutText(versionText, PlatformAboutSummary);
                    var documents = LegalNoticeProvider.GetDocuments();
                    documents.Select(document => document.Title);
                    ItemsSource = documents.Select(CreateLegalNoticeTabItem).ToList(),
                    AutomationProperties.SetAutomationId(tabControl, "LegalNoticesSectionTabs");
                    HasFocusableSheetTab: access.HasSheetTab(button => button.Focusable)
                    HasFocusableActiveSheetTab: access.ActiveSheetTab?.Focusable == true
                    HasShellFocusCycleTargets: _sheetGridHost.Focusable &&
                    access.ToolbarFocusTargets.Any(control => control.Focusable) &&
                    HasNativeWindowMenu: hasNativeWindowMenu;
                    HasNativeMinimizeWindowMenuItem: HasNativeMenuItem(_minimizeWindowMenuItem, "Minimize");
                    HasNativeZoomWindowMenuItem: HasNativeMenuItem(_zoomWindowMenuItem, "Zoom", requireGesture: false);
                    HasNativeBringAllToFrontMenuItem: HasNativeMenuItem(_bringAllToFrontMenuItem, "Bring All to Front", requireGesture: false);
                    GetToolbarFocusTargets().Any(control => control.Focusable) &&;
                    _formulaBox.Focusable &&;
                    _zoomText.Focusable;
                    HasFormatPainterButton: _formatPainterButton.Content?.ToString() == UiText.Get("MainWindow_TooltipTitle_FormatPainter");
                    HasNativeFormatPainterMenuItem: HasNativeMenuItem(_formatPainterMenuItem, "Format Painter", requireGesture: false);
                    private readonly NativeMenuItem _formatCellsMenuItem = new();
                    _formatCellsMenuItem.Header = "Format Cells...";
                    _formatCellsMenuItem.Gesture = new KeyGesture(Key.D1, KeyModifiers.Meta);
                    _formatCellsMenuItem.Click += async (_, _) => await ShowFormatCellsDialogAsync();
                    homeMenu.Items.Add(_formatCellsMenuItem);
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
                    new FormatCellsCompactDialogInput(
                    FormatCellsDialogPlanner.TryCreateCompactPlan(plannerInput
                    UseNormalFont: normalFont
                    FontNameText: fontNameBox.Text
                    FontColor: (fontColorBox.SelectedItem as FormatCellsColorChoice)?.Color
                    SelectFormatCellsColor(fontColorBox, normal.FontColor)
                    FillPatternStyle: SelectedFormatCellsValue(currentFillStyle.FillPatternStyle, fillPatternStyleBox)
                    FillPatternColorText: fillEditor.PatternColorTextBox.Text
                    CreateFormatCellsField("Pattern style", fillPatternStyleBox)
                    CreateFormatCellsField("Pattern color", fillPatternColorBox)
                    private static IReadOnlyList<FormatCellsNullableChoice<CellFillPatternStyle>> CreateFormatCellsFillPatternStyleChoices()
                    CellFillPatternStyle.DarkTrellis
                    HasFillCellsButton: _fillCellsButton.Content?.ToString() == "Fill Cells";
                    HasFillDownMenuItem: HasToolbarMenuItem(_fillDownFlyoutItem, "Down");
                    HasFillRightMenuItem: HasToolbarMenuItem(_fillRightFlyoutItem, "Right");
                    HasFillUpMenuItem: HasToolbarMenuItem(_fillUpFlyoutItem, "Up");
                    HasFillLeftMenuItem: HasToolbarMenuItem(_fillLeftFlyoutItem, "Left");
                    HasNativeFillCellsMenuItem: HasNativeMenuItem(_fillCellsMenuItem, NativeMenuItemId.FillCells);
                    HasNativeFillDownMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillDown);
                    HasNativeFillRightMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillRight);
                    HasNativeFillUpMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillUp);
                    HasNativeFillLeftMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillLeft);
                    HasClearButton: _clearButton.Content?.ToString() == "Clear";
                    HasClearAllMenuItem: HasToolbarMenuItem(_clearAllFlyoutItem, "Clear All");
                    HasNativeClearMenuItem: HasNativeMenuItem(_clearMenuItem, NativeMenuItemId.Clear);
                    HasNativeClearHyperlinksMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, NativeMenuItemId.ClearHyperlinks);
                    HasBordersButton: _bordersButton.Content?.ToString() == "Borders";
                    HasNativeBordersMenuItem: HasNativeMenuItem(_bordersMenuItem, "Borders", requireGesture: false);
                    NativeBordersPresetCount: nativeBordersPresetCount;
                    _mergeAndCenterButton.Content = UiText.Get("MainWindow_Text_MergeCenter");
                    AutomationProperties.SetAutomationId(_mergeAndCenterButton, "HomeMergeAndCenterButton");
                    AutomationProperties.SetHelpText(_mergeAndCenterButton, UiText.Get("Toolbar_MergeCenterHelpText"));
                    _mergeAndCenterMenuItem.Header = "Merge & Center";
                    _mergeAndCenterMenuItem.Click += async (_, _) => await MergeAndCenterSelectedRangeAsync();
                    _unmergeCellsMenuItem.Header = "Unmerge Cells";
                    _unmergeCellsMenuItem.Click += (_, _) => UnmergeSelectedRange();
                    homeMenu.Items.Add(_mergeAndCenterMenuItem);
                    homeMenu.Items.Add(_unmergeCellsMenuItem);
                    _mergeAndCenterButton.IsEnabled = isIdle;
                    _mergeAndCenterMenuItem.IsEnabled = _mergeAndCenterButton.IsEnabled;
                    _unmergeCellsMenuItem.IsEnabled = isIdle && _session.IsSelectedRangeMerged;
                    ShowMergeCellsContentWarningDialogAsync(contentPlan)
                    MergeCellsContentWarningDialog
                    HasMergeAndCenterButton: _mergeAndCenterButton.Content?.ToString() == "Merge & Center";
                    AutomationProperties.SetAutomationId(_formulaBox, "FormulaBox");
                    AutomationProperties.SetName(_formulaBox, FormulaBarText(FormulaBarChromePlanner.FormulaBox.AutomationNameResourceKey));
                    AutomationProperties.SetHelpText(_formulaBox, FormulaBarText(FormulaBarChromePlanner.FormulaBox.HelpTextResourceKey));
                    AutomationProperties.SetAutomationId(_statusText, "StatusText");
                    AutomationProperties.SetName(_statusText, UiText.Get("Toolbar_StatusAutomationName"));
                    AutomationProperties.SetHelpText(_statusText, UiText.Get("Toolbar_StatusHelpText"));
                    AutomationProperties.SetAutomationId(_cellAddressText, "CellAddressText");
                    AutomationProperties.SetName(_cellAddressText, UiText.Get("Toolbar_CellAddressAutomationName"));
                    AutomationProperties.SetHelpText(_cellAddressText, UiText.Get("Toolbar_CellAddressHelpText"));
                    AutomationProperties.SetAutomationId(_selectionStatsText, "SelectionStatsText");
                    AutomationProperties.SetName(_selectionStatsText, UiText.Get("Toolbar_SelectionStatisticsAutomationName"));
                    AutomationProperties.SetHelpText(_selectionStatsText, UiText.Get("Toolbar_SelectionStatisticsHelpText"));
                    HasFormulaBoxAutomationName: string.Equals(AutomationProperties.GetName(_formulaBox), FormulaBarText(FormulaBarChromePlanner.FormulaBox.AutomationNameResourceKey), StringComparison.Ordinal)
                    HasFormulaBoxAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_formulaBox), FormulaBarText(FormulaBarChromePlanner.FormulaBox.HelpTextResourceKey), StringComparison.Ordinal)
                    HasFormulaBoxAutomationId: string.Equals(AutomationProperties.GetAutomationId(_formulaBox), "FormulaBox", StringComparison.Ordinal)
                    HasStatusTextAutomationName: string.Equals(AutomationProperties.GetName(_statusText), "Status", StringComparison.Ordinal)
                    HasStatusTextAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_statusText), "Shows the current workbook status.", StringComparison.Ordinal)
                    HasStatusTextAutomationId: string.Equals(AutomationProperties.GetAutomationId(_statusText), "StatusText", StringComparison.Ordinal)
                    HasStatusTextValue: HasStatusBarAccessibleValue(_statusText, _selectionStatsText)
                    private static bool HasStatusBarAccessibleValue(TextBlock statusText, TextBlock selectionStatsText) =>
                        !string.IsNullOrWhiteSpace(statusText.Text) ||
                        !string.IsNullOrWhiteSpace(selectionStatsText.Text);
                    HasCellAddressAutomationName: string.Equals(AutomationProperties.GetName(_cellAddressText), "Cell address", StringComparison.Ordinal)
                    HasCellAddressAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_cellAddressText), "Shows the active cell address.", StringComparison.Ordinal)
                    HasCellAddressAutomationId: string.Equals(AutomationProperties.GetAutomationId(_cellAddressText), "CellAddressText", StringComparison.Ordinal)
                    HasSelectionStatsAutomationName: string.Equals(AutomationProperties.GetName(_selectionStatsText), "Selection statistics", StringComparison.Ordinal)
                    HasSelectionStatsAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_selectionStatsText), "Shows statistics for the current selection.", StringComparison.Ordinal)
                    HasSelectionStatsAutomationId: string.Equals(AutomationProperties.GetAutomationId(_selectionStatsText), "SelectionStatsText", StringComparison.Ordinal)
                    HasNativeMergeAndCenterMenuItem: HasNativeMenuItem(_mergeAndCenterMenuItem, "Merge & Center", requireGesture: false);
                    HasNativeUnmergeCellsMenuItem: HasNativeMenuItem(_unmergeCellsMenuItem, "Unmerge Cells", requireGesture: false);
                    HasSheetTabContextKeyboardHelp: access.HasSheetTab(button =>
                    string.Equals(
                        AutomationProperties.GetHelpText(button),
                        UiText.Get("SheetTabs_ContextHelpText"),
                        StringComparison.Ordinal));
                    HasSheetTabContextRenameMenuItem: access.HasSheetTabContextMenuItem(UiText.Get("MainWindow_Header_Rename"))
                    HasSheetTabContextTabColorMenuItem: access.HasSheetTabContextMenuItem(UiText.Get("MainWindow_Header_TabColor"))
                    HasSheetTabContextNoColorMenuItem: access.HasSheetTabContextSubmenuItem(
                        UiText.Get("MainWindow_Header_TabColor"),
                        UiText.Get("RibbonWire_TabColorNone"))
                    HasSheetTabContextSelectAllSheetsMenuItem: access.HasSheetTabContextMenuItem(UiText.Get("MainWindow_Header_SelectAllSheets"))
                    HasSheetTabContextUngroupSheetsMenuItem: access.HasSheetTabContextMenuItem(UiText.Get("MainWindow_Header_UngroupSheets"))
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
                    var result = _session.ApplySelectedRangeCompactFormat(
                        new StyleDiff(),
                        preset,
                        _borderPickerSession.Style,
                        _borderPickerSession.Color);
                }
                private async Task MergeAndCenterSelectedRangeAsync()
                {
                    var result = _session.MergeAndCenterSelectedRange(contentResolution);
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
                private async Task OpenRecentWorkbookAsync(string path, WorkbookFileAccessIdentity? fileAccessIdentity = null) => await Task.CompletedTask;
                private void RecordStartupRecentWorkbook(StartupWorkbookLoadResult source) { }
                private void RecordRecentWorkbook(string path, WorkbookFileAccessIdentity? fileAccessIdentity = null) { }
                private async Task CreateNewWorkbookAsync() => await Task.CompletedTask;
                private async Task CloseWorkbookAsync() => await Task.CompletedTask;
                private void ResetToNewWorkbook(string status) { }
                private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e) => await Task.CompletedTask;
                private async Task TryQuitApplicationAsync() => await Task.CompletedTask;
                private async Task<bool> ConfirmBeforeDestructiveWorkbookActionAsync(string title, string discardButtonText) => await Task.FromResult(true);
                private async Task<DirtyWorkbookCloseChoice> ShowDirtyWorkbookCloseDialogAsync(string title, string discardButtonText) => await Task.FromResult(DirtyWorkbookCloseChoice.Cancel);
                _fileWorkflow.CanProceedAfterDirtyGateWithCleanSaveAsync(
                SaveCurrentWorkbookAsync,
                private async Task SaveCurrentWorkbookAsync() => await Task.CompletedTask;
                private async Task RenameActiveSheetAsync() => await Task.CompletedTask;
                private async Task<string?> ShowRenameSheetDialogAsync(string currentName) => await Task.FromResult<string?>(currentName);
                private async Task PasteSpecialExternalTextFromClipboardAsync(string label) => await Task.CompletedTask;
                private async Task UnhideSheetAsync() => await Task.CompletedTask;
                private async Task<WorkbookHiddenSheet?> ShowUnhideSheetDialogAsync(IReadOnlyList<WorkbookHiddenSheet> hiddenSheets) => await Task.FromResult<WorkbookHiddenSheet?>(null);
                private ContextMenu CreateSheetTabContextMenu(WorkbookSheetTab tab) => new();
                private IEnumerable<Control> CreateSheetTabContextMenuItems(WorkbookSheetTab tab, bool isIdle, int sheetTabIndex) => [];
                private MenuItem CreateSheetTabContextMenuItem(WorkbookSheetTab tab, string header, Action action, bool isEnabled) => new();
                internal bool SelectSheetForContextCommand(SheetId sheetId) => true;
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
                private static void SheetTabContextMenu_Opened(object? sender, RoutedEventArgs args) { FocusFirstEnabledSheetTabMenuItem([]); }
                private static void FocusFirstEnabledSheetTabMenuItem(IEnumerable<Control> items) { foreach (var item in items) { if (item is MenuItem { IsEnabled: true } menuItem) { menuItem.Focus(); return; } } }
                private Button? FindSheetTabButton(SheetId sheetId) => button.Tag is SheetId tag &&
                    tag == sheetId ? new() : null;
                private bool HasSheetTabButton(Func<Button, bool> predicate) => true;
                (_, args) => BeginSheetTabPointer(tab.Id, args),
                if (args.ClickCount >= 2) { }
                InputElement.PointerPressedEvent,
                private void BeginSheetTabPointer(SheetId sheetId, PointerPressedEventArgs args) { }
                if (!point.Properties.IsLeftButtonPressed) { }
                if (SelectSheetForContextCommand(sheetId)) { }
                _ = RenameActiveSheetAsync();
                private NativeMenu CreateNativeSheetTabColorMenu() => new();
                private NativeMenuItem CreateNativeSheetTabColorSwatchMenuItem(CellColorSwatch swatch) => new();
                private void ApplyActiveSheetTabColor(CellColor? color) { }
                private void SelectAllVisibleSheets() { }
                private void UngroupSheets() { }
                private void ToggleShowGridlines() { }
                private void ToggleShowHeadings() { }
                var showHeadings = _session.IsShowingHeadings;
                private void ZoomIn() => ApplyZoomPercent(_session.ZoomPercent + StatusBarZoomSliderPlanner.ZoomStepPercent, "Zoom In failed.");
                private void ZoomOut() => ApplyZoomPercent(_session.ZoomPercent - StatusBarZoomSliderPlanner.ZoomStepPercent, "Zoom Out failed.");
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
                private void CycleShellFocus(bool reverse)
                {
                    ShellFocusCyclePlanner.TryFocusNextAvailable(
                        GetCurrentShellFocusTarget(),
                        reverse,
                        IsShellFocusTargetAvailable,
                        FocusShellRegion);
                }
                private bool IsShellFocusTargetAvailable(ShellFocusTarget target) =>
                    target != ShellFocusTarget.TaskPane ||
                    _pivotFieldPaneHost.IsVisible;
                private ShellFocusTarget GetCurrentShellFocusTarget() => ShellFocusTarget.Worksheet;
                private bool FocusShellRegion(ShellFocusTarget target) => target switch
                {
                    ShellFocusTarget.Ribbon => FocusFirstEnabledToolbarControl(),
                    ShellFocusTarget.FormulaBar => FocusControl(_formulaBox),
                    ShellFocusTarget.SheetTabs => FocusActiveSheetTab(),
                    ShellFocusTarget.TaskPane => FocusVisibleTaskPane(),
                    ShellFocusTarget.StatusBar => FocusControl(_zoomText),
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
                private static MacOsLaunchSmokeSnapshot CaptureSnapshot(
                {
                    ExternalImageClipboardPictureCount: shell.ExternalImageClipboardPictureCount;
                    ExternalImageClipboardPicturePngByteCount: shell.ExternalImageClipboardPicturePngByteCount;
                    return new();
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/AboutDialog.cs",
            """
            namespace FreeX.App.Avalonia;

            internal sealed class AboutDialog
            {
                private readonly object _presentation =
                    FreeXAboutDialogPresentation.Create(typeof(AboutDialog).Assembly, "Avalonia");
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/LegalNoticesDialog.cs",
            """
            namespace FreeX.App.Avalonia;

            internal sealed class LegalNoticesDialog : AvaloniaLegalNoticesDialog
            {
                private void PreserveSourceContract()
                {
                    FreeXLegalNoticesPresentation.Create(LegalNoticeProvider.GetDocuments(), UiText.Get);
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/FormatCellsFillEditor.cs",
            """
            namespace FreeX.App.Avalonia;

            internal sealed class FormatCellsFillEditor
            {
                private static void PreserveSourceContract()
                {
                    getText("FormatCells_PatternStyle"),
                    getText("FormatCells_PatternColor2"),
                    private static IReadOnlyList<FormatCellsNullableChoice<CellFillPatternStyle>> CreateFormatCellsFillPatternStyleChoices();
                    CellFillPatternStyle.DarkTrellis;
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/FormatCellsDialogPlanner.cs",
            """
            namespace FreeX.App.Services;

            public static class FormatCellsDialogPlanner
            {
                public static bool TryCreateCompactPlan(
                    FormatCellsCompactDialogInput input,
                    out object? plan,
                    out object? validation)
                {
                    FormatCellsInputParser.TryParseFontSize(input.FontSizeText);
                    MergeCells: Changed(input.InitialMergeCells, input.MergeCells);
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Presentation/Shell/WorkbookApplicationCommandRouter.cs",
            """
            namespace FreeX.App.Presentation.Shell;

            public static class WorkbookApplicationCommandBindingFactory
            {
                public static void PreserveSourceContract()
                {
                    WorkbookApplicationCommandBindingFactory.Create(
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Presentation/Shell/WorkbookApplicationWorkareaCommandEndpoint.cs",
            """
            namespace FreeX.App.Presentation.Shell;

            public static class WorkbookApplicationWorkareaCommandDispatcher
            {
                private static void PreserveSourceContract()
                {
                    new WorkbookApplicationWorkareaCommandEndpointProfile
                    WorkbookApplicationCommandIntent.WorkbookStatistics =>
                    WorkbookApplicationCommandIntent.FlashFill =>
                    WorkbookApplicationCommandIntent.Find =>
                    WorkbookApplicationCommandIntent.Replace =>
                    WorkbookApplicationCommandIntent.GoTo =>
                    WorkbookApplicationCommandIntent.FillDown =>
                    WorkbookApplicationCommandIntent.FillRight =>
                    SelectAdjacentVisibleSheetFromKeyboard(direction, selectRange: true)
                    SelectAdjacentVisibleSheetFromKeyboard(direction, selectRange: false)
                }
            }
            """);

        WriteFile(
            root,
            "shared/Free.Shared.Shell.Avalonia/AvaloniaLegalNoticesDialog.cs",
            """
            namespace Free.Shared.Shell.Avalonia;

            public class AvaloniaLegalNoticesDialog
            {
                private void PreserveSourceContract()
                {
                    AutomationProperties.SetAutomationId(_tabControl, LegalNoticesDialogPresentation.SectionsAutomationId);
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Presentation/Shell/NativeMenuCatalog.cs",
            """
            namespace FreeX.App.Presentation.Shell;

            public static class NativeMenuCatalog
            {
                public static IReadOnlyList<NativeMenuTopLevelPlan> TopLevelMenus { get; } =
                [
                    new(NativeMenuTopLevelId.File, "File"),
                    new(NativeMenuTopLevelId.Data, "Data"),
                    new(NativeMenuTopLevelId.Review, "Review"),
                    new(NativeMenuTopLevelId.View, "View"),
                    new(NativeMenuTopLevelId.Sheet, "Sheet"),
                    new(NativeMenuTopLevelId.Window, "Window"),
                    new(NativeMenuTopLevelId.Help, "Help")
                ];

                public static IReadOnlyList<NativeFileMenuEntryPlan> FileMenuEntries { get; } =
                [
                    FileItem(NativeFileMenuItemId.NewWorkbook),
                    FileItem(NativeFileMenuItemId.OpenRecent),
                    FileItem(NativeFileMenuItemId.ShareWorkbook),
                    FileItem(NativeFileMenuItemId.ExportPdf),
                    FileItem(NativeFileMenuItemId.WorkbookStatistics),
                    FileItem(NativeFileMenuItemId.CloseWorkbook)
                ];

                private static readonly object[] Metadata =
                [
                    "AvaloniaNativeMenu_OpenRecent",
                    "AvaloniaNativeMenu_ExportPdf",
                    "AvaloniaNativeMenu_WorkbookStatistics",
                    NativeMenuGesture(WorkbookShortcutRoute.WorkbookStatistics),
                    new(NativeFileMenuItemId.WorkbookStatistics, context.IsIdle),
                    new(NativeFileMenuItemId.ExportPdf, context.IsIdle && context.CanSaveThroughStorageProvider),
                    "public static IReadOnlyList<NativeMenuEntryPlan> HomeMenuEntries",
                    "public static IReadOnlyList<NativeMenuEntryPlan> DataMenuEntries",
                    "public static IReadOnlyList<NativeMenuEntryPlan> ReviewMenuEntries",
                    "public static IReadOnlyList<NativeMenuEntryPlan> ViewMenuEntries",
                    "public static IReadOnlyList<NativeMenuEntryPlan> SheetMenuEntries",
                    "public static IReadOnlyList<NativeMenuEntryPlan> WindowMenuEntries",
                    "public static IReadOnlyList<NativeMenuEntryPlan> HelpMenuEntries",
                    "public static IReadOnlyList<NativeMenuEntryPlan> FillCellsMenuEntries",
                    "public static IReadOnlyList<NativeMenuEntryPlan> ClearMenuEntries",
                    "public static IReadOnlyList<NativeMenuEntryPlan> WhatIfAnalysisMenuEntries",
                    "public static IReadOnlyList<NativeMenuEntryPlan> FormulasMenuEntries",
                    "public static IReadOnlyList<NativeMenuEntryPlan> AutoSumMenuEntries",
                    "new(NativeMenuItemId.SelectAll, "Select All", new NativeMenuGesturePlan(NativeMenuGestureKey.A, NativeMenuGestureModifiers.Meta))",
                    "new(NativeMenuItemId.Find, "Find...", NativeMenuGesture(WorkbookShortcutRoute.Find))",
                    "new(NativeMenuItemId.FillCells, "Fill", RequiresGestureInSmoke: false)",
                    "new(NativeMenuItemId.FillDown, "Down", NativeMenuGesture(WorkbookShortcutRoute.FillDown))",
                    "new(NativeMenuItemId.Clear, "Clear", RequiresGestureInSmoke: false)",
                    "new(NativeMenuItemId.ClearContents, "Clear Contents", new NativeMenuGesturePlan(NativeMenuGestureKey.Delete))",
                    "new(NativeMenuItemId.AutoSum, "AutoSum", RequiresGestureInSmoke: false)",
                    "new(NativeMenuItemId.AutoSumSum, "Sum", NativeMenuGesture(WorkbookShortcutRoute.AutoSum))",
                    "new(NativeMenuItemId.SortAscending, "Sort A to Z", RequiresGestureInSmoke: false)",
                    "new(NativeMenuItemId.FlashFill, "Flash Fill", NativeMenuGesture(WorkbookShortcutRoute.FlashFill))",
                    "new(NativeMenuItemId.RemoveDuplicates, "Remove Duplicates...", RequiresGestureInSmoke: false)",
                    "new(NativeMenuItemId.Subtotal, "Subtotal...", RequiresGestureInSmoke: false)",
                    "new(NativeMenuItemId.ReviewSummary, "Review Summary...", RequiresGestureInSmoke: false)",
                    "new(NativeMenuItemId.ShowGridlines, "Gridlines", RequiresGestureInSmoke: false)",
                    "new(NativeMenuItemId.ZoomIn, "Zoom In", new NativeMenuGesturePlan(NativeMenuGestureKey.OemPlus, NativeMenuGestureModifiers.Meta))",
                    "new(NativeMenuItemId.FreezePanes, "Freeze Panes", RequiresGestureInSmoke: false)",
                    "new(NativeMenuItemId.MinimizeWindow, "Minimize", new NativeMenuGesturePlan(NativeMenuGestureKey.M, NativeMenuGestureModifiers.Meta))",
                    "new(NativeMenuItemId.HelpOnline, "Help Online", new NativeMenuGesturePlan(NativeMenuGestureKey.F1))",
                    "Item(NativeMenuItemId.FormatPainter)",
                    "Item(NativeMenuItemId.FormatCells)",
                    "Item(NativeMenuItemId.FillCells)",
                    "Item(NativeMenuItemId.Clear)",
                    "Item(NativeMenuItemId.AutoSum)",
                    "Item(NativeMenuItemId.SortAscending)",
                    "Item(NativeMenuItemId.ReviewSummary)",
                    "Item(NativeMenuItemId.ShowGridlines)",
                    "Item(NativeMenuItemId.TabColor)",
                    "Item(NativeMenuItemId.MinimizeWindow)",
                    "Item(NativeMenuItemId.HelpOnline)",
                    "new(NativeMenuItemId.FormatPainter, context.CanFormatPainter)",
                    "new(NativeMenuItemId.SortAscending, context.IsIdle && context.CanSortSelectedRange)",
                    "new(NativeMenuItemId.RemoveDuplicates, context.IsIdle && context.SelectedRangeRowCount > 1)",
                    "new(NativeMenuItemId.FillCells, context.CanFillCells)",
                    "new(NativeMenuItemId.Clear, context.CanClear)",
                    "new(NativeMenuItemId.AutoSum, context.IsIdle)",
                    "new(NativeMenuItemId.ShowGridlines, context.IsIdle, context.IsShowingGridlines)",
                    "new(NativeMenuItemId.MinimizeWindow, true)",
                    "new(NativeMenuItemId.HelpOnline, true)"
                ];
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
            "tools/FreeX.Validation.Avalonia/MacOsLaunchSmoke.cs",
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

                public static void Start(MainWindow.RendererValidationAccess access, MacOsLaunchSmokeOptions options, LocalAppDiagnostics? diagnostics = null)
                {
                    RunAsync(access, options, diagnostics);
                }

                private static void RunAsync(MainWindow.RendererValidationAccess access, MacOsLaunchSmokeOptions options, LocalAppDiagnostics? diagnostics)
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
                    HasNativeDockMenu &&
                    HasNativeDockFileMenu &&
                    NativeDockFileMenuItemCount > 0 &&
                    HasNativeFileMenu &&
                    HasNativeHomeMenu &&
                    HasNativeInsertMenu &&
                    HasNativePageLayoutMenu &&
                    HasNativeFormulasMenu &&
                    HasNativeDataMenu &&
                    HasNativeReviewMenu &&
                    HasNativeViewMenu &&
                    HasNativeSheetMenu &&
                    HasNativeWindowMenu &&
                    HasNativeHelpMenu &&
                    HasNativeNewWorkbookMenuItem &&
                    HasNativeOpenRecentMenuItem &&
                    NativeOpenRecentItemCount > 0 &&
                    HasNativeExportPdfMenuItem &&
                    HasNativeShareWorkbookMenuItem &&
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
                    HasAccessibilitySmokeEvidence &&
                    HasFormulaBoxAutomationName &&
                    HasFormulaBoxAutomationHelp &&
                    HasFormulaBoxAutomationId &&
                    HasStatusTextAutomationName &&
                    HasStatusTextAutomationHelp &&
                    HasStatusTextAutomationId &&
                    HasStatusTextValue &&
                    HasCellAddressAutomationName &&
                    HasCellAddressAutomationHelp &&
                    HasCellAddressAutomationId &&
                    HasSelectionStatsAutomationName &&
                    HasSelectionStatsAutomationHelp &&
                    HasSelectionStatsAutomationId &&
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
                public bool HasAccessibilitySmokeEvidence =>
                    HasFormulaBoxAutomationName &&
                    HasFormulaBoxAutomationHelp &&
                    HasFormulaBoxAutomationId &&
                    HasStatusTextAutomationName &&
                    HasStatusTextAutomationHelp &&
                    HasStatusTextAutomationId &&
                    HasStatusTextValue &&
                    HasCellAddressAutomationName &&
                    HasCellAddressAutomationHelp &&
                    HasCellAddressAutomationId &&
                    HasSelectionStatsAutomationName &&
                    HasSelectionStatsAutomationHelp &&
                    HasSelectionStatsAutomationId;
                private bool HasNativeFileMenu { get; }
                private bool HasNativeDockMenu { get; }
                private bool HasNativeDockFileMenu { get; }
                private int NativeDockFileMenuItemCount { get; }
                private bool HasNativeHomeMenu { get; }
                private bool HasNativeInsertMenu { get; }
                private bool HasNativePageLayoutMenu { get; }
                private bool HasNativeFormulasMenu { get; }
                private bool HasNativeDataMenu { get; }
                private bool HasNativeViewMenu { get; }
                private bool HasNativeSheetMenu { get; }
                private bool HasNativeWindowMenu { get; }
                private bool HasNativeHelpMenu { get; }
                private bool HasNativeNewWorkbookMenuItem { get; }
                private bool HasNativeOpenRecentMenuItem { get; }
                private int NativeOpenRecentItemCount { get; }
                private bool HasNativeExportPdfMenuItem { get; }
                private bool HasNativeShareWorkbookMenuItem { get; }
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
                private bool HasFormulaBoxAutomationName { get; }
                private bool HasFormulaBoxAutomationHelp { get; }
                private bool HasFormulaBoxAutomationId { get; }
                private bool HasStatusTextAutomationName { get; }
                private bool HasStatusTextAutomationHelp { get; }
                private bool HasStatusTextAutomationId { get; }
                private bool HasStatusTextValue { get; }
                private bool HasStatusBarAccessibleValue() =>
                    !string.IsNullOrWhiteSpace(_statusText.Text) ||
                    !string.IsNullOrWhiteSpace(_selectionStatsText.Text);
                private bool HasCellAddressAutomationName { get; }
                private bool HasCellAddressAutomationHelp { get; }
                private bool HasCellAddressAutomationId { get; }
                private bool HasSelectionStatsAutomationName { get; }
                private bool HasSelectionStatsAutomationHelp { get; }
                private bool HasSelectionStatsAutomationId { get; }
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
                public string DialogReport => "macos_dialog_smoke= macos_dialog_smoke_attempted= macos_dialog_smoke_status= macos_dialog_activation_completed= find_dialog= find_dialog_text_box= find_dialog_action_buttons= find_dialog_options= find_dialog_format_controls= find_dialog_compact_layout= find_dialog_result_closed_without_accept= replace_dialog= replace_dialog_text_boxes= replace_dialog_action_buttons= replace_dialog_options= replace_dialog_format_controls= replace_dialog_compact_layout= replace_dialog_result_closed_without_accept= go_to_dialog= go_to_dialog_reference_controls= go_to_dialog_history_controls= go_to_dialog_special_control= go_to_dialog_compact_layout= go_to_dialog_result_closed_without_accept= go_to_special_dialog= go_to_special_dialog_kind_controls= go_to_special_dialog_value_type_controls= go_to_special_dialog_compact_layout= go_to_special_dialog_result_closed_without_accept= format_cells_dialog= format_cells_dialog_tab_strip= format_cells_dialog_default_number_tab= format_cells_dialog_number_controls= format_cells_dialog_action_buttons= format_cells_dialog_compact_layout= format_cells_dialog_result_closed_without_accept= sort_dialog= sort_dialog_sort_on_controls= sort_dialog_color_controls= sort_dialog_action_buttons= sort_dialog_compact_layout= sort_dialog_result_closed_without_accept= data_validation_dropdown_control= data_validation_dropdown_items= data_validation_dialog= data_validation_dialog_criteria_controls= data_validation_dialog_message_controls= data_validation_dialog_action_buttons= data_validation_dialog_compact_layout= data_validation_dialog_result_closed_without_accept=";
                public string NewRouteReport => "native_flash_fill_menu_item= native_review_menu= native_advanced_filter_menu_item= native_remove_duplicates_menu_item= native_subtotal_menu_item= native_data_validation_preview_menu_item= native_data_validation_menu_item= native_what_if_analysis_menu_item= native_goal_seek_menu_item= native_data_table_menu_item= native_scenario_manager_menu_item= native_forecast_sheet_menu_item= native_review_summary_menu_item= native_check_accessibility_menu_item= native_next_note_menu_item= native_previous_note_menu_item= native_next_comment_menu_item= native_previous_comment_menu_item=";
                public string Report => "live_command_key_smoke_required= live_command_key_smoke= live_command_key_smoke_attempted= live_command_key_smoke_ready= cmd_find_direct_route_source_guard= cmd_page_up_direct_route_source_guard= cmd_page_down_direct_route_source_guard= live_cmd_select_all_received= live_cmd_select_all_state_changed= live_cmd_bold_received= live_cmd_bold_state_changed= live_cmd_italic_received= live_cmd_italic_state_changed= live_cmd_underline_received= live_cmd_underline_state_changed= external_image_clipboard_paste_required= external_image_clipboard_paste= external_image_clipboard_picture_count= external_image_clipboard_picture_png_bytes= macos_accessibility_smoke= a11y_formula_box_name= a11y_formula_box_help= a11y_formula_box_id= a11y_status_text_name= a11y_status_text_help= a11y_status_text_id= a11y_status_text_value= a11y_cell_address_name= a11y_cell_address_help= a11y_cell_address_id= a11y_selection_stats_name= a11y_selection_stats_help= a11y_selection_stats_id= native_new_workbook_menu_item= native_open_recent_menu_item= native_open_recent_item_count= native_export_pdf_menu_item= native_share_workbook_menu_item= native_workbook_statistics_menu_item= native_close_workbook_menu_item= new_sheet_button= toolbar_format_painter_button= toolbar_autosum_button= toolbar_autosum_sum_menu_item= toolbar_autosum_average_menu_item= toolbar_autosum_count_numbers_menu_item= toolbar_autosum_count_all_menu_item= toolbar_autosum_max_menu_item= toolbar_autosum_min_menu_item= toolbar_fill_cells_button= toolbar_fill_down_menu_item= toolbar_fill_right_menu_item= toolbar_fill_up_menu_item= toolbar_fill_left_menu_item= toolbar_clear_button= toolbar_clear_all_menu_item= toolbar_clear_formats_menu_item= toolbar_clear_contents_menu_item= toolbar_clear_comments_menu_item= toolbar_clear_hyperlinks_menu_item= toolbar_borders_button= toolbar_wrap_text_button= toolbar_merge_and_center_button= focusable_sheet_tab= focusable_active_sheet_tab= shell_focus_cycle_targets= sheet_tab_context_keyboard_help= sheet_tab_context_rename_menu_item= sheet_tab_context_tab_color_menu_item= sheet_tab_context_no_color_menu_item= sheet_tab_context_select_all_sheets_menu_item= sheet_tab_context_ungroup_sheets_menu_item= native_data_menu= native_flash_fill_menu_item= native_remove_duplicates_menu_item= native_subtotal_menu_item= native_data_validation_preview_menu_item= native_view_menu= native_sheet_menu= native_window_menu= native_new_sheet_menu_item= native_rename_sheet_menu_item= native_duplicate_sheet_menu_item= native_move_sheet_left_menu_item= native_move_sheet_right_menu_item= native_tab_color_menu_item= native_tab_color_clear_item= native_tab_color_swatch_count= native_select_all_sheets_menu_item= native_ungroup_sheets_menu_item= native_hide_sheet_menu_item= native_unhide_sheet_menu_item= native_delete_sheet_menu_item= native_cut_menu_item= native_copy_menu_item= native_paste_special_menu_item= native_format_painter_menu_item= native_paste_special_comments_menu_item= native_paste_special_validation_menu_item= native_paste_special_all_except_borders_menu_item= native_paste_special_all_merging_conditional_formats_menu_item= native_paste_special_column_widths_menu_item= native_paste_special_formulas_and_number_formats_menu_item= native_paste_special_values_and_number_formats_menu_item= native_paste_special_values_and_source_formatting_menu_item= native_paste_special_keep_source_column_widths_menu_item= native_paste_special_paste_link_menu_item= native_paste_special_text_menu_item= native_paste_special_unicode_text_menu_item= native_paste_special_picture_menu_item= native_paste_special_linked_picture_menu_item= native_select_all_menu_item= native_find_menu_item= native_find_next_menu_item= native_replace_menu_item= native_go_to_menu_item= native_go_to_special_menu_item= native_sort_ascending_menu_item= native_sort_descending_menu_item= native_format_cells_menu_item= native_autosum_menu_item= native_autosum_sum_menu_item= native_autosum_average_menu_item= native_autosum_count_numbers_menu_item= native_autosum_count_all_menu_item= native_autosum_max_menu_item= native_autosum_min_menu_item= native_fill_cells_menu_item= native_fill_down_menu_item= native_fill_right_menu_item= native_fill_up_menu_item= native_fill_left_menu_item= native_clear_menu_item= native_clear_all_menu_item= native_clear_formats_menu_item= native_clear_contents_menu_item= native_clear_comments_menu_item= native_clear_hyperlinks_menu_item= native_bold_menu_item= native_fill_color_swatch_count= native_font_color_swatch_count= native_borders_menu_item= native_borders_preset_count= native_merge_and_center_menu_item= native_unmerge_cells_menu_item= native_cell_styles_menu_item= native_cell_styles_preset_count= native_horizontal_text_menu_item= native_angle_counterclockwise_menu_item= native_angle_clockwise_menu_item= native_vertical_text_menu_item= native_rotate_text_up_menu_item= native_rotate_text_down_menu_item= native_show_gridlines_menu_item= native_show_headings_menu_item= native_zoom_in_menu_item= native_zoom_out_menu_item= native_zoom_100_menu_item= native_zoom_to_selection_menu_item= native_freeze_panes_menu_item= native_freeze_top_row_menu_item= native_freeze_first_column_menu_item= native_unfreeze_panes_menu_item= native_show_formulas_menu_item= native_minimize_window_menu_item= native_zoom_window_menu_item= native_bring_all_to_front_menu_item= native_help_menu= native_help_online_menu_item= native_send_feedback_menu_item= native_check_for_updates_menu_item= native_about_menu_item= native_legal_notices_menu_item=";
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
                private static async Task RunAsync(MainWindow.RendererValidationAccess access, MacOsLaunchSmokeOptions options, LocalAppDiagnostics? diagnostics)
                {
                    var snapshot = access.CreateSnapshot();
                    var initialExternalImageClipboardPictureCount = snapshot.ExternalImageClipboardPictureCount;
                    var commandKeyEvidence = CaptureCommandKeyEvidence(access);
                    var liveCommandKeyEvidence = access.BeginCommandObservation(observation =>
                    liveCommandKeyEvidence.IsPassed.ToString();
                    await access.TryPasteExternalClipboardImageAsync();
                    IsPassed(snapshot, options, initialExternalImageClipboardPictureCount).ToString();
                    HasExternalImageClipboardPasteEvidence(snapshot, initialExternalImageClipboardPictureCount).ToString();
                }

                private static MacOsLaunchSmokeCommandKeySnapshot CaptureCommandKeyEvidence(MainWindow.RendererValidationAccess access) => new()
                {
                    HasFindDirectRouteSourceGuard = MainWindow.RendererValidationAccess.HasMethods("MainWindow_KeyDown"),
                    HasPageUpDirectRouteSourceGuard = MainWindow.RendererValidationAccess.HasMethods("SelectAdjacentVisibleSheetFromKeyboard"),
                    HasPageDownDirectRouteSourceGuard = MainWindow.RendererValidationAccess.HasMethods("SelectAdjacentVisibleSheetFromKeyboard")
                };
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
            "shared/Free.Shared.AppServices/RecentFilesStore.cs",
            """
            namespace Free.Shared.AppServices;

            public sealed class RecentFileEntry
            {
                public WorkbookFileAccessIdentity? FileAccessIdentity { get; set; }
            }

            public sealed class WorkbookFileAccessIdentity
            {
                public bool TryWithLocalPath(string path, out WorkbookFileAccessIdentity? identity)
                {
                    identity = this;
                    return true;
                }
            }

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
            "shared/Free.Shared.AppServices/AtomicFileWriter.cs",
            """
            namespace Free.Shared.AppServices;

            public static class AtomicFileWriter
            {
                public static void WriteAllText(string path, string content)
                {
                    fileStream.Flush(flushToDisk: true);
                    File.Move(sourceTempPath, destinationPath, overwrite: true);
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
                public WorkbookCellEditResult SetSelectedSheetTabColor(CellColor? color)
                new SetSheetTabColorCommand(selectedSheetIds[0], color)
                public bool IsFormatPainterActive =>
                public bool CaptureFormatPainterSource(bool persistent = false)
                public void CancelFormatPainter()
                public WorkbookCellEditResult ApplyFormatPainterToSelectedRange()
                CreateFormatPainterCommand(sourceSheet, sourceRange, targetRanges)
                IReadOnlyList<GridRange> targetRanges
                SelectionStyleCommandPlanner.CreateRangeCommand(
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
                AutoSumFormulaPlanner.TryCreatePlan(ActiveSheet, functionName, SelectedRange, out var plan)
                CreateEditCellsCommand([(plan.Target, Cell.FromFormula(plan.Formula))])
                ApplySuccessfulEditResult(result, plan.Target);
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
                WorksheetCommandPresentationCatalog.DescribeFill(direction).CommandTitle
                public bool CanSortSelectedRange => SelectedRange.RowCount > 1;
                public WorkbookCellEditResult SortSelectedRange(bool ascending)
                QuickSortRangePlanner.Create(ActiveSheet, range, ActiveCell)
                sortPlan.Range
                sortPlan.SortByColOffset
                "Select at least two rows to sort."
                public WorkbookCellEditResult SetSelectedRangeBorderPreset(CellBorderPreset preset)
                CreateBorderPresetCommand(range, preset)
                CellBorderPresetPlanner.Plan(preset, range, range.Start, borderStyle, borderColor)
                CellBorderPresetPlanner.RequiresPerCellPlanning(preset)
                BorderShortcutService.HasBorderChanges(diff)
                GroupedApplyStyleCommand(targetSheetIds, sourceRange, diff)
                public WorkbookCellEditResult ApplySelectedRangeCompactFormat(
                    bool? mergeCells = null,
                    MergeCellContentResolution mergeContentResolution = MergeCellContentResolution.KeepFirstCell)
                CreateFormatCellsMergeCommands(area, shouldMerge, mergeContentResolution)
                public bool IsSelectedRangeMerged => CellMergePlanner.IsSelectionMerged(ActiveSheet, SelectedRange);
                public WorkbookCellEditResult MergeAndCenterSelectedRange(
                    MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell)
                CreateMergeAndCenterCommand(area, contentResolution)
                public WorkbookCellEditResult UnmergeSelectedRange()
                areas.SelectMany(CreateUnmergeCommands)
                private IWorkbookCommand CreateMergeAndCenterCommand(
                    GridRange range,
                    MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell)
                CellMergePlanner.CreateMergeAndCenterCommands(
                private IReadOnlyList<IWorkbookCommand> CreateFormatCellsMergeCommands(
                    GridRange range,
                    bool mergeCells,
                    MergeCellContentResolution contentResolution = MergeCellContentResolution.KeepFirstCell)
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
                public WorkbookCellEditResult HideSelectedSheets()
                new SetSheetHiddenCommand(selectedSheetIds[0], hidden: true)
                public WorkbookCellEditResult UnhideSheet(SheetId sheetId)
                new SetSheetHiddenCommand(sheetId, hidden: false)
                public bool IsShowingFormulas => ActiveSheet.ShowFormulas;
                public WorkbookCellEditResult SetShowFormulas(bool showFormulas)
                sheetId => new SetWorksheetShowFormulasCommand(sheetId, showFormulas)
                public bool IsShowingGridlines => ActiveSheet.ShowGridlines;
                public bool IsShowingHeadings => ActiveSheet.ShowHeadings;
                public WorkbookCellEditResult SetShowGridlines(bool showGridlines)
                public WorkbookCellEditResult SetShowHeadings(bool showHeadings)
                return new SetWorksheetViewOptionsCommand(
                ExecuteGroupedWorksheetViewCommand(
                public int ZoomPercent => ActiveSheet.ZoomPercent;
                public WorkbookCellEditResult SetZoomPercent(int zoomPercent)
                sheetId => new SetWorksheetZoomCommand(sheetId, zoomPercent)
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
                private string FormatPictureCellText(ScalarValue value, string numberFormat)
                new PasteColumnWidthsCommand(
                private IWorkbookCommand CreatePasteLinkCommand(
                var sheetDestination = RemapAddressToSheet(destination, sheetId)
                IWorkbookCommand command = new EditCellsCommand(sheetId, linkedCells)
                private IWorkbookCommand CreateGroupedSheetCommand(
                Func<SheetId, IWorkbookCommand> createCommand
                bool keepSourceColumnWidths = false
                if (keepSourceColumnWidths)
                private readonly FindReplaceWorkflowSession _findReplaceWorkflow;
                _findReplaceWorkflow = new FindReplaceWorkflowSession(
                public string LastFindText => _findReplaceWorkflow.LastFindText;
                public StyleDiff? CreateFormatDiffFromActiveCell()
                public StyleDiff? CreateFormatDiffFromCell(CellAddress address)
                public IReadOnlyList<GridRange> SelectedRanges { get; private set; } = [];
                public WorkbookFindAllResult FindAll(
                _findReplaceWorkflow.FindAll(
                result.Matches.Select(CreateFindAllMatch).ToList()
                private WorkbookFindAllMatch CreateFindAllMatch(FindResult result)
                private string FindNameForAddress(CellAddress address)
                public WorkbookReplaceResult ReplaceAllValues(
                public WorkbookReplaceResult ReplaceNextValue(
                FindOptions? options,
                StyleDiff? replacementFormat = null
                _findReplaceWorkflow.ReplaceAll(
                _findReplaceWorkflow.ReplaceNext(
                new GridRange(match.Address, match.Address)
                public WorkbookNavigationResult GoToReference(string reference)
                public WorkbookGoToSpecialResult GoToSpecial(GoToSpecialKind kind, GoToSpecialOptions? options = null)
                var searchRange = kind is GoToSpecialKind.CurrentRegion or GoToSpecialKind.Precedents or GoToSpecialKind.Dependents
                ResolveGoToSpecialSearchRange()
                GoToSpecialService.Find(Workbook, ActiveSheet, searchRange, kind, ActiveCell, options)
                SelectionRangeService.CompressAddresses(matches)
                SelectRanges(selectedRange, ranges);
                WorkbookReferenceNavigator.TryParseReferenceRange(
                public WorkbookNavigationResult FindNext(
                _findReplaceWorkflow.FindNext(
                return WorkbookNavigationResult.Found(
                private WorkbookNavigationResult NavigateToRange(GridRange range)
                SelectSheet(range.Start.Sheet);
                private SheetId? ResolveSheetIdByName(string sheetName)
                */
                public WorkbookCellEditResult AddSheet(SheetId? insertBeforeSheetId = null) =>
                    ExecuteRepeatableCommandPreservingSelection(() =>
                        new AddSheetCommand(
                            SheetTabListPlanner.GenerateUniqueSheetName(Workbook),
                            insertBeforeSheetId is null ? Workbook.Sheets.Count : 0));

                public WorkbookCellEditResult RenameActiveSheet(string? name)
                {
                    var newName = (name ?? "").Trim();
                    var result = _cellEditService.ExecuteEditCommand(
                        Workbook,
                        new RenameSheetCommand(ActiveSheet.Id, newName));
                    ApplySuccessfulWorkbookMetadataResult(ActiveSheet.Id);
                    return result;
                }

                public WorkbookCellEditResult DuplicateActiveSheet() => DuplicateSelectedSheets();

                public WorkbookCellEditResult DuplicateSelectedSheets()
                {
                    var selectedSheetIds = CurrentGroupedStructureSheetIds();
                    var result = _cellEditService.ExecuteEditCommand(
                        Workbook,
                        new DuplicateSheetsCommand(selectedSheetIds, Workbook.Sheets.Count));
                    return result;
                }

                public WorkbookCellEditResult DeleteActiveSheet() => DeleteSelectedSheets();

                public WorkbookCellEditResult DeleteSelectedSheets()
                {
                    var selectedSheetIds = CurrentGroupedStructureSheetIds();
                    var result = _cellEditService.ExecuteEditCommand(
                        Workbook,
                        new RemoveSheetsCommand(selectedSheetIds));
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
                WorkbookRangeTextCodec.TryResolveReferenceSheet(
                WorkbookRangeTextCodec.TryParse(defaultSheetId, text, resolveSheetId, out range)
                WorkbookRangeTextCodec.SplitReferences(text)
                private static bool TryParseAbsoluteR1C1CellReference(string input, SheetId sheetId, out CellAddress address)
                */
            }
            """);

        WriteFile(
            root,
            "shared/Free.Shared.AppServices/WorkbookShareActionPlanner.cs",
            """
            namespace Free.Shared.AppServices;

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
            "shared/Free.Shared.AppServices/LocalFilePath.cs",
            """
            using Free.Shared.IO;

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
                    return FilePathPolicy.TryGetFullPath(path, out normalizedPath);
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
            "shared/Free.Shared.IO/FileDialogFilterBuilder.cs",
            """
            namespace Free.Shared.IO;

            public static class FileDialogFilterBuilder
            {
                public static string Create(string displayName, params string[] extensions) => displayName;
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
                DateTimeOffset LastOpened,
                WorkbookFileAccessIdentity? FileAccessIdentity = null);

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
                                item.Entry.LastOpened,
                                ResolveIdentityForPath(item.Entry.FileAccessIdentity, item.Path!)))
                            .ToList());
                }

                public static string FormatHeader(string path)
                {
                    Path.GetFileName(path);
                    Path.GetDirectoryName(path);
                    return path;
                }

                private static WorkbookFileAccessIdentity? ResolveIdentityForPath(
                    WorkbookFileAccessIdentity? identity,
                    string path) =>
                    identity is not null && identity.TryWithLocalPath(path, out var resolvedIdentity)
                        ? resolvedIdentity
                        : null;
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
                    CountVisibleScrollableRows(viewport, sheet.FrozenRows);
                    CountVisibleScrollableColumns(viewport, sheet.FrozenCols);
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
            "tools/FreeX.Validation.Avalonia/PackagingSmokeValidation.cs",
            """
            namespace FreeX.Validation.Avalonia;

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

            internal static class PackagingSmokeCommand
            {
                public const string Argument = SisterAppPackagingSmoke.Argument;
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
                    PortablePdfTextCapabilityPlanner.CreatePlan(workbook, exportPlan, options);
                    var document = WorkbookPdfContentBuilder.Build(workbook, exportPlan, options);
                    PortablePdfWriter.WriteToBytes(document, "FreeX portable PDF");
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Services/WorkbookPdfContentBuilder.cs",
            """
            namespace FreeX.App.Services;

            public static class WorkbookPdfContentBuilder
            {
                public static object Build(object workbook, object exportPlan, object options)
                {
                    PortablePdfPageContentPlanner.CreatePlan(workbook, request);
                    PdfWinAnsiTextCapability.Truncate(cell.DisplayText, options.MaximumCellTextLength);
                    return new object();
                }
            }
            """);

        WriteFile(
            root,
            "shared/Free.Shared.Pdf/PortablePdfWriter.cs",
            """
            namespace Free.Shared.Pdf;

            public static class PortablePdfWriter
            {
                public static byte[] WriteToBytes(object document, string title)
                {
                    "/Encoding /WinAnsiEncoding".ToString();
                    EncodeWinAnsiHexText(normalized);
                    _ = "built-in Helvetica/WinAnsi set";
                    return [];
                }

                private static byte EncodeWinAnsiByte(char ch) => 0;
            }
            """);

        WriteFile(
            root,
            "shared/Free.Shared.Pdf.Skia/SkiaPdfDocumentExporter.cs",
            """
            namespace Free.Shared.Pdf.Skia;

            public static class SkiaPdfDocumentExporter
            {
                public static void Save(object workbook, object exportPlan, object stream)
                {
                }
            }
            """);

        WriteFile(
            root,
            "src/FreeX.App.Avalonia/Pdf/AvaloniaPdfDocumentExporter.cs",
            """
            namespace FreeX.App.Avalonia.Pdf;

            public static class AvaloniaPdfDocumentExporter
            {
                public static PdfExportOutcome Save(object workbook, object exportPlan, Stream stream)
                {
                    return PdfBackendFallbackExecutor.Execute(
                        stream,
                        target => SkiaPdfDocumentExporter.Save(workbook, exportPlan, target, options, workbookDirectory),
                        target => PortablePdfDocumentExporter.Save(workbook, exportPlan, target, options));
                }
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

        WriteFile(
            root,
            "src/FreeX.App.Presentation/WorkbookPresentationModel.cs",
            """
            namespace FreeX.App.Presentation;

            public sealed class WorkbookPresentationModel
            {
                public string Title { get; init; } = "FreeX";
            }
            """);

        WriteFile(
            root,
            "shared/Free.Shared.Ribbon.Avalonia/RibbonHost.cs",
            """
            namespace Free.Shared.Ribbon.Avalonia;

            public sealed class RibbonHost
            {
                public string Name => "Ribbon";
            }
            """);

        WriteFile(
            root,
            "shared/Free.Shared.Ribbon/RibbonCommandDescriptor.cs",
            """
            namespace Free.Shared.Ribbon;

            public sealed class RibbonCommandDescriptor
            {
                public string Id { get; init; } = "home.open";
            }
            """);

        CreatePortableSourceRoots(root);

        if (!string.IsNullOrWhiteSpace(extraAvaloniaSource))
        {
            WriteFile(root, extraAvaloniaSourcePath, extraAvaloniaSource);
        }
    }

    private static void CreatePortableSourceRoots(string root)
    {
        foreach (var relativePath in new[]
        {
            "src/FreeX.App.Avalonia",
            "src/FreeX.App.Presentation",
            "src/FreeX.App.Services",
            "shared/Free.Shared.Ribbon.Avalonia",
            "shared/Free.Shared.AppServices",
            "shared/Free.Shared.Drawing",
            "shared/Free.Shared.Drawing.Avalonia",
            "shared/Free.Shared.IO",
            "shared/Free.Shared.Pdf",
            "shared/Free.Shared.Pdf.Skia",
            "shared/Free.Shared.Ribbon",
            "shared/Free.Shared.Shell.Avalonia",
            "tools/FreeX.ParityCapture.Support"
        })
        {
            Directory.CreateDirectory(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
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
