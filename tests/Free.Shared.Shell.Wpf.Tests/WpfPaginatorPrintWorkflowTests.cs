using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Free.Shared.Shell.Wpf.Tests;

public sealed class WpfPaginatorPrintWorkflowTests
{
    [Fact]
    public void Execute_ConfiguresTicketRangeAndSubmitsDomainRangeProjection()
    {
        var dialog = new RecordingPrintDialog
        {
            PageRangeSelection = PageRangeSelection.UserPages,
            PageFrom = 2,
            PageTo = 4,
            PrintableAreaWidth = 720,
            PrintableAreaHeight = 540,
        };
        var inner = new RecordingPaginator(5);
        WpfPaginatorPrintDialogContext? context = null;

        var result = WpfPaginatorPrintWorkflow.Execute(
            new WpfPaginatorPrintRequest(
                "Shared print job",
                value =>
                {
                    context = value;
                    return inner;
                },
                new WpfPaginatorPrintTicketOptions(
                    Copies: 3,
                    Collate: false,
                    Color: OutputColor.Monochrome,
                    Orientation: PageOrientation.Landscape,
                    PageWidthDip: 816,
                    PageHeightDip: 1056),
                new WpfPaginatorPageRangeOptions(
                    5,
                    (from, to) => new WpfPaginatorPageRange(from, to))),
            dialog);

        result.Outcome.Should().Be(WpfPaginatorPrintOutcome.Printed);
        result.Error.Should().BeNull();
        dialog.PrintTicket!.CopyCount.Should().Be(3);
        dialog.PrintTicket.Collation.Should().Be(Collation.Uncollated);
        dialog.PrintTicket.OutputColor.Should().Be(OutputColor.Monochrome);
        dialog.PrintTicket.PageOrientation.Should().Be(PageOrientation.Landscape);
        dialog.PrintTicket.PageMediaSize!.Width.Should().Be(816);
        dialog.PrintTicket.PageMediaSize.Height.Should().Be(1056);
        dialog.UserPageRangeEnabled.Should().BeTrue();
        dialog.MinPage.Should().Be(1);
        dialog.MaxPage.Should().Be(5);
        context!.PrintableAreaWidth.Should().Be(720);
        context.PrintableAreaHeight.Should().Be(540);
        dialog.SubmittedPaginator.Should().BeOfType<WpfPageRangeDocumentPaginator>();
        dialog.SubmittedPaginator!.PageCount.Should().Be(3);
        dialog.SubmittedDescription.Should().Be("Shared print job");
    }

    [Fact]
    public void Execute_CancelledDialogDoesNotConstructOrSubmitPaginator()
    {
        var dialog = new RecordingPrintDialog { ShowResult = false };
        var factoryCalled = false;

        var result = WpfPaginatorPrintWorkflow.Execute(
            new WpfPaginatorPrintRequest(
                "Cancelled print job",
                _ =>
                {
                    factoryCalled = true;
                    return new RecordingPaginator(1);
                }),
            dialog);

        result.Outcome.Should().Be(WpfPaginatorPrintOutcome.Cancelled);
        factoryCalled.Should().BeFalse();
        dialog.SubmittedPaginator.Should().BeNull();
    }

    [Fact]
    public void Execute_ReturnsRecoverableFailureAndPreservesOutOfMemoryException()
    {
        var failure = new InvalidOperationException("printer unavailable");
        var dialog = new RecordingPrintDialog { PrintFailure = failure };
        var request = new WpfPaginatorPrintRequest(
            "Failed print job",
            _ => new RecordingPaginator(1));

        var result = WpfPaginatorPrintWorkflow.Execute(request, dialog);

        result.Outcome.Should().Be(WpfPaginatorPrintOutcome.Failed);
        result.Error.Should().BeSameAs(failure);

        dialog.PrintFailure = new OutOfMemoryException();
        var act = () => WpfPaginatorPrintWorkflow.Execute(request, dialog);
        act.Should().Throw<OutOfMemoryException>();
    }

    [Fact]
    public void Execute_RejectsIncompleteOrInvalidStructuredOptions()
    {
        var dialog = new RecordingPrintDialog();
        var incompleteMedia = new WpfPaginatorPrintRequest(
            "Invalid media",
            _ => new RecordingPaginator(1),
            new WpfPaginatorPrintTicketOptions(PageWidthDip: 816));
        var noPages = new WpfPaginatorPrintRequest(
            "Invalid range",
            _ => new RecordingPaginator(1),
            PageRange: new WpfPaginatorPageRangeOptions(
                0,
                (from, to) => new WpfPaginatorPageRange(from, to)));

        ((Action)(() => WpfPaginatorPrintWorkflow.Execute(incompleteMedia, dialog)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => WpfPaginatorPrintWorkflow.Execute(noPages, dialog)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class RecordingPrintDialog : IWpfPaginatorPrintDialog
    {
        public PrintTicket? PrintTicket { get; set; } = new();
        public double PrintableAreaWidth { get; set; } = 816;
        public double PrintableAreaHeight { get; set; } = 1056;
        public bool UserPageRangeEnabled { get; set; }
        public uint MinPage { get; set; }
        public uint MaxPage { get; set; }
        public PageRangeSelection PageRangeSelection { get; set; } = PageRangeSelection.AllPages;
        public int PageFrom { get; set; } = 1;
        public int PageTo { get; set; } = 1;
        public bool? ShowResult { get; set; } = true;
        public Exception? PrintFailure { get; set; }
        public DocumentPaginator? SubmittedPaginator { get; private set; }
        public string? SubmittedDescription { get; private set; }

        public bool? ShowDialog(Window? owner) => ShowResult;

        public void PrintDocument(DocumentPaginator paginator, string description)
        {
            if (PrintFailure is not null)
                throw PrintFailure;
            SubmittedPaginator = paginator;
            SubmittedDescription = description;
        }
    }

    private sealed class RecordingPaginator(int pageCount) : DocumentPaginator
    {
        public override bool IsPageCountValid => true;
        public override int PageCount => pageCount;
        public override Size PageSize { get; set; } = new(816, 1056);
        public override IDocumentPaginatorSource Source => null!;

        public override DocumentPage GetPage(int pageNumber) =>
            pageNumber >= 0 && pageNumber < pageCount
                ? new DocumentPage(new DrawingVisual(), PageSize, new Rect(PageSize), new Rect(PageSize))
                : DocumentPage.Missing;
    }
}
