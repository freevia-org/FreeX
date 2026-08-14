using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// File ▸ Print for the Avalonia shell (the WPF host already prints; this brings the cross-platform shell
/// to parity). All non-UI logic is shared and portable so macOS inherits it: scope selection comes from
/// <see cref="WorkbookExportScopePlanner"/>, the page/copy/range/collate decisions from
/// <see cref="PrintJobPlanner"/>, and the document is rendered through the same
/// <see cref="PortablePdfExportPlanner"/> + <see cref="Pdf.AvaloniaPdfDocumentExporter"/> path that File ▸
/// Export to PDF uses — Print is "render the print-ready document, then spool it" rather than a second
/// rendering engine.
///
/// The OS spooler call sits behind the canonical <see cref="IPlatformPrintService"/> seam. Linux and macOS
/// use <see cref="CupsPrintService"/>; unsupported hosts report no destinations. When no spooler is available,
/// Print degrades to writing the
/// print-ready PDF to a file the user picks, so the feature still produces correct output everywhere.
///
/// This file is UI + platform glue only; it deliberately holds no selection/validation logic of its own.
/// </summary>
public sealed partial class MainWindow
{
    internal static bool HasPrintSelection(GridRange? selectedRange) => selectedRange is not null;

    /// <summary>
    /// Directory that contains the workbook's saved file, with a trailing separator, for the
    /// print/export header/footer <c>&amp;Z</c> / <c>&amp;[Path]</c> tokens
    /// (R15-header-footer-print-titles-2); empty when the workbook has never been saved.
    /// </summary>
    private string ResolveWorkbookDirectoryForHeaderFooter() =>
        PagePrintTextPlanner.ResolveWorkbookDirectoryTokenValue(_session.CurrentFilePath);

    private async Task ShowPrintDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        ClearSelectedDrawingObject();

        var hasSelection = HasPrintSelection(_session.SelectedRange);
        var workflowPlan = WorkbookPrintWorkflow.CreatePlan(
            _session.Workbook,
            hasSelection,
            new PrintJobRequest(
                WorkbookExportPrintScope.ActiveSheet,
                ActiveSheetIndex: ResolveActiveSheetIndex()),
            PrintExportHostCapabilities.AvaloniaPortable());
        var scopePlan = workflowPlan.Readiness.ScopePlan;

        if (!scopePlan.CanExport)
        {
            ShowEditIssue(UiText.Get("Print_Unavailable"));
            return;
        }

