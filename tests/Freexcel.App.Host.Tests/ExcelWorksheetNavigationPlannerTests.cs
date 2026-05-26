using System.Diagnostics;
using System.Windows.Input;
using FluentAssertions;
using Freexcel.Core.Model;
using Xunit.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class ExcelWorksheetNavigationPlannerTests(ITestOutputHelper output)
{
    private static readonly SheetId SheetId = SheetId.New();

    [Theory]
    [InlineData(Key.PageDown, Key.None, ModifierKeys.Alt, 10u)]
    [InlineData(Key.None, Key.PageDown, ModifierKeys.Alt, 10u)]
    [InlineData(Key.PageUp, Key.None, ModifierKeys.Alt, 1u)]
    [InlineData(Key.None, Key.PageUp, ModifierKeys.Alt, 1u)]
    [InlineData(Key.PageDown, Key.None, ModifierKeys.Alt | ModifierKeys.Shift, 10u)]
    public void GetHorizontalPageTarget_MapsExcelAltPageNavigation(
        Key key,
        Key systemKey,
        ModifierKeys modifiers,
        uint expectedCol)
    {
        var current = new CellAddress(SheetId, 5, 4);

        var target = ExcelWorksheetNavigationPlanner.GetHorizontalPageTarget(
            key,
            systemKey,
            modifiers,
            current,
            pageSize: 6);

        target.Should().Be(new CellAddress(SheetId, 5, expectedCol));
    }

    [Fact]
    public void GetHorizontalPageTarget_IgnoresPlainPageKeys()
    {
        var current = new CellAddress(SheetId, 5, 4);

        var target = ExcelWorksheetNavigationPlanner.GetHorizontalPageTarget(
            Key.PageDown,
            Key.None,
            ModifierKeys.None,
            current,
            pageSize: 6);

        target.Should().BeNull();
    }

    [Theory]
    [InlineData(Key.End, ModifierKeys.None, false, true)]
    [InlineData(Key.End, ModifierKeys.None, true, false)]
    public void TryToggleEndMode_MapsPlainEndKey(
        Key key,
        ModifierKeys modifiers,
        bool current,
        bool expected)
    {
        var handled = ExcelWorksheetNavigationPlanner.TryToggleEndMode(key, modifiers, current, out var next);

        handled.Should().BeTrue();
        next.Should().Be(expected);
    }

    [Theory]
    [InlineData(Key.Right, true)]
    [InlineData(Key.Left, true)]
    [InlineData(Key.Up, true)]
    [InlineData(Key.Down, true)]
    [InlineData(Key.PageDown, false)]
    public void ShouldUseDataBoundary_TreatsEndModeArrowsLikeCtrlArrow(Key key, bool expected)
    {
        ExcelWorksheetNavigationPlanner.ShouldUseDataBoundary(
                key,
                ModifierKeys.None,
                endMode: true)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void FindVerticalDataBoundary_FromFilledCellStopsBeforeFirstGap()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(SheetId, 3, 3), new NumberValue(20));

        ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(
                sheet,
                new CellAddress(SheetId, 2, 3),
                rowDirection: 1)
            .Should()
            .Be(new CellAddress(SheetId, 3, 3));
    }

    [Fact]
    public void FindHorizontalDataBoundary_FromBlankCellStopsOnFirstFilledCell()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 5, 4), new TextValue("Found"));

        ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(
                sheet,
                new CellAddress(SheetId, 5, 2),
                columnDirection: 1)
            .Should()
            .Be(new CellAddress(SheetId, 5, 4));
    }

    [Fact]
    public void FindVerticalDataBoundary_FromSparseBlankCellStopsOnDistantFilledCell()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 900_000, 7), new TextValue("Found"));

        ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(
                sheet,
                new CellAddress(SheetId, 2, 7),
                rowDirection: 1)
            .Should()
            .Be(new CellAddress(SheetId, 900_000, 7));
    }

    [Fact]
    public void FindHorizontalDataBoundary_FromSparseBlankCellStopsOnDistantFilledCell()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 5, 16_000), new TextValue("Found"));

        ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(
                sheet,
                new CellAddress(SheetId, 5, 2),
                columnDirection: 1)
            .Should()
            .Be(new CellAddress(SheetId, 5, 16_000));
    }

    [Fact]
    public void FindVerticalDataBoundary_FromBlankCellStopsOnSpillValue()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        var anchor = new CellAddress(SheetId, 10, 3);
        sheet.SetCell(anchor, new NumberValue(1));
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(1), new NumberValue(2) },
            { new NumberValue(3), new TextValue("Spill") }
        }));

        ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(
                sheet,
                new CellAddress(SheetId, 1, 4),
                rowDirection: 1)
            .Should()
            .Be(new CellAddress(SheetId, 10, 4));

        ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(
                sheet,
                new CellAddress(SheetId, 11, 4),
                rowDirection: -1)
            .Should()
            .Be(new CellAddress(SheetId, 10, 4));
    }

    [Fact]
    public void FindHorizontalDataBoundary_FromBlankCellStopsOnHorizontalSpillValue()
    {
        var sheet = new Sheet(SheetId, "Sheet1");
        var anchor = new CellAddress(SheetId, 7, 20);
        sheet.SetCell(anchor, new NumberValue(1));
        sheet.SetSpillRange(anchor, new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(1), new NumberValue(2) },
            { new NumberValue(3), new TextValue("Spill") }
        }));

        ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(
                sheet,
                new CellAddress(SheetId, 8, 1),
                columnDirection: 1)
            .Should()
            .Be(new CellAddress(SheetId, 8, 20));

        ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(
                sheet,
                new CellAddress(SheetId, 8, 21),
                columnDirection: -1)
            .Should()
            .Be(new CellAddress(SheetId, 8, 20));
    }

    [Fact]
    public void FindDataBoundary_RepeatedSparseBlankNavigationHasLowCost()
    {
        var sheet = new Sheet(SheetId, "Sparse");
        sheet.SetCell(new CellAddress(SheetId, 900_000, 10), new TextValue("vertical"));
        sheet.SetCell(new CellAddress(SheetId, 20, 16_000), new TextValue("horizontal"));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int repetitions = 100;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        CellAddress vertical = default;
        CellAddress horizontal = default;
        for (var i = 0; i < repetitions; i++)
        {
            vertical = ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary(
                sheet,
                new CellAddress(SheetId, 1, 10),
                rowDirection: 1);
            horizontal = ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary(
                sheet,
                new CellAddress(SheetId, 20, 1),
                columnDirection: 1);
        }
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        vertical.Should().Be(new CellAddress(SheetId, 900_000, 10));
        horizontal.Should().Be(new CellAddress(SheetId, 20, 16_000));
        allocated.Should().BeLessThan(100_000);
        output.WriteLine(
            $"FindDataBoundary sparse blank navigation repeated {repetitions}x: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes allocated.");
    }

    [Fact]
    public void GetCtrlEndCell_UsesBottomRightUsedCellOrA1ForEmptySheets()
    {
        var empty = new Sheet(SheetId, "Empty");
        ExcelWorksheetNavigationPlanner.GetCtrlEndCell(empty, SheetId)
            .Should()
            .Be(new CellAddress(SheetId, 1, 1));

        var sheet = new Sheet(SheetId, "Sheet1");
        sheet.SetCell(new CellAddress(SheetId, 8, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(SheetId, 2, 7), new NumberValue(2));

        ExcelWorksheetNavigationPlanner.GetCtrlEndCell(sheet, SheetId)
            .Should()
            .Be(new CellAddress(SheetId, 8, 7));
    }
}
