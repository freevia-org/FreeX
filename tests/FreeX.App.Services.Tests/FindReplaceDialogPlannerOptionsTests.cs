using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class FindReplaceDialogPlannerOptionsTests
{
    [Fact]
    public void CreateFindOptions_MapsControlIndicesAndCarriesSearchConstraints()
    {
        var sheetId = SheetId.New();
        var format = StyleDiff.FromStyle(CellStyle.Default);
        GridRange[] selectionScope =
        [
            new GridRange(
                new CellAddress(sheetId, 2, 3),
                new CellAddress(sheetId, 4, 5))
        ];

        var options = FindReplaceDialogPlanner.CreateFindOptions(
            sheetId,
            withinSelectedIndex: 1,
            searchOrderSelectedIndex: 1,
            lookInSelectedIndex: 3,
            requiredFormat: format,
            selectionScope: selectionScope);

        options.Within.Should().Be(FindWithin.Workbook);
        options.CurrentSheetId.Should().Be(sheetId);
        options.SearchOrder.Should().Be(FindSearchOrder.ByColumns);
        options.LookIn.Should().Be(FindLookIn.Comments);
        options.RequiredFormat.Should().BeSameAs(format);
        options.SelectionScope.Should().BeSameAs(selectionScope);
    }

    [Theory]
    [InlineData(0, 0, 0, FindWithin.Sheet, FindSearchOrder.ByRows, FindLookIn.Formulas)]
    [InlineData(-1, 7, 1, FindWithin.Sheet, FindSearchOrder.ByRows, FindLookIn.Values)]
    [InlineData(1, 1, 2, FindWithin.Workbook, FindSearchOrder.ByColumns, FindLookIn.Notes)]
    public void CreateFindOptions_PreservesIndexFallbackPolicy(
        int withinIndex,
        int orderIndex,
        int lookInIndex,
        FindWithin expectedWithin,
        FindSearchOrder expectedOrder,
        FindLookIn expectedLookIn)
    {
        var options = FindReplaceDialogPlanner.CreateFindOptions(
            SheetId.New(),
            withinIndex,
            orderIndex,
            lookInIndex);

        options.Within.Should().Be(expectedWithin);
        options.SearchOrder.Should().Be(expectedOrder);
        options.LookIn.Should().Be(expectedLookIn);
    }

    [Fact]
    public void CreateFindOptions_PreservesMissingCurrentSheet()
    {
        var options = FindReplaceDialogPlanner.CreateFindOptions(
            currentSheetId: null,
            withinSelectedIndex: 0,
            searchOrderSelectedIndex: 0,
            lookInSelectedIndex: 1);

        options.CurrentSheetId.Should().BeNull();
    }

    [Fact]
    public void ResolveSelectionScopeAtOpen_SuppressesSingleCellAndPreservesMultiAreaScope()
    {
        var sheetId = SheetId.New();
        var singleCell = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 2, 3));
        var firstArea = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 4, 5));
        var secondArea = new GridRange(
            new CellAddress(sheetId, 8, 1),
            new CellAddress(sheetId, 9, 2));

        FindReplaceDialogPlanner.ResolveSelectionScopeAtOpen(singleCell, [singleCell])
            .Should().BeNull();
        FindReplaceDialogPlanner.ResolveSelectionScopeAtOpen(firstArea, [firstArea, secondArea])
            .Should().Equal(firstArea, secondArea);
    }
}
