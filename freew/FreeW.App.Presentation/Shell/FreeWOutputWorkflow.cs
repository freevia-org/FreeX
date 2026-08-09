using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using Free.Shared.Shell;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Shell;

public enum FreeWExportFormat
{
    Pdf,
    Xps,
}

public sealed record FreeWExportRequestPlan(
    FreeWExportFormat Format,
    string PickerTitle,
    string CommandName,
    FileDialogPickerTypeDescriptor FileType,
    string SuggestedFileName,
    string DefaultExtensionWithDot,
    string Filter)
{
    public string DefaultExtensionWithoutDot => DefaultExtensionWithDot.TrimStart('.');
}

public sealed record FreeWExportArtifact(int? PageCount = null, string? Backend = null);

public enum FreeWExportExecutionOutcome
{
    Succeeded,
    Canceled,
    Failed,
}

public sealed record FreeWExportExecutionResult(
    FreeWExportExecutionOutcome Outcome,
    FreeWExportRequestPlan Plan,
    string? Path,
    string Message,
    FreeWExportArtifact? Artifact = null,
    Exception? Exception = null)
{
    public bool Succeeded => Outcome == FreeWExportExecutionOutcome.Succeeded;
}

public static class FreeWExportWorkflow
{
    public static FreeWExportRequestPlan CreatePlan(FreeWExportFormat format, string? displayName)
    {
        var baseName = string.IsNullOrWhiteSpace(displayName)
            ? DocumentPersistenceWorkflow.DefaultFallbackDisplayName
            : Path.GetFileNameWithoutExtension(displayName.Trim());
        var isPdf = format == FreeWExportFormat.Pdf;
        var extension = isPdf ? ".pdf" : ".xps";
        var typeName = isPdf
            ? FreeWFileTextResources.PdfFileTypeName
            : FreeWFileTextResources.XpsFileTypeName;
        return new(
            format,
            isPdf ? FreeWFileTextResources.ExportPdfPickerTitle : FreeWFileTextResources.ExportXpsPickerTitle,
            isPdf ? FreeWFileTextResources.PdfExportCommand : FreeWFileTextResources.XpsExportCommand,
            new FileDialogPickerTypeDescriptor(
                typeName,
                [$"*{extension}"],
                isPdf
                    ? ["application/pdf"]
                    : ["application/oxps", "application/vnd.ms-xpsdocument"]),
            baseName + extension,
            extension,
            $"{typeName} (*{extension})|*{extension}");
    }

    public static async Task<FreeWExportExecutionResult> ExecuteAsync(
        FreeWExportRequestPlan plan,
        string path,
        Func<Stream, CancellationToken, ValueTask<FreeWExportArtifact>> renderAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(renderAsync);

        string? temporaryPath = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            temporaryPath = ExportAtomicWriter.CreateTempPath(path);
            FreeWExportArtifact artifact;
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                artifact = await renderAsync(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            ExportAtomicWriter.ReplaceTarget(temporaryPath, path);
            temporaryPath = null;
            return new(
                FreeWExportExecutionOutcome.Succeeded,
                plan,
                path,
                FormatSuccess(plan, path, artifact),
                artifact);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            return new(
                FreeWExportExecutionOutcome.Canceled,
                plan,
                path,
                $"{plan.CommandName} canceled.",
                Exception: ex);
        }
        catch (Exception ex)
        {
            return new(
                FreeWExportExecutionOutcome.Failed,
                plan,
                path,
                SisterAppFileTextPlanner.FormatCommandFailed(
                    FreeWFileTextResources.Document,
                    plan.CommandName,
                    ex.Message),
                Exception: ex);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { /* best effort */ }
            }
        }
    }

    private static string FormatSuccess(
        FreeWExportRequestPlan plan,
        string path,
        FreeWExportArtifact artifact)
    {
        if (plan.Format == FreeWExportFormat.Pdf &&
            artifact.PageCount is { } pageCount &&
            artifact.Backend is { } backend)
        {
            return FreeWFileTextResources.FormatPdfExported(
                pageCount,
                backend,
                Path.GetFileName(path));
        }

        return plan.Format == FreeWExportFormat.Xps
            ? FreeWFileTextResources.FormatXpsExported(path)
            : $"Exported to PDF: {path}";
    }
}

public sealed record FreeWPrintRequestPlan(
    string Description,
    int TotalPages,
    double PageWidthDip,
    double PageHeightDip,
    PrintSelection Selection);

