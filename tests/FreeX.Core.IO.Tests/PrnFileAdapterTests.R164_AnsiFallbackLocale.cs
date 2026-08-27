using System.Globalization;
using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R164 / shared-encoding-detection F1 &amp; F4: <see cref="PrnFileAdapter"/> (Excel's "Formatted
/// Text (Space delimited)" .prn format) must resolve the OS's current-culture ANSI code page for
/// its non-UTF8 fallback -- both when reading (F1) and when writing (F4) -- mirroring
/// <c>DelimitedTextWorkbookWriter.ResolveAnsiEncoding</c> (the R111-fixed CSV/TXT sibling), not a
/// hard-coded Windows-1252 / always-UTF-8 choice. Before the fix, PrnWorkbookReader.DecodeText's
/// fallback always decoded as CP1252 regardless of CultureInfo.CurrentCulture (F1), and
/// PrnWorkbookWriter.Save always wrote UTF-8 regardless of locale (F4) -- both of which mojibake
/// non-ASCII text on a non-Western-European-locale machine when round-tripped through real Excel
/// or reopened in FreeX itself.
/// </summary>
public sealed class PrnFileAdapterTests_R164_AnsiFallbackLocale
{
    /// <summary>
    /// F1: real entry point PrnFileAdapter.Load (what File&gt;Open reaches for a .prn). Feeds it a
    /// plain, BOM-less .prn line encoded in Shift-JIS (code page 932) -- exactly what a Japanese-
    /// locale Excel install's ANSI Save-As of "Formatted Text (Space delimited)" would produce --
    /// while CurrentCulture is ja-JP. Before the fix, the UTF-8-decode-failure fallback ignored
    /// CurrentCulture entirely and forced CP1252, turning the Shift-JIS bytes for "田中" (a common
    /// Japanese surname) into mojibake instead of decoding them correctly.
    /// </summary>
    [Fact]
    public void R164_PrnLoad_DecodesShiftJisFallbackUnderJapaneseCulture_NotHardcoded1252()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var shiftJis = Encoding.GetEncoding(932);
            // "田中 42\r\n" -- not valid UTF-8 (Shift-JIS multi-byte sequences for 田中 fail strict
            // UTF-8 decoding), forcing DecodeText's fallback path.
            var bytes = shiftJis.GetBytes("田中 42\r\n");

            var adapter = new PrnFileAdapter();
            using var stream = new MemoryStream(bytes);
            var workbook = adapter.Load(stream);
            var sheet = workbook.Sheets.Single();

            sheet.GetCell(1, 1)?.Value.Should().Be(new TextValue("田中"));
            sheet.GetCell(1, 2)?.Value.Should().Be(new NumberValue(42));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// F4: real entry point PrnFileAdapter.Save. Under Japanese culture, saving a workbook
    /// containing "田中" must write Shift-JIS (code page 932) bytes -- the current-culture ANSI
    /// code page -- not UTF-8. Before the fix, PrnWorkbookWriter.Save unconditionally used
    /// UTF-8-no-BOM, so this would have produced UTF-8 bytes for 田中 instead of Shift-JIS ones,
    /// which real Excel (assuming ANSI for a BOM-less .prn) would mojibake on open.
    /// </summary>
    [Fact]
    public void R164_PrnSave_WritesShiftJisUnderJapaneseCulture_NotAlwaysUtf8()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");

            var workbook = new Workbook("Untitled");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("田中"));

            var adapter = new PrnFileAdapter();
            using var stream = new MemoryStream();
            adapter.Save(workbook, stream);
            var savedBytes = stream.ToArray();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var shiftJis = Encoding.GetEncoding(932);
            var expectedBytes = shiftJis.GetBytes("田中\r\n");

            savedBytes.Should().Equal(expectedBytes);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// F1 + F4 combined: the full user-visible round trip -- Save then Load, both under the same
    /// non-Western locale, via the real PrnFileAdapter entry points on both ends -- the exact
    /// "save a workbook with Japanese text to .prn, then File&gt;Open it back in FreeX" scenario
    /// from the defect report. This is the two-path-agreement test required for a writer/reader
    /// disagreement finding: it doesn't just check a substring of the output, it verifies the
    /// value that comes back out the other end matches what went in.
    /// </summary>
    [Fact]
    public void R164_PrnSaveThenLoad_RoundTripsNonAsciiTextUnderJapaneseCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ja-JP");

            var workbook = new Workbook("Untitled");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("田中"));
            sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(42));

            var adapter = new PrnFileAdapter();
            using var stream = new MemoryStream();
            adapter.Save(workbook, stream);
            stream.Position = 0;

            var loaded = adapter.Load(stream);
            var loadedSheet = loaded.Sheets.Single();

            loadedSheet.GetCell(1, 1)?.Value.Should().Be(new TextValue("田中"));
            loadedSheet.GetCell(1, 2)?.Value.Should().Be(new NumberValue(42));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// No-regression sibling: the pre-existing English/CP1252 fallback behaviour on Load must
    /// still hold under an en-US-like culture whose ANSI code page genuinely is 1252 -- the fix
    /// must resolve to 1252 via CurrentCulture, not merely happen to match by coincidence.
    /// </summary>
    [Fact]
    public void R164_PrnLoad_StillDecodesWindows1252FallbackUnderEnglishCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            var adapter = new PrnFileAdapter();
            // "Caf<0xE9> 42\r\n" in Windows-1252 -- 0xE9 is not valid standalone UTF-8, forcing the
            // fallback path.
            using var stream = new MemoryStream([0x43, 0x61, 0x66, 0xE9, 0x20, 0x34, 0x32, 0x0D, 0x0A]);
            var workbook = adapter.Load(stream);
            var sheet = workbook.Sheets.Single();

            sheet.GetCell(1, 1)?.Value.Should().Be(new TextValue("Café"));
            sheet.GetCell(1, 2)?.Value.Should().Be(new NumberValue(42));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    /// <summary>
    /// No-regression sibling: a genuinely UTF-8-encoded .prn (the common case -- FreeX writing
    /// under an en-US-like culture whose ANSI code page happens to be ASCII-compatible for plain
    /// text, or any strictly-valid-UTF8 byte stream) must still decode via the primary UTF-8 path,
    /// never falling into the ANSI fallback at all.
    /// </summary>
    [Fact]
    public void R164_PrnLoad_StillDecodesValidUtf8WithoutFallback()
    {
        var prn = "hello world\r\n";
        var bytes = Encoding.UTF8.GetBytes(prn);
        var adapter = new PrnFileAdapter();
        using var stream = new MemoryStream(bytes);
        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetCell(1, 1)?.Value.Should().Be(new TextValue("hello"));
        sheet.GetCell(1, 2)?.Value.Should().Be(new TextValue("world"));
    }
}
