using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R86-io-styles-dedup-index-5-1: the xf <c>quotePrefix</c> flag was
/// modeled on <see cref="Cell.QuotePrefix"/> and the ClosedXML mapper had
/// <see cref="XlsxClosedXmlCellMapper.MapQuotePrefix"/>/<see cref="XlsxClosedXmlCellMapper.ApplyQuotePrefix"/>
/// ready to use (see <c>XlsxCellQuotePrefixMapperTests</c>), but neither was ever called from the
/// per-cell load loop (XlsxFileAdapter.cs) or the per-cell full-save loop
/// (XlsxFileAdapter.Save.cs). A real leading-apostrophe forced-text cell therefore silently lost
/// its quotePrefix marker on a plain open/edit/save round trip through <see cref="XlsxFileAdapter"/>
/// — Excel would show the "Number Stored as Text" warning triangle again on reopen even though the
/// user had already dismissed it by typing the apostrophe. This test drives the real adapter
/// (not the lower-level mapper) to prove the flag now survives a load-then-save round trip.
/// </summary>
public sealed class R86_QuotePrefixLoadSaveWiringTests
{
    [Fact]
    public void XlsxAdapter_QuotePrefixedTextCell_RoundTrips_ThroughLoadAndSave()
    {
        // Build a source workbook the same way a real Excel file with a leading-apostrophe cell
        // would come in: quotePrefix stamped via the ClosedXML-facing mapper before the first save.
        var source = new Workbook("QuotePrefixWiring");
        var sourceSheet = source.AddSheet("Sheet1");
        var address = new CellAddress(sourceSheet.Id, 1, 1);
        var cell = Cell.FromValue(new TextValue("04512"));
        cell.QuotePrefix = true;
        sourceSheet.SetCell(address, cell);

        var adapter = new XlsxFileAdapter();
        using var firstSaveStream = new MemoryStream();
        adapter.Save(source, firstSaveStream);

        // Reload it (this is the load loop under test) and immediately re-save (the save loop
        // under test) without touching A1 at all -- simulating "open, edit something unrelated,
        // save".
        firstSaveStream.Position = 0;
        var reloaded = adapter.Load(firstSaveStream);
        using var secondSaveStream = new MemoryStream();
        adapter.Save(reloaded, secondSaveStream);

        secondSaveStream.Position = 0;
        var reloadedAgain = adapter.Load(secondSaveStream);
        var reloadedCell = reloadedAgain.GetSheetAt(0)!.GetCell(1, 1);

        reloadedCell.Should().NotBeNull();
        reloadedCell!.QuotePrefix.Should().BeTrue(
            "a leading-apostrophe forced-text cell must keep its quotePrefix marker through a real load/save round trip");
    }

    [Fact]
    public void XlsxAdapter_SiblingRegression_PlainTextCell_NeverGainsQuotePrefix_ThroughLoadAndSave()
    {
        var source = new Workbook("QuotePrefixWiringSibling");
        var sourceSheet = source.AddSheet("Sheet1");
        // No QuotePrefix set -- an ordinary text cell.
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), Cell.FromValue(new TextValue("plain text")));

        var adapter = new XlsxFileAdapter();
        using var firstSaveStream = new MemoryStream();
        adapter.Save(source, firstSaveStream);

        firstSaveStream.Position = 0;
        var reloaded = adapter.Load(firstSaveStream);
        using var secondSaveStream = new MemoryStream();
        adapter.Save(reloaded, secondSaveStream);

        secondSaveStream.Position = 0;
        var reloadedAgain = adapter.Load(secondSaveStream);
        var reloadedCell = reloadedAgain.GetSheetAt(0)!.GetCell(1, 1);

        reloadedCell.Should().NotBeNull();
        reloadedCell!.QuotePrefix.Should().BeFalse(
            "a plain text cell must not spuriously pick up quotePrefix through the load/save wiring");
    }
}