public static class FreeWPrintRequestPlanner
{
    public static FreeWPrintRequestPlan Create(
        string description,
        PageSettings page,
        int totalPages,
        PrintSelection? selection = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(page);
        selection ??= new PrintSelection();
        selection.Validate();
        var (pageWidth, pageHeight) = PageLayout.PageSizeDip(page);
        return new(description, Math.Max(1, totalPages), pageWidth, pageHeight, selection);
    }

    public static (int FirstPage, int LastPage) ResolvePageRange(
        PrintPageRange range,
        int totalPages)
    {
        ArgumentNullException.ThrowIfNull(range);
        range.Validate();
        var lastDocumentPage = Math.Max(1, totalPages);
        return range.Kind switch
        {
            PrintPageRangeKind.All => (1, lastDocumentPage),
            PrintPageRangeKind.Single =>
                (Math.Clamp(range.FirstPage!.Value, 1, lastDocumentPage),
                 Math.Clamp(range.FirstPage.Value, 1, lastDocumentPage)),
            _ => ResolveBoundedRange(range, lastDocumentPage),
        };
    }

    public static PrintPageRange FromOneBasedRange(int firstPage, int lastPage, int totalPages)
    {
        var lastDocumentPage = Math.Max(1, totalPages);
        var first = Math.Clamp(firstPage, 1, lastDocumentPage);
        var last = Math.Clamp(lastPage, first, lastDocumentPage);
        return first == last ? PrintPageRange.Single(first) : PrintPageRange.Between(first, last);
    }

    private static (int FirstPage, int LastPage) ResolveBoundedRange(
        PrintPageRange range,
        int lastDocumentPage)
    {
        var first = Math.Clamp(range.FirstPage!.Value, 1, lastDocumentPage);
        return (first, Math.Clamp(range.LastPage!.Value, first, lastDocumentPage));
    }
}

public enum FreeWPrintExecutionOutcome
{
    Submitted,
    Canceled,
    Unavailable,
    Failed,
}

public sealed record FreeWPrintExecutionResult(
    FreeWPrintExecutionOutcome Outcome,
    string Message,
    PrinterDiscoveryResult? Discovery = null,
    PrintSelection? Selection = null,
    PrintSubmissionResult? Submission = null,
    Exception? Exception = null)
{
    public bool Succeeded => Outcome == FreeWPrintExecutionOutcome.Submitted;
}

public sealed record FreeWPrintSelectionHandoffPlan(
    PrintSelection PdfSelection,
    PrintSelection SubmissionSelection);

public static class FreeWPrintSelectionHandoffPlanner
{
    public static FreeWPrintSelectionHandoffPlan Build(
        PrintSelection selection,
        PrintRangeAndOrientationHandling handling)
    {
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();

        return handling == PrintRangeAndOrientationHandling.PreparedPdf
            ? new FreeWPrintSelectionHandoffPlan(
                selection,
                selection with
                {
                    PageRange = PrintPageRange.All,
                    Orientation = PrintOrientation.Document,
                })
            : new FreeWPrintSelectionHandoffPlan(new PrintSelection(), selection);
    }
}

public sealed class FreeWPortablePrintWorkflow
{
    private readonly IPlatformPrintService _printService;

    public FreeWPortablePrintWorkflow(IPlatformPrintService printService) =>
        _printService = printService ?? throw new ArgumentNullException(nameof(printService));

