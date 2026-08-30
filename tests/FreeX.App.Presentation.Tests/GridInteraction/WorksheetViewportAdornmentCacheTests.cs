using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class WorksheetViewportAdornmentCacheTests
{
    [Fact]
    public void SameIdentityAndRevision_ReusesImmutableProjection()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.ShownComments.Add(address);
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C4", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(1, ["Open"]));
        sheet.Hyperlinks[address] = " https://example.test ";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(ScreenTip: " Example tip ");
        var cache = new WorksheetViewportAdornmentCache();

        var first = cache.GetOrCreate(workbook, sheet, revision: 7);
        var second = cache.GetOrCreate(workbook, sheet, revision: 7);

        second.Should().BeSameAs(first);
        first.PinnedNoteAddresses.Should().Contain((2u, 3u));
        first.AutoFilterRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3)));
        first.ActiveAutoFilterColumns.Should().Contain(1u);
        var gridAddress = new CellAddress(default, 2, 3);
        first.HyperlinkCells.Should().Contain(gridAddress);
        first.HyperlinkTooltips.Should().Contain(gridAddress, "Example tip");
    }

    [Fact]
    public void RevisionChange_RebuildsAndRevealsSourceMutation()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var firstAddress = new CellAddress(sheet.Id, 1, 1);
        var secondAddress = new CellAddress(sheet.Id, 2, 2);
        sheet.Hyperlinks[firstAddress] = "first";
        var cache = new WorksheetViewportAdornmentCache();

        var first = cache.GetOrCreate(workbook, sheet, revision: 1);
        sheet.Hyperlinks[secondAddress] = "second";
        var unchangedRevision = cache.GetOrCreate(workbook, sheet, revision: 1);
        var nextRevision = cache.GetOrCreate(workbook, sheet, revision: 2);

        unchangedRevision.Should().BeSameAs(first);
        unchangedRevision.HyperlinkCells.Should().NotContain(new CellAddress(default, 2, 2));
        nextRevision.Should().NotBeSameAs(first);
        nextRevision.HyperlinkCells.Should().Contain(new CellAddress(default, 2, 2));
    }

    [Fact]
    public void BlankTarget_RemainsClickableWithoutTooltip()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 4, 5);
        sheet.Hyperlinks[address] = "   ";

        var result = new WorksheetViewportAdornmentCache().GetOrCreate(workbook, sheet, revision: 0);
        var gridAddress = new CellAddress(default, 4, 5);

        result.HyperlinkCells.Should().Contain(gridAddress);
        result.HyperlinkTooltips.Should().NotContainKey(gridAddress);
    }

    [Fact]
    public void Clear_DropsCachedProjection()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var cache = new WorksheetViewportAdornmentCache();
        var first = cache.GetOrCreate(workbook, sheet, revision: 3);

        cache.Clear();
        var second = cache.GetOrCreate(workbook, sheet, revision: 3);

        second.Should().NotBeSameAs(first);
    }
}
