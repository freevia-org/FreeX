extern alias ProductionAvalonia;

using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.App.Services;
using FreeX.Core.Model;
#if FREEX_GRID_NATIVE_BRIDGE
using Avalonia.Native.UnitTests;
#endif

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class WorksheetGridProviderTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task LookupMatchesEveryLogicalChildAndSelection_WithWpfViewportIndices() => Run(window =>
    {
        var grid = GridPeer(window);
        AssertCartesian(window, grid);
        var provider = grid.GetProvider<IGridProvider>()!;
        foreach (var (row, column) in new[] { (-1, 0), (0, -1), (provider.RowCount, 0), (0, provider.ColumnCount) })
        {
            Action lookup = () => provider.GetItem(row, column);
            lookup.Should().Throw<ArgumentOutOfRangeException>();
        }
    });

    [Fact]
    public Task SplitAxes_UseWpfMainFirstUnionAndActualRenderedCellGeometry() => Run(window =>
    {
        window.Session.SetSplitPanes(2, 2).Success.Should().BeTrue();
        window.Session.SetViewportOrigin(40, 30);
        Refresh(window);
        var grid = GridPeer(window);
        AssertCartesian(window, grid);
        var viewport = window.Session.Viewport;
        var (rows, columns) = ExpectedAxes(viewport);
        rows[0].Should().Be(viewport.RowMetrics[0].Row);
        columns[0].Should().Be(viewport.ColMetrics[0].Col);
        rows.Should().OnlyHaveUniqueItems();
        columns.Should().OnlyHaveUniqueItems();
        // Current production split panes share their bottom/right scroll bands (r56).
        // Use actual unmerged cell Borders as the physical oracle, never the axis helper.
        var borders = window.SheetGridHostForTest.GetVisualDescendants().OfType<WorksheetCellBorder>()
            .ToDictionary(border => border.AutomationCoordinate);
        var provider = grid.GetProvider<IGridProvider>()!;
        foreach (var (coordinate, border) in borders)
        {
            var cell = provider.GetItem(Array.IndexOf(rows, coordinate.Row), Array.IndexOf(columns, coordinate.Column));
            cell.IsOffscreen().Should().BeFalse();
            var root = TopLevel.GetTopLevel(border)!;
            var transform = border.TransformToVisual(root)!.Value;
            var actual = new Rect(border.Bounds.Size).TransformToAABB(transform);
            cell.GetBoundingRectangle().Should().Be(actual);
        }
        Cells(grid).Where(peer => !peer.IsOffscreen()).Select(peer => peer.GetAutomationId()).Should()
            .Equal(rows.SelectMany(row => columns.Select(column => (Row: row, Column: column)))
                .Where(borders.ContainsKey).Select(key => $"Cell_{CellAddress.NumberToColumnName(key.Column)}{key.Row}"));
#if FREEX_GRID_NATIVE_BRIDGE
        using var native = new FreeXGridBridgeGateway(grid);
        native.Verify(provider.GetItem(0, 0));
#endif
    });

    [Fact]
    public Task SyntheticSparseSnapshot_KeepsCartesianLookupWithoutPhantomVisibleCells() => Session.Dispatch(() =>
    {
        // Snapshot-level contract only. Current UI split scroll bands intentionally mirror;
        // this explicitly sparse fixture makes no claim to be an ordinary UI state.
        var host = new WorksheetGridHost();
        var surface = new WorksheetGridSurface { Width = 160, Height = 80 };
        host.Content = surface;
        host.UpdateAutomationViewport(surface, [40, 1], [30, 1], (row, column) => new(
            $"{CellAddress.NumberToColumnName(column)}{row}", "", "", row == 40 && column == 30, false,
            row == 40 && column == 30 ? new Rect(0, 0, 64, 20) : default));
        var border = new WorksheetCellBorder
        {
            Width = 64, Height = 20, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
        };
        border.SetAutomationOwner(host, 40, 30);
        surface.Children.Add(border);
        var window = new Window { Content = host, Width = 200, Height = 100 };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var grid = ControlAutomationPeer.CreatePeerForElement(host);
            var provider = grid.GetProvider<IGridProvider>()!;
            provider.RowCount.Should().Be(2);
            provider.ColumnCount.Should().Be(2);
            var missing = provider.GetItem(1, 1);
            missing.GetAutomationId().Should().Be("Cell_A1");
            missing.GetProvider<IGridItemProvider>()!.Row.Should().Be(1);
            missing.GetBoundingRectangle().Size.Should().Be(default(Size));
            missing.IsOffscreen().Should().BeTrue();
            Cells(grid).Should().HaveCount(4);
            Cells(grid).Count(peer => !peer.IsOffscreen()).Should().Be(1);
#if FREEX_GRID_NATIVE_BRIDGE
            using var native = new FreeXGridBridgeGateway(grid);
            native.Verify(missing);
#endif
            host.CloseAutomationViewport();
        }
        finally
        {
            host.CloseAutomationViewport();
            window.Close();
            Dispatcher.UIThread.RunJobs();
#if FREEX_GRID_NATIVE_BRIDGE
            FreeXGridBridgeGateway.RetireClosedPeers();
#endif
        }
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task ScrollAndHiddenFrozenAxes_PublishCurrentLocationsIncludingEvictedItems() => Run(window =>
    {
        var grid = GridPeer(window);
        var held = Cell(grid, "C3");
        var item = held.GetProvider<IGridItemProvider>()!;
#if FREEX_GRID_NATIVE_BRIDGE
        using var native = new FreeXGridBridgeGateway(grid);
        native.Verify(held);
#endif
        var changes = new List<AutomationProperty>();
        held.PropertyChanged += (_, args) =>
        {
            changes.Add(args.Property);
            if (args.Property == GridItemPatternIdentifiers.RowProperty || args.Property == GridItemPatternIdentifiers.ColumnProperty)
                AssertCartesian(window, grid); // no dispatcher drain: event-time snapshot coherence
        };
        window.Session.ActiveSheet.HiddenRows.Add(2);
        window.Session.ActiveSheet.HiddenCols.Add(2);
        window.Session.SetFreezePanes(1, 1).Success.Should().BeTrue();
        Refresh(window);
        Cell(grid, "C3").Should().BeSameAs(held);
        item.Row.Should().Be(1);
        item.Column.Should().Be(1);
#if FREEX_GRID_NATIVE_BRIDGE
        native.Verify(held);
#endif
        changes.Should().Contain(GridItemPatternIdentifiers.RowProperty).And.Contain(GridItemPatternIdentifiers.ColumnProperty);
        AssertCartesian(window, grid);
        changes.Clear();
        window.Session.SetViewportOrigin(1000, 100);
        Refresh(window);
        item.Row.Should().Be(-1);
        item.Column.Should().Be(-1);
        changes.Should().Contain(GridItemPatternIdentifiers.RowProperty).And.Contain(GridItemPatternIdentifiers.ColumnProperty);
        held.IsOffscreen().Should().BeTrue();
#if FREEX_GRID_NATIVE_BRIDGE
        native.Verify(held);
#endif
        AssertCartesian(window, grid);
    });

    [Fact]
    public Task ResizeAndSameCountScroll_InvalidateCountsLocationsAndNativeOrdering() => Run(window =>
    {
        var grid = GridPeer(window);
        var provider = grid.GetProvider<IGridProvider>()!;
        var held = Cell(grid, "C3");
        var oldRows = provider.RowCount;
        var oldColumns = provider.ColumnCount;
        var changes = new List<AutomationProperty>();
        grid.PropertyChanged += (_, args) => changes.Add(args.Property);
        window.Session.UpdateViewportSize(200, 300);
        window.RefreshShellForTest();
        Drain(window);
        provider.RowCount.Should().BeLessThan(oldRows);
        provider.ColumnCount.Should().BeLessThan(oldColumns);
        changes.Should().Contain(GridPatternIdentifiers.RowCountProperty).And.Contain(GridPatternIdentifiers.ColumnCountProperty);
        window.Session.SetViewportOrigin(2, 2);
        window.RefreshShellForTest();
        Drain(window);
        Cell(grid, "C3").Should().BeSameAs(held);
        held.GetProvider<IGridItemProvider>()!.Row.Should().Be(1);
        held.GetProvider<IGridItemProvider>()!.Column.Should().Be(1);
        AssertCartesian(window, grid);
#if FREEX_GRID_NATIVE_BRIDGE
        using var native = new FreeXGridBridgeGateway(grid);
        native.Verify(held);
#endif
    });

    [Fact]
    public Task LayoutMinimumChanges_UpdatePhysicalBoundsAndNotificationsWithoutSnapshotRebuild() => Run(window =>
    {
        var grid = GridPeer(window);
        var held = Cell(grid, "A1");
        var surface = window.SheetGridHostForTest.GetVisualDescendants().OfType<WorksheetGridSurface>().Single();
        var before = held.GetBoundingRectangle();
        var boundsChanges = 0;
        held.PropertyChanged += (_, args) =>
        {
            if (args.Property == AutomationElementIdentifiers.BoundingRectangleProperty) boundsChanges++;
        };
#if FREEX_GRID_NATIVE_BRIDGE
        using var native = new FreeXGridBridgeGateway(grid);
        native.Verify(held);
#endif
        surface.ColumnDefinitions[0].MinWidth = surface.ColumnDefinitions[0].ActualWidth + 8;
        Drain(window);
        window.SheetGridHostForTest.GetVisualDescendants().OfType<WorksheetGridSurface>().Single().Should().BeSameAs(surface);
        var border = surface.GetVisualDescendants().OfType<WorksheetCellBorder>()
            .Single(border => border.AutomationCoordinate == (1u, 1u));
        var transform = border.TransformToVisual(TopLevel.GetTopLevel(border)!)!.Value;
        var actual = new Rect(border.Bounds.Size).TransformToAABB(transform);
        held.GetBoundingRectangle().Should().Be(actual).And.NotBe(before);
        boundsChanges.Should().BeGreaterThan(0);
        grid.GetProvider<IGridProvider>()!.GetItem(0, 0).Should().BeSameAs(held);
#if FREEX_GRID_NATIVE_BRIDGE
        native.Verify(held);
#endif
    });

    [Fact]
    public Task SheetSwitchUsesAbsoluteCoordinates_NotOldSheetOrViewportOrdinal() => Run(window =>
    {
        var grid = GridPeer(window);
        var held = Cell(grid, "A1");
#if FREEX_GRID_NATIVE_BRIDGE
        using var native = new FreeXGridBridgeGateway(grid);
        native.Verify(held);
#endif
        var next = window.Session.Workbook.AddSheet("Different data and origin");
        next.SetCell(Address(next, 1, 1), new NumberValue(19));
        next.SetCell(Address(next, 40, 30), new NumberValue(42));
        window.Session.SelectSheet(next.Id);
        window.Session.SetViewportOrigin(40, 30);
        Refresh(window);
        held.GetProvider<IGridItemProvider>()!.Row.Should().Be(-1);
        held.GetProvider<IGridItemProvider>()!.Column.Should().Be(-1);
        held.GetProvider<IValueProvider>()!.Value.Should().BeEmpty();
        var first = grid.GetProvider<IGridProvider>()!.GetItem(0, 0);
        first.GetAutomationId().Should().Be("Cell_AD40");
        first.GetProvider<IValueProvider>()!.Value.Should().Be("42");
        first.Should().NotBeSameAs(held);
#if FREEX_GRID_NATIVE_BRIDGE
        native.Verify(held);
        native.Verify(first);
#endif
        window.Session.SetViewportOrigin(1, 1);
        Refresh(window);
        Cell(grid, "A1").Should().BeSameAs(held);
        held.GetProvider<IValueProvider>()!.Value.Should().Be("19");
        held.GetProvider<IGridItemProvider>()!.Row.Should().Be(0);
#if FREEX_GRID_NATIVE_BRIDGE
        native.Verify(held);
#endif
        AssertCartesian(window, grid);
    });

    [Fact]
    public Task MergedLiteralItemsKeepSpanOneLookupFocusAndEditableChildIdentity() => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var anchor = Address(sheet, 1, 1);
        var merge = new GridRange(anchor, Address(sheet, 2, 2));
        sheet.SetCell(anchor, new NumberValue(42));
        sheet.AddMergedRegion(merge);
        window.Session.SelectRanges(merge, [merge], anchor);
        Refresh(window);
        var grid = GridPeer(window);
        var provider = grid.GetProvider<IGridProvider>()!;
        var covered = provider.GetItem(0, 1);
        covered.Should().BeSameAs(Cell(grid, "B1"));
        covered.GetProvider<IValueProvider>()!.Value.Should().BeEmpty();
        covered.GetProvider<IGridItemProvider>()!.RowSpan.Should().Be(1);
        covered.GetProvider<IGridItemProvider>()!.ColumnSpan.Should().Be(1);
        window.ActiveCellBorderForTest!.Focus();
        await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.OemPeriod, KeyModifiers = KeyModifiers.Control });
        Drain(window);
        covered.HasKeyboardFocus().Should().BeTrue();
        await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.F2 });
        Drain(window);
        var editor = Editor(covered);
        editor.GetParent().Should().BeSameAs(covered);
        editor.HasKeyboardFocus().Should().BeTrue();