    public async Task<PrinterDiscoveryResult> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _printService.DiscoverAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(
                PrinterDiscoveryStatus.Cancelled,
                [],
                null,
                FreeWPrintMessagePlanner.Canceled);
        }
        catch (Exception ex)
        {
            return new(
                PrinterDiscoveryStatus.Failed,
                [],
                null,
                $"Printer discovery failed: {ex.Message}");
        }
    }

    public async Task<FreeWPrintExecutionResult> ExecuteAsync(
        Func<PrinterDiscoveryResult, CancellationToken, Task<PrintSelection?>> selectAsync,
        Func<Stream, PrintSelection, CancellationToken, ValueTask> renderPdfAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectAsync);
        ArgumentNullException.ThrowIfNull(renderPdfAsync);

        string? temporaryPath = null;
        PrinterDiscoveryResult? discovery = null;
        try
        {
            discovery = await DiscoverAsync(cancellationToken);
            if (discovery.Status == PrinterDiscoveryStatus.Cancelled)
                return Canceled(discovery);
            if (!discovery.IsAvailable)
            {
                return new(
                    FreeWPrintExecutionOutcome.Unavailable,
                    FreeWPrintMessagePlanner.FormatDiscovery(discovery),
                    discovery);
            }

            var selection = await selectAsync(discovery, cancellationToken);
            if (selection is null)
                return Canceled(discovery);
            selection.Validate();
            var handoff = FreeWPrintSelectionHandoffPlanner.Build(
                selection,
                _printService.RangeAndOrientationHandling);

            temporaryPath = Path.Combine(
                Path.GetTempPath(),
                $"FreeW-print-{Guid.NewGuid():N}.pdf");
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await renderPdfAsync(stream, handoff.PdfSelection, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var submission = await _printService.SubmitAsync(
                temporaryPath,
                handoff.SubmissionSelection,
                cancellationToken);
            return new(
                submission.Succeeded
                    ? FreeWPrintExecutionOutcome.Submitted
                    : submission.Status == PrintSubmissionStatus.Cancelled
                        ? FreeWPrintExecutionOutcome.Canceled
                        : submission.Status is PrintSubmissionStatus.NoPrinters or PrintSubmissionStatus.Unavailable
                            ? FreeWPrintExecutionOutcome.Unavailable
                            : FreeWPrintExecutionOutcome.Failed,
                FreeWPrintMessagePlanner.FormatSubmission(submission),
                discovery,
                selection,
                submission);
        }
        catch (OperationCanceledException ex)
        {
            return new(
                FreeWPrintExecutionOutcome.Canceled,
                FreeWPrintMessagePlanner.Canceled,
                discovery,
                Exception: ex);
        }
        catch (Exception ex)
        {
            return new(
                FreeWPrintExecutionOutcome.Failed,
                SisterAppFileTextPlanner.FormatCommandFailed(
                    FreeWFileTextResources.Document,
                    "Print",
                    ex.Message),
                discovery,
                Exception: ex);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { /* best effort */ }
            }
        }
    }

    private static FreeWPrintExecutionResult Canceled(PrinterDiscoveryResult discovery) =>
        new(FreeWPrintExecutionOutcome.Canceled, FreeWPrintMessagePlanner.Canceled, discovery);
}

public static class FreeWPrintMessagePlanner
{
    public const string Canceled = "Print canceled.";
    private const string Fallback = "Use Print Preview or Create PDF.";

    public static string FormatDiscovery(PrinterDiscoveryResult discovery)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        return discovery.Status switch
        {
            PrinterDiscoveryStatus.NoPrinters =>
                $"No printers are installed or available. {Fallback}",
            PrinterDiscoveryStatus.Unavailable =>
                AppendFallback(discovery.Message, "Direct printing is unavailable on this host."),
            PrinterDiscoveryStatus.Failed =>
                AppendFallback(discovery.Message, "Printer discovery failed."),
            PrinterDiscoveryStatus.Cancelled => Canceled,
            _ => $"Direct printing is unavailable. {Fallback}",
        };
    }

    public static string FormatSubmission(PrintSubmissionResult submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return submission.Status switch
        {
            PrintSubmissionStatus.Submitted => $"Sent to printer {submission.PrinterName}.",
            PrintSubmissionStatus.Cancelled => Canceled,
            PrintSubmissionStatus.NoPrinters =>
                $"No printers are installed or available. {Fallback}",
            PrintSubmissionStatus.Unavailable =>
                AppendFallback(submission.Message, "Direct printing is unavailable on this host."),
            _ => string.IsNullOrWhiteSpace(submission.Message)
                ? $"Print submission failed. {Fallback}"
                : submission.Message,
        };
    }

    public static BackstageDirectPrintCapability PlanCapability(
        bool isPrintServiceSupported,
        PrinterDiscoveryResult? discovery)
    {
        if (discovery?.IsAvailable == true)
        {
            return BackstageDirectPrintCapability.PlatformPrinterAvailable(
                "Platform printer discovery and foreground PDF submission are available on this host; no native system print dialog is used.");
        }

        if (!isPrintServiceSupported)
        {
            return BackstageDirectPrintCapability.Deferred(
                $"This host has no supported native printer service; {Fallback}");
        }

        var reason = discovery?.Status switch
        {
            PrinterDiscoveryStatus.NoPrinters => "No usable printer was discovered on this host.",
            PrinterDiscoveryStatus.Unavailable => "The platform printer backend is unavailable on this host.",
            PrinterDiscoveryStatus.Failed => "Platform printer discovery failed on this host.",
            PrinterDiscoveryStatus.Cancelled => "Printer discovery was canceled.",
            _ => "Printer discovery is still in progress.",
        };
        return BackstageDirectPrintCapability.Deferred($"{reason} {Fallback}");
    }

    private static string AppendFallback(string? message, string fallbackMessage) =>
        $"{(string.IsNullOrWhiteSpace(message) ? fallbackMessage : message.Trim())} {Fallback}";
}

