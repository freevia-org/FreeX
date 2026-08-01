using System.IO;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Authoritative XLSX save/reopen evidence for nested row and column outlines while a filtered
/// range is active. The assertions inspect the reloaded model, not screenshots or XML alone.
/// </summary>
public sealed class R99_NestedOutlineFilteredRangeRoundTripTests
{
    [Fact]
    public void SaveReopen_RetainsNestedRowAndColumnOutlinesAndFilteredRows()
    {
        var workbook = BuildFixture(out var sheet, out var context);
        ApplyNestedOutlineState(sheet, context);

        using var firstSave = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, firstSave);
        firstSave.Position = 0;

        var reopened = adapter.Load(firstSave);
        var reopenedSheet = reopened.GetSheetAt(0);
        AssertNestedState(reopenedSheet);

        using var secondSave = new MemoryStream();
        adapter.Save(reopened, secondSave);
        secondSave.Position = 0;

        var reopenedAgain = adapter.Load(secondSave).GetSheetAt(0);
        AssertNestedState(reopenedAgain);
    }

    private static Workbook BuildFixture(out Sheet sheet, out TestCommandContext context)
    {
        var workbook = new Workbook("NestedOutlineFilteredRoundTrip");
        sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 8; row++)
        {
            sheet.SetCell(
                new CellAddress(sheet.Id, row, 1),
                new TextValue(row is 3 or 5 ? "Drop" : "Keep"));
        }

        for (uint column = 8; column <= 12; column++)
        {
            sheet.SetCell(
                new CellAddress(sheet.Id, 1, column),
                new TextValue($"Column{column}"));
        }

        var filterRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 8, 12));
        sheet.AutoFilter = new WorksheetAutoFilterModel(filterRange.ToString(), null);
        context = new TestCommandContext(workbook);
        new FilterCommand(sheet.Id, filterRange, filterColOffset: 0, ["Keep"])
            .Apply(context)
            .Success
            .Should()
            .BeTrue();
        return workbook;
    }

    private static void ApplyNestedOutlineState(Sheet sheet, TestCommandContext context)
    {
        new GroupRowsCommand(sheet.Id, 2, 7, 1, preserveExistingHierarchy: true)
            .Apply(context)
            .Success
            .Should()
            .BeTrue();
        new GroupRowsCommand(sheet.Id, 3, 4, 2, preserveExistingHierarchy: true)
            .Apply(context)
            .Success
            .Should()
            .BeTrue();
        new GroupColumnsCommand(sheet.Id, 8, 12, 1, preserveExistingHierarchy: true)
            .Apply(context)
            .Success
            .Should()
            .BeTrue();
        new GroupColumnsCommand(sheet.Id, 9, 11, 2, preserveExistingHierarchy: true)
            .Apply(context)
            .Success
            .Should()
            .BeTrue();

        new CollapseRowGroupCommand(sheet.Id, 2).Apply(context).Success.Should().BeTrue();
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(context).Success.Should().BeTrue();
        new CollapseColGroupCommand(sheet.Id, 2).Apply(context).Success.Should().BeTrue();
        new CollapseColGroupCommand(sheet.Id, 1).Apply(context).Success.Should().BeTrue();
    }

    private static void AssertNestedState(Sheet sheet)
    {
        sheet.RowOutlineLevels.Should().BeEquivalentTo(new Dictionary<uint, int>
        {
            [2] = 1,
            [3] = 2,
            [4] = 2,
            [5] = 1,
            [6] = 1,
            [7] = 1
        });
        sheet.ColOutlineLevels.Should().BeEquivalentTo(new Dictionary<uint, int>
        {
            [8] = 1,
            [9] = 2,
            [10] = 2,
            [11] = 2,
            [12] = 1
        });
        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
        sheet.ValueFilterHiddenRows.Should().BeEquivalentTo([3u, 5u]);
        sheet.GroupHiddenRows.Should().Contain([2u, 3u, 4u, 5u, 6u, 7u]);
        sheet.GroupHiddenCols.Should().Contain([8u, 9u, 10u, 11u, 12u]);
        sheet.CollapsedAnchorRows.Should().Contain([5u, 8u]);
        sheet.CollapsedAnchorCols.Should().Contain([12u, 13u]);
        sheet.IsRowFilterHidden(3).Should().BeTrue();
        sheet.IsRowFilterHidden(5).Should().BeTrue();
    }
}
