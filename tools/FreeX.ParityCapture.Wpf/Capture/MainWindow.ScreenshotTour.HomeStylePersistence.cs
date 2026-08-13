using System.IO;
using System.Text.Json;
using System.Windows;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureHomeStylePersistenceTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHomeStylePersistenceTourEvidence(outputDir);

        var savedWorkbookPath = Path.Combine(outputDir, HomeStylePersistenceTourSavedWorkbookFileName);
        DeleteIfExists(savedWorkbookPath);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 780;
        await Task.Delay(700);

        var captures = new List<HomeStylePersistenceTourManifestCapture>();
        var submittedCommands = new List<string>();

        try
        {
            var context = EnsureHomeStylePersistenceTourContext(savedWorkbookPath, savedWorkbookBytes: 0, persistenceStage: "seeded");
            captures.Add(await CaptureHomeStylePersistenceWindowStateAsync(
                outputDir,
                context,
                "seeded-grid",
                "freex_home_style_persistence_seeded_grid",
                "Seeded worksheet before submitted Home styling commands, with value ranges reserved for font/color/fill, borders, number formats, alignment, merge, cell styles, and conditional formatting.",
                "Seeded worksheet"));

            SubmitHomeStylePersistenceCommands(context, submittedCommands);
            context = ResolveHomeStylePersistenceCurrentContext(savedWorkbookPath, "submitted", savedWorkbookBytes: 0);
            captures.Add(await CaptureHomeStylePersistenceWindowStateAsync(
                outputDir,
                context,
                "applied-home-style-result",
                "freex_home_style_persistence_applied_home_style_result",
                "Submitted ApplyStyleCommand, MergeCellsCommand, and ApplyConditionalFormatCommand results are visible: merged title, styled header, currency/percent formats, colored font/fill, borders, centered/wrapped alignment, cell-style samples, and highlighted conditional-format cells.",
                "Submitted Home style commands"));

            context = await SaveHomeStylePersistenceWorkbookAsync(savedWorkbookPath, context);
            captures.Add(await CaptureHomeStylePersistenceWindowStateAsync(
                outputDir,
                context,
                "saved-native-workbook",
                "freex_home_style_persistence_saved_native_workbook",
                "Native FreeX .fxl workbook saved through SaveWorkbookToTargetAsync while submitted Home style state remains visible.",
                "SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, .fxl adapter))"));

            await OpenFileAsync(savedWorkbookPath);
            context = ResolveHomeStylePersistenceCurrentContext(
                savedWorkbookPath,
                "after-reopen",
                File.Exists(savedWorkbookPath) ? new FileInfo(savedWorkbookPath).Length : 0);
            captures.Add(await CaptureHomeStylePersistenceWindowStateAsync(
                outputDir,
                context,
                "reopened-persisted-home-styles",
                "freex_home_style_persistence_reopened_grid",
                "OpenFileAsync reopened the saved native workbook and restored Home font style/color/fill, borders, number formats, alignment, merged range, cell-style samples, and conditional-format metadata.",
                "OpenFileAsync(savedWorkbookPath) -> native .fxl adapter"));

            ValidateHomeStylePersistenceTourEvidence(outputDir, captures, savedWorkbookPath, context);
            await WriteHomeStylePersistenceTourManifestAsync(outputDir, context, captures, submittedCommands);
        }
        catch
        {
            DeleteHomeStylePersistenceTourEvidence(outputDir);
            throw;
        }
    }

    private HomeStylePersistenceTourContext EnsureHomeStylePersistenceTourContext(
        string savedWorkbookPath,
        long savedWorkbookBytes,
        string persistenceStage)
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Home style persistence tour requires an active worksheet.");

        HideStartScreen();
        _currentSheetId = sheet.Id;
        _currentFilePath = null;
        _workbook.Name = "Home style persistence";
        sheet.Name = "Home Style Persistence";
        sheet.HiddenRows.Clear();
        sheet.FilterHiddenRows.Clear();
        sheet.HiddenCols.Clear();
        sheet.Comments.Clear();
        sheet.ThreadedComments.Clear();
        sheet.Hyperlinks.Clear();
        sheet.ConditionalFormats.Clear();
        sheet.ReplaceMergedRegions([]);

        for (uint row = 1; row <= 13; row++)
        {
            for (uint col = 1; col <= 8; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        SeedHomeStylePersistenceCells(sheet);
        var context = CreateHomeStylePersistenceContext(sheet, savedWorkbookPath, savedWorkbookBytes, persistenceStage);
        SetSelectionRange(context.ResultRange, context.ResultRange.Start);
        EnsureCellVisible(context.ResultRange.Start);
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
        RefreshToolbar();
        RefreshStatusBar();
        UpdateViewport();
        UpdateLayout();
        MarkWorkbookDirty();
        UpdateTitleBar();

        return context;
    }

    private static void SeedHomeStylePersistenceCells(Sheet sheet)
    {
        var cells = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Home style persistence")),
            (3, 1, new TextValue("Region")),
            (3, 2, new TextValue("Revenue")),
            (3, 3, new TextValue("Delta")),
            (3, 4, new TextValue("Status")),
            (3, 5, new TextValue("Margin")),
            (3, 6, new TextValue("Score")),
            (3, 7, new TextValue("Cell Style")),
            (4, 1, new TextValue("North")),
            (4, 2, new NumberValue(1240.5)),
            (4, 3, new NumberValue(185.25)),
            (4, 4, new TextValue("Wrapped approval note")),
            (4, 5, new NumberValue(0.312)),
            (4, 6, new NumberValue(94)),
            (4, 7, new TextValue("Good")),
            (5, 1, new TextValue("South")),
            (5, 2, new NumberValue(980.25)),
            (5, 3, new NumberValue(-42.1)),
            (5, 4, new TextValue("Center aligned")),
            (5, 5, new NumberValue(0.184)),
            (5, 6, new NumberValue(71)),
            (5, 7, new TextValue("Neutral")),
            (6, 1, new TextValue("West")),
            (6, 2, new NumberValue(1425.75)),
            (6, 3, new NumberValue(211.4)),
            (6, 4, new TextValue("Rotated")),
            (6, 5, new NumberValue(0.401)),
            (6, 6, new NumberValue(88)),
            (6, 7, new TextValue("Bad")),
            (8, 1, new TextValue("Persistence checks")),
            (9, 1, new TextValue("Font/color/fill")),
            (9, 2, new TextValue("Borders")),
            (9, 3, new TextValue("Number format")),
            (9, 4, new TextValue("Alignment")),
            (9, 5, new TextValue("Merge")),
            (9, 6, new TextValue("Conditional format"))
        };

        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
    }

    private void SubmitHomeStylePersistenceCommands(
        HomeStylePersistenceTourContext context,
        List<string> submittedCommands)
    {
        ExecuteHomeStylePersistenceCommand(new MergeCellsCommand(context.Sheet.Id, context.TitleRange), "Merge & Center", submittedCommands);

        ExecuteHomeStylePersistenceCommand(
            new ApplyStyleCommand(
                context.Sheet.Id,
                context.TitleRange,
                new StyleDiff(
                    Bold: true,
                    FontSize: 16,
                    FontColor: CellColor.White,
                    FillColor: new CellColor(31, 78, 121),
                    HAlign: FreeX.Core.Model.HorizontalAlignment.Center,
                    VAlign: FreeX.Core.Model.VerticalAlignment.Center)),
            "Title font/fill/alignment",
            submittedCommands);

        ExecuteHomeStylePersistenceCommand(
            new ApplyStyleCommand(
                context.Sheet.Id,
                context.HeaderRange,
                CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Heading2, _workbook.Theme)),
            "Header cell style",
            submittedCommands);

        ExecuteHomeStylePersistenceCommand(
            new ApplyStyleCommand(
                context.Sheet.Id,
                context.DataRange,
                BorderShortcutService.GetAllBorderDiff(BorderStyle.Thin, new CellColor(91, 155, 213))),
            "All Borders",
            submittedCommands);

        ExecuteHomeStylePersistenceCommand(
            new ApplyStyleCommand(
                context.Sheet.Id,
                context.FontColorFillRange,
                new StyleDiff(
                    Bold: true,
                    Italic: true,
                    FontColor: new CellColor(156, 0, 6),
                    FillColor: new CellColor(255, 235, 156))),
            "Font style/color/fill",
            submittedCommands);

        ExecuteHomeStylePersistenceCommand(
            new ApplyStyleCommand(
                context.Sheet.Id,
                context.CurrencyRange,
                new StyleDiff(NumberFormat: "$#,##0.00")),
            "Currency number format",
            submittedCommands);

        ExecuteHomeStylePersistenceCommand(
            new ApplyStyleCommand(
                context.Sheet.Id,
                context.PercentRange,
                new StyleDiff(NumberFormat: "0.0%")),
            "Percent number format",
            submittedCommands);

        ExecuteHomeStylePersistenceCommand(
            new ApplyStyleCommand(
                context.Sheet.Id,
                context.AlignmentRange,
                new StyleDiff(
                    HAlign: FreeX.Core.Model.HorizontalAlignment.Center,
                    VAlign: FreeX.Core.Model.VerticalAlignment.Center,
                    WrapText: true,
                    IndentLevel: 1,
                    TextRotation: 15)),
            "Alignment/wrap/rotation",
            submittedCommands);

        ExecuteHomeStylePersistenceCommand(
            new ApplyStyleCommand(
                context.Sheet.Id,
                context.GoodCellStyleRange,
                CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Good, _workbook.Theme)),
            "Cell Style Good",
            submittedCommands);

        ExecuteHomeStylePersistenceCommand(
            new ApplyStyleCommand(
                context.Sheet.Id,
                context.NeutralCellStyleRange,
                CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Neutral, _workbook.Theme)),
            "Cell Style Neutral",
            submittedCommands);

        ExecuteHomeStylePersistenceCommand(
            new ApplyStyleCommand(
                context.Sheet.Id,
                context.BadCellStyleRange,
                CellStyleDiffPlanner.GetCellStylePresetDiff(CellStylePreset.Bad, _workbook.Theme)),
            "Cell Style Bad",
            submittedCommands);

        ExecuteHomeStylePersistenceCommand(
            new ApplyConditionalFormatCommand(
                context.Sheet.Id,
                new ConditionalFormat
                {
                    AppliesTo = context.ConditionalFormatRange,
                    Priority = 1,
                    RuleType = CfRuleType.CellValue,
                    Operator = CfOperator.GreaterThanOrEqual,
                    Value1 = "88",
                    FormatIfTrue = new CellStyle
                    {
                        Bold = true,
                        FillColor = new CellColor(198, 239, 206),
                        FontColor = new CellColor(0, 97, 0)
                    }
                }),
            "Conditional Formatting Greater Than Or Equal",
            submittedCommands);

        RefreshToolbar();
        RefreshStatusBar();
        UpdateViewport();
        UpdateTitleBar();
    }

    private void ExecuteHomeStylePersistenceCommand(
        IWorkbookCommand command,
        string title,
        List<string> submittedCommands)
    {
        if (!TryExecuteCommand(command, title, out var outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? $"Home style persistence tour command '{title}' failed.");

        submittedCommands.Add($"{command.GetType().Name}:{title}");
    }

    private async Task<HomeStylePersistenceTourContext> SaveHomeStylePersistenceWorkbookAsync(
        string savedWorkbookPath,
        HomeStylePersistenceTourContext context)
    {
        var adapter = FileFormatResolver.FindSaveAdapter(_fileAdapters, ".fxl", out _)
            ?? throw new InvalidOperationException("Home style persistence tour could not find the native FreeX save adapter.");
        if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter)))
            throw new InvalidOperationException("Home style persistence tour could not save the native FreeX workbook.");

        return ResolveHomeStylePersistenceCurrentContext(savedWorkbookPath, "saved", new FileInfo(savedWorkbookPath).Length);
    }

    private HomeStylePersistenceTourContext ResolveHomeStylePersistenceCurrentContext(
        string savedWorkbookPath,
        string persistenceStage,
        long savedWorkbookBytes)
    {
        var sheet = _workbook.Sheets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Home Style Persistence", StringComparison.OrdinalIgnoreCase))
            ?? GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Home style persistence tour could not resolve the current worksheet.");

        _currentSheetId = sheet.Id;
        return CreateHomeStylePersistenceContext(sheet, savedWorkbookPath, savedWorkbookBytes, persistenceStage);
    }

    private HomeStylePersistenceTourContext CreateHomeStylePersistenceContext(
        Sheet sheet,
        string savedWorkbookPath,
        long savedWorkbookBytes,
        string persistenceStage) =>
        new(
            Sheet: sheet,
            ResultRange: Range(sheet.Id, 1, 1, 9, 7),
            TitleRange: Range(sheet.Id, 1, 1, 1, 7),
            HeaderRange: Range(sheet.Id, 3, 1, 3, 7),
            DataRange: Range(sheet.Id, 3, 1, 6, 7),
            FontColorFillRange: Range(sheet.Id, 4, 3, 6, 3),
            CurrencyRange: Range(sheet.Id, 4, 2, 6, 2),
            PercentRange: Range(sheet.Id, 4, 5, 6, 5),
            AlignmentRange: Range(sheet.Id, 4, 4, 6, 4),
            GoodCellStyleRange: Range(sheet.Id, 4, 7, 4, 7),
            NeutralCellStyleRange: Range(sheet.Id, 5, 7, 5, 7),
            BadCellStyleRange: Range(sheet.Id, 6, 7, 6, 7),
            ConditionalFormatRange: Range(sheet.Id, 4, 6, 6, 6),
            SavedWorkbookPath: savedWorkbookPath,
            SavedWorkbookOutputFileName: string.IsNullOrWhiteSpace(savedWorkbookPath) ? string.Empty : Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes: savedWorkbookBytes,
            PersistenceStage: persistenceStage);

    private async Task<HomeStylePersistenceTourManifestCapture> CaptureHomeStylePersistenceWindowStateAsync(
        string outputDir,
        HomeStylePersistenceTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string commandPath)
    {
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
        SetSelectionRange(context.ResultRange, context.ResultRange.Start);
        EnsureCellVisible(context.ResultRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 780);
        return CreateHomeStylePersistenceCapture(context, state, fileName, evidenceSummary, commandPath);
    }

    private HomeStylePersistenceTourManifestCapture CreateHomeStylePersistenceCapture(
        HomeStylePersistenceTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string commandPath)
    {
        var snapshot = CreateHomeStylePersistenceStyleSnapshot(context);
        return new HomeStylePersistenceTourManifestCapture(
            CaptureKey: $"interactive:home-style-persistence:{state}",
            PairKey: $"interactive:home-style-persistence:{state}",
            CatalogIds: ["UI-CAT-HOME-002", "UI-CAT-HOME-003", "UI-CMD-HOME-FONT-002", "UI-CMD-HOME-FONT-003", "UI-CMD-HOME-FONT-004", "UI-CMD-HOME-ALIGN-001", "UI-CMD-HOME-NUM-002", "UI-CMD-HOME-NUM-003", "UI-CMD-HOME-STYLE-001", "UI-CMD-HOME-STYLE-003"],
            State: state,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-window-full",
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: Math.Min(ActualHeight, 780),
            SheetName: context.Sheet.Name,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            PersistenceStage: context.PersistenceStage,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            MergedRegionCount: context.Sheet.MergedRegions.Count,
            ConditionalFormatCount: context.Sheet.ConditionalFormats.Count,
            StyleSnapshot: snapshot,
            CommandPath: commandPath,
            EvidenceSummary: evidenceSummary);
    }

    private HomeStylePersistenceStyleSnapshot CreateHomeStylePersistenceStyleSnapshot(HomeStylePersistenceTourContext context)
    {
        var titleStyle = StyleAt(context.Sheet, new CellAddress(context.Sheet.Id, 1, 1));
        var currencyStyle = StyleAt(context.Sheet, new CellAddress(context.Sheet.Id, 4, 2));
        var colorStyle = StyleAt(context.Sheet, new CellAddress(context.Sheet.Id, 4, 3));
        var alignmentStyle = StyleAt(context.Sheet, new CellAddress(context.Sheet.Id, 4, 4));
        var percentStyle = StyleAt(context.Sheet, new CellAddress(context.Sheet.Id, 4, 5));
        var borderStyle = StyleAt(context.Sheet, new CellAddress(context.Sheet.Id, 4, 2));
        var goodStyle = StyleAt(context.Sheet, new CellAddress(context.Sheet.Id, 4, 7));
        var cfRule = context.Sheet.ConditionalFormats.FirstOrDefault();

        return new HomeStylePersistenceStyleSnapshot(
            TitleMerged: context.Sheet.MergedRegions.Contains(context.TitleRange),
            TitleBold: titleStyle.Bold,
            TitleFillColor: FormatHomeStylePersistenceColor(titleStyle.FillColor),
            TitleFontColor: FormatHomeStylePersistenceColor(titleStyle.FontColor),
            TitleHorizontalAlignment: titleStyle.HorizontalAlignment.ToString(),
            CurrencyNumberFormat: currencyStyle.NumberFormat,
            PercentNumberFormat: percentStyle.NumberFormat,
            ColorFontColor: FormatHomeStylePersistenceColor(colorStyle.FontColor),
            ColorFillColor: FormatHomeStylePersistenceColor(colorStyle.FillColor),
            ColorBold: colorStyle.Bold,
            ColorItalic: colorStyle.Italic,
            AlignmentHorizontal: alignmentStyle.HorizontalAlignment.ToString(),
            AlignmentVertical: alignmentStyle.VerticalAlignment.ToString(),
            AlignmentWrapText: alignmentStyle.WrapText,
            AlignmentTextRotation: alignmentStyle.TextRotation,
            BorderTop: borderStyle.BorderTop.Style.ToString(),
            BorderRight: borderStyle.BorderRight.Style.ToString(),
            BorderBottom: borderStyle.BorderBottom.Style.ToString(),
            BorderLeft: borderStyle.BorderLeft.Style.ToString(),
            GoodStyleFillColor: FormatHomeStylePersistenceColor(goodStyle.FillColor),
            ConditionalFormatRuleType: cfRule?.RuleType.ToString() ?? string.Empty,
            ConditionalFormatOperator: cfRule?.Operator.ToString() ?? string.Empty,
            ConditionalFormatValue1: cfRule?.Value1 ?? string.Empty,
            ConditionalFormatFillColor: FormatHomeStylePersistenceColor(cfRule?.FormatIfTrue?.FillColor));
    }

    private CellStyle StyleAt(Sheet sheet, CellAddress address)
    {
        var styleId = sheet.GetCell(address)?.StyleId ?? sheet.GetStyleOnly(address.Row, address.Col) ?? StyleId.Default;
        return _workbook.GetStyle(styleId);
    }

    private static string FormatHomeStylePersistenceColor(CellColor? color) =>
        color is { } value ? $"#{value.R:X2}{value.G:X2}{value.B:X2}" : string.Empty;

    private static void DeleteHomeStylePersistenceTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_home_style_persistence_*.*"))
            File.Delete(file);

        DeleteIfExists(Path.Combine(outputDir, HomeStylePersistenceTourManifestFileName));
    }

    private void ValidateHomeStylePersistenceTourEvidence(
        string outputDir,
        IReadOnlyList<HomeStylePersistenceTourManifestCapture> captures,
        string savedWorkbookPath,
        HomeStylePersistenceTourContext reopenedContext)
    {
        if (captures.Count != 4)
            throw new InvalidOperationException($"Home style persistence tour expected 4 captures but created {captures.Count}.");

        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Home style persistence tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");

        var blank = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !IsNonBlankPng(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (blank.Length > 0)
            throw new InvalidOperationException(
                $"Home style persistence tour created blank capture(s): {string.Join(", ", blank)}.");

        if (!File.Exists(savedWorkbookPath) || new FileInfo(savedWorkbookPath).Length <= 0)
            throw new InvalidOperationException("Home style persistence tour did not retain a non-empty native FreeX workbook.");

        var snapshot = CreateHomeStylePersistenceStyleSnapshot(reopenedContext);
        if (!snapshot.TitleMerged ||
            snapshot.CurrencyNumberFormat != "$#,##0.00" ||
            snapshot.PercentNumberFormat != "0.0%" ||
            snapshot.BorderTop != BorderStyle.Thin.ToString() ||
            snapshot.ConditionalFormatRuleType != CfRuleType.CellValue.ToString() ||
            snapshot.ConditionalFormatValue1 != "88")
        {
            throw new InvalidOperationException("Home style persistence tour reopened workbook without the expected persisted style, merge, border, number, and conditional-format state.");
        }
    }

    private static async Task WriteHomeStylePersistenceTourManifestAsync(
        string outputDir,
        HomeStylePersistenceTourContext context,
        IReadOnlyList<HomeStylePersistenceTourManifestCapture> captures,
        IReadOnlyList<string> submittedCommands)
    {
        var manifest = new HomeStylePersistenceTourManifest(
            Tool: "FREEX_HOME_STYLE_PERSISTENCE_TOUR",
            EvidenceFamily: "home-style-persistence",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "home:style-number-border-persistence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_home_style_persistence_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-HOME-002", "UI-CAT-HOME-003", "UI-CMD-HOME-FONT-002", "UI-CMD-HOME-FONT-003", "UI-CMD-HOME-FONT-004", "UI-CMD-HOME-ALIGN-001", "UI-CMD-HOME-NUM-002", "UI-CMD-HOME-NUM-003", "UI-CMD-HOME-STYLE-001", "UI-CMD-HOME-STYLE-003"],
            SheetName: context.Sheet.Name,
            ResultRange: context.ResultRange.ToString(),
            TitleRange: context.TitleRange.ToString(),
            DataRange: context.DataRange.ToString(),
            ConditionalFormatRange: context.ConditionalFormatRange.ToString(),
            SavedWorkbookPath: context.SavedWorkbookPath,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            PersistencePath: "SaveWorkbookToTargetAsync(.fxl native FreeX adapter) then OpenFileAsync(saved .fxl)",
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new HomeStylePersistenceTourManifestPairing(
                "interactive:home-style-persistence:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, keytip, UIA, dropdown gallery input, native save dialog, or screen capture input is used."
                    : "Window captures abort unless the expected FreeX WPF surface owns foreground focus before RenderTargetBitmap capture; dropdown/keytip paths are recorded as remaining foreground-only evidence."),
            SubmittedCommands: submittedCommands,
            Captures: captures,
            CoveredStates:
            [
                "Seeded worksheet before submitted Home style commands.",
                "Submitted font style/color/fill, border, number format, alignment/wrap/rotation, merge, cell-style preset, and conditional-format commands.",
                "Native FreeX .fxl save while submitted Home style state remains visible.",
                "Native FreeX .fxl reopen restoring style, merge, number, border, cell-style, and conditional-format metadata."
            ],
            Limitations:
            [
                "This tour drives FreeX command paths in process and captures WPF windows with RenderTargetBitmap; it is not foreground CopyFromScreen proof.",
                "No physical mouse, keytip, dropdown-gallery traversal, Format Cells Ctrl+1 submission, or UI Automation input is synthesized.",
                "Foreground-only dropdown/keytip gaps remain for font/color/fill galleries, Borders menu keyboard traversal, number-format dropdown, Cell Styles gallery, Conditional Formatting gallery, and Format Cells dialog OK/access-key paths.",
                "Persistence is proven for the native FreeX .fxl adapter through host save/open services; XLSX style/conditional-format interoperability and Microsoft Excel paired evidence remain separate compatibility lanes.",
                "Representative ranges cover value cells and plain ranges; protected, table, formula, merged-overlap, LCID/date-token, and full theme-matrix breadth remain follow-up targets."
            ]);

        var path = Path.Combine(outputDir, HomeStylePersistenceTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HomeStylePersistenceTourManifest);
    }

    private sealed record HomeStylePersistenceTourContext(
        Sheet Sheet,
        GridRange ResultRange,
        GridRange TitleRange,
        GridRange HeaderRange,
        GridRange DataRange,
        GridRange FontColorFillRange,
        GridRange CurrencyRange,
        GridRange PercentRange,
        GridRange AlignmentRange,
        GridRange GoodCellStyleRange,
        GridRange NeutralCellStyleRange,
        GridRange BadCellStyleRange,
        GridRange ConditionalFormatRange,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistenceStage);

    private sealed record HomeStylePersistenceTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string SheetName,
        string ResultRange,
        string TitleRange,
        string DataRange,
        string ConditionalFormatRange,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistencePath,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        HomeStylePersistenceTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<string> SubmittedCommands,
        IReadOnlyList<HomeStylePersistenceTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record HomeStylePersistenceTourManifestPairing(
        string PairKeyTemplate,
        string CounterpartApp,
        string CounterpartTool,
        string CounterpartStatus);

    private sealed record HomeStylePersistenceTourManifestCapture(
        string CaptureKey,
        string PairKey,
        IReadOnlyList<string> CatalogIds,
        string State,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SheetName,
        string SelectedRange,
        string PersistenceStage,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        int MergedRegionCount,
        int ConditionalFormatCount,
        HomeStylePersistenceStyleSnapshot StyleSnapshot,
        string CommandPath,
        string EvidenceSummary);

    private sealed record HomeStylePersistenceStyleSnapshot(
        bool TitleMerged,
        bool TitleBold,
        string TitleFillColor,
        string TitleFontColor,
        string TitleHorizontalAlignment,
        string CurrencyNumberFormat,
        string PercentNumberFormat,
        string ColorFontColor,
        string ColorFillColor,
        bool ColorBold,
        bool ColorItalic,
        string AlignmentHorizontal,
        string AlignmentVertical,
        bool AlignmentWrapText,
        int AlignmentTextRotation,
        string BorderTop,
        string BorderRight,
        string BorderBottom,
        string BorderLeft,
        string GoodStyleFillColor,
        string ConditionalFormatRuleType,
        string ConditionalFormatOperator,
        string ConditionalFormatValue1,
        string ConditionalFormatFillColor);
}
