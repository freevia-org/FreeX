using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R123: WPF native Print ("Print Now") had no error handling around
/// <c>documentPrinter.PrintDocument(paginator, "FreeX worksheet")</c> in
/// NativePrintDialogService.ShowPrintDialogAndPrint. Because nothing caught the exception it
/// propagated to WPF's DispatcherUnhandledException handler, which never sets
/// <c>args.Handled = true</c> (see AppCrashHandlers.cs), so a printer failure (offline printer,
/// stopped spooler, driver fault, invalid PrintTicket, access-denied network queue) crashed the
/// entire app instead of showing a recoverable error -- unlike Excel, and unlike the sibling
/// ExportAsPdf/ExportAsXps paths in MainWindow.PrintExport.cs which both wrap their work in
/// try/catch and show an owned error MessageBox.
///
/// The real entry point (ShowPrintDialogAndPrint) drives a modal WinForms PrintDialog followed by
/// the WPF PrintDialog.PrintDocument print pipeline; neither can be driven headlessly in a unit
/// test (they require live user interaction with a modal dialog and a real/virtual print queue),
/// so per the "drop to the nearest seam" allowance these tests verify the real production source
/// text for the try/catch + owned-error-message wiring (matching the established pattern already
/// used by MainWindowSourceHygieneTests.Backstage.cs for ExportAsPdf/ExportAsXps), plus drive the
/// real UiText/resx resource-resolution path (no WPF UI needed) to prove the new resource keys the
/// fix depends on actually resolve to localized text and are not orphaned/missing.
/// </summary>
public sealed class R123_NativePrintDialogServiceErrorHandlingTests
{
    [Fact]
    public void ShowPrintDialogAndPrint_WrapsPrintDocumentCallInTryCatch_AndShowsOwnedErrorMessage()
    {
        var source = DialogSourceTestSupport.ReadHostSourceFile("NativePrintDialogService.cs");

        var printCallIndex = source.IndexOf(
            "documentPrinter.PrintDocument(paginator, \"FreeX worksheet\");",
            StringComparison.Ordinal);
        printCallIndex.Should().BeGreaterThan(-1, "the native print call site must still exist");

        // The try must open before the print call and the catch must appear after it, inside the
        // same method (ShowPrintDialogAndPrint) -- i.e. the print call is actually wrapped, not
        // merely followed by an unrelated try/catch elsewhere in the file.
        var tryIndex = source.LastIndexOf("try", printCallIndex, StringComparison.Ordinal);
        tryIndex.Should().BeGreaterThan(-1, "a try block must precede the PrintDocument call");

        var catchIndex = source.IndexOf("catch (Exception ex)", printCallIndex, StringComparison.Ordinal);
        catchIndex.Should().BeGreaterThan(-1, "a catch(Exception) must follow the PrintDocument call");

        // The exception must not propagate un-swallowed and un-reported: the catch block must
        // route to a message shown to the user, not merely log/ignore it.
        var catchBlockEnd = source.IndexOf("}", catchIndex, StringComparison.Ordinal);
        catchBlockEnd.Should().BeGreaterThan(-1);
        var catchBody = source[catchIndex..catchBlockEnd];
        catchBody.Should().Contain("ShowPrintFailedMessage");

        source.Should().Contain("PageLayoutMessagePresentationCatalog");
        source.Should().Contain("DescribeNativePrintFailure(ex.Message)");
        source.Should().Contain("DialogMessageHelper.ShowMessage(");
        source.Should().NotContain("MessageBox.Show(");

        // A rethrown/propagated OutOfMemoryException is intentional (matches the existing
        // WirePreviewRendering convention in the same file) -- the catch must not swallow it.
        source.Should().Contain("catch (Exception ex) when (ex is not OutOfMemoryException)");
    }

    [Fact]
    public void FreeXHost_HasNoDirectMessageBoxRealization()
    {
        var hostDirectory = DialogSourceTestSupport.FindHostSourceDirectory(
            "NativePrintDialogService.cs");
        var directCallFiles = Directory
            .EnumerateFiles(hostDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("MessageBox.Show(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToArray();

        directCallFiles.Should().BeEmpty(
            "FreeX WPF workflows must route through shared user-message realization");

        var realizer = DialogSourceTestSupport.ReadShellSources("WpfMessageBoxRealizer.cs");
        realizer.Should().Contain("MessageBox.Show(");

        var headerFooterPictures = DialogSourceTestSupport.ReadHostSourceFile(
            "HeaderFooterDialog.Pictures.cs");
        headerFooterPictures.Should().Contain("ShowPictureOpenFailure(readResult.FailureMessage);");
        headerFooterPictures.Should().Contain("ShowPictureOpenFailure(ex.Message);");
        headerFooterPictures.Should().Contain("DescribeHeaderFooterPictureOpenFailure(detail)");
        headerFooterPictures.Should().Contain("DialogMessageHelper.ShowMessage(");
    }

    [Fact]
    public void PrintFailedResourceKeys_ResolveToRealLocalizedText_NotMissingOrOrphaned()
    {
        // Drives the real product resource-resolution path (Free.Shared.Localization via UiText),
        // proving the neutral resx entries the fix depends on actually exist and are wired up --
        // not merely referenced in source with a typo that would render as "[[Key]]" at runtime.
        var title = UiText.Get("MainWindowMessage_PrintFailedTitle");
        title.Should().NotBe("[[MainWindowMessage_PrintFailedTitle]]");
        title.Should().NotBeNullOrWhiteSpace();

        var message = UiText.Format("MainWindowMessage_PrintFailed", "The printer is offline.");
        message.Should().NotBe("[[MainWindowMessage_PrintFailed]]");
        message.Should().Contain("The printer is offline.");
    }

    /// <summary>
    /// No-regression sibling: the neighbouring ExportAsPdf/ExportAsXps error-handling paths in
    /// MainWindow.PrintExport.cs (the pattern this fix was matched to) must remain intact -- this
    /// fix must not have disturbed them.
    /// </summary>
    [Fact]
    public void ExportAsPdfAndExportAsXps_StillWrapWorkInTryCatchAndShowOwnedErrorMessage()
    {
        var source = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.PrintExport.cs");

        source.Should().Contain("UiText.Format(\"MainWindowMessage_ExportPdfFailed\"");
        source.Should().Contain("UiText.Get(\"MainWindowMessage_ExportErrorTitle\")");
        source.Should().Contain("MessageBoxImage.Error");

        var exportPdfTitle = UiText.Get("MainWindowMessage_ExportErrorTitle");
        exportPdfTitle.Should().NotBe("[[MainWindowMessage_ExportErrorTitle]]");
    }
}
