using System.IO;
using FluentAssertions;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// R124: native WPF Print ("File &gt; Print" / Ctrl+P, and Mail Merge's 'print' finish
/// destination) had no error handling around <c>dialog.PrintDocument(paginator, description)</c>
/// in <c>MainWindow.PrintDocument(DocumentView, string)</c>. Because nothing caught the
/// exception it propagated to WPF's DispatcherUnhandledException handler, which never sets
/// <c>args.Handled = true</c> (see AppCrashHandlers.cs), so a printer failure (offline printer,
/// stopped spooler, driver fault, invalid PrintTicket/PageMediaSize, access-denied network
/// queue) crashed the entire app instead of showing a recoverable error -- unlike Word, and
/// unlike the sibling ExportToPdf/ExportToXps paths in the same file which both wrap their work
/// in try/catch and show an owned error MessageBox via DialogMessageHelper.ShowError. This
/// mirrors FreeX's round-123 fix of the identical gap in
/// src/FreeX.App.Host/NativePrintDialogService.cs.ShowPrintDialogAndPrint.
///
/// The real entry point (MainWindow.PrintDocument) drives a modal WPF PrintDialog.ShowDialog()
/// followed by PrintDialog.PrintDocument against a real/virtual print queue; neither can be
/// driven headlessly in a unit test (they require live user interaction with a modal dialog),
/// so per the "drop to the nearest seam" allowance these tests verify the real production
/// source text for the try/catch + owned-error-message wiring (matching the established pattern
/// already used by R123_NativePrintDialogServiceErrorHandlingTests.cs in FreeX.App.Host.Tests
/// for the identical bug class), plus assert the sibling ExportToPdf/ExportToXps handling this
/// fix must not disturb.
/// </summary>
public sealed class R124_PrintDocumentErrorHandlingTests
{
    [Fact]
    public void PrintDocument_WrapsDialogPrintDocumentCallInTryCatch_AndShowsOwnedErrorMessage()
    {
        var source = ReadSource("freew", "FreeW.App.Host", "MainWindow.cs");

        var printCallIndex = source.IndexOf(
            "dialog.PrintDocument(paginator, description);",
            StringComparison.Ordinal);
        printCallIndex.Should().BeGreaterThan(-1, "the native print call site must still exist");

        // Anchor to the PrintDocument method itself (not some unrelated try/catch elsewhere in
        // the file): the nearest preceding method signature must be PrintDocument's.
        var methodIndex = source.LastIndexOf(
            "private void PrintDocument(DocumentView editor, string description)",
            printCallIndex,
            StringComparison.Ordinal);
        methodIndex.Should().BeGreaterThan(-1, "the print call must live inside PrintDocument(DocumentView, string)");

        // The try must open between the method start and the print call, and the catch must
        // appear after the print call -- i.e. the print call is actually wrapped, not merely
        // followed by an unrelated try/catch.
        var tryIndex = source.LastIndexOf("try", printCallIndex, StringComparison.Ordinal);
        tryIndex.Should().BeGreaterThan(methodIndex, "a try block must precede the PrintDocument call, inside this method");

        var catchIndex = source.IndexOf("catch (Exception ex)", printCallIndex, StringComparison.Ordinal);
        catchIndex.Should().BeGreaterThan(-1, "a catch(Exception) must follow the PrintDocument call");

        // The catch must actually route to a message shown to the user, not merely log/ignore it.
        var catchBlockEnd = source.IndexOf("}", catchIndex, StringComparison.Ordinal);
        catchBlockEnd.Should().BeGreaterThan(-1);
        var catchBody = source[catchIndex..catchBlockEnd];
        catchBody.Should().Contain("DialogMessageHelper.ShowError");

        // A rethrown/propagated OutOfMemoryException is intentional (matches FreeX's
        // NativePrintDialogService convention for the identical bug class) -- must not swallow it.
        source.Should().Contain("catch (Exception ex) when (ex is not OutOfMemoryException)");
    }

    /// <summary>
    /// No-regression sibling: the neighbouring ExportToPdf/ExportToXps error-handling paths in
    /// the same file (the pattern this fix was matched to) must remain intact -- this fix must
    /// not have disturbed them.
    /// </summary>
    [Fact]
    public void ExportToPdfAndExportToXps_StillWrapWorkInTryCatchAndShowOwnedErrorMessage()
    {
        var source = ReadSource("freew", "FreeW.App.Host", "MainWindow.cs");

        source.Should().Contain("could not be exported to PDF");
        source.Should().Contain("could not be exported to XPS");

        var pdfCatchIndex = source.IndexOf(
            "\"The document could not be exported to PDF.",
            StringComparison.Ordinal);
        pdfCatchIndex.Should().BeGreaterThan(-1);

        var xpsCatchIndex = source.IndexOf(
            "\"The document could not be exported to XPS.",
            StringComparison.Ordinal);
        xpsCatchIndex.Should().BeGreaterThan(-1);
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(
            new[] { TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx") }
                .Concat(parts)
                .ToArray()));
}