#if FREEX_GRID_NATIVE_BRIDGE
        using var native = new FreeXGridBridgeGateway(grid);
        native.Verify(covered, editor, focusChild: true);
#endif
        window.RaiseInlineCellEditorKeyDownForTest(new KeyEventArgs { Key = Key.Escape });
        Drain(window);
        provider.GetItem(0, 1).Should().BeSameAs(covered);
        AssertCartesian(window, grid);
    });

    [Fact]
    public Task SharedWorkbookViewsKeepIndependentGridIndicesAndCloseLifecycle() => Run(window =>
    {
        var firstGrid = GridPeer(window);
        var firstCell = Cell(firstGrid, "A1");
        var sibling = window.CreateSharedViewForTest();
        try
        {
            sibling.Show();
            sibling.Measure(new Size(1120, 720));
            sibling.Arrange(new Rect(0, 0, 1120, 720));
            sibling.Session.SetViewportOrigin(40, 30);
            Refresh(sibling);
            var siblingGrid = GridPeer(sibling);
            var siblingCell = siblingGrid.GetProvider<IGridProvider>()!.GetItem(0, 0);
            siblingCell.GetAutomationId().Should().Be("Cell_AD40");
            siblingCell.GetProvider<IGridItemProvider>()!.ContainingGrid.Should().BeSameAs(siblingGrid);
            siblingCell.Should().NotBeSameAs(firstCell);
            firstCell.GetProvider<IGridItemProvider>()!.Row.Should().Be(0);
            firstGrid.GetProvider<IGridProvider>()!.GetItem(0, 0).Should().BeSameAs(firstCell);
            AssertCartesian(window, firstGrid);
            AssertCartesian(sibling, siblingGrid);
#if FREEX_GRID_NATIVE_BRIDGE
            using var firstNative = new FreeXGridBridgeGateway(firstGrid);
            using var siblingNative = new FreeXGridBridgeGateway(siblingGrid);
            firstNative.Verify(firstCell);
            siblingNative.Verify(siblingCell);
#endif
            sibling.AllowCloseWithoutDirtyPromptForParityCapture();
            sibling.Close();
            Drain(window);
            siblingCell.GetProvider<IGridItemProvider>()!.Row.Should().Be(-1);
            siblingGrid.GetProvider<IGridProvider>()!.RowCount.Should().Be(0);
            firstCell.GetProvider<IGridItemProvider>()!.Row.Should().Be(0);
            firstCell.IsEnabled().Should().BeTrue();
#if FREEX_GRID_NATIVE_BRIDGE
            firstNative.Verify(firstCell);
            siblingNative.Verify(siblingCell);
#endif
        }
        finally
        {
            if (sibling.IsVisible)
            {
                sibling.AllowCloseWithoutDirtyPromptForParityCapture();
                sibling.Close();
            }
        }
    });

    [Fact]
    public Task CloseInvalidatesAllIndicesCountsAndRetainedNativeCells() => Run(window =>
    {
        var grid = GridPeer(window);
        var held = Cell(grid, "A1");
#if FREEX_GRID_NATIVE_BRIDGE
        using var native = new FreeXGridBridgeGateway(grid);
        native.Verify(held);
#endif
        window.AllowCloseWithoutDirtyPromptForParityCapture();
        window.Close();
        grid.GetChildren().Should().BeEmpty();
        grid.GetProvider<IGridProvider>()!.RowCount.Should().Be(0);
        Dispatcher.UIThread.RunJobs();
        grid.GetProvider<IGridProvider>()!.RowCount.Should().Be(0);
        grid.GetProvider<IGridProvider>()!.ColumnCount.Should().Be(0);
        held.GetProvider<IGridItemProvider>()!.Row.Should().Be(-1);
        held.GetProvider<IGridItemProvider>()!.Column.Should().Be(-1);
        held.IsEnabled().Should().BeFalse();
#if FREEX_GRID_NATIVE_BRIDGE
        native.Verify(held);
#endif
    });

