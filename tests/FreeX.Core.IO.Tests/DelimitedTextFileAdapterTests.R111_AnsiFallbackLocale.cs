using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R111: DelimitedTextWorkbookReader.DecodeText's non-UTF8 fallback must resolve the OS's
/// current-culture ANSI code page (mirroring DelimitedTextWorkbookWriter.ResolveAnsiEncoding),
/// not a hard-coded Windows-1252. Before the fix, any non-Western-European Windows locale
/// (Japanese, Cyrillic, etc.) would mojibake a plain CSV/TXT the writer itself produced in that
/// locale's ANSI code page, because the reader always decoded the fallback bytes as CP1252
/// regardless of CultureInfo.CurrentCulture.
/// </summary>
public sealed class DelimitedTextFileAdapterTests_R111_AnsiFallbackLocale
{
    /// <summary>
    /// Real entry point: CsvFileAdapter.Load (what File&gt;Open reaches for a .csv). Feeds it a
    /// plain, BOM-less CSV encoded in Shift-JIS (code page 932) -- exactly what
    /// DelimitedTextWorkbookWriter.ResolveAnsiEncoding would have produced on a Japanese Windows
    /// install, and what real Excel's plain "CSV (Comma delimited)" Save-As also produces there --
    /// while CurrentCulture is ja-JP. Before the fix, the UTF-8-decode-failure fallback ignored
    /// CurrentCulture entirely and forced CP1252, turning the Shift-JIS bytes for "田中" (a common
    /// Japanese surname) into mojibake instead of decoding them correctly.
    /// </summary>
    [Fact]
    public void R111_CsvLoad_DecodesShiftJisFallbackUnderJapaneseCulture_NotHardcoded1252()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var shiftJis = Encoding.GetEncoding(932);
            // "Name,Amount\r\n田中,42\r\n" -- not valid UTF-8 (Shift-JIS multi-byte sequences for
            // 田中 fail strict UTF-8 decoding), forcing DecodeText's fallback path.
            var bytes = shiftJis.GetBytes("Name,Amount\r\n田中,42\r\n");

            var adapter = new CsvFileAdapter();
            using var stream = new MemoryStream(bytes);
            var workbook = adapter.Load(stream);
            var sheet = workbook.Sheets.Single();

            sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("田中"));
            sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(42));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// Sibling/no-regression coverage for the full user-visible round trip: Save (writer's
    /// ResolveAnsiEncoding) then Open (reader's DecodeText fallback) of a workbook containing
    /// non-ASCII text, both under the same non-Western locale, via the real CsvFileAdapter entry
    /// points on both ends -- the exact "save a workbook with Japanese text to plain .csv, then
    /// File&gt;Open it back in FreeX" scenario from the defect report.
    /// </summary>
    [Fact]
    public void R111_CsvSaveThenLoad_RoundTripsNonAsciiTextUnderJapaneseCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");

            var workbook = new Workbook("Untitled");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("田中"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(42));

            var adapter = new CsvFileAdapter();
            using var stream = new MemoryStream();
            adapter.Save(workbook, stream);
            stream.Position = 0;

            var loaded = adapter.Load(stream);
            var loadedSheet = loaded.Sheets.Single();

            loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 1)).Should().Be(new TextValue("田中"));
            loadedSheet.GetValue(new CellAddress(loadedSheet.Id, 1, 2)).Should().Be(new NumberValue(42));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// No-regression sibling: the pre-existing English/CP1252 fallback behaviour
    /// (DelimitedTextFileAdapterTests.LoadEncoding's
    /// Load_FallsBackToWindows1252ForTextExportsWhenUtf8DecodingFails) must still hold under an
    /// en-US-like culture whose ANSI code page genuinely is 1252 -- the fix must resolve to 1252
    /// via CurrentCulture, not merely happen to match by coincidence.
    /// </summary>
    [Fact]
    public void R111_CsvLoad_StillDecodesWindows1252FallbackUnderEnglishCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            var adapter = new CsvFileAdapter();
            using var stream = new MemoryStream([0x43, 0x61, 0x66, 0xE9, 0x2C, 0x34, 0x32, 0x0D, 0x0A]);
            var workbook = adapter.Load(stream);
            var sheet = workbook.Sheets.Single();

            sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Café"));
            sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new NumberValue(42));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
