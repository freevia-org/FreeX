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
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class WorksheetOverlayAutomationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task Chartsheet_ExposesNamedHostAndRenderedChartThroughTheWorksheetPeer() => Run(window =>
    {
        var source = window.Session.ActiveSheet;
        var chart = AddChart(source, "Accessible chartsheet title");
        const string chartsheetName = "Accessible chart sheet";
        window.Session.ExecuteReviewCommand(new MoveChartToNewSheetCommand(source.Id, chart.Id, chartsheetName))
            .Success.Should().BeTrue();
        var chartsheet = window.Session.Workbook.Sheets.Single(sheet => sheet.Name == chartsheetName);
        window.Session.SelectSheet(chartsheet.Id);
        Refresh(window);

        var grid = GridPeer(window);
        var tree = Descendants(grid).ToArray();
        var host = tree.Single(peer => peer.GetAutomationId() == "ChartsheetView");
        host.GetName().Should().Be(chartsheetName);
        var title = Descendants(host).Single(peer => peer.GetName() == "Accessible chartsheet title");
        title.GetAutomationControlType().Should().Be(AutomationControlType.Text);
        tree.Should().NotContain(peer => peer.GetAutomationControlType() == AutomationControlType.DataItem,
            "a chartsheet must not retain the preceding worksheet's coordinate peers");
        AssertReachesRoot(host, grid);
        AssertReachesRoot(title, grid);
        AssertReachesRoot(title, ControlAutomationPeer.CreatePeerForElement(window));
    });

    [Fact]
    public Task EmbeddedPivotChart_ExposesChartAndFieldButtonsWithConnectedParents() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        var chart = AddChart(sheet, "Accessible pivot chart");
        var pivot = new PivotTableModel
        {
            Name = "AccessiblePivot",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 3, 2),
            TargetRange = Range(sheet, 1, 5, 4, 6),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        chart.IsPivotChart = true;
        chart.PivotTableName = pivot.Name;
        chart.ShowPivotChartFieldButtons = true;
        chart.ShowPivotChartAxisFieldButtons = true;
        chart.ShowPivotChartValueFieldButtons = true;
        Refresh(window);

        var grid = GridPeer(window);
        var tree = Descendants(grid).ToArray();
        var chartPeer = tree.Single(peer => peer.GetAutomationId() == $"Chart{chart.Id:N}");
        chartPeer.GetName().Should().NotBeNullOrWhiteSpace();
        var windowPeer = ControlAutomationPeer.CreatePeerForElement(window);
        AssertReachesRoot(chartPeer, grid);
        AssertReachesRoot(chartPeer, windowPeer);
        foreach (var id in new[] { "PivotChartFieldButton_AxisFields", "PivotChartFieldButton_Values" })
        {
            var button = tree.Single(peer => peer.GetAutomationId() == id);
            button.GetAutomationControlType().Should().Be(AutomationControlType.Button);
            button.GetName().Should().NotBeNullOrWhiteSpace();
            button.GetProvider<IInvokeProvider>().Should().NotBeNull();
            AssertReachesRoot(button, chartPeer);
            AssertReachesRoot(button, grid);
            AssertReachesRoot(button, windowPeer);
        }
        AssertUniqueLogicalCells(window, grid);
    });

    [Fact]
    public Task WorksheetSurface_PreservesHeadersAndOverlaysWithoutDuplicatingCellsOrEditors() => Run(async window =>
    {
        var sheet = window.Session.ActiveSheet;
        var chart = AddChart(sheet, "Visible overlay chart");
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "AccessibleTable",
            Range = Range(sheet, 1, 1, 3, 2),
            HasAutoFilter = true,
        });
        var editAddress = new CellAddress(sheet.Id, 5, 3);
        sheet.SetCell(editAddress, new NumberValue(5));
        window.Session.SelectCell(editAddress);
        Refresh(window);
        window.SheetGridHostForTest.GetVisualDescendants().OfType<Button>()
            .Should().Contain(button => AutomationProperties.GetAutomationId(button) == "AutoFilterButton_1_1",
                "the fixture must exercise decorated filter-header cell borders");
        var grid = GridPeer(window);
        AssertUniqueLogicalCells(window, grid);
        Descendants(grid).Should().Contain(peer => peer.GetAutomationId() == $"Chart{chart.Id:N}");
        window.ActiveCellBorderForTest!.Focus().Should().BeTrue();

        await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.F2 });
        Drain(window);

        AssertUniqueLogicalCells(window, grid);
        var tree = Descendants(grid).ToArray();
        var cell = tree.Single(peer => peer.GetAutomationId() == "Cell_C5");
        var editor = tree.Single(peer => peer.GetAutomationId() == "WorksheetInlineCellEditor");
        editor.GetAutomationControlType().Should().Be(AutomationControlType.Edit);
        editor.GetParent().Should().BeSameAs(cell);
        editor.HasKeyboardFocus().Should().BeTrue();
        editor.GetProvider<IValueProvider>()!.Value.Should().Be("5");
        tree.Should().Contain(peer => peer.GetAutomationId() == $"Chart{chart.Id:N}");
        AssertReachesRoot(editor, grid);
        AssertReachesRoot(editor, ControlAutomationPeer.CreatePeerForElement(window));
        window.RaiseInlineCellEditorKeyDownForTest(new KeyEventArgs { Key = Key.Escape });
        Drain(window);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AutoFilterButtons_AreDiscoveredAndInvokedThroughTheirLogicalCellPeers(bool useTable) => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        AddChart(sheet, "Filter accessibility chart");
        if (useTable)
        {
            sheet.StructuredTables.Add(new StructuredTableModel
            {
                Id = 1,
                Name = "AccessibleFilterTable",
                Range = Range(sheet, 1, 1, 3, 2),
                HasAutoFilter = true,
            });
        }
        else
        {
            sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B3", null);
        }
        Refresh(window);

        var grid = GridPeer(window);
        var windowPeer = ControlAutomationPeer.CreatePeerForElement(window);
        var firstCell = Descendants(grid).Single(peer => peer.GetAutomationId() == "Cell_A1");
        foreach (var (cellId, buttonId) in new[]
        {
            ("Cell_A1", "AutoFilterButton_1_1"), ("Cell_B1", "AutoFilterButton_1_2"),
        })
        {
            var cell = Descendants(grid).Single(peer => peer.GetAutomationId() == cellId);
            var button = cell.GetChildren().Single(peer => peer.GetAutomationId() == buttonId);
            button.GetAutomationControlType().Should().Be(AutomationControlType.Button);
            button.GetName().Should().NotBeNullOrWhiteSpace();
            button.GetParent().Should().BeSameAs(cell);
            Descendants(windowPeer).Single(peer => peer.GetAutomationId() == buttonId).Should().BeSameAs(button);
            AssertReachesRoot(button, grid);
            AssertReachesRoot(button, windowPeer);
            AssertUniqueLogicalCells(window, grid);
            var invoke = button.GetProvider<IInvokeProvider>();
            invoke.Should().NotBeNull();

            invoke!.Invoke();
            Drain(window);

            window.AutoFilterFlyoutOpenForTest.Should().BeTrue();
            window.AutoFilterFlyoutPlacementTargetAutomationIdForTest.Should().Be(buttonId);
            CloseFilterThroughAutomation(window);
            window.AutoFilterFlyoutOpenForTest.Should().BeFalse();
        }

        var originalButton = firstCell.GetChildren().Single(peer => peer.GetAutomationId() == "AutoFilterButton_1_1");
        var childChanges = 0;
        firstCell.ChildrenChanged += (_, _) => childChanges++;
        Refresh(window);
        Descendants(grid).Single(peer => peer.GetAutomationId() == "Cell_A1").Should().BeSameAs(firstCell);
        var currentButton = firstCell.GetChildren().Single(peer => peer.GetAutomationId() == "AutoFilterButton_1_1");
        currentButton.Should().NotBeSameAs(originalButton);
        currentButton.GetParent().Should().BeSameAs(firstCell);
        Descendants(grid).Count(peer => peer.GetAutomationId() == "AutoFilterButton_1_1").Should().Be(1);
        childChanges.Should().BeGreaterThan(0);
        currentButton.GetProvider<IInvokeProvider>()!.Invoke();
        Drain(window);
        window.AutoFilterFlyoutOpenForTest.Should().BeTrue();
        CloseFilterThroughAutomation(window);

        window.Session.SetViewportOrigin(1000, 100);
        Refresh(window);
        firstCell.GetChildren().Should().BeEmpty();
        Descendants(grid).Should().NotContain(peer => peer.GetAutomationId() != null &&
            peer.GetAutomationId()!.StartsWith("AutoFilterButton_", StringComparison.Ordinal));
        window.Session.SetViewportOrigin(1, 1);
        Refresh(window);
        Descendants(grid).Single(peer => peer.GetAutomationId() == "Cell_A1").Should().BeSameAs(firstCell);
        var restoredButton = firstCell.GetChildren().Single(peer => peer.GetAutomationId() == "AutoFilterButton_1_1");
        restoredButton.GetParent().Should().BeSameAs(firstCell);
        Descendants(grid).Count(peer => peer.GetAutomationId() == "AutoFilterButton_1_1").Should().Be(1);
        AssertUniqueLogicalCells(window, grid);
    });

    [Fact]
    public Task PivotDropdowns_AreDiscoveredAndInvokeTheCorrectFieldsAcrossRefreshAndScroll() => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        var pivot = AddHeaderPivot(window, sheet);
        Refresh(window);
        var targets = PivotGridAdornmentPlanner.BuildHeaderTargets(window.Session.Workbook, sheet)
            .Where(target => target.MenuTarget.PivotTableName == pivot.Name).ToArray();
        targets.Select(target => target.MenuTarget.FieldCaption).Should().Contain("Category").And.Contain("Quarter");
        var grid = GridPeer(window);
        var windowPeer = ControlAutomationPeer.CreatePeerForElement(window);
        foreach (var target in targets)
        {
            var address = target.HeaderCell;
            var cellId = $"Cell_{CellAddress.NumberToColumnName(address.Col)}{address.Row}";
            var actionId = $"PivotDropdown_{address.Row}_{address.Col}";
            var cell = Descendants(grid).Single(peer => peer.GetAutomationId() == cellId);
            var action = cell.GetChildren().Single(peer => peer.GetAutomationId() == actionId);
            action.GetAutomationControlType().Should().Be(AutomationControlType.Button);
            action.GetName().Should().NotBeNullOrWhiteSpace();
            action.GetParent().Should().BeSameAs(cell);
            Descendants(windowPeer).Single(peer => peer.GetAutomationId() == actionId).Should().BeSameAs(action);
            AssertReachesRoot(action, grid);
            AssertReachesRoot(action, windowPeer);
            action.GetProvider<IInvokeProvider>().Should().NotBeNull();
            action.SetFocus();
            action.GetProvider<IInvokeProvider>()!.Invoke();
            Drain(window);
            var menu = AssertOpenPivotMenu(window, target.MenuTarget.FieldCaption);
            ClosePivotMenu(window, menu);

            Refresh(window);
            var refreshedCell = Descendants(grid).Single(peer => peer.GetAutomationId() == cellId);
            refreshedCell.Should().BeSameAs(cell);
            var refreshedAction = refreshedCell.GetChildren().Single(peer => peer.GetAutomationId() == actionId);
            refreshedAction.Should().NotBeSameAs(action);
            refreshedAction.GetParent().Should().BeSameAs(cell);
            Descendants(grid).Count(peer => peer.GetAutomationId() == actionId).Should().Be(1);
            refreshedAction.GetProvider<IInvokeProvider>()!.Invoke();
            Drain(window);
            ClosePivotMenu(window, AssertOpenPivotMenu(window, target.MenuTarget.FieldCaption));
        }
        AssertUniqueLogicalCells(window, grid);

        var firstTarget = targets[0];
        var heldCellId = $"Cell_{CellAddress.NumberToColumnName(firstTarget.HeaderCell.Col)}{firstTarget.HeaderCell.Row}";
        var heldCell = Descendants(grid).Single(peer => peer.GetAutomationId() == heldCellId);
        window.Session.SetViewportOrigin(1000, 100);
        Refresh(window);
        heldCell.GetChildren().Should().BeEmpty();
        Descendants(grid).Should().NotContain(peer => peer.GetAutomationId() != null &&
            peer.GetAutomationId()!.StartsWith("PivotDropdown_", StringComparison.Ordinal));
        window.Session.SetViewportOrigin(1, 1);
        Refresh(window);
        foreach (var target in targets)
        {
            var cellId = $"Cell_{CellAddress.NumberToColumnName(target.HeaderCell.Col)}{target.HeaderCell.Row}";
            var actionId = $"PivotDropdown_{target.HeaderCell.Row}_{target.HeaderCell.Col}";
            var restoredCell = Descendants(grid).Single(peer => peer.GetAutomationId() == cellId);
            restoredCell.GetChildren().Single(peer => peer.GetAutomationId() == actionId)
                .GetParent().Should().BeSameAs(restoredCell);
            Descendants(grid).Count(peer => peer.GetAutomationId() == actionId).Should().Be(1);
        }
        AssertUniqueLogicalCells(window, grid);
    });

    [Theory]
    [InlineData(Key.Enter, PhysicalKey.Enter)]
    [InlineData(Key.Space, PhysicalKey.Space)]
    public Task FocusedPivotDropdown_PhysicalActivationOpensFieldMenuWithoutNavigatingWorksheet(
        Key key, PhysicalKey physicalKey) => Run(window =>
    {
        var sheet = window.Session.ActiveSheet;
        var pivot = AddHeaderPivot(window, sheet);
        Refresh(window);
        var target = PivotGridAdornmentPlanner.BuildHeaderTargets(window.Session.Workbook, sheet)
            .Single(candidate => candidate.MenuTarget.PivotTableName == pivot.Name && candidate.MenuTarget.FieldCaption == "Category");
        var actionId = $"PivotDropdown_{target.HeaderCell.Row}_{target.HeaderCell.Col}";
        var action = Descendants(GridPeer(window)).Single(peer => peer.GetAutomationId() == actionId);
        var active = window.Session.ActiveCell;
        var selection = window.Session.SelectedRange;
        action.SetFocus();
        action.HasKeyboardFocus().Should().BeTrue();

        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
        Drain(window);

        var menu = AssertOpenPivotMenu(window, "Category");
        window.Session.ActiveCell.Should().Be(active);
        window.Session.SelectedRange.Should().Be(selection);
        ClosePivotMenu(window, menu);
    });

    private static PivotTableModel AddHeaderPivot(MainWindow window, Sheet sheet)
    {
        foreach (var (row, category, quarter, amount) in new[]
        {
            (2u, "A", "Q1", 10d), (3u, "A", "Q2", 20d), (4u, "B", "Q1", 30d),
        })
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(category));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(quarter));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(amount));
        }
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Amount"));
        var pivot = new PivotTableModel
        {
            Name = "AccessibleHeaderPivot",
            CacheId = 1,
            SourceRange = Range(sheet, 1, 1, 4, 3),
            TargetRange = Range(sheet, 3, 5, 8, 8),
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(1));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(window.Session.Workbook, sheet, pivot);
        return pivot;
    }

    private static ContextMenu AssertOpenPivotMenu(MainWindow window, string fieldCaption)
    {
        var focused = window.FocusManager!.GetFocusedElement();
        focused.Should().BeAssignableTo<Control>();
        var focusedControl = (Control)focused!;
        var menu = focusedControl as ContextMenu ?? focusedControl.GetVisualAncestors().OfType<ContextMenu>().FirstOrDefault();
        menu.Should().NotBeNull("the invoked dropdown must focus its real context menu");
        menu!.IsOpen.Should().BeTrue();
        var menuPeer = ControlAutomationPeer.CreatePeerForElement(menu);
        Descendants(menuPeer).Should().Contain(peer => peer.GetName() == $"No active filters for {fieldCaption}.");
        return menu;
    }

    private static void ClosePivotMenu(MainWindow window, ContextMenu menu)
    {
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Drain(window);
        menu.IsOpen.Should().BeFalse();
    }

    private static void CloseFilterThroughAutomation(MainWindow window)
    {
        var focused = window.FocusManager!.GetFocusedElement();
        focused.Should().BeAssignableTo<Control>();
        var focusPeer = ControlAutomationPeer.CreatePeerForElement((Control)focused!);
        var seen = new HashSet<AutomationPeer>();
        for (AutomationPeer? current = focusPeer; current is not null && seen.Add(current); current = current.GetParent())
        {
            var cancel = Descendants(current).FirstOrDefault(peer => peer.GetAutomationId() == "AutoFilterCancelButton");
            if (cancel is null)
                continue;
            cancel.GetProvider<IInvokeProvider>().Should().NotBeNull();
            cancel.GetProvider<IInvokeProvider>()!.Invoke();
            Drain(window);
            return;
        }
        throw new InvalidOperationException("The opened filter's focused accessibility tree did not expose its Cancel action.");
    }

    private static ChartModel AddChart(Sheet sheet, string title)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            Title = title,
            DataRange = Range(sheet, 1, 1, 3, 2),
            Left = 160,
            Top = 90,
            Width = 380,
            Height = 260,
            ShowLegend = false,
        };
        sheet.Charts.Add(chart);
        return chart;
    }

    private static void AssertUniqueLogicalCells(MainWindow window, AutomationPeer grid)
    {
        var cells = Descendants(grid)
            .Where(peer => peer.GetAutomationControlType() == AutomationControlType.DataItem).ToArray();
        var viewport = window.Session.Viewport;
        cells.Length.Should().Be(viewport.RowMetrics.Count * viewport.ColMetrics.Count,
            "each viewport coordinate must appear exactly once despite the additional visual branches");
        cells.Select(peer => peer.GetAutomationId()).Should().OnlyHaveUniqueItems();
        foreach (var cell in cells)
            cell.GetParent().Should().BeSameAs(grid);
    }

    private static void AssertReachesRoot(AutomationPeer leaf, AutomationPeer root)
    {
        var seen = new HashSet<AutomationPeer>();
        for (AutomationPeer? current = leaf; current is not null && seen.Count < 64; current = current.GetParent())
        {
            if (ReferenceEquals(current, root))
                return;
            if (!seen.Add(current))
                break;
        }
        seen.Should().Contain(root, $"the parent chain of '{leaf.GetAutomationId()}' must reach the requested root without a cycle");
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static AutomationPeer GridPeer(MainWindow window) =>
        ControlAutomationPeer.CreatePeerForElement(window.SheetGridHostForTest);

    private static GridRange Range(Sheet sheet, uint top, uint left, uint bottom, uint right) =>
        new(new CellAddress(sheet.Id, top, left), new CellAddress(sheet.Id, bottom, right));

    private static void Refresh(MainWindow window)
    {
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
            var sheet = window.Session.Workbook.AddSheet("Overlay accessibility");
            window.Session.SelectSheet(sheet.Id);
            window.Show();
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
            Refresh(window);
            await test(window);
        }
        finally
        {
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }
        return true;
    }, CancellationToken.None);
}
