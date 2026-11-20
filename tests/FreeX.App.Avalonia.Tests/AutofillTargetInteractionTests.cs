using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AutofillTargetInteractionTests
{
    private const string PreviewId = "WorksheetAutofillPreview";

    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(100, false)]
    [InlineData(100, true)]
    [InlineData(135, false)]
    [InlineData(135, true)]
    public Task FillIncludesAnotherRowOnlyAfterItsMidpoint(int zoomPercent, bool crossesMidpoint) =>
        WithWorksheet(async (window, sheet) =>
        {
            var source = new CellAddress(sheet.Id, 2, 2);
            var first = new CellAddress(sheet.Id, 3, source.Col);
            var second = new CellAddress(sheet.Id, 4, source.Col);
            sheet.SetFormula(source, "A2+1");
            window.Session.SelectCell(source);
            await RefreshAsync(window);

            BeginFillDrag(window);
            window.MouseMove(CellPoint(window, first, 0.75), RawInputModifiers.LeftMouseButton);
            await DrainInputAsync();
            AssertPreviewEndsAt(window, first);

            var release = CellPoint(window, second, crossesMidpoint ? 0.75 : 0.25);
            window.MouseMove(release, RawInputModifiers.LeftMouseButton);
            await DrainInputAsync();

            var target = crossesMidpoint ? second : first;
            AssertPreviewEndsAt(window, target);
            await ReleaseAsync(window, release);

            sheet.GetCell(first)!.FormulaText.Should().Be("A3+1");
            (sheet.GetCell(second)?.FormulaText).Should().Be(crossesMidpoint ? "A4+1" : null);
            window.Session.SelectedRange.Should().Be(new GridRange(source, target));
        }, zoomPercent);

    [Fact]
    public Task DragBelowWorksheetFillsThroughLastRowOfNewViewportWhenSourceIsFrozen() =>
        WithWorksheet(async (window, sheet) =>
        {
            var source = new CellAddress(sheet.Id, 1, 2);
            window.Session.SetFreezePanes(frozenRows: 1, frozenCols: 0).Success.Should().BeTrue();
            sheet.SetFormula(source, "A1+1");
            window.Session.SelectCell(source);
            await RefreshAsync(window);

            var originalOrigin = window.Session.ViewportOrigin.TopRow;
            var originalLastRow = window.Session.Viewport.RowMetrics[^1].Row;
            var outside = PointBelowWorksheet(window, source.Col);
            BeginFillDrag(window);
            window.MouseMove(outside, RawInputModifiers.LeftMouseButton);
            await DrainInputAsync();

            window.Session.ViewportOrigin.TopRow.Should().BeGreaterThan(originalOrigin);
            window.Session.Viewport.RowMetrics.Should().Contain(row => row.Row == source.Row);
            var newLastRow = window.Session.Viewport.RowMetrics[^1].Row;
            newLastRow.Should().BeGreaterThan(originalLastRow);
            var target = new CellAddress(sheet.Id, newLastRow, source.Col);
            AssertPreviewEndsAt(window, target);
            await ReleaseAsync(window, outside);

            sheet.GetCell(target)!.FormulaText.Should().Be($"A{newLastRow}+1");
            sheet.GetCell(new CellAddress(sheet.Id, newLastRow + 1, source.Col))?.FormulaText.Should().BeNull();
            window.Session.SelectedRange.Should().Be(new GridRange(source, target));
        });

    [Fact]
    public Task DragRetainsLastTargetWhenScrollingHidesTheStartOfTheFillSource() =>
        WithWorksheet(async (window, sheet) =>
        {
            var sourceStart = new CellAddress(sheet.Id, 1, 2);
            var sourceEnd = new CellAddress(sheet.Id, 2, 2);
            var target = new CellAddress(sheet.Id, 3, 2);
            sheet.SetFormula(sourceStart, "A1+1");
            sheet.SetFormula(sourceEnd, "A2+1");
            window.Session.SelectRange(new GridRange(sourceStart, sourceEnd));
            await RefreshAsync(window);

            BeginFillDrag(window);
            window.MouseMove(CellPoint(window, target, 0.75), RawInputModifiers.LeftMouseButton);
            await DrainInputAsync();
            AssertPreviewEndsAt(window, target);

            var outside = PointBelowWorksheet(window, target.Col);
            window.MouseMove(outside, RawInputModifiers.LeftMouseButton);
            await DrainInputAsync();

            window.Session.Viewport.RowMetrics.Should().NotContain(row => row.Row == sourceStart.Row);
            window.Session.Viewport.RowMetrics.Should().Contain(row => row.Row == sourceEnd.Row);
            AssertPreviewEndsAt(window, target);
            await ReleaseAsync(window, outside);

            sheet.GetCell(sourceEnd)!.FormulaText.Should().Be("A2+1");
            sheet.GetCell(target)!.FormulaText.Should().Be("A3+1");
            sheet.GetCell(new CellAddress(sheet.Id, target.Row + 1, target.Col))?.FormulaText.Should().BeNull();
            window.Session.SelectedRange.Should().Be(new GridRange(sourceStart, target));
        });

    [Fact]
    public Task DragInsideLinearSourceClearsOnlyTheTail() =>
        WithWorksheet(async (window, sheet) =>
        {
            var start = new CellAddress(sheet.Id, 2, 2);
            var retainedEnd = new CellAddress(sheet.Id, 3, 2);
            var oldEnd = new CellAddress(sheet.Id, 4, 2);
            sheet.SetFormula(start, "A2+1");
            sheet.SetFormula(retainedEnd, "A3+1");
            sheet.SetFormula(oldEnd, "A4+1");
            window.Session.SelectRange(new GridRange(start, oldEnd));
            await RefreshAsync(window);

            BeginFillDrag(window);
            var pointer = CellPoint(window, retainedEnd, 0.5);
            window.MouseMove(pointer, RawInputModifiers.LeftMouseButton);
            await DrainInputAsync();
            await ReleaseAsync(window, pointer);

            (sheet.GetCell(start)?.FormulaText).Should().Be("A2+1");
            (sheet.GetCell(retainedEnd)?.FormulaText).Should().Be("A3+1");
            sheet.GetCell(oldEnd)?.FormulaText.Should().BeNull();
            window.Session.SelectedRange.Should().Be(new GridRange(start, retainedEnd));
        });

    private static Task WithWorksheet(Func<MainWindow, Sheet, Task> test, int zoomPercent = 100) =>
        Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var optionsSession = (FreeXOptionsRuntimeSession)typeof(MainWindow)
                .GetField("_optionsRuntimeSession", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;
            var options = optionsSession.LiveOptions;
            var originalFillHandleEnabled = options.EnableFillHandleAndCellDragAndDrop;
            try
            {
                options.EnableFillHandleAndCellDragAndDrop = true;
                var sheet = window.Session.Workbook.AddSheet("AutofillTargetFixture");
                window.Session.SelectSheet(sheet.Id);
                window.Session.SetZoomPercent(zoomPercent).Success.Should().BeTrue();
                window.Show();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                await test(window, sheet);
            }
            finally
            {
                options.EnableFillHandleAndCellDragAndDrop = originalFillHandleEnabled;
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);

    private static void BeginFillDrag(MainWindow window)
    {
        var handle = FindById<Border>(window, "WorksheetAutofillHandle");
        // The square straddles the cell corner. Start in the portion inside the source cell.
        var point = handle.TranslatePoint(new Point(handle.Bounds.Width / 4, handle.Bounds.Height / 4), window)!.Value;
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
    }

    private static async Task ReleaseAsync(MainWindow window, Point point)
    {
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        await DrainInputAsync();
        window.GetVisualDescendants().OfType<Control>()
            .Should().NotContain(control => AutomationProperties.GetAutomationId(control) == PreviewId);
    }

    private static Point PointBelowWorksheet(MainWindow window, uint column)
    {
        var host = window.SheetGridHostForTest;
        var hostBottom = host.TranslatePoint(new Point(0, host.Bounds.Height), window)!.Value.Y;
        var viewport = host.GetVisualAncestors().OfType<ScrollViewer>().First();
        var viewportBottom = viewport.TranslatePoint(new Point(0, viewport.Bounds.Height), window)!.Value.Y;
        var renderedLastRow = window.Session.Viewport.RowMetrics[^1].Row;
        var lastCellBottom = CellPoint(window, new CellAddress(window.Session.ActiveSheet.Id, renderedLastRow, column), 1);
        return new Point(lastCellBottom.X, Math.Max(Math.Max(hostBottom, viewportBottom), lastCellBottom.Y) + 1);
    }

    private static Point CellPoint(MainWindow window, CellAddress address, double verticalFraction)
    {
        var cell = FindCell(window, address);
        return cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height * verticalFraction), window)!.Value;
    }

    private static void AssertPreviewEndsAt(MainWindow window, CellAddress target)
    {
        var preview = FindById<Control>(window, PreviewId);
        var targetCell = FindCell(window, target);
        preview.IsVisible.Should().BeTrue();
        (Grid.GetRow(preview) + Grid.GetRowSpan(preview) - 1).Should().Be(Grid.GetRow(targetCell));
        (Grid.GetColumn(preview) + Grid.GetColumnSpan(preview) - 1).Should().Be(Grid.GetColumn(targetCell));
    }

    private static Border FindCell(MainWindow window, CellAddress address) =>
        FindById<Border>(window, $"Cell_{CellAddress.NumberToColumnName(address.Col)}{address.Row}");

    private static T FindById<T>(MainWindow window, string automationId) where T : Control =>
        window.GetVisualDescendants().OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == automationId);

    private static Task RefreshAsync(MainWindow window)
    {
        typeof(MainWindow).GetMethod("RefreshShell", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, ["Ready"]);
        return DrainInputAsync();
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
