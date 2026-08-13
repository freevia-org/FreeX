using Avalonia.Headless;
using Avalonia.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R72-services-clipboard-interop-4-2: the Avalonia shell's copy (<c>AddClipboardTextAndHtml</c>) placed
/// plain text plus the two HTML clipboard variants on the OS clipboard, but never a CSV-typed format --
/// unlike the WPF host (R57), which additionally places a comma-delimited "CSV" format so a destination app that specifically
/// enumerates for CSV (skipping plain text) still gets a payload. These tests exercise the new
/// <c>MainWindow.AddClipboardTextAndHtmlForTest</c> path directly against a real <see cref="DataTransfer"/>
/// (a concrete Avalonia type, unlike the platform clipboard itself, which is <c>[NotClientImplementable]</c>).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R72_ClipboardCsvFormatTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Copy_AddsCsvTypedFormat_WithCommaDelimitedQuotedPayload()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateFixtureWindow(out var sheet, out var range);
            var viewport = window.Session.Viewport;
            var text = ClipboardSerializer.Serialize(viewport, range);

            using var transfer = new DataTransfer();
            MainWindow.AddClipboardTextAndHtmlForTest(transfer, text, viewport, sheet, range, window.Session.Workbook.Theme);

            var csv = GetPayload(transfer, MainWindow.CsvPlatformFormatForTest);
            csv.Should().Be("Name,\"Amount, USD\"\r\nWidget,Ten",
                "the comma inside \"Amount, USD\" must be RFC4180-quoted and the tab-delimited source text re-delimited with commas");

            var csvWindows = GetPayload(transfer, MainWindow.CsvWindowsPlatformFormatForTest);
            csvWindows.Should().Be(csv, "the Windows 'Csv' clipboard format name must carry the identical payload as 'text/csv'");
        }, CancellationToken.None);
    }

    // ── No-regression sibling: the existing text + HTML formats are still present alongside CSV ─────

    [Fact]
    public async Task Copy_StillIncludesPlainTextAndHtmlFormats_AlongsideNewCsvFormat()
    {
        await Session.Dispatch(() =>
        {
            var window = CreateFixtureWindow(out var sheet, out var range);
            var viewport = window.Session.Viewport;
            var text = ClipboardSerializer.Serialize(viewport, range);

            using var transfer = new DataTransfer();
            MainWindow.AddClipboardTextAndHtmlForTest(transfer, text, viewport, sheet, range, window.Session.Workbook.Theme);

            var htmlPlatformFormat = DataFormat.CreateStringPlatformFormat("text/html");
            var htmlWindowsFormat = DataFormat.CreateStringPlatformFormat("HTML Format");

            transfer.Formats.Should().Contain(DataFormat.Text, "plain text must still be on the clipboard, unchanged by adding CSV");
            transfer.Formats.Should().Contain(htmlPlatformFormat, "the 'text/html' fragment must still be present (R66)");
            transfer.Formats.Should().Contain(htmlWindowsFormat, "the Windows 'HTML Format' CF_HTML variant must still be present (R66)");
            transfer.Formats.Should().Contain(MainWindow.CsvPlatformFormatForTest);
            transfer.Formats.Should().Contain(MainWindow.CsvWindowsPlatformFormatForTest);

            GetPayload(transfer, DataFormat.Text).Should().Be(text);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Copy_WithNoCommaOrQuoteFields_ProducesUnquotedCsvPayload()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("PlainCsvFixture");
            window.Session.SelectSheet(sheet.Id);
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("One"));
            sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Two"));
            window.Session.UpdateViewportSize(881, 1440);

            var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
            var viewport = window.Session.Viewport;
            var text = ClipboardSerializer.Serialize(viewport, range);

            using var transfer = new DataTransfer();
            MainWindow.AddClipboardTextAndHtmlForTest(transfer, text, viewport, sheet, range, window.Session.Workbook.Theme);

            GetPayload(transfer, MainWindow.CsvPlatformFormatForTest).Should().Be("A,B\r\nOne,Two",
                "fields with no comma/quote/newline must not be wrapped in CSV quotes");
        }, CancellationToken.None);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static MainWindow CreateFixtureWindow(out Sheet sheet, out GridRange range)
    {
        var window = new MainWindow([]);
        var createdSheet = window.Session.Workbook.AddSheet("CsvFixture");
        window.Session.SelectSheet(createdSheet.Id);
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 1, 1), new TextValue("Name"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 1, 2), new TextValue("Amount, USD"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 2, 1), new TextValue("Widget"));
        createdSheet.SetCell(new CellAddress(createdSheet.Id, 2, 2), new TextValue("Ten"));
        window.Session.UpdateViewportSize(881, 1440);

        sheet = createdSheet;
        range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        return window;
    }

    private static string? GetPayload(DataTransfer transfer, DataFormat<string> format)
    {
        foreach (var item in transfer.Items)
        {
            if (item.Formats.Contains(format))
                return (string?)item.TryGetRaw(format);
        }

        return null;
    }
}
