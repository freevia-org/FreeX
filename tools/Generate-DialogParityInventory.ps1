param(
    [string]$JsonPath = "docs\parity\dialog-parity-inventory.json",
    [string]$MarkdownPath = "docs\parity\dialog-parity-inventory.md",
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Test-RepoFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    Test-Path -LiteralPath (Join-Path $repoRoot $Path) -PathType Leaf
}

function Get-ExistingRelativeFiles {
    param([string[]]$Patterns)

    $matches = New-Object System.Collections.Generic.List[string]
foreach ($pattern in $Patterns) {
        if ([string]::IsNullOrWhiteSpace($pattern)) {
            continue
        }

        $absolutePattern = Join-Path $repoRoot $pattern
        foreach ($file in @(Get-ChildItem -Path $absolutePattern -File -ErrorAction SilentlyContinue)) {
            $matches.Add((ConvertTo-ToolRepoRelativePath -Path $file.FullName -RepoRoot $repoRoot))
        }
    }

    $matches | Sort-Object -Unique
}

function Get-AvaloniaParityDialogIds {
    $sourcePath = Join-Path $repoRoot "tools\FreeX.ParityCapture.Avalonia\Capture\MainWindow.ParityCapture.cs"
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        return @()
    }

    $source = Get-Content -LiteralPath $sourcePath -Raw
    $matches = [regex]::Matches($source, '"(dialog\.[^"]+)"')
    $matches | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
}

function Get-CaptureAssetEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$RouteId,
        [Parameter(Mandatory = $true)][string]$Shell
    )

    $assetRoot = Join-Path $repoRoot "docs\parity\dialog-visual-assets\$Shell-capture"
    $manifestPath = Join-Path $assetRoot "manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        return @()
    }

    $manifest = Read-ToolJson -Path $manifestPath -RepoRoot $repoRoot -MissingMessage "Dialog capture asset manifest was not found"
    $surface = @($manifest.surfaces) | Where-Object { $_.id -eq $RouteId -and $_.captured -eq $true } | Select-Object -First 1
    if ($null -eq $surface -or [string]::IsNullOrWhiteSpace($surface.png)) {
        return @()
    }

    $pngPath = Join-Path $assetRoot $surface.png
    if (-not (Test-Path -LiteralPath $pngPath -PathType Leaf)) {
        return @()
    }

    @((ConvertTo-ToolRepoRelativePath -Path $pngPath -RepoRoot $repoRoot))
}

function New-CaptureStatus {
    param(
        [Parameter(Mandatory = $true)][string]$RouteId,
        [string[]]$EvidencePatterns = @(),
        [string]$AssetShell = ""
    )

    $evidence = @(Get-ExistingRelativeFiles -Patterns $EvidencePatterns)
    if (-not [string]::IsNullOrWhiteSpace($AssetShell)) {
        $evidence += @(Get-CaptureAssetEvidence -RouteId $RouteId -Shell $AssetShell)
        $evidence = @($evidence | Sort-Object -Unique)
    }

    [ordered]@{
        exists = $evidence.Count -gt 0
        evidence = @($evidence)
    }
}

function New-SharedStatus {
    param(
        [string[]]$Patterns = @(),
        [string]$FallbackStatus = "wpf-only-or-not-inferred",
        [string]$FallbackNote = "No shared/presentation backing file was inferred by this inventory slice."
    )

    $evidence = @(Get-ExistingRelativeFiles -Patterns $Patterns)
    if ($evidence.Count -gt 0) {
        return [ordered]@{
            status = "shared-or-presentation-backed"
            evidence = @($evidence)
            note = "Inferred from current shared/presentation source files."
        }
    }

    [ordered]@{
        status = $FallbackStatus
        evidence = @()
        note = $FallbackNote
    }
}