#if FREEX_GRID_NATIVE_BRIDGE
    [Fact]
    public Task ShippingAssemblyMainWindow_ExposesPackagedGridThroughItsActualRootTree() => Session.Dispatch(() =>
    {
        var qaRoot = Environment.GetEnvironmentVariable("FREEX_GRID_PRODUCTION_QA_ROOT")
            ?? throw new InvalidOperationException("Run this isolated native component case through its QA script.");
        Directory.Exists(qaRoot).Should().BeTrue();
        var originalProduct = Free.Shared.AppServices.AppProduct.Current;
        var originalOptionsPath = Environment.GetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable);
        ProductionAvalonia::FreeX.App.Avalonia.MainWindow? window = null;
        FreeXGridBridgeGateway? native = null;
        try
        {
            Free.Shared.AppServices.AppProduct.Current = new(qaRoot, "FREEX_GRID_QA_DIAGNOSTICS", "FreeX Grid QA");
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, Path.Combine(qaRoot, "options.json"));
            AppOptionsStore.Save(new AppOptions()).Should().BeTrue();
            // Public production constructor; no capture constants/test access/private reflection.
            // No product Application lifetime or autosave coordinator is started.
            window = new ProductionAvalonia::FreeX.App.Avalonia.MainWindow([]);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var windowPeer = ControlAutomationPeer.CreatePeerForElement(window);
            var grid = Descendants(windowPeer).Single(peer => peer.GetProvider<IGridProvider>() is not null);
            Assert.Same(typeof(ProductionAvalonia::FreeX.App.Avalonia.MainWindow).Assembly, grid.GetType().Assembly);
            var provider = grid.GetProvider<IGridProvider>()!;
            provider.RowCount.Should().BeGreaterThan(0);
            provider.ColumnCount.Should().BeGreaterThan(0);
            var first = provider.GetItem(0, 0);
            first.GetParent().Should().BeSameAs(grid);
            native = new FreeXGridBridgeGateway(grid);
            native.Verify(first);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            provider.RowCount.Should().Be(0);
            first.GetProvider<IGridItemProvider>()!.Row.Should().Be(-1);
            native.Verify(first);
        }
        finally
        {
            native?.Dispose();
            if (window?.IsVisible == true) window.Close();
            Dispatcher.UIThread.RunJobs();
            FreeXGridBridgeGateway.RetireClosedPeers();
            Environment.SetEnvironmentVariable(AppOptionsStore.OptionsPathEnvironmentVariable, originalOptionsPath);
            Free.Shared.AppServices.AppProduct.Current = originalProduct;
        }
        return true;
    }, CancellationToken.None);
