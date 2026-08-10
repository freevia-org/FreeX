using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

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
/// The OS spooler call sits behind the injectable <see cref="IPlatformPrinter"/> seam (Linux/macOS bind
/// the CUPS <c>lp</c>/<c>lpstat</c> utilities via <see cref="CupsPlatformPrinter"/>; tests/headless hosts
/// inject <see cref="NullPlatformPrinter"/>). When no spooler is available, Print degrades to writing the
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

    // -------------------------------------------------------------------------------------------------------
    // Print dialog chrome helpers
    // -------------------------------------------------------------------------------------------------------

    private static AvaloniaCompactDialogChromeStyle PrintDialogChromeStyle => new(FormulaBarFontFamily);

    private static void ApplyPrintButtonChrome(Button button, double minWidth = 80, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, PrintDialogChromeStyle, minWidth, isDefault);

    private static void ApplyPrintTextBoxChrome(TextBox tb)
        => AvaloniaCompactDialogChrome.ApplyTextBox(tb, PrintDialogChromeStyle);

    private static void ApplyPrintComboBoxChrome(ComboBox cb)
        => AvaloniaCompactDialogChrome.ApplyComboBox(cb, PrintDialogChromeStyle);

    private static void ApplyPrintRadioButtonChrome(RadioButton rb)
    {
        StripContentMnemonic(rb);
        AvaloniaCompactDialogChrome.ApplyRadioButton(rb, PrintDialogChromeStyle);
    }

    private static void ApplyPrintCheckBoxChrome(CheckBox cb)
    {
        StripContentMnemonic(cb);
        AvaloniaCompactDialogChrome.ApplyCheckBox(cb, PrintDialogChromeStyle);
    }

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

        var printers = _platformPrinter.CanPrint
            ? await _platformPrinter.GetPrintersAsync()
            : [];

        await ShowPrintDialogCoreAsync(scopePlan, printers);
    }

    private async Task ShowPrintDialogCoreAsync(
        WorkbookExportScopePlan scopePlan,
        IReadOnlyList<PrinterDescriptor> printers)
    {
        var dialog = new Window
        {
            Title = UiText.Get("Print_Title"),
            Width = 420,
            Height = 480,
            MinWidth = 380,
            MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PrintDialog");

        var content = new StackPanel { Spacing = 14 };

        // ── Printer destination ───────────────────────────────────────────────
        content.Children.Add(CreatePrintSectionHeader(UiText.Get("Print_PrinterHeader")));
        var canSpool = _platformPrinter.CanPrint && printers.Count > 0;
        var printerCombo = new ComboBox
        {
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
            IsEnabled = canSpool,
        };
        ApplyPrintComboBoxChrome(printerCombo);
        AutomationProperties.SetAutomationId(printerCombo, "PrintPrinterComboBox");
        foreach (var printer in printers)
            printerCombo.Items.Add(printer.DisplayName);

        var defaultIndex = -1;
        for (var i = 0; i < printers.Count; i++)
        {
            if (printers[i].IsDefault)
            {
                defaultIndex = i;
                break;
            }
        }

        if (printers.Count > 0)
            printerCombo.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;

        content.Children.Add(printerCombo);

        if (!canSpool)
        {
            content.Children.Add(new TextBlock
            {
                Text = UiText.Get("Print_NoPrinterNote"),
                Foreground = HeaderForeground,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            });
        }

        // ── Scope ─────────────────────────────────────────────────────────────
        content.Children.Add(CreatePrintSectionHeader(UiText.Get("Print_ScopeHeader")));
        var selectedScope = scopePlan.DefaultScope;
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
            ApplyPrintRadioButtonChrome(radio);
            AutomationProperties.SetAutomationId(radio, "PrintScope_" + option.Scope);
            var capturedScope = option.Scope;
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                    selectedScope = capturedScope;
            };
            content.Children.Add(radio);
        }

        // ── Pages ─────────────────────────────────────────────────────────────
        content.Children.Add(CreatePrintSectionHeader(UiText.Get("Print_PagesHeader")));
        var pageRangeKind = PrintJobPageRangeKind.AllPages;

        var allPagesRadio = new RadioButton
        {
            GroupName = "PrintPages",
            Content = UiText.Get("Print_PagesAll"),
            IsChecked = true,
            Margin = new Thickness(0, 2),
        };
        ApplyPrintRadioButtonChrome(allPagesRadio);
        AutomationProperties.SetAutomationId(allPagesRadio, "PrintPagesAll");

        var rangeRadio = new RadioButton
        {
            GroupName = "PrintPages",
            Content = UiText.Get("Print_PagesRange"),
            Margin = new Thickness(0, 2),
        };
        ApplyPrintRadioButtonChrome(rangeRadio);
        AutomationProperties.SetAutomationId(rangeRadio, "PrintPagesRange");

        var fromBox = new TextBox { Text = "1", Width = 64 };
        ApplyPrintTextBoxChrome(fromBox);
        AutomationProperties.SetAutomationId(fromBox, "PrintPagesFrom");
        AutomationProperties.SetName(fromBox, UiText.Get("Print_PagesFrom"));
        var toBox = new TextBox { Text = "1", Width = 64 };
        ApplyPrintTextBoxChrome(toBox);
        AutomationProperties.SetAutomationId(toBox, "PrintPagesTo");
        AutomationProperties.SetName(toBox, UiText.Get("Print_PagesTo"));
        fromBox.IsEnabled = false;
        toBox.IsEnabled = false;

        allPagesRadio.IsCheckedChanged += (_, _) =>
        {
            if (allPagesRadio.IsChecked == true)
            {
                pageRangeKind = PrintJobPageRangeKind.AllPages;
                fromBox.IsEnabled = false;
                toBox.IsEnabled = false;
            }
        };
        rangeRadio.IsCheckedChanged += (_, _) =>
        {
            if (rangeRadio.IsChecked == true)
            {
                pageRangeKind = PrintJobPageRangeKind.PageRange;
                fromBox.IsEnabled = true;
                toBox.IsEnabled = true;
            }
        };

        content.Children.Add(allPagesRadio);
        content.Children.Add(rangeRadio);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(22, 0, 0, 0),
            Children =
            {
                new TextBlock { Text = UiText.Get("Print_PagesFrom"), VerticalAlignment = AvaloniaVerticalAlignment.Center, FontSize = 12, FontFamily = FormulaBarFontFamily },
                fromBox,
                new TextBlock { Text = UiText.Get("Print_PagesTo"), VerticalAlignment = AvaloniaVerticalAlignment.Center, FontSize = 12, FontFamily = FormulaBarFontFamily },
                toBox,
            },
        });

        // ── Copies + collate ───────────────────────────────────────────────────
        content.Children.Add(CreatePrintSectionHeader(UiText.Get("Print_CopiesHeader")));
        var copiesBox = new TextBox { Text = "1", Width = 72 };
        ApplyPrintTextBoxChrome(copiesBox);
        AutomationProperties.SetAutomationId(copiesBox, "PrintCopies");
        AutomationProperties.SetName(copiesBox, UiText.Get("Print_CopiesHeader"));

        var collateCheck = new CheckBox
        {
            Content = UiText.Get("Print_Collate"),
            IsChecked = true,
            Margin = new Thickness(0, 2),
        };
        ApplyPrintCheckBoxChrome(collateCheck);
        AutomationProperties.SetAutomationId(collateCheck, "PrintCollate");

        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = UiText.Get("Print_CopiesLabel"), VerticalAlignment = AvaloniaVerticalAlignment.Center, FontSize = 12, FontFamily = FormulaBarFontFamily },
                copiesBox,
            },
        });
        content.Children.Add(collateCheck);

        // ── Buttons ─────────────────────────────────────────────────────────────
        var printButton = new Button
        {
            Content = canSpool ? UiText.Get("Print_PrintButton") : UiText.Get("Print_SaveAsPdfButton"),
            MinWidth = 96,
        };
        ApplyPrintButtonChrome(printButton, minWidth: 96, isDefault: true);
        AutomationProperties.SetAutomationId(printButton, "PrintConfirmButton");

        var cancelButton = new Button
        {
            Content = UiText.Get("Print_CancelButton"),
            MinWidth = 96,
            IsCancel = true,
        };
        ApplyPrintButtonChrome(cancelButton, minWidth: 96);
        AutomationProperties.SetAutomationId(cancelButton, "PrintCancelButton");
        cancelButton.Click += (_, _) => dialog.Close();

        printButton.Click += async (_, _) =>
        {
            var request = BuildPrintJobRequest(
                selectedScope,
                pageRangeKind,
                ParsePositiveInt(fromBox.Text, fallback: 1),
                ParsePositiveInt(toBox.Text, fallback: null),
                ParsePositiveInt(copiesBox.Text, fallback: 1) ?? 1,
                collateCheck.IsChecked == true);

            var printerId = canSpool && printerCombo.SelectedIndex >= 0 && printerCombo.SelectedIndex < printers.Count
                ? printers[printerCombo.SelectedIndex].Id
                : null;

            dialog.Close();
            await ExecutePrintJobAsync(request, printerId, canSpool);
        };

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([cancelButton, printButton]);

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        root.Children.Add(buttonRow);
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 12),
            Content = content,
        });

        dialog.Content = root;
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialog.Close();
                e.Handled = true;
            }
        };
        dialog.Opened += (_, _) =>
        {
            if (PrintSettingsPlanner.InitialDialogFocusTarget == PrintDialogFocusTarget.ConfirmAction)
                printButton.Focus();
        };
        await dialog.ShowDialog(this);
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
    /// spools it through <see cref="IPlatformPrinter"/> or, when no spooler is available, falls back to the
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
            if (result.Submission is { } submission)
                RefreshShell(UiText.Format("Print_Sent", submission.StatusText));
            else if (result.Fallback is { } fallback)
                RefreshShell(fallback.StatusText);
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

    private Task<byte[]> RenderPrintReadyPdfAsync(
        PortablePdfExportPlan exportPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var pdfBuffer = new MemoryStream();
        Pdf.AvaloniaPdfDocumentExporter.Save(
            _session.Workbook,
            exportPlan,
            pdfBuffer,
            options: null,
            workbookDirectory: ResolveWorkbookDirectoryForHeaderFooter());
        return Task.FromResult(pdfBuffer.ToArray());
    }

    private async Task<PrintSubmissionResult> SpoolPrintJobAsync(
        PrintJobSubmission submission,
        CancellationToken cancellationToken)
    {
        _isSaving = true;
        UpdateSaveButton();
        try
        {
            _statusText.Text = UiText.Get("Print_Spooling");
            _statusText.Foreground = Brush(67, 113, 83);

            return await _platformPrinter.SubmitAsync(submission, cancellationToken);
        }
        finally
        {
            _isSaving = false;
            UpdateSaveButton();
        }
    }

    /// <summary>
    /// No-spooler fallback: write the print-ready PDF where the user chooses. This keeps Print useful on
    /// hosts without CUPS (and is what tests exercise via <see cref="NullPlatformPrinter"/>).
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
                    !await ConfirmNormalizedPdfOverwriteAsync(exportTargetPlan.Path))
                {
                    return WorkbookPrintFallbackResult.Canceled(UiText.Get("Print_SaveCanceled"));
                }

                path = exportTargetPlan.Path;

                try
                {
                    await File.WriteAllBytesAsync(path, documentBytes, cancellationToken);
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

    private static int? ParsePositiveInt(string? text, int? fallback)
    {
        if (int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 1)
            return value;

        return fallback;
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
