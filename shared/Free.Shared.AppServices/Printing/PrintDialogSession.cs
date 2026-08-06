namespace Free.Shared.AppServices.Printing;

public enum PrintDialogValidationIssue
{
    None,
    CopiesOutOfRange,
    FirstPageInvalid,
    LastPageBeforeFirstPage,
}

public sealed record PrintDialogText(
    string ReadyStatus,
    string UnavailableStatus,
    string CopiesOutOfRange,
    string FirstPageInvalid,
    string LastPageBeforeFirstPage)
{
    public static PrintDialogText DefaultEnglish { get; } = new(
        "Choose the printer and print settings.",
        "Printing is unavailable on this host.",
        "Copies must be between 1 and 999.",
        "Enter a positive first page number.",
        "The last page must be at least the first page.");

    public string ValidationMessage(PrintDialogValidationIssue issue) => issue switch
    {
        PrintDialogValidationIssue.CopiesOutOfRange => CopiesOutOfRange,
        PrintDialogValidationIssue.FirstPageInvalid => FirstPageInvalid,
        PrintDialogValidationIssue.LastPageBeforeFirstPage => LastPageBeforeFirstPage,
        _ => string.Empty,
    };
}

public sealed record PrintDialogState(
    IReadOnlyList<string> PrinterNames,
    int SelectedPrinterIndex,
    string CopiesText,
    int PageRangeIndex,
    string FirstPageText,
    string LastPageText,
    int OrientationIndex,
    bool Collate,
    bool CanSubmit,
    string? DiscoveryMessage)
{
    public string StatusMessage(PrintDialogText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return DiscoveryMessage ?? (CanSubmit ? text.ReadyStatus : text.UnavailableStatus);
    }
}

public readonly record struct PrintDialogRangeVisibility(bool ShowFirstPage, bool ShowLastPage);

public sealed record PrintDialogSubmission(
    PrintSelection? Selection,
    PrintDialogValidationIssue ValidationIssue = PrintDialogValidationIssue.None)
{
    public bool Succeeded => Selection is not null && ValidationIssue == PrintDialogValidationIssue.None;
}

/// <summary>
/// Renderer-neutral state and validation for the portable print dialogs. Renderers retain native
/// queue discovery, controls, focus behavior, and submission while this session owns the duplicated
/// selection initialization and parsing rules.
/// </summary>
public sealed class PrintDialogSession
{
    private PrintDialogSession(PrintDialogPlan plan, bool collate)
    {
        var printerNames = plan.Printers.Select(printer => printer.Name).ToArray();
        var selectedPrinterIndex = Array.FindIndex(
            printerNames,
            name => string.Equals(name, plan.SelectedPrinter, StringComparison.OrdinalIgnoreCase));

        State = new PrintDialogState(
            printerNames,
            Math.Max(0, selectedPrinterIndex),
            plan.Copies.ToString(),
            (int)plan.PageRange.Kind,
            (plan.PageRange.FirstPage ?? 1).ToString(),
            (plan.PageRange.LastPage ?? 1).ToString(),
            (int)plan.Orientation,
            collate,
            plan.CanSubmit,
            plan.Message);
    }

    public PrintDialogState State { get; }

    public static PrintDialogSession Start(
        PrinterDiscoveryResult discovery,
        PrintSelection? requested = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        requested ??= new PrintSelection();
        return new PrintDialogSession(PrintSelectionPlanner.Build(discovery, requested), requested.Collate);
    }

    public static PrintDialogRangeVisibility RangeVisibility(int pageRangeIndex) =>
        new(pageRangeIndex != (int)PrintPageRangeKind.All, pageRangeIndex == (int)PrintPageRangeKind.Range);

    public PrintDialogSubmission Submit(
        string? printerName,
        string? copiesText,
        int pageRangeIndex,
        string? firstPageText,
        string? lastPageText,
        int orientationIndex,
        bool collate)
    {
        if (!int.TryParse(copiesText, out var copies) || copies is < 1 or > 999)
            return Invalid(PrintDialogValidationIssue.CopiesOutOfRange);

        PrintPageRange pageRange;
        if (pageRangeIndex == (int)PrintPageRangeKind.All)
        {
            pageRange = PrintPageRange.All;
        }
        else if (!int.TryParse(firstPageText, out var firstPage) || firstPage < 1)
        {
            return Invalid(PrintDialogValidationIssue.FirstPageInvalid);
        }
        else if (pageRangeIndex == (int)PrintPageRangeKind.Single)
        {
            pageRange = PrintPageRange.Single(firstPage);
        }
        else if (!int.TryParse(lastPageText, out var lastPage) || lastPage < firstPage)
        {
            return Invalid(PrintDialogValidationIssue.LastPageBeforeFirstPage);
        }
        else
        {
            pageRange = PrintPageRange.Between(firstPage, lastPage);
        }

        return new PrintDialogSubmission(new PrintSelection(
            printerName,
            copies,
            pageRange,
            (PrintOrientation)Math.Clamp(orientationIndex, 0, 2),
            collate));
    }

    private static PrintDialogSubmission Invalid(PrintDialogValidationIssue issue) => new(null, issue);
}
