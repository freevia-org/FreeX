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
