using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression tests for review group S-name-nav findings K24/K25/K38:
///   K24 — Name Box navigation that activates another sheet must refresh the sheet-tab strip's
///         active-tab state (RefreshSheetTabs), matching every other cross-sheet navigation path.
///   K25 — Name Box / Go To reference resolution must resolve sheet-scoped defined names (not
///         just workbook-global names), with sheet-scope-first precedence, matching formula
///         evaluation (Workbook.TryGetNamedRange(name, contextSheetId, ...)).
///   K38 — The Name Box dropdown list must include sheet-scoped names for the active sheet.
/// </summary>
public sealed class NameBoxSheetScopedNavigationTests
{
    [Fact]
    public void NameBoxEnter_ToNameOnAnotherSheet_RefreshesSheetTabActiveState()
    {
        StaTestRunner.Run(() =>
        {
        using var harness = MainWindowHarness.Create();
        var sheet2 = harness.Workbook.AddSheet("Sheet2");
        var targetRange = new GridRange(new CellAddress(sheet2.Id, 2, 2), new CellAddress(sheet2.Id, 2, 2));
        harness.Workbook.DefineNamedRange("OtherSheetName", targetRange);
        harness.RefreshSheetTabs();

        harness.ActiveSheetTabId.Should().Be(harness.Workbook.Sheets[0].Id);

        harness.SetCellAddressBoxText("OtherSheetName");
        harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

        harness.CurrentSheetId.Should().Be(sheet2.Id);
        harness.ActiveSheetTabId.Should().Be(sheet2.Id,
            "Name Box navigation to a name on another sheet must refresh the sheet-tab strip's active tab");
        });
    }

    [Fact]
    public void NameBoxSelectionChanged_ToNameOnAnotherSheet_RefreshesSheetTabActiveState()
    {
        StaTestRunner.Run(() =>
        {
        using var harness = MainWindowHarness.Create();
        var sheet2 = harness.Workbook.AddSheet("Sheet2");
        var targetRange = new GridRange(new CellAddress(sheet2.Id, 3, 3), new CellAddress(sheet2.Id, 3, 3));
        harness.Workbook.DefineNamedRange("PickedName", targetRange);
        harness.RefreshSheetTabs();

        harness.SelectCellAddressBoxDropdownItem("PickedName");

        harness.CurrentSheetId.Should().Be(sheet2.Id);
        harness.ActiveSheetTabId.Should().Be(sheet2.Id,
            "selecting a Name Box dropdown entry on another sheet must refresh the sheet-tab strip's active tab");
        });
    }

    [Fact]
    public void NameBoxEnter_WithSheetScopedNameOnActiveSheet_NavigatesToScopedRange()
    {
        StaTestRunner.Run(() =>
        {
        using var harness = MainWindowHarness.Create();
        var sheet1 = harness.Workbook.Sheets[0];
        var scopedRange = new GridRange(new CellAddress(sheet1.Id, 6, 2), new CellAddress(sheet1.Id, 7, 3));
        // Defined with sheet scope only -- no matching entry in the workbook-global NamedRanges
        // dictionary, exactly like a name created via Name Manager with scope = current sheet.
        harness.Workbook.DefineNamedRange("ScopedOnly", scopedRange, metadata: null, scopeSheetId: sheet1.Id);

        harness.SetCellAddressBoxText("ScopedOnly");
        harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

        harness.SelectedRange.Should().Be(scopedRange);
        });
    }

