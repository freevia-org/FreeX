using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum PrintExportNativePrintRouteKind
{
    None,
    NativePrintDialog,
    PlatformPrinter,
    PrintReadyPdfFallback
}

public enum PrintExportNativePrintReadinessStatus
{
    ReadyForNativePrintDialog,
    ReadyForPlatformPrinter,
    PrintReadyPdfFallback,
    PrintJobNotReady
}

public sealed record PrintExportHostCapabilities(
    string HostLabel,
    WorkbookExportPrintSurface Surface,
    bool SupportsNativePrintDialog,
    bool CanSubmitToPlatformPrinter,
    bool HasPrinterDestination)
{
    public string HostLabel { get; init; } =
        string.IsNullOrWhiteSpace(HostLabel) ? "FreeX host" : HostLabel.Trim();

    public WorkbookExportPrintSurface Surface { get; init; } =
        Surface ?? WorkbookExportPrintSurface.PortablePdf;

    public static PrintExportHostCapabilities WindowsWpf(bool hasPrinterDestination = true) =>
        new(
            "WPF Windows desktop",
            WorkbookExportPrintSurface.WindowsDesktop,
            SupportsNativePrintDialog: true,
            CanSubmitToPlatformPrinter: false,
            HasPrinterDestination: hasPrinterDestination);

    public static PrintExportHostCapabilities AvaloniaPortable(
        bool canSubmitToPlatformPrinter = false,
        bool hasPrinterDestination = false) =>
        new(
            "Avalonia portable",
            WorkbookExportPrintSurface.MacOs,
            SupportsNativePrintDialog: false,
            CanSubmitToPlatformPrinter: canSubmitToPlatformPrinter,
            HasPrinterDestination: hasPrinterDestination);
}

public sealed record PrintExportNativePrintReadinessPlan(
    PrintExportNativePrintReadinessStatus Status,
    PrintExportNativePrintRouteKind RouteKind,
    string StatusText,
    PrintJobPlan JobPlan)
{
    public bool IsNativePrintReady =>
        Status is PrintExportNativePrintReadinessStatus.ReadyForNativePrintDialog or
            PrintExportNativePrintReadinessStatus.ReadyForPlatformPrinter;

    public bool CanProducePrintReadyPdfFallback =>
        Status == PrintExportNativePrintReadinessStatus.PrintReadyPdfFallback && JobPlan.IsReady;
}

public sealed record PrintExportHostReadinessPlan(
    PrintExportHostCapabilities Host,
    WorkbookExportScopePlan ScopePlan,
    WorkbookExportPrintPlan PdfExportPlan,
    WorkbookExportPrintPlan XpsExportPlan,
    PrintExportNativePrintReadinessPlan NativePrintPlan);

/// <summary>
/// Host-neutral readiness contract for FreeX print/export. WPF and Avalonia still own their native UI
/// adapters, but the workbook/scope/page validation, XPS availability, and native-print fallback decision
/// are planned here so host glue cannot silently drift.
/// </summary>
public static class PrintExportHostReadinessPlanner
{
    public static PrintExportHostReadinessPlan Create(
        Workbook workbook,
        bool hasSelection,
        PrintJobRequest printJobRequest,
        PrintExportHostCapabilities host) =>
        Create(workbook, hasSelection, printJobRequest, host, out _);

    internal static PrintExportHostReadinessPlan Create(
        Workbook workbook,
        bool hasSelection,
        PrintJobRequest printJobRequest,
        PrintExportHostCapabilities host,
        out PortablePdfExportPlan? portablePlan)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(printJobRequest);
        ArgumentNullException.ThrowIfNull(host);

        var scopePlan = WorkbookExportScopePlanner.Build(workbook, hasSelection, host.Surface);
        var pdfPlan = CreateExportPlan(workbook, printJobRequest, host.Surface, WorkbookExportPrintOutputKind.Pdf);
        var xpsPlan = CreateExportPlan(workbook, printJobRequest, host.Surface, WorkbookExportPrintOutputKind.Xps);
        portablePlan = !host.SupportsNativePrintDialog && pdfPlan.IsReady && printJobRequest.Copies > 0
            ? PortablePdfExportPlanner.CreatePlan(pdfPlan, workbook)
            : null;
        var printJobPlan = PrintJobPlanner.BuildJobPlan(
            printJobRequest,
            pdfPlan,
            portablePlan is { IsReady: true } ? portablePlan.TotalPageCount : null);

        return new PrintExportHostReadinessPlan(
            host,
            scopePlan,
            pdfPlan,
            xpsPlan,
            CreateNativePrintPlan(host, printJobPlan));
    }

    private static WorkbookExportPrintPlan CreateExportPlan(
        Workbook workbook,
        PrintJobRequest printJobRequest,
        WorkbookExportPrintSurface surface,
        WorkbookExportPrintOutputKind outputKind) =>
        WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            printJobRequest.CreateExportIntent(outputKind),
            surface);

    private static PrintExportNativePrintReadinessPlan CreateNativePrintPlan(
        PrintExportHostCapabilities host,
        PrintJobPlan printJobPlan)
    {
        if (!printJobPlan.IsReady)
        {
            return new PrintExportNativePrintReadinessPlan(
                PrintExportNativePrintReadinessStatus.PrintJobNotReady,
                PrintExportNativePrintRouteKind.None,
                printJobPlan.StatusText,
                printJobPlan);
        }

        if (host.SupportsNativePrintDialog)
        {
            return new PrintExportNativePrintReadinessPlan(
                PrintExportNativePrintReadinessStatus.ReadyForNativePrintDialog,
                PrintExportNativePrintRouteKind.NativePrintDialog,
                $"Ready to open the {host.HostLabel} native print dialog. {printJobPlan.StatusText}",
                printJobPlan);
        }

        if (host.CanSubmitToPlatformPrinter && host.HasPrinterDestination)
        {
            return new PrintExportNativePrintReadinessPlan(
                PrintExportNativePrintReadinessStatus.ReadyForPlatformPrinter,
                PrintExportNativePrintRouteKind.PlatformPrinter,
                $"Ready to submit the shared print-ready PDF through the {host.HostLabel} platform printer adapter. {printJobPlan.StatusText}",
                printJobPlan);
        }

        return new PrintExportNativePrintReadinessPlan(
            PrintExportNativePrintReadinessStatus.PrintReadyPdfFallback,
            PrintExportNativePrintRouteKind.PrintReadyPdfFallback,
            $"No native printer destination is available for {host.HostLabel}; create the shared print-ready PDF fallback instead. {printJobPlan.StatusText}",
            printJobPlan);
    }
}