$routes = @(
    @{
        RouteId = "dialog.FormatCells"; DisplayName = "Format Cells"
        Wpf = @("screenshots\home-alignment-number-tour\freex_home_*_format_cells_dialog.png")
        Shared = @("src\FreeX.App.Presentation\FormatCells\*.cs")
    },
    @{
        RouteId = "dialog.FindReplace"; DisplayName = "Find and Replace"
        Wpf = @("screenshots\home-clipboard-cells-editing-tour\freex_home_clipboard_cells_editing_find_dialog.png", "screenshots\home-clipboard-cells-editing-tour\freex_home_clipboard_cells_editing_replace_dialog.png")
        Shared = @("src\FreeX.App.Presentation\Dialogs\FindReplaceOptions.cs")
    },
    @{
        RouteId = "dialog.GoTo"; DisplayName = "Go To"
        Wpf = @("screenshots\home-clipboard-cells-editing-tour\freex_home_clipboard_cells_editing_go_to_dialog.png")
        Shared = @("src\FreeX.App.Services\GoToDialogPlanner.cs", "src\FreeX.App.Services\WorkbookReferenceNavigator.cs")
    },
    @{
        RouteId = "dialog.GoToSpecial"; DisplayName = "Go To Special"
        Wpf = @("screenshots\home-clipboard-cells-editing-tour\freex_home_clipboard_cells_editing_go_to_special_dialog.png")
        Shared = @("src\FreeX.App.Services\GoToDialogPlanner.cs")
    },
    @{
        RouteId = "dialog.Sort"; DisplayName = "Sort"
        Wpf = @("screenshots\data-sort-filter-outline-tour\freex_data_sort_filter_outline_sort_dialog.png", "screenshots\home-clipboard-cells-editing-tour\freex_home_clipboard_cells_editing_custom_sort_dialog.png")
        Shared = @("src\FreeX.App.Services\SortDialogPlanner.cs")
    },
    @{
        RouteId = "dialog.SortOptions"; DisplayName = "Sort Options"
        Wpf = @("screenshots\data-sort-filter-outline-tour\freex_data_sort_filter_outline_sort_options_dialog.png")
        Shared = @("src\FreeX.App.Services\SortDialogPlanner.cs")
    },
    @{
        RouteId = "dialog.AutoFilter"; DisplayName = "AutoFilter"
        Wpf = @("screenshots\autofilter-flyout-tour\freex_table_autofilter_dropdown.png", "tools\foreground-captures\freex-autofilter\*.png")
        Shared = @("src\FreeX.App.Presentation\Filtering\AutoFilter*.cs", "src\FreeX.App.Presentation\AutoFilter\*.cs")
    },
    @{
        RouteId = "dialog.DataValidation"; DisplayName = "Data Validation"
        Wpf = @("screenshots\data-tools-dialogs-tour\freex_data_tools_data_validation_*.png")
        Shared = @("src\FreeX.App.Presentation\Dialogs\DataValidationDialogModel.cs")
    },
    @{
        RouteId = "dialog.TextToColumns"; DisplayName = "Text to Columns"
        Wpf = @("screenshots\data-tools-dialogs-tour\freex_data_tools_text_to_columns_*.png")
        Shared = @("src\FreeX.App.Presentation\CellReferenceInputParser.cs", "src\FreeX.App.Presentation\TextToColumns\*.cs")
    },
    @{
        RouteId = "dialog.AdvancedFilter"; DisplayName = "Advanced Filter"
        Wpf = @("screenshots\data-tools-dialogs-tour\freex_data_tools_advanced_filter_dialog.png")
        Shared = @("src\FreeX.App.Presentation\Filtering\AdvancedFilterPlanner.cs")
    },
    @{
        RouteId = "dialog.Consolidate"; DisplayName = "Consolidate"
        Wpf = @("screenshots\data-tools-dialogs-tour\freex_data_tools_consolidate_dialog.png")
        Shared = @("src\FreeX.App.Presentation\Consolidate\*.cs")
    },
    @{
        RouteId = "dialog.RemoveDuplicates"; DisplayName = "Remove Duplicates"
        Wpf = @("screenshots\data-tools-dialogs-tour\freex_data_tools_remove_duplicates_*.png")
        Shared = @("src\FreeX.App.Services\RemoveDuplicatesPlanner.cs")
    },
    @{
        RouteId = "dialog.GoalSeek"; DisplayName = "Goal Seek"
        Wpf = @("screenshots\data-tools-dialogs-tour\freex_data_tools_goal_seek_dialog.png", "screenshots\data-what-if-workflows-tour\freex_data_what_if_workflows_goal_seek_dialog.png")
        Shared = @("src\FreeX.App.Services\GoalSeekRequestParser.cs", "src\FreeX.App.Services\WorkbookSession.cs")
    },
    @{
        RouteId = "dialog.GoalSeekStatus"; DisplayName = "Goal Seek Status"
        Wpf = @("screenshots\data-tools-dialogs-tour\freex_data_tools_goal_seek_status_dialog.png", "screenshots\data-what-if-workflows-tour\freex_data_what_if_workflows_goal_seek_status_*.png")
        Shared = @("src\FreeX.App.Services\WorkbookGoalSeekResult.cs")
    },
    @{
        RouteId = "dialog.DataTable"; DisplayName = "Data Table"
        Wpf = @("screenshots\data-tools-dialogs-tour\freex_data_tools_data_table_dialog.png", "screenshots\data-what-if-workflows-tour\freex_data_what_if_workflows_data_table_dialog.png")
        Shared = @("src\FreeX.App.Services\DataTablePlanner.cs", "src\FreeX.App.Services\WorkbookSession.cs")
    },
    @{
        RouteId = "dialog.ScenarioManager"; DisplayName = "Scenario Manager"
        Wpf = @("screenshots\data-tools-dialogs-tour\freex_data_tools_scenario_manager_dialog.png", "screenshots\data-what-if-workflows-tour\freex_data_what_if_workflows_scenario_manager_dialog.png")
        Shared = @("src\FreeX.App.Services\ScenarioManagerPlanner.cs", "src\FreeX.App.Services\WorkbookSession.cs")
    },
    @{
        RouteId = "dialog.ForecastSheet"; DisplayName = "Forecast Sheet"
        Wpf = @("screenshots\data-tools-dialogs-tour\freex_data_tools_forecast_sheet_dialog.png")
        Shared = @("src\FreeX.App.Services\ForecastSheetPlanner.cs", "src\FreeX.App.Services\WorkbookSession.cs")
    },
    @{
        RouteId = "dialog.Subtotal"; DisplayName = "Subtotal"
        Wpf = @("screenshots\data-sort-filter-outline-tour\freex_data_sort_filter_outline_subtotal_dialog.png")
        Shared = @("src\FreeX.App.Services\SubtotalPlanner.cs", "src\FreeX.App.Services\WorkbookSession.cs")
    },
    @{
        RouteId = "dialog.CreateTable"; DisplayName = "Create Table"
        Wpf = @("screenshots\table-workflows-tour\freex_table_workflows_create_table_dialog.png", "screenshots\insert-tables-charts-tour\freex_insert_tables_charts_create_table_dialog.png")
        Shared = @("src\FreeX.App.Services\CreateTableDialogPlanner.cs", "src\FreeX.App.Presentation\TableUI\TableCreationPlanner.cs")
    },
    @{
        RouteId = "dialog.RecommendedPivotTables"; DisplayName = "Recommended PivotTables"
        Wpf = @("screenshots\insert-tables-charts-tour\freex_insert_tables_charts_recommended_pivottables_dialog.png")
        Shared = @("src\FreeX.App.Services\RecommendedPivotTablesDialogPlanner.cs")
    },
    @{
        RouteId = "dialog.Sparkline"; DisplayName = "Sparkline"
        Wpf = @("screenshots\insert-tables-charts-tour\freex_insert_tables_charts_sparkline_dialog.png")
        Shared = @("src\FreeX.App.Presentation\SparklineUI\*.cs", "src\FreeX.App.Presentation\Sparklines\*.cs")
    },
    @{
        RouteId = "dialog.InsertHyperlink"; DisplayName = "Insert Hyperlink"
        Wpf = @("screenshots\insert-objects-links-tour\freex_insert_hyperlink_dialog_address_focus.png")
        Shared = @("src\FreeX.App.Services\HyperlinkDialogPlanner.cs", "src\FreeX.App.Services\WorkbookSession.cs")
    },
    @{
        RouteId = "dialog.SymbolPicker"; DisplayName = "Symbol Picker"
        Wpf = @("screenshots\insert-objects-links-tour\freex_insert_symbol_picker_opened.png")
        Shared = @("src\FreeX.App.Services\SymbolPickerSelectionPlanner.cs")
    },
    @{
        RouteId = "dialog.ErrorChecking"; DisplayName = "Error Checking"
        Wpf = @("screenshots\formula-diagnostics-tour\freex_formula_diagnostics_error_checking_dialog.png")
        Shared = @("src\FreeX.App.Services\ErrorCheckingDialogPlanner.cs")
    },
    @{
        RouteId = "dialog.EvaluateFormula"; DisplayName = "Evaluate Formula"
        Wpf = @("screenshots\formula-diagnostics-tour\freex_formula_diagnostics_evaluate_*.png")
        Shared = @("src\FreeX.App.Services\EvaluateFormulaDialogPlanner.cs")
    },
    @{
        RouteId = "dialog.WatchWindow"; DisplayName = "Watch Window"
        Wpf = @("screenshots\formula-diagnostics-tour\freex_formula_diagnostics_watch_window_*.png")
        Shared = @("src\FreeX.Core.Commands\WatchWindowService.cs")
    },
    @{
        RouteId = "dialog.AddWatch"; DisplayName = "Add Watch"
        Wpf = @("screenshots\formula-diagnostics-tour\freex_formula_diagnostics_watch_add_dialog.png")
        Shared = @("src\FreeX.App.Services\AddWatchDialogPlanner.cs", "src\FreeX.App.Services\WatchWindowDialogPlanner.cs", "src\FreeX.Core.Commands\WatchWindowService.cs")
    },
    @{
        RouteId = "dialog.PageSetup"; DisplayName = "Page Setup"
        Wpf = @("screenshots\page-layout-setup-tour\freex_page_layout_setup_dialog_*.png")
        Shared = @("src\FreeX.App.Presentation\PageLayout\PageSetup*.cs", "src\FreeX.App.Presentation\PageLayout\PageLayoutInputParser.cs")
    },
    @{
        RouteId = "dialog.SelectionPane"; DisplayName = "Selection Pane"
        Wpf = @("screenshots\page-layout-setup-tour\freex_page_layout_setup_arrange_selection_pane_dialog.png")
        Shared = @("src\FreeX.App.Services\SelectionPane*.cs", "src\FreeX.App.Presentation\DrawingUI\SelectionPane*.cs")
    },
    @{
        RouteId = "dialog.InsertSlicer"; DisplayName = "Insert Slicer"
        Wpf = @("screenshots\pivot-options-slicer-tour\freex_pivot_insert_slicer_dialog.png")
        Shared = @("src\FreeX.App.Presentation\SlicerTimeline\*.cs")
    },
    @{
        RouteId = "dialog.InsertTimeline"; DisplayName = "Insert Timeline"
        Wpf = @("screenshots\pivot-options-slicer-tour\freex_pivot_insert_timeline_dialog.png")
        Shared = @("src\FreeX.App.Presentation\SlicerTimeline\*.cs")
    },
    @{
        RouteId = "dialog.PivotTableOptions"; DisplayName = "PivotTable Options"
        Wpf = @("screenshots\pivot-options-slicer-tour\freex_pivot_options_dialog_*.png")
        Shared = @("src\FreeX.App.Presentation\PivotUI\PivotOptionsPlanner.cs")
    },
    @{
        RouteId = "dialog.PivotFieldFilter"; DisplayName = "Pivot Field Filter"
        Wpf = @("screenshots\pivot-field-list-context-tour\freex_pivot_field_filter_dialog.png")
        Shared = @("src\FreeX.App.Presentation\PivotUI\PivotFieldFilterPlanner.cs")
    },
    @{
        RouteId = "dialog.PivotValueFieldSettings"; DisplayName = "Value Field Settings"
        Wpf = @("screenshots\pivot-advanced-workflows-tour\freex_pivot_advanced_value_field_settings_dialog.png", "screenshots\pivot-field-list-context-tour\freex_pivot_value_field_settings_dialog.png")
        Shared = @("src\FreeX.App.Presentation\PivotUI\PivotValueFieldPlanner.cs")
    },
    @{
        RouteId = "dialog.ChangeChartType"; DisplayName = "Change Chart Type"
        Wpf = @("screenshots\chart-data-layout-tour\freex_chart_data_layout_change_chart_type_dialog.png", "screenshots\chart-object-selection-tour\freex_chart_object_selection_change_chart_type_dialog.png")
        Shared = @("src\FreeX.App.Presentation\Charts\Editing\ChartTypeChangePlanner.cs")
    },
    @{
        RouteId = "dialog.SelectDataSource"; DisplayName = "Select Data Source"
        Wpf = @("screenshots\chart-data-layout-tour\freex_chart_data_layout_select_data_dialog.png", "screenshots\chart-object-selection-tour\freex_chart_object_selection_select_data_dialog.png")
        Shared = @("src\FreeX.Core.Commands\ChartCommands.Mutate.cs", "src\FreeX.App.Presentation\Charts\*.cs")
    },
    @{
        RouteId = "dialog.FormatChartArea"; DisplayName = "Format Chart Area"
        Wpf = @("screenshots\chart-data-layout-tour\freex_chart_data_layout_format_chart_area_dialog.png")
        Shared = @("src\FreeX.App.Presentation\Charts\Editing\ChartAreaFormatPlanner.cs")
    },
    @{
        RouteId = "dialog.ShapeEffects"; DisplayName = "Shape Effects"
        Wpf = @("screenshots\draw-object-formatting-tour\freex_draw_object_formatting_shape_effects_dialog.png")
        Shared = @("src\FreeX.App.Services\ShapeEffectsPlanner.cs")
    },
    @{
        RouteId = "dialog.ShapeGradient"; DisplayName = "Shape Gradient"
        Wpf = @("screenshots\draw-object-formatting-tour\freex_draw_object_formatting_shape_gradient_dialog.png")
        Shared = @("src\FreeX.App.Services\ShapeGradientPlanner.cs")
    },
    @{
        RouteId = "dialog.Options"; DisplayName = "Options"
        Wpf = @()
        Shared = @("src\FreeX.App.Services\OptionsDialogPlanner.cs")
    },
    @{
        RouteId = "dialog.ConditionalFormatNewRule"; DisplayName = "New Conditional Formatting Rule"
        Wpf = @("screenshots\home-styles-cf-tour\freex_home_styles_cf_new_rule_*.png")
        Shared = @("src\FreeX.App.Presentation\ConditionalFormatting\*.cs", "src\FreeX.App.Presentation\Dialogs\ConditionalFormatRuleSchema.cs")
    },
    @{
        RouteId = "dialog.ConditionalFormatManage"; DisplayName = "Conditional Formatting Rules Manager"
        Wpf = @("screenshots\home-styles-cf-tour\freex_home_styles_cf_manage_rules_dialog.png")
        Shared = @("src\FreeX.App.Presentation\ConditionalFormatting\*.cs", "src\FreeX.App.Presentation\Dialogs\ConditionalFormatRuleSchema.cs")
    },
    @{
        RouteId = "dialog.Zoom"; DisplayName = "Zoom"
        Wpf = @("screenshots\view-panes-zoom-tour\freex_view_panes_zoom_dialog_custom_125.png", "screenshots\status-footer-interactions-tour\freex_status_footer_interactions_zoom_dialog_*.png", "tools\foreground-captures\freex-status-zoom-text-dialog-click\*.png")
        Shared = @("src\FreeX.App.Services\ZoomLevelMapper.cs", "src\FreeX.App.Services\WorkbookSession.cs")
    },
    @{
        RouteId = "dialog.CustomViews"; DisplayName = "Custom Views"
        Wpf = @("screenshots\view-panes-zoom-tour\freex_view_panes_zoom_custom_views_dialog.png")
        Shared = @("src\FreeX.App.Presentation\CustomViews\*.cs")
    },
    @{
        RouteId = "dialog.PrintPreview"; DisplayName = "Print Preview / Print"
        Wpf = @("tools\foreground-captures\freex-native-print-dialog\*.png")
        Shared = @("src\FreeX.App.Presentation\PageLayout\PrintPreview*.cs", "src\FreeX.App.Services\PrintJobPlanner.cs", "src\FreeX.App.Services\WorkbookExportPrintPlanner.cs")
    },
    @{
        RouteId = "dialog.OpenWorkbook"; DisplayName = "Open Workbook"
        Wpf = @("screenshots\open-workbook-dialog-tour\freex_open_workbook_dialog_opened.png", "tools\foreground-captures\freex-open-dialog\*.png")
        Shared = @("src\FreeX.App.Services\WorkbookFileDialogSurfacePlanner.cs", "src\FreeX.App.Services\WorkbookFilePickerPlanner.cs", "src\FreeX.App.Services\WorkbookOpenIngressPlanner.cs")
    },
    @{
        RouteId = "dialog.SaveAsWorkbook"; DisplayName = "Save As Workbook"
        Wpf = @("screenshots\save-as-workbook-dialog-tour\freex_save_as_workbook_dialog_opened.png", "tools\foreground-captures\freex-save-as-dialog\*.png")
        Shared = @("src\FreeX.App.Services\WorkbookFileDialogSurfacePlanner.cs", "src\FreeX.App.Services\WorkbookFilePickerPlanner.cs", "src\FreeX.App.Services\WorkbookSaveService.cs")
    },
    @{
        RouteId = "dialog.ExportOptions"; DisplayName = "Export Options"
        Wpf = @("screenshots\file-io-import-smoke-tour\freex_file_io_import_smoke_export_*_options.png", "screenshots\file-backstage-workflows-tour\freex_file_backstage_export_*_options.png")
        Shared = @("src\FreeX.App.Services\ExportOptionsDialogSurfacePlanner.cs", "src\FreeX.App.Services\ExportFilePickerPlanner.cs", "src\FreeX.Core.Model\ExportPathPlanner.cs")
    },
    @{
        RouteId = "dialog.AccessibilityChecker"; DisplayName = "Accessibility Checker"
        Wpf = @("screenshots\review-comments-protection-tour\freex_review_accessibility_checker_dialog.png")
        Shared = @("src\FreeX.Core.Commands\AccessibilityCheckerService*.cs", "src\FreeX.App.Services\ReviewWorkflowPlanner.cs")
    },
    @{
        RouteId = "dialog.AllowEditRanges"; DisplayName = "Allow Edit Ranges"
        Wpf = @("screenshots\review-comments-protection-tour\freex_review_allow_edit_ranges_dialog.png")
        Shared = @("src\FreeX.App.Presentation\Protection\AllowEditRangePlanner.cs")
    },
    @{
        RouteId = "dialog.ProtectSheet"; DisplayName = "Protect Sheet"
        Wpf = @("screenshots\review-comments-protection-tour\freex_review_protect_sheet_dialog.png")
        Shared = @("src\FreeX.App.Presentation\Protection\*.cs")
    },
    @{
        RouteId = "dialog.ProtectWorkbook"; DisplayName = "Protect Workbook"
        Wpf = @("screenshots\review-comments-protection-tour\freex_review_protect_workbook_dialog.png")
        Shared = @("src\FreeX.App.Presentation\Protection\*.cs")
    },
    @{
        RouteId = "dialog.WorkbookStatistics"; DisplayName = "Workbook Statistics"
        Wpf = @("screenshots\review-stats-share-tour\freex_review_workbook_statistics_dialog.png")
        Shared = @("src\FreeX.Core.Commands\WorkbookStatisticsService.cs", "src\FreeX.App.Services\WorkbookStatisticsFormatter.cs")
    },
    @{
        RouteId = "dialog.RenameSheet"; DisplayName = "Rename Sheet"
        Wpf = @("screenshots\sheet-tabs-tour\freex_sheet_tabs_rename_dialog_opened.png")
        Shared = @("src\FreeX.App.Services\WorkbookSession.cs")
    },
    @{
        RouteId = "dialog.UnhideSheet"; DisplayName = "Unhide Sheet"
        Wpf = @("screenshots\sheet-tabs-tour\freex_sheet_tabs_unhide_dialog_opened.png")
        Shared = @("src\FreeX.App.Services\WorkbookSheetSelectionService.cs", "src\FreeX.App.Services\WorkbookSession.cs")
    },
    @{
        RouteId = "dialog.About"; DisplayName = "About"
        Wpf = @("screenshots\help-about-legal-tour\freex_about_dialog.png")
        Shared = @("src\FreeX.App.Services\AppHelpInfo.cs")
    },
    @{
        RouteId = "dialog.LegalNotices"; DisplayName = "Legal Notices"
        Wpf = @("screenshots\help-about-legal-tour\freex_legal_notices_dialog.png")
        Shared = @("src\FreeX.App.Services\LegalNoticeProvider.cs")
    }
)

