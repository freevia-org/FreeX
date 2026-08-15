using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Free.Shared.Shell.Wpf;

public enum WpfPaginatorPrintOutcome
{
    Printed,
    Cancelled,
    Failed,
}

public sealed record WpfPaginatorPrintResult(
    WpfPaginatorPrintOutcome Outcome,
    Exception? Error = null)
{
    public bool Printed => Outcome == WpfPaginatorPrintOutcome.Printed;
}

public sealed record WpfPaginatorPrintTicketOptions(
    int? Copies = null,
    bool? Collate = null,
    OutputColor? Color = null,
    PageOrientation? Orientation = null,
    double? PageWidthDip = null,
    double? PageHeightDip = null);

public sealed record WpfPaginatorPageRange(int FirstPage, int LastPage);

public sealed record WpfPaginatorPageRangeOptions(
    int TotalPages,
    Func<int, int, WpfPaginatorPageRange> ResolveUserRange);

public sealed record WpfPaginatorPrintDialogContext(
    double PrintableAreaWidth,
    double PrintableAreaHeight,
    PrintTicket PrintTicket);

public sealed record WpfPaginatorPrintRequest(
    string Description,
    Func<WpfPaginatorPrintDialogContext, DocumentPaginator> CreatePaginator,
    WpfPaginatorPrintTicketOptions? TicketOptions = null,
    WpfPaginatorPageRangeOptions? PageRange = null,
    Window? Owner = null);

/// <summary>
/// Owns the native WPF print-dialog lifecycle and paginator submission. Apps retain page
/// construction, domain range mapping, job names, and presentation of a failed result.
/// </summary>
public static class WpfPaginatorPrintWorkflow
{
    public static WpfPaginatorPrintResult Execute(WpfPaginatorPrintRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Execute(request, new WpfPrintDialogPort(new PrintDialog()));
    }

    internal static WpfPaginatorPrintResult Execute(
        WpfPaginatorPrintRequest request,
        IWpfPaginatorPrintDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dialog);
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ArgumentException("A print job description is required.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.CreatePaginator);

        ValidatePageRange(request.PageRange);
        ValidateTicketOptions(request.TicketOptions);

        try
        {
            var ticket = dialog.PrintTicket ?? new PrintTicket();
            ApplyTicketOptions(ticket, request.TicketOptions);
            dialog.PrintTicket = ticket;
            ApplyPageRangeOptions(dialog, request.PageRange);

            if (dialog.ShowDialog(request.Owner) != true)
                return new WpfPaginatorPrintResult(WpfPaginatorPrintOutcome.Cancelled);

            var context = new WpfPaginatorPrintDialogContext(
                Math.Max(1, dialog.PrintableAreaWidth),
                Math.Max(1, dialog.PrintableAreaHeight),
                dialog.PrintTicket ?? ticket);
            var paginator = request.CreatePaginator(context) ??
                throw new InvalidOperationException("The print paginator factory returned null.");

            if (request.PageRange is not null &&
                dialog.PageRangeSelection == PageRangeSelection.UserPages)
            {
                var range = request.PageRange.ResolveUserRange(
                    dialog.PageFrom,
                    dialog.PageTo);
                paginator = WpfPageRangeDocumentPaginator.CreateClampedInclusive(
                    paginator,
                    range.FirstPage,
                    range.LastPage);
            }

            dialog.PrintDocument(paginator, request.Description);
            return new WpfPaginatorPrintResult(WpfPaginatorPrintOutcome.Printed);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new WpfPaginatorPrintResult(WpfPaginatorPrintOutcome.Failed, ex);
        }
    }

    internal static void ApplyTicketOptions(
        PrintTicket ticket,
        WpfPaginatorPrintTicketOptions? options)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (options is null)
            return;

        if (options.Copies is int copies)
            ticket.CopyCount = Math.Clamp(copies, 1, 999);
        if (options.Collate is bool collate)
            ticket.Collation = collate ? Collation.Collated : Collation.Uncollated;
        if (options.Color is OutputColor color)
            ticket.OutputColor = color;
        if (options.Orientation is PageOrientation orientation)
            ticket.PageOrientation = orientation;
        if (options.PageWidthDip is double width && options.PageHeightDip is double height)
            ticket.PageMediaSize = new PageMediaSize(width, height);
    }

    private static void ApplyPageRangeOptions(
        IWpfPaginatorPrintDialog dialog,
        WpfPaginatorPageRangeOptions? options)
    {
        if (options is null)
            return;

        dialog.UserPageRangeEnabled = options.TotalPages > 1;
        dialog.MinPage = 1;
        dialog.MaxPage = checked((uint)options.TotalPages);
    }

    private static void ValidatePageRange(WpfPaginatorPageRangeOptions? options)
    {
        if (options is null)
            return;
        if (options.TotalPages <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "The total page count must be positive.");
        ArgumentNullException.ThrowIfNull(options.ResolveUserRange);
    }

    private static void ValidateTicketOptions(WpfPaginatorPrintTicketOptions? options)
    {
        if (options is null)
            return;

        var hasWidth = options.PageWidthDip is not null;
        var hasHeight = options.PageHeightDip is not null;
        if (hasWidth != hasHeight)
            throw new ArgumentException("Page width and height must be supplied together.", nameof(options));
        if (options.PageWidthDip is double width && (!double.IsFinite(width) || width <= 0))
            throw new ArgumentOutOfRangeException(nameof(options), "Page width must be finite and positive.");
        if (options.PageHeightDip is double height && (!double.IsFinite(height) || height <= 0))
            throw new ArgumentOutOfRangeException(nameof(options), "Page height must be finite and positive.");
    }
}

internal interface IWpfPaginatorPrintDialog
{
    PrintTicket? PrintTicket { get; set; }
    double PrintableAreaWidth { get; }
    double PrintableAreaHeight { get; }
    bool UserPageRangeEnabled { get; set; }
    uint MinPage { get; set; }
    uint MaxPage { get; set; }
    PageRangeSelection PageRangeSelection { get; }
    int PageFrom { get; }
    int PageTo { get; }

    bool? ShowDialog(Window? owner);
    void PrintDocument(DocumentPaginator paginator, string description);
}

internal sealed class WpfPrintDialogPort(PrintDialog dialog) : IWpfPaginatorPrintDialog
{
    public PrintTicket? PrintTicket
    {
        get => dialog.PrintTicket;
        set => dialog.PrintTicket = value;
    }

    public double PrintableAreaWidth => dialog.PrintableAreaWidth;
    public double PrintableAreaHeight => dialog.PrintableAreaHeight;
    public bool UserPageRangeEnabled
    {
        get => dialog.UserPageRangeEnabled;
        set => dialog.UserPageRangeEnabled = value;
    }

    public uint MinPage
    {
        get => dialog.MinPage;
        set => dialog.MinPage = value;
    }

    public uint MaxPage
    {
        get => dialog.MaxPage;
        set => dialog.MaxPage = value;
    }

    public PageRangeSelection PageRangeSelection => dialog.PageRangeSelection;
    public int PageFrom => dialog.PageRange.PageFrom;
    public int PageTo => dialog.PageRange.PageTo;

    public bool? ShowDialog(Window? owner)
    {
        if (owner is { IsVisible: true })
            owner.Activate();
        return dialog.ShowDialog();
    }

    public void PrintDocument(DocumentPaginator paginator, string description) =>
        dialog.PrintDocument(paginator, description);
}