        var discovery = _printService.IsSupported
            ? await _printService.DiscoverAsync()
            : new PrinterDiscoveryResult(PrinterDiscoveryStatus.Unavailable, [], null);
        await ShowPrintDialogCoreAsync(scopePlan, discovery);
    }

    private async Task ShowPrintDialogCoreAsync(
        WorkbookExportScopePlan scopePlan,
        PrinterDiscoveryResult discovery)
    {
        var selectedScope = scopePlan.DefaultScope;
        var canSpool = _printService.IsSupported && discovery.IsAvailable;
        var dialogDiscovery = canSpool
            ? discovery
            : discovery with { Message = UiText.Get("Print_NoPrinterNote") };
        var selection = await AvaloniaPrintDialogWorkflow.ShowAsync(
            this,
            dialogDiscovery,
            static () => new FreeXPrintDialogWindow(),
            new AvaloniaPrintDialogOptions
            {
                Width = 420,
                ChoiceMinWidth = 220,
                AutomationIds = new AvaloniaPrintDialogAutomationIds(
                    Printer: "PrintPrinterComboBox",
                    Copies: "PrintCopies",
                    PageRange: "PrintPageRange",
                    Collation: "PrintCollate",
                    Submit: "PrintConfirmButton",
                    Dialog: "PrintDialog",
                    Cancel: "PrintCancelButton",
                    FirstPage: "PrintPagesFrom",
                    LastPage: "PrintPagesTo"),
                Collation = AvaloniaPrintDialogCollation.Selectable,
                PageRangeKinds = [PrintPageRangeKind.All, PrintPageRangeKind.Range],
                CreateAdditionalContent = () => CreatePrintScopePanel(
                    scopePlan,
                    scope => selectedScope = scope),
                AllowSubmissionWithoutPrinter = true,
                ShowOrientation = false,
                Text = BuildPrintDialogText(canSpool),
            });

        if (selection is null)
            return;

        var effectiveRange = selection.EffectivePageRange;
        var request = BuildPrintJobRequest(
            selectedScope,
            effectiveRange.Kind == PrintPageRangeKind.All
                ? PrintJobPageRangeKind.AllPages
                : PrintJobPageRangeKind.PageRange,
            effectiveRange.FirstPage,
            effectiveRange.LastPage,
            selection.Copies,
            selection.Collate);

        await ExecutePrintJobAsync(
            request,
            canSpool ? selection.PrinterName : null,
            canSpool);
    }

    private static AvaloniaPrintDialogText BuildPrintDialogText(bool canSpool) =>
        new(
            UiText.Get("Print_Title"),
            UiText.Get("Print_PrinterHeader"),
            UiText.Get("Print_CopiesLabel"),
            UiText.Get("Print_PagesHeader"),
            UiText.Get("Print_PagesFrom"),
            UiText.Get("Print_PagesTo"),
            string.Empty,
            string.Empty,
            [UiText.Get("Print_PagesAll"), UiText.Get("Print_PagesRange")],
            [string.Empty],
            UiText.Get("Print_Collate"),
            canSpool ? UiText.Get("Print_PrintButton") : UiText.Get("Print_SaveAsPdfButton"),
            UiText.Get("Print_CancelButton"),
            new PrintDialogText(
                UiText.Get("Print_ReadyStatus"),
                UiText.Get("Print_NoPrinterNote"),
                UiText.Get("Print_CopiesOutOfRange"),
                UiText.Get("Print_FirstPageInvalid"),
                UiText.Get("Print_LastPageBeforeFirstPage")));

    private Control CreatePrintScopePanel(
        WorkbookExportScopePlan scopePlan,
        Action<WorkbookExportPrintScope> selectScope)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(CreatePrintSectionHeader(UiText.Get("Print_ScopeHeader")));
        foreach (var option in scopePlan.Scopes)
        {
            var radio = new RadioButton
            {
                GroupName = "PrintScope",
                Content = FormatPrintScopeLabel(option.Scope, option.IsAvailable),
                IsEnabled = option.IsAvailable,
                IsChecked = option.IsDefault,
                Margin = new Thickness(0, 2),
            };
            StripContentMnemonic(radio);
            AvaloniaCompactDialogChrome.ApplyRadioButton(
                radio,
                new AvaloniaCompactDialogChromeStyle(FormulaBarFontFamily));
            AutomationProperties.SetAutomationId(radio, "PrintScope_" + option.Scope);
            var capturedScope = option.Scope;
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                    selectScope(capturedScope);
            };
            panel.Children.Add(radio);
        }

        return panel;
    }

    private sealed class FreeXPrintDialogWindow : AvaloniaDialogWindow
    {
        public FreeXPrintDialogWindow()
            : base(new AvaloniaCompactDialogChromeStyle(FormulaBarFontFamily))
        {
        }
    }

    private PrintJobRequest BuildPrintJobRequest(
        WorkbookExportPrintScope scope,
        PrintJobPageRangeKind pageRangeKind,
        int? fromPage,
        int? toPage,
        int copies,
        bool collate)
    {
        var selectedRange = scope == WorkbookExportPrintScope.SelectedRange
            ? _session.SelectedRange
            : (GridRange?)null;

        return new PrintJobRequest(
            scope,
            copies,
            collate,
            pageRangeKind,
            fromPage,
            toPage,
            ActiveSheetIndex: ResolveActiveSheetIndex(),
            SelectedRange: selectedRange);
    }

    /// <summary>
    /// Renders the chosen scope to a print-ready PDF (the same exporter File ▸ Export uses), then either
    /// spools it through <see cref="IPlatformPrintService"/> or, when no spooler is available, falls back to the
    /// save-file picker so the print-ready document still reaches the user.
    /// </summary>
    private async Task ExecutePrintJobAsync(PrintJobRequest request, string? printerId, bool canSpool)
    {
        if (_isSaving)
            return;

        var workflowPlan = WorkbookPrintWorkflow.CreatePlan(
            _session.Workbook,
            HasPrintSelection(_session.SelectedRange),
            request,
            PrintExportHostCapabilities.AvaloniaPortable(
                canSubmitToPlatformPrinter: canSpool,
                hasPrinterDestination: !string.IsNullOrWhiteSpace(printerId)));
        var result = await WorkbookPrintWorkflow.ExecutePortableAsync(
            workflowPlan,
            printerId,
            BuildPrintJobTitle(),
            RenderPrintReadyPdfAsync,
            SpoolPrintJobAsync,
            SavePrintReadyPdfAsync);

        if (result.Succeeded)
        {
            var imageDiagnostics = result.RenderedDocument?.ImageDiagnostics ?? [];
            if (result.Submission is { } submission)
                RefreshShell(AppendImageDiagnosticsSuffix(
                    UiText.Format("Print_Sent", result.StatusText),
                    imageDiagnostics));
            else if (result.Fallback is { } fallback)
                RefreshShell(AppendImageDiagnosticsSuffix(fallback.StatusText, imageDiagnostics));
            return;
        }

        if (result.Outcome == WorkbookPrintExecutionOutcome.Canceled &&
            string.IsNullOrWhiteSpace(result.StatusText))
        {
            return;
        }

        ShowExportIssue(result.Exception is not null
            ? UiText.Format("Print_RenderFailed", result.Exception.Message)
            : result.StatusText);
    }

    private Task<WorkbookPrintRenderResult> RenderPrintReadyPdfAsync(
        PortablePdfExportPlan exportPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var pdfBuffer = new MemoryStream();
        var outcome = Pdf.AvaloniaPdfDocumentExporter.Save(
            _session.Workbook,
            exportPlan,
            pdfBuffer,
            options: null,
            workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter());
        return Task.FromResult(new WorkbookPrintRenderResult(
            pdfBuffer.ToArray(),
            outcome.Result.ImageDiagnostics));
    }

    private static string AppendImageDiagnosticsSuffix(
        string statusText,
        IReadOnlyList<string> imageDiagnostics) =>
        imageDiagnostics.Count == 0
            ? statusText
            : $"{statusText} ({imageDiagnostics.Count} image warning{(imageDiagnostics.Count == 1 ? "" : "s")})";

    private async Task<PrintSubmissionResult> SpoolPrintJobAsync(
        string pdfPath,
        PrintSelection selection,
        CancellationToken cancellationToken)
    {
        _isSaving = true;
        UpdateSaveButton();
        try
        {
            _statusText.Text = UiText.Get("Print_Spooling");
            _statusText.Foreground = Brush(67, 113, 83);

            return await _printService.SubmitAsync(pdfPath, selection, cancellationToken);
        }
        finally
        {
            _isSaving = false;
            UpdateSaveButton();
        }
    }

    /// <summary>
    /// No-spooler fallback: write the print-ready PDF where the user chooses. This keeps Print useful on
    /// hosts without CUPS.
    /// </summary>
    private async Task<WorkbookPrintFallbackResult> SavePrintReadyPdfAsync(
        byte[] documentBytes,
        CancellationToken cancellationToken)
    {
        if (!TryBeginFileOperation())
            return WorkbookPrintFallbackResult.Canceled(statusText: "");

        try
        {
            if (!StorageProvider.CanSave)
            {
                return WorkbookPrintFallbackResult.Failure(UiText.Get("Print_NoSpoolerNoSave"));
            }

            var storageFile = await ShowPortablePdfSavePickerAsync(UiText.Get("Print_SaveAsPdfButton"));

            if (storageFile is null)
                return WorkbookPrintFallbackResult.Canceled(statusText: "");

            using (storageFile)
            {
                var path = storageFile.LocalPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return WorkbookPrintFallbackResult.Failure(UiText.Get("Print_RequiresLocalPath"));
                }

                var exportTargetPlan = ExportFilePickerPlanner.BuildPortablePdfSaveTargetPlan(path, File.Exists);
                if (exportTargetPlan.ShouldConfirmNormalizedOverwrite &&
                    !await ConfirmNormalizedOverwriteAsync(
                        exportTargetPlan.Path,
                        NormalizedOverwriteTargetKind.Pdf))
                {
                    return WorkbookPrintFallbackResult.Canceled(UiText.Get("Print_SaveCanceled"));
                }

                path = exportTargetPlan.Path;

                try
                {
                    await AtomicFileWriter.WriteAllBytesAsync(path, documentBytes, cancellationToken);
                    return WorkbookPrintFallbackResult.Success(
                        UiText.Format("Print_SavedPdf", Path.GetFileName(path)),
                        path);
                }
                catch (Exception ex)
                {
                    return WorkbookPrintFallbackResult.Failure(
                        UiText.Format("Print_RenderFailed", ex.Message),
                        ex);
                }
            }
        }
        finally
        {
            EndFileOperation();
        }
    }

    private string BuildPrintJobTitle()
    {
        var name = Path.GetFileNameWithoutExtension(_session.DisplayName);
        return string.IsNullOrWhiteSpace(name) ? "FreeX" : name;
    }

    private static string FormatPrintScopeLabel(WorkbookExportPrintScope scope, bool isAvailable) =>
        scope switch
        {
            WorkbookExportPrintScope.SelectedRange => isAvailable
                ? UiText.Get("Print_ScopeSelection")
                : UiText.Get("Print_ScopeSelectionUnavailable"),
            WorkbookExportPrintScope.VisibleWorkbook => UiText.Get("Print_ScopeWorkbook"),
            _ => UiText.Get("Print_ScopeActiveSheet")
        };

    private TextBlock CreatePrintSectionHeader(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 6, 0, 0),
        };
}