#endif

#if FREEX_GRID_NATIVE_BRIDGE
    [Fact]
    public Task PackagedNativeFilterActionUsesRealCellParentAndProductionOpenRoute() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Category"));
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B3", null);
        Refresh(window);
        var grid = GridPeer(window);
        var cell = grid.GetProvider<IGridProvider>()!.GetItem(0, 0);
        var button = cell.GetChildren().Single(peer => peer.GetAutomationId() == "AutoFilterButton_1_1");
        using var native = new FreeXGridBridgeGateway(grid);
        native.Verify(cell, button, pressChild: true);
        Drain(window);
        window.AutoFilterFlyoutOpenForTest.Should().BeTrue();
        window.AutoFilterFlyoutPlacementTargetAutomationIdForTest.Should().Be("AutoFilterButton_1_1");
        var focused = ControlAutomationPeer.CreatePeerForElement((Control)window.FocusManager!.GetFocusedElement()!);
        var seen = new HashSet<AutomationPeer>();
        AutomationPeer? cancel = null;
        for (AutomationPeer? current = focused; current is not null && seen.Add(current); current = current.GetParent())
        {
            cancel = Descendants(current).FirstOrDefault(peer => peer.GetAutomationId() == "AutoFilterCancelButton");
            if (cancel is not null) break;
        }
        cancel.Should().NotBeNull();
        cancel!.GetProvider<IInvokeProvider>()!.Invoke();
        Drain(window);
        window.AutoFilterFlyoutOpenForTest.Should().BeFalse();
    });