$avaloniaHarnessRouteIds = @(Get-AvaloniaParityDialogIds)
$rows = foreach ($route in $routes) {
    $routeId = [string]$route.RouteId
    $wpfCapture = New-CaptureStatus -RouteId $routeId -EvidencePatterns @($route.Wpf) -AssetShell "wpf"
    $avaloniaCapture = New-CaptureStatus -RouteId $routeId -EvidencePatterns @() -AssetShell "avalonia"
    $inAvaloniaHarness = $avaloniaHarnessRouteIds -contains $routeId
    $sharedPatterns = if ($route.ContainsKey("Shared")) { @($route.Shared) } else { @() }
    $sharedStatus = New-SharedStatus -Patterns $sharedPatterns

    [ordered]@{
        routeId = $routeId
        displayName = [string]$route.DisplayName
        wpfCaptureExists = [bool]$wpfCapture.exists
        avaloniaCaptureExists = [bool]$avaloniaCapture.exists
        sharedBackingStatus = $sharedStatus.status
        wpfEvidence = @($wpfCapture.evidence)
        avaloniaEvidence = @($avaloniaCapture.evidence)
        avaloniaCaptureHarnessRoute = [bool]$inAvaloniaHarness
        sharedBackingEvidence = @($sharedStatus.evidence)
        notes = @(
            if ($inAvaloniaHarness -and -not $avaloniaCapture.exists) {
                "Avalonia capture route exists in MainWindow.ParityCapture.cs, but no committed Avalonia capture asset was found."
            }
            if (-not $wpfCapture.exists) {
                "No current checked-in WPF capture asset was found by this inventory slice."
            }
            if ($sharedStatus.evidence.Count -eq 0) {
                $sharedStatus.note
            }
        )
    }
}

