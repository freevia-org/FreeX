using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Input;

using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R66-services-clipboard-formats-6-1: the Avalonia shell's paste (<c>MainWindow.PasteClipboardTextAsync</c>
/// / <c>PasteSpecialClipboardTextAsync</c> / <c>PasteSpecialExternalTextFromClipboardAsync</c>) used to read
/// only the OS clipboard's plain-text payload (<c>IClipboard.TryGetTextAsync</c>), never its 'text/html' /
/// Windows 'HTML Format' payload -- so <c>WorkbookSession.TryParseHtmlClipboardTableRows</c>'s HTML-table-
/// aware row/column recovery (added for the WPF host in R57, and already wired to accept an <c>html</c>
/// argument on the shared session side) was never reached from this shell, misaligning a pasted external
/// HTML table with a wrapped cell or colspan header. These tests exercise the new
/// <c>MainWindow.TryGetHtmlFromDataTransferAsync</c> helper directly: <c>IClipboard</c> itself is
/// <c>[NotClientImplementable]</c> (Avalonia hard-blocks any non-platform implementation, so no
/// clipboard test double can be built for it), but the <c>IAsyncDataTransfer</c>/<c>IAsyncDataTransferItem</c>
/// it wraps around are ordinary interfaces a fake can implement -- reading the same two formats
/// <c>MainWindow.ClipboardHtml.AddClipboardTextAndHtml</c> writes on copy.
/// </summary>
public sealed class R66_ClipboardHtmlReadPasteTests
{
    private static readonly DataFormat<string> HtmlPlatformFormat = DataFormat.CreateStringPlatformFormat("text/html");
    private static readonly DataFormat<string> HtmlWindowsPlatformFormat = DataFormat.CreateStringPlatformFormat("HTML Format");

    [Fact]
    public async Task TryGetHtmlFromDataTransferAsync_WhenTransferCarriesHtmlPlatformFormat_ReturnsItsHtml()
    {
        const string html = "<html><body><table><tr><td>A</td></tr></table></body></html>";
        var dataTransfer = new FakeAsyncDataTransfer(
            new FakeAsyncDataTransferItem(new Dictionary<DataFormat, object> { [HtmlPlatformFormat] = html }));

        var result = await MainWindow.TryGetHtmlFromDataTransferAsync(dataTransfer);

        result.Should().Be(html, "the shell must read the 'text/html' clipboard format written on copy instead of ignoring it");
    }

    [Fact]
    public async Task TryGetHtmlFromDataTransferAsync_WhenTransferOnlyCarriesWindowsHtmlFormat_FallsBackToIt()
    {
        const string cfHtml = "Version:0.9\r\nStartHTML:00000000\r\n<html><body><table><tr><td>A</td></tr></table></body></html>";
        var dataTransfer = new FakeAsyncDataTransfer(
            new FakeAsyncDataTransferItem(new Dictionary<DataFormat, object> { [HtmlWindowsPlatformFormat] = cfHtml }));

        var result = await MainWindow.TryGetHtmlFromDataTransferAsync(dataTransfer);

        result.Should().Be(cfHtml, "a source that only writes the Windows 'HTML Format' name must still be recovered");
    }

    [Fact]
    public async Task TryGetHtmlFromDataTransferAsync_WhenTransferHasNoHtmlFormat_ReturnsNullInsteadOfThrowing()
    {
        // Sibling no-regression check: a plain-text-only clipboard (the overwhelming common case) must
        // not break or throw just because no HTML payload is present.
        var dataTransfer = new FakeAsyncDataTransfer(
            new FakeAsyncDataTransferItem(new Dictionary<DataFormat, object> { [DataFormat.Text] = "plain text only" }));

        var result = await MainWindow.TryGetHtmlFromDataTransferAsync(dataTransfer);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryGetHtmlFromDataTransferAsync_WhenDataTransferIsNull_ReturnsNull()
    {
        // Sibling no-regression check: IClipboard.TryGetDataAsync() itself can legitimately return null
        // (nothing on the clipboard, or an unreadable platform format) -- must not throw a
        // NullReferenceException.
        var result = await MainWindow.TryGetHtmlFromDataTransferAsync(dataTransfer: null);

        result.Should().BeNull();
    }

    /// <summary>
    /// End-to-end proof of the actual bug scenario, wiring the new Avalonia clipboard-HTML read
    /// together with the shared session's HTML-table-aware paste (exactly what
    /// <c>MainWindow.PasteClipboardTextAsync</c> now does at its call site): a wrapped source cell's
    /// rendered "Springfield\nIL 62704" must land as ONE pasted cell (the real &lt;td&gt; boundary from
    /// the HTML), not be split into an extra row by the plain-text tab/newline splitter.
    /// </summary>
    [Fact]
    public async Task ClipboardHtmlRead_FeedIntoPasteClipboardTextAtActiveCell_AlignsWrappedCellAsOneRow()
    {
        const string plainText = "Springfield\nIL 62704\tRow1B\nNextRow\tRow2B";
        const string html =
            "<html><body><!--StartFragment-->" +
            "<table><tr><td>Springfield<br>IL 62704</td><td>Row1B</td></tr>" +
            "<tr><td>NextRow</td><td>Row2B</td></tr></table>" +
            "<!--EndFragment--></body></html>";
        var dataTransfer = new FakeAsyncDataTransfer(
            new FakeAsyncDataTransferItem(new Dictionary<DataFormat, object> { [HtmlPlatformFormat] = html }));

        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var sheet = workbook.Sheets[0];
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        session.SelectCell(a1);

        var readHtml = await MainWindow.TryGetHtmlFromDataTransferAsync(dataTransfer);
        var result = session.PasteClipboardTextAtActiveCell(plainText, html: readHtml);

        result.Success.Should().BeTrue();
        sheet.GetValue(a1).Should().Be(new TextValue("Springfield\nIL 62704"),
            "the wrapped source cell must paste as a single cell, using the HTML <td> boundary");
        sheet.GetValue(b1).Should().Be(new TextValue("Row1B"));
        sheet.GetValue(a2).Should().Be(new TextValue("NextRow"),
            "without HTML awareness, the embedded newline in the first cell would misread this as a 3rd data row instead of row 2");
        sheet.GetValue(b2).Should().Be(new TextValue("Row2B"));
    }

    private sealed class FakeAsyncDataTransferItem : IAsyncDataTransferItem
    {
        private readonly IReadOnlyDictionary<DataFormat, object> _values;

        public FakeAsyncDataTransferItem(IReadOnlyDictionary<DataFormat, object> values)
        {
            _values = values;
            Formats = [.. values.Keys];
        }

        public IReadOnlyList<DataFormat> Formats { get; }

        public Task<object?> TryGetRawAsync(DataFormat format) =>
            Task.FromResult(_values.TryGetValue(format, out var value) ? value : null);
    }

    private sealed class FakeAsyncDataTransfer(IAsyncDataTransferItem item) : IAsyncDataTransfer
    {
        public IReadOnlyList<DataFormat> Formats { get; } = item.Formats;

        public IReadOnlyList<IAsyncDataTransferItem> Items { get; } = [item];

        public void Dispose()
        {
        }
    }
}
