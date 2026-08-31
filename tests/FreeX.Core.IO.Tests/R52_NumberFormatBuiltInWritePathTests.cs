using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R52-io-numfmt-builtin-custom-3-1: the cell-style write path (XlsxClosedXmlCellMapper.ApplyStyle)
/// unconditionally assigned the model's NumberFormat string to ClosedXML's
/// <c>xlStyle.NumberFormat.Format</c>, even when that string is byte-identical to one of the
/// ECMA-376 builtin format codes (e.g. "0%" == builtin id 9, the Home ribbon's Percentage button).
/// ClosedXML then always allocates a brand-new custom numFmtId (&gt;=164) and writes an explicit
/// &lt;numFmt&gt; entry instead of using the implicit builtin id -- so a file saved after clicking
/// Comma Style/Accounting/Percentage/Fraction/Scientific/Text always shows "Custom" instead of the
/// real category when reopened in Excel's Format Cells dialog. The fix resolves a builtin-matching
/// format code back to its NumberFormatId (mirroring the read-side fallback in
/// XlsxClosedXmlCellMapper.MapNumberFormat) instead of always setting an explicit Format string.
/// </summary>
public sealed class R52_NumberFormatBuiltInWritePathTests
{
    public static TheoryData<string, int> BuiltInFormatCodes => new()
    {
        { "0%", 9 },                    // Percentage ribbon button / Ctrl+Shift+%
        { "# ?/?", 12 },                // Fraction
        { "0.00E+00", 11 },             // Scientific
        { "m/d/yy", 14 },                // Ctrl+; current date
        { "@", 49 },                    // Text
        { "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)", 43 }, // Comma Style
        { "_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)", 44 } // Accounting
    };

    private static byte[] SaveCellWithNumberFormat(string numberFormat)
    {
        var workbook = new Workbook("NumFmt");
        var sheet = workbook.AddSheet("Sheet1");
        var styleId = workbook.RegisterStyle(new CellStyle { NumberFormat = numberFormat });
        var cell = Cell.FromValue(new NumberValue(0.5));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    [Theory]
    [MemberData(nameof(BuiltInFormatCodes))]
    public void Save_BuiltInMatchingNumberFormat_WritesImplicitNumberFormatIdInsteadOfCustomNumFmt(
        string numberFormat,
        int expectedBuiltInId)
    {
        var savedBytes = SaveCellWithNumberFormat(numberFormat);

        using var savedStream = new MemoryStream(savedBytes, writable: false);
        using var reopened = new XLWorkbook(savedStream);
        var style = reopened.Worksheet(1).Cell("A1").Style;

        style.NumberFormat.NumberFormatId.Should().Be(
            expectedBuiltInId,
            "a format code byte-identical to a builtin should round-trip as that builtin's implicit numFmtId");
        style.NumberFormat.Format.Should().BeEmpty(
            "the builtin id alone should govern the format -- no redundant explicit formatCode should be stored");

        // Round-trip through FreeX's own reader must still recover the exact original format string
        // (the read-side fallback already resolves an empty Format + builtin NumberFormatId back to
        // the canonical code).
        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = new XlsxFileAdapter().Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloaded.GetStyle(reloadedSheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be(numberFormat);
    }

    /// <summary>
    /// Sibling no-regression case: a genuinely custom format code (not byte-identical to any builtin)
    /// must still be written as an explicit &lt;numFmt&gt; entry with a custom (&gt;=164) numFmtId,
    /// exactly as before this fix.
    /// </summary>
    [Fact]
    public void Save_TrulyCustomNumberFormat_StillWritesExplicitCustomNumFmt()
    {
        const string customFormat = "#,##0.0000 \"widgets\"";
        var savedBytes = SaveCellWithNumberFormat(customFormat);

        using var savedStream = new MemoryStream(savedBytes, writable: false);
        using var reopened = new XLWorkbook(savedStream);
        var style = reopened.Worksheet(1).Cell("A1").Style;

        // ClosedXML surfaces NumberFormatId == -1 to mean "not a recognized builtin id" -- i.e. this
        // format was NOT (mis)resolved to a builtin, exactly as before this fix. Format still carries
        // the literal custom code, which is what actually gets persisted as an explicit <numFmt>
        // entry with a custom (>=164) numFmtId in the saved package.
        style.NumberFormat.NumberFormatId.Should().Be(
            -1,
            "a non-builtin format code must not be resolved to any builtin id, unchanged from before this fix");
        style.NumberFormat.Format.Should().Be(customFormat);

        using var reloadStream = new MemoryStream(savedBytes, writable: false);
        var reloaded = new XlsxFileAdapter().Load(reloadStream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloaded.GetStyle(reloadedSheet.GetCell(1, 1)!.StyleId).NumberFormat.Should().Be(customFormat);
    }
}