public enum FreeWPrintPreviewPrimaryAction
{
    None,
    DirectPrint,
    CreatePdf,
}

public sealed record FreeWPrintPreviewActionPlan(
    FreeWPrintPreviewPrimaryAction Action,
    string Label,
    string Description,
    bool IsEnabled);

public sealed record FreeWPrintPreviewState(
    string Title,
    string DisplayName,
    string Description,
    IReadOnlyList<BackstageFieldRow> Fields,
    int CurrentPage,
    int TotalPages,
    string PageCountText,
    PrintSelection Options,
    FreeWPrintPreviewActionPlan PrimaryAction);

/// <summary>
/// Renderer-neutral print preview state. Hosts realize the returned page, option, summary, and primary
/// action plans through their native viewer controls and renderer-specific paginated surfaces.
/// </summary>
public sealed class FreeWPrintPreviewSession
{
    private readonly string _displayName;
    private readonly BackstagePrintPanePlan _summary;
    private readonly BackstageDirectPrintCapability _capability;
    private readonly bool _canCreatePdf;
    private readonly bool _canDirectPrint;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private PrintSelection _options = new();

    public FreeWPrintPreviewSession(
        string? displayName,
        PageSettings page,
        BackstageDirectPrintCapability capability,
        bool canCreatePdf,
        bool canDirectPrint)
    {
        _displayName = string.IsNullOrWhiteSpace(displayName) ? "Untitled" : displayName.Trim();
        _capability = capability ?? throw new ArgumentNullException(nameof(capability));
        _summary = BackstagePrintPanePlanner.Build(_displayName, page, capability);
        _canCreatePdf = canCreatePdf;
        _canDirectPrint = canDirectPrint;
    }

    public FreeWPrintPreviewState State => new(
        $"Print Preview - {_displayName}",
        _displayName,
        $"Preview uses the current paginated layout. {_capability.ActionDescription}",
        _summary.Fields,
        _currentPage,
        _totalPages,
        FormatPageCount(_totalPages),
        _options,
        BuildPrimaryAction());

    public FreeWPrintPreviewState SetPageCount(int totalPages)
    {
        _totalPages = Math.Max(1, totalPages);
        _currentPage = Math.Clamp(_currentPage, 1, _totalPages);
        _options = _options with
        {
            PageRange = NormalizeRange(_options.EffectivePageRange, _totalPages),
        };
        return State;
    }

    public FreeWPrintPreviewState GoToPage(int page)
    {
        _currentPage = Math.Clamp(page, 1, _totalPages);
        return State;
    }

    public FreeWPrintPreviewState ApplyOptions(PrintSelection options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options with
        {
            PageRange = NormalizeRange(options.EffectivePageRange, _totalPages),
        };
        return State;
    }

    private FreeWPrintPreviewActionPlan BuildPrimaryAction()
    {
        if (_capability.IsAvailable && _canDirectPrint)
        {
            return new(
                FreeWPrintPreviewPrimaryAction.DirectPrint,
                "Print",
                _capability.ActionDescription,
                IsEnabled: true);
        }

        if (_canCreatePdf)
        {
            return new(
                FreeWPrintPreviewPrimaryAction.CreatePdf,
                BackstageViewTextResources.CreatePdfLabel,
                _capability.DeferredNote ?? _capability.ActionDescription,
                IsEnabled: true);
        }

        return new(
            FreeWPrintPreviewPrimaryAction.None,
            BackstageViewTextResources.CreatePdfLabel,
            _capability.DeferredNote ?? _capability.ActionDescription,
            IsEnabled: false);
    }

    private static PrintPageRange NormalizeRange(PrintPageRange range, int totalPages)
    {
        if (range.Kind == PrintPageRangeKind.All)
            return PrintPageRange.All;

        var (first, last) = FreeWPrintRequestPlanner.ResolvePageRange(range, totalPages);
        return first == last ? PrintPageRange.Single(first) : PrintPageRange.Between(first, last);
    }

    private static string FormatPageCount(int pages) => pages == 1 ? "1 page" : $"{pages} pages";
}

public static class FreeWDocumentSnapshot
{
    public static TextDocument Clone(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var buffer = new MemoryStream();
        DocxWriter.Write(document, buffer);
        buffer.Position = 0;
        return DocxReader.Read(buffer);
    }
}
