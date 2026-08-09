using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R99-render-header-select-merge-expand: WPF expands whole-row/whole-column header selections
/// through every merged region they partially intersect. Avalonia previously selected only the
/// clicked header band, leaving a merged cell split across the selection boundary.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R99_AvaloniaHeaderMergeSelectionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task RowHeaderSelection_ExpandsThroughVerticallySpanningMerge()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("RowHeaderMergeFixture");
                window.Session.SelectSheet(sheet.Id);
                sheet.AddMergedRegion(new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 3, 2)));

                InvokeSelectEntireRow(window, 2, extend: false);

                window.Session.SelectedRange.Should().Be(new GridRange(
                    new CellAddress(sheet.Id, 2, 1),
                    new CellAddress(sheet.Id, 3, CellAddress.MaxCol)));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ColumnHeaderSelection_ExpandsThroughHorizontallySpanningMerge()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("ColumnHeaderMergeFixture");
                window.Session.SelectSheet(sheet.Id);
                sheet.AddMergedRegion(new GridRange(
                    new CellAddress(sheet.Id, 2, 2),
                    new CellAddress(sheet.Id, 2, 3)));

                InvokeSelectEntireColumn(window, 2, extend: false);

                window.Session.SelectedRange.Should().Be(new GridRange(
                    new CellAddress(sheet.Id, 1, 2),
                    new CellAddress(sheet.Id, CellAddress.MaxRow, 3)));
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlClickRowHeader_AddsExpandedMergedBandAsDisjointArea()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("AdditionalRowMergeFixture");
                window.Session.SelectSheet(sheet.Id);
                sheet.AddMergedRegion(new GridRange(
                    new CellAddress(sheet.Id, 5, 4),
                    new CellAddress(sheet.Id, 6, 4)));

                InvokeSelectEntireRow(window, 2, extend: false);
                InvokeAddAdditionalRowSelection(window, 5);

                window.Session.SelectedRanges.Should().BeEquivalentTo([
                    new GridRange(
                        new CellAddress(sheet.Id, 2, 1),
                        new CellAddress(sheet.Id, 2, CellAddress.MaxCol)),
                    new GridRange(
                        new CellAddress(sheet.Id, 5, 1),
                        new CellAddress(sheet.Id, 6, CellAddress.MaxCol))]);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlClickColumnHeader_AddsExpandedMergedBandAsDisjointArea()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("AdditionalColumnMergeFixture");
                window.Session.SelectSheet(sheet.Id);
                sheet.AddMergedRegion(new GridRange(
                    new CellAddress(sheet.Id, 4, 5),
                    new CellAddress(sheet.Id, 4, 6)));

                InvokeSelectEntireColumn(window, 2, extend: false);
                InvokeAddAdditionalColumnSelection(window, 5);

                window.Session.SelectedRanges.Should().BeEquivalentTo([
                    new GridRange(
                        new CellAddress(sheet.Id, 1, 2),
                        new CellAddress(sheet.Id, CellAddress.MaxRow, 2)),
                    new GridRange(
                        new CellAddress(sheet.Id, 1, 5),
                        new CellAddress(sheet.Id, CellAddress.MaxRow, 6))]);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static void InvokeSelectEntireRow(MainWindow window, uint row, bool extend) =>
        typeof(MainWindow)
            .GetMethod("SelectEntireRow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [row, extend]);

    private static void InvokeAddAdditionalRowSelection(MainWindow window, uint row) =>
        typeof(MainWindow)
            .GetMethod("AddAdditionalRowSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [row]);

    private static void InvokeSelectEntireColumn(MainWindow window, uint col, bool extend) =>
        typeof(MainWindow)
            .GetMethod("SelectEntireColumn", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [col, extend]);

    private static void InvokeAddAdditionalColumnSelection(MainWindow window, uint col) =>
        typeof(MainWindow)
            .GetMethod("AddAdditionalColumnSelection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [col]);
}
