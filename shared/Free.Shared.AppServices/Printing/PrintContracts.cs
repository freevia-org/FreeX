using System.Globalization;

namespace Free.Shared.AppServices.Printing;

public enum PrintOrientation
{
    Document,
    Portrait,
    Landscape,
}

public enum PrintPageRangeKind
{
    All,
    Single,
    Range,
}

public sealed record PrintPageRange(PrintPageRangeKind Kind, int? FirstPage = null, int? LastPage = null)
{
    public static PrintPageRange All { get; } = new(PrintPageRangeKind.All);

    public static PrintPageRange Single(int page) => new(PrintPageRangeKind.Single, page, page);

    public static PrintPageRange Between(int firstPage, int lastPage) =>
        new(PrintPageRangeKind.Range, firstPage, lastPage);

    public void Validate()
    {
        switch (Kind)
        {
            case PrintPageRangeKind.All:
                if (FirstPage is not null || LastPage is not null)
                    throw new ArgumentException("An all-pages range cannot contain page numbers.");
                break;
            case PrintPageRangeKind.Single:
                if (FirstPage is not > 0 || LastPage != FirstPage)
                    throw new ArgumentException("A single-page range requires one positive page number.");
                break;
            case PrintPageRangeKind.Range:
                if (FirstPage is not > 0 || LastPage is not > 0 || FirstPage > LastPage)
                    throw new ArgumentException("A page range requires positive, ascending page numbers.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Kind));
        }
    }

    public string? ToCupsPageList()
    {
        Validate();
        return Kind == PrintPageRangeKind.All
            ? null
            : Kind == PrintPageRangeKind.Single
                ? FirstPage!.Value.ToString(CultureInfo.InvariantCulture)
                : $"{FirstPage!.Value}-{LastPage!.Value}";
    }
}

public sealed record PrintSelection(
    string? PrinterName = null,
    int Copies = 1,
    PrintPageRange? PageRange = null,
    PrintOrientation Orientation = PrintOrientation.Document,
    bool Collate = true,
    string? JobTitle = null)
{
    public PrintPageRange EffectivePageRange => PageRange ?? PrintPageRange.All;

    public void Validate()
    {
        if (Copies is < 1 or > 999)
            throw new ArgumentOutOfRangeException(nameof(Copies), "Copies must be between 1 and 999.");
        EffectivePageRange.Validate();
        if (PrinterName is not null && string.IsNullOrWhiteSpace(PrinterName))
            throw new ArgumentException("A printer name must be non-empty when supplied.", nameof(PrinterName));
    }
}

public sealed record PrinterInfo(string Name, bool IsDefault = false);

public enum PrinterDiscoveryStatus
{
    Available,
    NoPrinters,
    Unavailable,
    Failed,
    Cancelled,
}

public sealed record PrinterDiscoveryResult(
    PrinterDiscoveryStatus Status,
    IReadOnlyList<PrinterInfo> Printers,
    string? DefaultPrinter,
    string? Message = null)
{
    public bool IsAvailable => Status == PrinterDiscoveryStatus.Available && Printers.Count > 0;
}

public enum PrintSubmissionStatus
{
    Submitted,
    NoPrinters,
    Unavailable,
    Failed,
    Cancelled,
}

public sealed record PrintSubmissionResult(
    PrintSubmissionStatus Status,
    string? PrinterName,
    string? JobDescription = null,
    string? Message = null,
    int? NativeExitCode = null,
    int? NativeErrorCode = null)
{
    public bool Succeeded => Status == PrintSubmissionStatus.Submitted;
}

/// <summary>
/// Platform boundary for printer discovery and submission used by the non-WPF shell adapters. The
/// print-selection model is shared with the WPF authority, while WPF continues to own its native
/// <c>PrintDialog</c>/<c>PrintQueue</c> path. Only the OS-specific queue bridge implements this contract.
/// Implementations must translate cancellation into <see cref="PrinterDiscoveryStatus.Cancelled"/> or
/// <see cref="PrintSubmissionStatus.Cancelled"/> when the operation can return a result, and must not
/// leave a child process running after cancellation.
/// </summary>
public interface IPlatformPrintService
{
    /// <summary>Whether this adapter can attempt native printer operations on the current host.</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Declares whether page range and orientation are passed to the printer backend or must already
    /// be reflected in the submitted PDF. Shell-verb backends cannot express those settings.
    /// </summary>
    PrintRangeAndOrientationHandling RangeAndOrientationHandling =>
        PrintRangeAndOrientationHandling.PrinterSubmission;

    Task<PrinterDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default);

    Task<PrintSubmissionResult> SubmitAsync(
        string pdfPath,
        PrintSelection selection,
        CancellationToken cancellationToken = default);
}

public enum PrintRangeAndOrientationHandling
{
    PrinterSubmission,
    PreparedPdf,
}

public enum PrintCapabilityStatus
{
    Ready,
    NoPrinters,
    Unavailable,
    Failed,
}

public sealed record PrintDialogPlan(
    PrintCapabilityStatus Status,
    IReadOnlyList<PrinterInfo> Printers,
    string? SelectedPrinter,
    int Copies,
    PrintPageRange PageRange,
    PrintOrientation Orientation,
    string? Message = null)
{
    public bool CanSubmit => Status == PrintCapabilityStatus.Ready && SelectedPrinter is not null;
}
