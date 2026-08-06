using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;

using FluentAssertions;

using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R163: physical double-click AutoFit must reach the same committed sizing path as WPF for both
/// axes. The long seeded values make a default-width/default-height result an observable failure.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R163_HeaderDoubleClickAutoFitTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task HeaderAutoFit_GrowsLongColumnAndShrinksTallRow()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("HeaderAutoFitFixture");
                window.Session.SelectSheet(sheet.Id);
                sheet.SetCell(
                    new CellAddress(sheet.Id, 1, 1),
                    new TextValue("A deterministic long value proving physical header AutoFit routing"));

                var defaultColumnWidth = sheet.DefaultColumnWidth;
                InvokeAutoFitColumnFromHeader(window, 1);

                sheet.ColumnWidths[1].Should().BeGreaterThan(defaultColumnWidth);

                sheet.RowHeights[2] = sheet.DefaultRowHeight + 24;
                var tallRowHeight = sheet.RowHeights[2];
                InvokeAutoFitRowFromHeader(window, 2);

                sheet.RowHeights[2].Should().BeLessThan(tallRowHeight);
                sheet.RowHeights[2].Should().Be(sheet.DefaultRowHeight);
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HeaderAutoFitAcrossHiddenBoundary_UnhidesAndSizesContiguousSpan()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("HiddenBoundaryAutoFitFixture");
                window.Session.SelectSheet(sheet.Id);
                sheet.SetCell(
                    new CellAddress(sheet.Id, 1, 2),
                    new TextValue("A long value in the first hidden column"));
                sheet.SetCell(
                    new CellAddress(sheet.Id, 1, 3),
                    new TextValue("A longer value in the second hidden column"));
                sheet.HiddenCols.Add(2);
                sheet.HiddenCols.Add(3);

                InvokeAutoFitColumnFromHeader(window, 2);

                sheet.HiddenCols.Should().BeEmpty();
                sheet.ColumnWidths[2].Should().BeGreaterThan(sheet.DefaultColumnWidth);
                sheet.ColumnWidths[3].Should().BeGreaterThan(sheet.DefaultColumnWidth);
                window.Session.SelectedRange.Should().Be(
                    new GridRange(
                        new CellAddress(sheet.Id, 1, 2),
                        new CellAddress(sheet.Id, CellAddress.MaxRow, 3)));
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RowHeaderAutoFitAcrossHiddenBoundary_UnhidesAndSizesContiguousSpan()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("HiddenRowBoundaryAutoFitFixture");
                window.Session.SelectSheet(sheet.Id);
                sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Hidden row value"));
                sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Another hidden row value"));
                sheet.HiddenRows.Add(2);
                sheet.HiddenRows.Add(3);

                InvokeAutoFitRowFromHeader(window, 2);

                sheet.HiddenRows.Should().BeEmpty();
                sheet.RowHeights[2].Should().Be(sheet.DefaultRowHeight);
                sheet.RowHeights[3].Should().Be(sheet.DefaultRowHeight);
                window.Session.SelectedRange.Should().Be(
                    new GridRange(
                        new CellAddress(sheet.Id, 2, 1),
                        new CellAddress(sheet.Id, 3, CellAddress.MaxCol)));
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task VisibleRowResizeHandle_MapsFollowingHiddenRunThroughSharedPlanner()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var sheet = window.Session.Workbook.AddSheet("HiddenBoundaryHandleFixture");
                window.Session.SelectSheet(sheet.Id);
                sheet.HiddenRows.Add(4);
                sheet.HiddenRows.Add(5);

                var target = (uint)typeof(MainWindow)
                    .GetMethod("ResolveRowResizeHandleTarget", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, [3u])!;

                target.Should().Be(4u);
                GridResizePreviewPlanner.GetRowResizeRange(sheet, selectedRange: null, target)
                    .Should().Be((4u, 5u));
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public void HiddenBoundaryFirstPress_DoesNotStartZeroSizeResizeCapture()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var rowHeader = source[
            source.IndexOf("private Control CreateRowHeaderCell(", StringComparison.Ordinal)..
            source.IndexOf("private uint ResolveRowResizeHandleTarget", StringComparison.Ordinal)];
        var rowHandle = source[
            source.IndexOf("private Control AddRowResizeHandle(", StringComparison.Ordinal)..
            source.IndexOf("private static Border CreateHeaderResizeHandle", StringComparison.Ordinal)];

        var parentGuard = rowHeader.IndexOf("else if (resizeRow != row)", StringComparison.Ordinal);
        var hiddenGuard = rowHandle.IndexOf("if (displayedHeight <= 0)", StringComparison.Ordinal);
        var beginResize = rowHandle.IndexOf("BeginHeaderResize(", StringComparison.Ordinal);

        parentGuard.Should().BeGreaterThanOrEqualTo(0);
        rowHeader[parentGuard..rowHeader.IndexOf("BeginHeaderResize(", StringComparison.Ordinal)]
            .Should().Contain("args.Handled = true");
        hiddenGuard.Should().BeGreaterThanOrEqualTo(0);
        beginResize.Should().BeGreaterThan(hiddenGuard,
            "a collapsed hidden-row boundary must not capture a zero-size first click");
        rowHandle[hiddenGuard..beginResize].Should().Contain("args.Handled = true");
        rowHandle[hiddenGuard..beginResize].Should().Contain("return;");
    }

    private static void InvokeAutoFitColumnFromHeader(MainWindow window, uint col) =>
        typeof(MainWindow)
            .GetMethod("AutoFitColumnFromHeader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [col]);

    private static void InvokeAutoFitRowFromHeader(MainWindow window, uint row) =>
        typeof(MainWindow)
            .GetMethod("AutoFitRowFromHeader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [row]);

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