$sourceSnapshot = "repository files at generation time"

$report = [ordered]@{
    schema = "freex.dialog-parity-inventory.v1"
    sourceSnapshot = $sourceSnapshot
    generatedBy = "tools/Generate-DialogParityInventory.ps1"
    scope = "Current-main dialog parity inventory slice. Capture booleans mean current checked-in evidence exists; they do not imply functional parity."
    summary = [ordered]@{
        totalRoutes = @($rows).Count
        wpfCaptures = @($rows | Where-Object { $_.wpfCaptureExists }).Count
        avaloniaCaptures = @($rows | Where-Object { $_.avaloniaCaptureExists }).Count
        avaloniaHarnessRoutes = @($rows | Where-Object { $_.avaloniaCaptureHarnessRoute }).Count
        sharedOrPresentationBacked = @($rows | Where-Object { $_.sharedBackingStatus -eq "shared-or-presentation-backed" }).Count
    }
    rows = @($rows)
}

$json = ($report | ConvertTo-Json -Depth 8) + [Environment]::NewLine

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Dialog parity inventory")
[void]$md.AppendLine()
[void]$md.AppendLine("Generated by tools/Generate-DialogParityInventory.ps1 from $sourceSnapshot.")
[void]$md.AppendLine()
[void]$md.AppendLine("This is an inventory slice, not a parity claim. WPF capture and Avalonia capture mean a current checked-in PNG or capture-asset manifest entry was found. Avalonia harness means the route is present in tools/FreeX.ParityCapture.Avalonia/Capture/MainWindow.ParityCapture.cs, even when no committed capture asset exists.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Route id | Dialog display name | WPF capture | Avalonia capture | Avalonia harness | Shared backing status | Evidence |")
[void]$md.AppendLine("| --- | --- | --- | --- | --- | --- | --- |")