    [Fact]
    public void NameBoxEnter_WithNameScopedToOtherSheet_IsNotVisibleFromActiveSheet()
    {
        StaTestRunner.Run(() =>
        {
        using var harness = MainWindowHarness.Create();
        var sheet1 = harness.Workbook.Sheets[0];
        var sheet2 = harness.Workbook.AddSheet("Sheet2");
        var scopedRange = new GridRange(new CellAddress(sheet2.Id, 6, 2), new CellAddress(sheet2.Id, 6, 2));
        harness.Workbook.DefineNamedRange("Sheet2Only", scopedRange, metadata: null, scopeSheetId: sheet2.Id);
        harness.SelectActiveCell(1, 1);

        harness.SetCellAddressBoxText("Sheet2Only");
        harness.PressCellAddressBoxKey(Key.Enter);

        // Not resolvable from Sheet1 (wrong scope) -- the Name Box falls through to its
        // "not a valid reference / define new name" handling, so the selection is unchanged.
        harness.CurrentSheetId.Should().Be(sheet1.Id);
        harness.SelectedRange.Should().Be(new GridRange(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 1, 1)));
        });
    }

    [Fact]
    public void NameBoxEnter_WithScopedNameShadowingGlobalName_PrefersScopedRange()
    {
        StaTestRunner.Run(() =>
        {
        using var harness = MainWindowHarness.Create();
        var sheet1 = harness.Workbook.Sheets[0];
        var globalRange = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 1, 1));
        var scopedRange = new GridRange(new CellAddress(sheet1.Id, 9, 9), new CellAddress(sheet1.Id, 9, 9));
        harness.Workbook.DefineNamedRange("Shadowed", globalRange);
        harness.Workbook.DefineNamedRange("Shadowed", scopedRange, metadata: null, scopeSheetId: sheet1.Id);

        harness.SetCellAddressBoxText("Shadowed");
        harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

        harness.SelectedRange.Should().Be(scopedRange,
            "sheet-scoped names take precedence over a same-named workbook-global name, matching formula evaluation");
        });
    }

    [Fact]
    public void NameBoxDropDownOpened_IncludesSheetScopedNameForActiveSheet()
    {
        StaTestRunner.Run(() =>
        {
        using var harness = MainWindowHarness.Create();
        var sheet1 = harness.Workbook.Sheets[0];
        var scopedRange = new GridRange(new CellAddress(sheet1.Id, 4, 4), new CellAddress(sheet1.Id, 4, 4));
        harness.Workbook.DefineNamedRange("ScopedDropdownName", scopedRange, metadata: null, scopeSheetId: sheet1.Id);

        var names = harness.OpenCellAddressBoxDropdown();

        names.Should().Contain("ScopedDropdownName");
        });
    }

    [Fact]
    public void NameBoxDropDownOpened_ExcludesSheetScopedNameFromOtherSheet()
    {
        StaTestRunner.Run(() =>
        {
        using var harness = MainWindowHarness.Create();
        var sheet2 = harness.Workbook.AddSheet("Sheet2");
        var scopedRange = new GridRange(new CellAddress(sheet2.Id, 4, 4), new CellAddress(sheet2.Id, 4, 4));
        harness.Workbook.DefineNamedRange("OtherSheetScopedName", scopedRange, metadata: null, scopeSheetId: sheet2.Id);

        var names = harness.OpenCellAddressBoxDropdown();

        names.Should().NotContain("OtherSheetScopedName");
        });
    }

    [Fact]
    public void NameBoxDropDownSelection_NavigatesToTableAndSelectsNamedObjectThroughHostRoutes()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var sheet = harness.Workbook.Sheets[0];
            var table = new StructuredTableModel
            {
                Id = 17,
                Name = "OrdersTable",
                DisplayName = "OrdersTable",
                Range = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 4, 2)),
            };
            sheet.StructuredTables.Add(table);
            var shape = new DrawingShapeModel
            {
                Name = "OrdersShape",
                Anchor = new CellAddress(sheet.Id, 8, 3),
            };
            sheet.DrawingShapes.Add(shape);

            harness.SelectCellAddressBoxDropdownItem("OrdersTable", NameBoxNavigationItemKind.Table);
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sheet.Id, 2, 1),
                new CellAddress(sheet.Id, 4, 2)));

            harness.SelectCellAddressBoxDropdownItem("OrdersShape", NameBoxNavigationItemKind.Object);
            harness.SelectedObjectId.Should().Be(shape.Id);
            harness.SelectedObjectKind.Should().Be(ObjectKind.Shape);
            harness.CurrentSheetId.Should().Be(sheet.Id);
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly FieldInfo _currentSheetIdField;
        private readonly FieldInfo _sheetTabsField;
        private readonly MethodInfo _setActiveCell;
        private readonly MethodInfo _refreshSheetTabs;
        private readonly MethodInfo _cellAddressBoxKeyDown;
        private readonly MethodInfo _cellAddressBoxSelectionChanged;
        private readonly MethodInfo _cellAddressBoxDropDownOpened;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _currentSheetIdField = typeof(MainWindow)
                .GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_currentSheetId");
            _sheetTabsField = typeof(MainWindow)
                .GetField("_sheetTabs", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_sheetTabs");
            _setActiveCell = typeof(MainWindow)
                .GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
            _refreshSheetTabs = typeof(MainWindow)
                .GetMethod("RefreshSheetTabs", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "RefreshSheetTabs");
            _cellAddressBoxKeyDown = typeof(MainWindow)
                .GetMethod("CellAddressBox_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CellAddressBox_KeyDown");
            _cellAddressBoxSelectionChanged = typeof(MainWindow)
                .GetMethod("CellAddressBox_SelectionChanged", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CellAddressBox_SelectionChanged");
            _cellAddressBoxDropDownOpened = typeof(MainWindow)
                .GetMethod("CellAddressBox_DropDownOpened", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CellAddressBox_DropDownOpened");
        }

        public Workbook Workbook => _window.Session.Workbook;

        public SheetId CurrentSheetId => (SheetId)_currentSheetIdField.GetValue(_window)!;

        public GridRange? SelectedRange => ((SheetGridView)_window.FindName("SheetGrid")).SelectedRange;

        public Guid SelectedObjectId => ((SheetGridView)_window.FindName("SheetGrid")).SelectedObjectId;

        public ObjectKind SelectedObjectKind => ((SheetGridView)_window.FindName("SheetGrid")).SelectedObjectKind;

        private ComboBox CellAddressBox => (ComboBox)_window.FindName("CellAddressBox");

        public SheetId? ActiveSheetTabId
        {
            get
            {
                var tabs = (IEnumerable)_sheetTabsField.GetValue(_window)!;
                foreach (var tab in tabs)
                {
                    var type = tab.GetType();
                    var isActive = (bool)type.GetProperty("IsActive")!.GetValue(tab)!;
                    if (isActive)
                        return (SheetId)type.GetProperty("Id")!.GetValue(tab)!;
                }

                return null;
            }
        }

        public void RefreshSheetTabs()
        {
            _refreshSheetTabs.Invoke(_window, null);
            PumpDispatcher();
        }

        public void SelectActiveCell(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _setActiveCell.Invoke(_window, [new CellAddress(sheet.Id, row, col)]);
            PumpDispatcher();
        }

        public void SetCellAddressBoxText(string text)
        {
            CellAddressBox.Text = text;
            PumpDispatcher();
        }

        public bool PressCellAddressBoxKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window)
                ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _cellAddressBoxKeyDown.Invoke(_window, [CellAddressBox, args]);
            PumpDispatcher();
            return args.Handled;
        }

        public IReadOnlyList<string> OpenCellAddressBoxDropdown()
        {
            _cellAddressBoxDropDownOpened.Invoke(_window, [CellAddressBox, EventArgs.Empty]);
            PumpDispatcher();
            return ((IEnumerable<NameBoxNavigationItem>)CellAddressBox.ItemsSource)
                .Select(item => item.Name)
                .ToList();
        }

        public void SelectCellAddressBoxDropdownItem(
            string name,
            NameBoxNavigationItemKind kind = NameBoxNavigationItemKind.DefinedName)
        {
            var comboBox = CellAddressBox;
            var items = NameBoxDropdownPlanner.Build(Workbook, CurrentSheetId);
            comboBox.ItemsSource = items;
            comboBox.IsDropDownOpen = true;
            var item = items.Single(item =>
                item.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && item.Kind == kind);
            comboBox.SelectedItem = item;
            var args = new SelectionChangedEventArgs(
                Selector.SelectionChangedEvent,
                new List<object>(),
                new List<object> { item });
            _cellAddressBoxSelectionChanged.Invoke(_window, [comboBox, args]);
            comboBox.IsDropDownOpen = false;
            PumpDispatcher();
        }

        public static MainWindowHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window);
        }

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