#endif

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static (uint[] Rows, uint[] Columns) ExpectedAxes(ViewportModel viewport) =>
        (viewport.RowMetrics.Select(metric => metric.Row)
            .Concat(viewport.SplitPanes?.TopRows?.Select(metric => metric.Row) ?? [])
            .Concat(viewport.SplitPanes?.BottomLeftRows?.Select(metric => metric.Row) ?? []).Distinct().ToArray(),
         viewport.ColMetrics.Select(metric => metric.Col)
            .Concat(viewport.SplitPanes?.LeftColumns?.Select(metric => metric.Col) ?? [])
            .Concat(viewport.SplitPanes?.TopRightColumns?.Select(metric => metric.Col) ?? []).Distinct().ToArray());

    private static void AssertCartesian(MainWindow window, AutomationPeer grid)
    {
        var provider = grid.GetProvider<IGridProvider>();
        provider.Should().NotBeNull();
        var (rows, columns) = ExpectedAxes(window.Session.Viewport);
        provider!.RowCount.Should().Be(rows.Length);
        provider.ColumnCount.Should().Be(columns.Length);
        var children = Cells(grid).ToArray();
        children.Should().HaveCount(rows.Length * columns.Length);
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < columns.Length; column++)
            {
                var peer = provider.GetItem(row, column);
                peer.Should().BeSameAs(children[row * columns.Length + column]);
                peer.GetAutomationId().Should().Be($"Cell_{CellAddress.NumberToColumnName(columns[column])}{rows[row]}");
                var item = peer.GetProvider<IGridItemProvider>()!;
                item.Row.Should().Be(row);
                item.Column.Should().Be(column);
                item.RowSpan.Should().Be(1);
                item.ColumnSpan.Should().Be(1);
                item.ContainingGrid.Should().BeSameAs(grid);
                peer.GetParent().Should().BeSameAs(grid);
            }
        grid.GetProvider<ISelectionProvider>()!.GetSelection().Should().Equal(children.Where(peer => peer.GetProvider<ISelectionItemProvider>()!.IsSelected));
    }

    private static AutomationPeer FocusedPeer(MainWindow window)
    {
        var focused = window.FocusManager!.GetFocusedElement();
        focused.Should().BeAssignableTo<Control>();
        return ControlAutomationPeer.CreatePeerForElement((Control)focused!);
    }

    private static WorksheetGridHost GridHost(MainWindow window) => (WorksheetGridHost)window.SheetGridHostForTest;

    private static AutomationPeer GridPeer(MainWindow window) =>
        ControlAutomationPeer.CreatePeerForElement(window.SheetGridHostForTest);

    private static AutomationPeer Cell(AutomationPeer grid, string address) =>
        Cells(grid).Single(peer => peer.GetAutomationId() == $"Cell_{address}");

    private static IEnumerable<AutomationPeer> Cells(AutomationPeer grid) =>
        grid.GetChildren().Where(peer => peer.GetAutomationControlType() == AutomationControlType.DataItem);

    private static AutomationPeer Editor(AutomationPeer cell) =>
        cell.GetChildren().Single(peer => peer.GetAutomationControlType() == AutomationControlType.Edit);

    private static CellAddress Address(Sheet sheet, uint row, uint column) => new(sheet.Id, row, column);

    private static void Refresh(MainWindow window)
    {
        // Direct model mutations do not refresh WorkbookSession.Viewport. A repeated identical
        // size is a no-op, so force reflow before rebuilding the real window at the fixed size.
        window.Session.UpdateViewportSize(482, 882);
        window.Session.UpdateViewportSize(481, 881);
        window.RefreshShellForTest();
        Drain(window);
    }

    private static void Drain(MainWindow window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static Task Run(Action<MainWindow> test) => Run(window =>
    {
        test(window);
        return Task.CompletedTask;
    });

    private static Task Run(Func<MainWindow, Task> test) => Session.Dispatch(async () =>
    {
        var options = new AppOptions { ShowFormulaBar = true };
        var optionsSession = new FreeXOptionsRuntimeSession(
            initialOptions: options, load: () => options.Clone(), save: _ => true);
        var window = new MainWindow([], null!, optionsSession);
        try
        {
            var sheet = window.Session.Workbook.AddSheet("Accessibility identity");
            window.Session.SelectSheet(sheet.Id);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            Refresh(window);
            await test(window);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            Dispatcher.UIThread.RunJobs();
#if FREEX_GRID_NATIVE_BRIDGE
            FreeXGridBridgeGateway.RetireClosedPeers();
#endif
        }
        return true;
    }, CancellationToken.None);
}
