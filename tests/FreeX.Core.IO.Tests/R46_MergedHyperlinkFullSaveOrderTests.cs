using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for R46-io-hyperlink-3-1: on a FULL (ClosedXML) save, hyperlinks assigned to
/// every cell of a merged region -- the shape ClosedXML's own loader produces from a real Excel
/// range hyperlink (one hyperlink object per cell in the range) -- must all survive the save.
/// Previously, the per-cell hyperlink-writing loop ran BEFORE the merged-region loop, and
/// ClosedXML's Range.Merge() clears every non-anchor cell of a region (including any hyperlink
/// just assigned to it), so only the anchor cell's hyperlink survived.
/// </summary>
public sealed class R46_MergedHyperlinkFullSaveOrderTests
{
    [Fact]
    public void FullSave_HyperlinksOnEveryCellOfAMergedRegion_AllSurvive()
    {
        var workbook = new Workbook("MergedHyperlinkTest");
        var sheet = workbook.AddSheet("S1");

        // A1:B2 merged, with a hyperlink assigned to all four cells -- exactly what ClosedXML's
        // own loader materializes from a single real-Excel range hyperlink (<hyperlink
        // ref="A1:B2" .../>) over a merged region.
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Merged link"));

        var addresses = new[]
        {
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 2),
        };
        foreach (var address in addresses)
            sheet.Hyperlinks[address] = "https://example.com/merged";

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);

        // A freshly-constructed in-memory workbook was never loaded via the adapter, so this Save
        // call always takes the full ClosedXML rebuild path (there is no source package to patch).
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var reloadedSheet = reloaded.GetSheetAt(0);

        foreach (var address in addresses)
        {
            var reloadedAddress = new CellAddress(reloadedSheet.Id, address.Row, address.Col);
            reloadedSheet.Hyperlinks.Should().ContainKey(
                reloadedAddress,
                $"the hyperlink on non-anchor cell {address} of the merged region must survive a full save");
            reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("https://example.com/merged");
        }
    }

    [Fact]
    public void FullSave_HyperlinkOnASingleUnmergedCell_StillSurvives()
    {
        // Sibling no-regression case: an ordinary single-cell hyperlink with no merge involved at
        // all must keep working exactly as before the merge/hyperlink write-order fix.
        var workbook = new Workbook("SingleHyperlinkTest");
        var sheet = workbook.AddSheet("S1");

        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new TextValue("Link"));
        sheet.Hyperlinks[address] = "https://example.com/single";

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave, adapter.LastSaveDiagnostics.Reason);

        ms.Position = 0;
        var reloaded = adapter.Load(ms);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAddress = new CellAddress(reloadedSheet.Id, 1, 1);

        reloadedSheet.Hyperlinks.Should().ContainKey(reloadedAddress);
        reloadedSheet.Hyperlinks[reloadedAddress].Should().Be("https://example.com/single");
    }
}