foreach ($row in $rows) {
    $wpf = if ($row.wpfCaptureExists) { "yes" } else { "no" }
    $avalonia = if ($row.avaloniaCaptureExists) { "yes" } else { "no" }
    $harness = if ($row.avaloniaCaptureHarnessRoute) { "yes" } else { "no" }
    $evidenceItems = @($row.wpfEvidence + $row.avaloniaEvidence + $row.sharedBackingEvidence) | Select-Object -First 3
    $evidence = if ($evidenceItems.Count -gt 0) { ($evidenceItems -join "<br>") } else { "" }
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $row.routeId) | $(ConvertTo-ToolMarkdownCell $row.displayName) | $wpf | $avalonia | $harness | $(ConvertTo-ToolMarkdownCell $row.sharedBackingStatus) | $(ConvertTo-ToolMarkdownCell $evidence) |")
}

$markdown = $md.ToString()

$resolvedJsonPath = Resolve-ToolRepoPath -Path $JsonPath -RepoRoot $repoRoot
$resolvedMarkdownPath = Resolve-ToolRepoPath -Path $MarkdownPath -RepoRoot $repoRoot

if ($Check) {
    Test-ToolGeneratedContentMatches -ExpectedContent $json -ActualPath $resolvedJsonPath -Label "Dialog parity inventory JSON" -GeneratorScriptName "tools\Generate-DialogParityInventory.ps1" -NormalizeNewlines
    Test-ToolGeneratedContentMatches -ExpectedContent $markdown -ActualPath $resolvedMarkdownPath -Label "Dialog parity inventory Markdown" -GeneratorScriptName "tools\Generate-DialogParityInventory.ps1" -NormalizeNewlines

    Write-Host "Dialog parity inventory is up to date."
    return
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedJsonPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedMarkdownPath) | Out-Null
Set-Content -LiteralPath $resolvedJsonPath -Value $json -Encoding utf8 -NoNewline
Set-Content -LiteralPath $resolvedMarkdownPath -Value $markdown -Encoding utf8 -NoNewline

Write-Host "Dialog routes: $($report.summary.totalRoutes)"
Write-Host "WPF captures: $($report.summary.wpfCaptures)"
Write-Host "Avalonia captures: $($report.summary.avaloniaCaptures)"
Write-Host "Avalonia harness routes: $($report.summary.avaloniaHarnessRoutes)"
Write-Host "Shared/presentation-backed routes: $($report.summary.sharedOrPresentationBacked)"
Write-Host "Wrote $(ConvertTo-ToolRepoRelativePath -Path $resolvedJsonPath -RepoRoot $repoRoot)"
Write-Host "Wrote $(ConvertTo-ToolRepoRelativePath -Path $resolvedMarkdownPath -RepoRoot $repoRoot)"
