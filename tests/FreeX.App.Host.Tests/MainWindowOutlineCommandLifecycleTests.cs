using System.Reflection;
using System.Windows;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

public sealed class MainWindowOutlineCommandLifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HideShowDetail_SelectedColumnsStayScopedAndUseMutationLifecycle(bool collapse)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SeedColumnGroups(collapse);
            harness.SelectWholeColumns(2, 4);
            var before = harness.CaptureLifecycle();

            harness.Invoke(collapse ? "CollapseGroupBtn_Click" : "ExpandGroupBtn_Click", null, new RoutedEventArgs());

            var expectedHidden = collapse
                ? new uint[] { 2, 3, 4 }
                : new uint[] { 7, 8, 9 };
            harness.CurrentSheet.GroupHiddenCols.Should().BeEquivalentTo(expectedHidden);
            harness.AssertLifecycleAdvanced(before);

            harness.Undo().Should().BeTrue();
            var expectedAfterUndo = collapse
                ? Array.Empty<uint>()
                : new uint[] { 2, 3, 4, 7, 8, 9 };
            harness.CurrentSheet.GroupHiddenCols.Should().BeEquivalentTo(expectedAfterUndo);
            harness.Sibling.RefreshCount.Should().Be(2);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutlineGutterToggle_UsesMutationLifecycle(bool collapse)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SeedRowGroups(collapse);
            var before = harness.CaptureLifecycle();
            var request = new GridOutlineGroupToggleRequest(
                GridOutlineGroupAxis.Rows,
                Level: 1,
                Start: 2,
                End: 4,
                Collapse: collapse);

            harness.Invoke("OnOutlineGroupToggleRequested", request);

            var expectedHidden = collapse
                ? new uint[] { 2, 3, 4 }
                : new uint[] { 7, 8, 9 };
            harness.CurrentSheet.GroupHiddenRows.Should().BeEquivalentTo(expectedHidden);
            harness.AssertLifecycleAdvanced(before);

            harness.Undo().Should().BeTrue();
            var expectedAfterUndo = collapse
                ? Array.Empty<uint>()
                : new uint[] { 2, 3, 4, 7, 8, 9 };
            harness.CurrentSheet.GroupHiddenRows.Should().BeEquivalentTo(expectedAfterUndo);
            harness.Sibling.RefreshCount.Should().Be(2);
        });
    }

    private readonly record struct LifecycleSnapshot(int DirtyGeneration, ulong NavigationRevision);

    private sealed class MainWindowHarness : IDisposable
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly MainWindow _window;
        private readonly WorkbookWindowRegistry _registry;
        private readonly WorkbookDocumentState _documentState;
        private readonly CommandBus _commandBus;
        private readonly FieldInfo _currentSheetIdField;
        private readonly FieldInfo _navigationRevisionField;

        private MainWindowHarness(
            MainWindow window,
            WorkbookWindowRegistry registry,
            WorkbookDocumentState documentState,
            CommandBus commandBus)
        {
            _window = window;
            _registry = registry;
            _documentState = documentState;
            _commandBus = commandBus;
            _currentSheetIdField = GetField("_currentSheetId");
            _navigationRevisionField = GetField("_navigationCacheRevision");
            Sibling = new TestWorkbookWindow { DocumentId = CurrentWorkbook.Id };
            _registry.Register(Sibling);
        }

        public TestWorkbookWindow Sibling { get; }

        public Sheet CurrentSheet
        {
            get
            {
                var sheetId = (SheetId)_currentSheetIdField.GetValue(_window)!;
                return CurrentWorkbook.GetSheet(sheetId)
                       ?? throw new InvalidOperationException("Current sheet was not found.");
            }
        }

        private Workbook CurrentWorkbook => _window.Session.Workbook;

        public static MainWindowHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var registry = new WorkbookWindowRegistry();
            var documentState = new WorkbookDocumentState();
            var commandBus = new CommandBus(_ => new TestCommandContext(workbookRef.Current));
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                commandBus,
                new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance,
                documentState,
                windowRegistry: registry)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window, registry, documentState, commandBus);
        }

        public void SeedColumnGroups(bool collapse)
        {
            foreach (var col in new uint[] { 2, 3, 4, 7, 8, 9 })
                CurrentSheet.ColOutlineLevels[col] = 1;

            if (collapse)
                return;

            CurrentSheet.GroupHiddenCols.UnionWith([2u, 3u, 4u, 7u, 8u, 9u]);
            CurrentSheet.CollapsedAnchorCols.UnionWith([5u, 10u]);
        }

        public void SeedRowGroups(bool collapse)
        {
            foreach (var row in new uint[] { 2, 3, 4, 7, 8, 9 })
                CurrentSheet.RowOutlineLevels[row] = 1;

            if (collapse)
                return;

            CurrentSheet.GroupHiddenRows.UnionWith([2u, 3u, 4u, 7u, 8u, 9u]);
            CurrentSheet.CollapsedAnchorRows.UnionWith([5u, 10u]);
        }

        public void SelectWholeColumns(uint startCol, uint endCol)
        {
            var sheetId = CurrentSheet.Id;
            _window.SheetGrid.SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, startCol),
                new CellAddress(sheetId, CellAddress.MaxRow, endCol));
        }

        public LifecycleSnapshot CaptureLifecycle() =>
            new(_documentState.DirtyGeneration, NavigationRevision);

        public void AssertLifecycleAdvanced(LifecycleSnapshot before)
        {
            _documentState.IsDirty.Should().BeTrue();
            _documentState.DirtyGeneration.Should().Be(before.DirtyGeneration + 1);
            NavigationRevision.Should().BeGreaterThan(before.NavigationRevision);
            Sibling.RefreshCount.Should().Be(1);
            _commandBus.GetUndoStackDepth(CurrentWorkbook.Id).Should().Be(1);
            _commandBus.CanRepeat(CurrentWorkbook.Id).Should().BeTrue();
        }

        public void Invoke(string methodName, params object?[] arguments)
        {
            var method = typeof(MainWindow).GetMethod(methodName, PrivateInstance)
                         ?? throw new MissingMethodException(nameof(MainWindow), methodName);
            method.Invoke(_window, arguments);
            PumpDispatcher();
        }

        public bool Undo()
        {
            var method = typeof(MainWindow).GetMethod("ExecuteUndo", PrivateInstance)
                         ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteUndo");
            var result = (bool)method.Invoke(_window, [])!;
            PumpDispatcher();
            return result;
        }

        private ulong NavigationRevision => (ulong)_navigationRevisionField.GetValue(_window)!;

        private static FieldInfo GetField(string fieldName) =>
            typeof(MainWindow).GetField(fieldName, PrivateInstance)
            ?? throw new MissingFieldException(nameof(MainWindow), fieldName);

        public void Dispose()
        {
            _registry.Unregister(Sibling);
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
