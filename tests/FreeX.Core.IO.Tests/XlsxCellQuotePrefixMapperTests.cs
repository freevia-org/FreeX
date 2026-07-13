using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R38-io-cellstyle-xf-dedup-2: quotePrefix (the ECMA-376 cellXfs
/// <c>xf@quotePrefix</c> attribute for a leading-apostrophe forced-text cell, e.g. a part number
/// entered as <c>'04512</c>) was completely unmodeled — <see cref="Cell"/> had no field for it and
/// <see cref="XlsxClosedXmlCellMapper"/> never read or wrote it, so it could never be authored or
/// round-tripped on any save path.
///
/// This is a scoped model+IO-mapper fix: <see cref="Cell.QuotePrefix"/> plus
/// <see cref="XlsxClosedXmlCellMapper.MapQuotePrefix"/>/<see cref="XlsxClosedXmlCellMapper.ApplyQuotePrefix"/>
/// now carry the flag correctly through ClosedXML and survive a real workbook save/reload. Wiring
/// these calls into the per-cell load/save loops (XlsxFileAdapter.cs / XlsxFileAdapter.Save.cs) so a
/// live open-edit-save round trip populates <c>Cell.QuotePrefix</c> automatically is a follow-up
/// outside this change's file scope.
/// </summary>
public sealed class XlsxCellQuotePrefixMapperTests
{
    // ------------------------------------------------------------------
    // Model: Cell.QuotePrefix
    // ------------------------------------------------------------------

    [Fact]
    public void Cell_QuotePrefix_DefaultsFalse()
    {
        var cell = Cell.FromValue(new TextValue("04512"));

        cell.QuotePrefix.Should().BeFalse("a cell with no leading apostrophe must not carry the marker");
    }

    [Fact]
    public void Cell_Clone_PreservesQuotePrefix()
    {
        var cell = Cell.FromValue(new TextValue("04512"));
        cell.QuotePrefix = true;

        var clone = cell.Clone();

        clone.QuotePrefix.Should().BeTrue("Clone() must copy every round-trippable per-cell flag, including QuotePrefix");
    }

    [Fact]
    public void Cell_Clone_SiblingRegression_NonQuotePrefixCellStaysFalse()
    {
        var cell = Cell.FromValue(new TextValue("plain text"));

        var clone = cell.Clone();

        clone.QuotePrefix.Should().BeFalse("Clone() must not spuriously set QuotePrefix on an unrelated cell");
    }

    // ------------------------------------------------------------------
    // IO mapper: MapQuotePrefix / ApplyQuotePrefix
    // ------------------------------------------------------------------

    [Fact]
    public void MapQuotePrefix_ReadsIncludeQuotePrefix_WhenSet()
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Sheet1").Cell("A1");
        cell.Value = "04512";
        cell.Style.IncludeQuotePrefix = true;

        XlsxClosedXmlCellMapper.MapQuotePrefix(cell).Should().BeTrue();
    }

    [Fact]
    public void MapQuotePrefix_SiblingRegression_ReadsFalse_WhenNotSet()
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Sheet1").Cell("A1");
        cell.Value = "plain text";

        XlsxClosedXmlCellMapper.MapQuotePrefix(cell).Should().BeFalse();
    }

    [Fact]
    public void ApplyQuotePrefix_True_SetsIncludeQuotePrefix()
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Sheet1").Cell("A1");
        cell.Value = "04512";

        XlsxClosedXmlCellMapper.ApplyQuotePrefix(cell, true);

        cell.Style.IncludeQuotePrefix.Should().BeTrue();
    }

    [Fact]
    public void ApplyQuotePrefix_SiblingRegression_False_DoesNotSetIncludeQuotePrefix()
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Sheet1").Cell("A1");
        cell.Value = "plain text";

        XlsxClosedXmlCellMapper.ApplyQuotePrefix(cell, false);

        cell.Style.IncludeQuotePrefix.Should().BeFalse();
    }

    [Fact]
    public void ApplyQuotePrefix_RoundTripsThroughRealWorkbookSave()
    {
        // Proves the flag actually persists through a real xlsx save/reload via ClosedXML — the
        // mechanism XlsxFileAdapter's full-rebuild save path uses — not just an in-memory property set.
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var cell = workbook.AddWorksheet("Sheet1").Cell("A1");
            cell.Value = "04512";
            XlsxClosedXmlCellMapper.ApplyQuotePrefix(cell, true);
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        using var reloaded = new XLWorkbook(stream);
        var reloadedCell = reloaded.Worksheet("Sheet1").Cell("A1");

        XlsxClosedXmlCellMapper.MapQuotePrefix(reloadedCell).Should().BeTrue(
            "quotePrefix must survive a real save/reload once ApplyQuotePrefix has stamped it");
    }

    [Fact]
    public void ApplyQuotePrefix_SiblingRegression_UnrelatedCellStaysFalse_ThroughRealWorkbookSave()
    {
        using var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Sheet1");
            var quotedCell = sheet.Cell("A1");
            quotedCell.Value = "04512";
            XlsxClosedXmlCellMapper.ApplyQuotePrefix(quotedCell, true);

            var plainCell = sheet.Cell("A2");
            plainCell.Value = "plain text";
            // No ApplyQuotePrefix call for A2 — it must not inherit the flag from A1.
            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        using var reloaded = new XLWorkbook(stream);
        var reloadedPlainCell = reloaded.Worksheet("Sheet1").Cell("A2");

        XlsxClosedXmlCellMapper.MapQuotePrefix(reloadedPlainCell).Should().BeFalse(
            "an unrelated cell must not pick up quotePrefix from a sibling cell's xf");
    }
}
