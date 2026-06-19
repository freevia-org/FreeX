namespace FreeX.App.Services;

/// <summary>
/// One printer the host OS exposes. <see cref="IsDefault"/> marks the system default destination so the
/// shell can pre-select it. <see cref="Id"/> is the platform's stable queue/printer name passed back to
/// <see cref="IPlatformPrinter.SubmitAsync"/>; <see cref="DisplayName"/> is what the user sees.
/// </summary>
public sealed record PrinterDescriptor(string Id, string DisplayName, bool IsDefault)
{
    public string DisplayName { get; init; } =
        string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName.Trim();
}

/// <summary>
/// A print job ready to hand to a printer queue: the rendered print-ready document bytes (currently PDF)
/// plus the validated job plan that produced them (copies / collate / page window / a human title).
/// </summary>
public sealed record PrintJobSubmission(
    string PrinterId,
    byte[] DocumentBytes,
    int Copies,
    bool Collate,
    int FirstPage,
    int LastPage,
    string JobTitle);

/// <summary>Outcome of asking the OS to spool a job.</summary>
public sealed record PrintSubmissionResult(bool Succeeded, string StatusText)
{
    public static PrintSubmissionResult Success(string statusText) => new(true, statusText);

    public static PrintSubmissionResult Failure(string statusText) => new(false, statusText);
}

/// <summary>
/// The single seam between the framework-neutral print planner and the real OS spooler. Enumerating
/// printers and submitting a job are the only platform-specific operations, so each platform (Linux/CUPS,
/// macOS, Windows) plugs in one implementation and the rest of the print path — scope/copies/range
/// planning, the print dialog, and rendering the document — stays shared. Tests and headless hosts inject
/// <see cref="NullPlatformPrinter"/> so nothing is actually spooled.
/// </summary>
public interface IPlatformPrinter
{
    /// <summary>True when this host can enumerate printers and spool jobs. False forces the caller's fallback.</summary>
    bool CanPrint { get; }

    /// <summary>Enumerates the printers the OS exposes (default first when one is known). Never null.</summary>
    Task<IReadOnlyList<PrinterDescriptor>> GetPrintersAsync(CancellationToken cancellationToken = default);

    /// <summary>Spools <paramref name="submission"/> to its target printer queue.</summary>
    Task<PrintSubmissionResult> SubmitAsync(
        PrintJobSubmission submission,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A do-nothing printer used by tests and any host without a real spooler binding. It reports it cannot
/// print and enumerates no destinations, so the shell takes its print-to-PDF fallback instead of pretending
/// to spool.
/// </summary>
public sealed class NullPlatformPrinter : IPlatformPrinter
{
    public static NullPlatformPrinter Instance { get; } = new();

    public bool CanPrint => false;

    public Task<IReadOnlyList<PrinterDescriptor>> GetPrintersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PrinterDescriptor>>([]);

    public Task<PrintSubmissionResult> SubmitAsync(
        PrintJobSubmission submission,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PrintSubmissionResult.Failure("No print spooler is available on this host."));
}
