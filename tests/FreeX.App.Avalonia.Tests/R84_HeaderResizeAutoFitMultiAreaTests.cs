using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using FreeX.Core.Model;
using FreeX.App.Presentation;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R84-app-mouse-selection-5-2: double-clicking a column/row header's resize border to autofit
/// only ever touched the single header under the pointer, ignoring a pre-existing multi-column/row
/// selection. <see cref="MainWindow.AutoFitColumnFromHeader"/> / <c>AutoFitRowFromHeader</c> used
/// to call <c>SelectEntireColumn(col)</c>/<c>SelectEntireRow(row)</c> FIRST -- unconditionally
/// collapsing any wider selection down to just the double-clicked header -- before autofitting,
/// destroying the original multi-column/row selection as a side effect (unlike Excel and the WPF
/// host's OnColumnAutoFitRequested/OnRowAutoFitRequested, which autofit the whole selected band via
/// <c>GridResizePreviewPlanner.GetSelectedColumnResizeRange</c>/<c>GetSelectedRowResizeRange</c>).
/// The fix only falls back to <c>SelectEntireColumn</c>/<c>SelectEntireRow</c> when the
/// double-clicked header ISN'T already part of a wider multi-column/row band.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R84_HeaderResizeAutoFitMultiAreaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task AutoFitColumnFromHeader_WithMultiColumnSelection_AutoFitsWholeBandInsteadOfCollapsing()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("AutoFitMultiColFixture");
                window.Session.SelectSheet(sheet.Id);

                // Select the whole B:D column band, as if the user clicked B's header then
                // Shift-clicked D's header.
                InvokeSelectEntireColumn(window, 2, extend: false);
                InvokeSelectEntireColumn(window, 4, extend: true);
                var wholeBand = new GridRange(
                    new CellAddress(sheet.Id, 1, 2),
                    new CellAddress(sheet.Id, CellAddress.MaxRow, 4));
                window.Session.SelectedRange.Should().Be(wholeBand,
                    "the fixture must start with columns B:D selected as a whole-column band");

                // Double-click column C's (the middle column's) resize border to autofit.
                InvokeAutoFitColumnFromHeader(window, 3);

                window.Session.SelectedRange.Should().Be(wholeBand,
                    "autofitting a column inside a pre-existing multi-column selection must autofit " +
                    "(and leave selected) the WHOLE B:D band, not collapse it down to just C first");
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
    public async Task AutoFitColumnFromHeader_WithoutMultiColumnSelection_NoRegression_StillTargetsJustTheClickedColumn()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("AutoFitSingleColFixture");
                window.Session.SelectSheet(sheet.Id);

                // Active selection is a single cell elsewhere, not touching column C at all.
                window.Session.SelectCell(new CellAddress(sheet.Id, 9, 9));

                InvokeAutoFitColumnFromHeader(window, 3);

                window.Session.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, CellAddress.MaxRow, 3)),
                    "without a pre-existing multi-column selection touching the double-clicked column, " +
                    "autofit must still just target that single column (unchanged, pre-existing behavior)");
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
    public async Task AutoFitRowFromHeader_WithMultiRowSelection_AutoFitsWholeBandInsteadOfCollapsing()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("AutoFitMultiRowFixture");
                window.Session.SelectSheet(sheet.Id);

                InvokeSelectEntireRow(window, 2, extend: false);
                InvokeSelectEntireRow(window, 4, extend: true);
                var wholeBand = new GridRange(
                    new CellAddress(sheet.Id, 2, 1),
                    new CellAddress(sheet.Id, 4, CellAddress.MaxCol));
                window.Session.SelectedRange.Should().Be(wholeBand,
                    "the fixture must start with rows 2:4 selected as a whole-row band");

                InvokeAutoFitRowFromHeader(window, 3);

                window.Session.SelectedRange.Should().Be(wholeBand,
                    "autofitting a row inside a pre-existing multi-row selection must autofit " +
                    "(and leave selected) the WHOLE 2:4 band, not collapse it down to just row 3 first");
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
    public async Task AutoFitRowFromHeader_WithoutMultiRowSelection_NoRegression_StillTargetsJustTheClickedRow()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("AutoFitSingleRowFixture");
                window.Session.SelectSheet(sheet.Id);

                window.Session.SelectCell(new CellAddress(sheet.Id, 9, 9));

                InvokeAutoFitRowFromHeader(window, 3);

                window.Session.SelectedRange.Should().Be(
                    new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, CellAddress.MaxCol)),
                    "without a pre-existing multi-row selection touching the double-clicked row, " +
                    "autofit must still just target that single row (unchanged, pre-existing behavior)");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static void InvokeSelectEntireColumn(MainWindow window, uint col, bool extend) =>
        typeof(MainWindow)
            .GetMethod("SelectEntireColumn", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [col, extend]);

    private static void InvokeSelectEntireRow(MainWindow window, uint row, bool extend) =>
        typeof(MainWindow)
            .GetMethod("SelectEntireRow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [row, extend]);

    private static void InvokeAutoFitColumnFromHeader(MainWindow window, uint col) =>
        typeof(MainWindow)
            .GetMethod("AutoFitColumnFromHeader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [col]);

    private static void InvokeAutoFitRowFromHeader(MainWindow window, uint row) =>
        typeof(MainWindow)
            .GetMethod("AutoFitRowFromHeader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [row]);
}
