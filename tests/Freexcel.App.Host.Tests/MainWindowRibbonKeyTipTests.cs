using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void TopLevelAndCommandKeyTips_RouteThroughVisibleRibbonControls()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().Contain(["H", "N", "1"]);
            harness.OverlayBadgeTexts.Should().NotContain("B", "top-level Alt mode should show tabs and QAT, not active-tab command badges");
            harness.HandleKeyTip(Key.N);
            harness.SelectedRibbonTabHeader.Should().Be("Insert");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);

            harness.SelectedRibbonTabHeader.Should().Be("Home");
            harness.KeyTipScope.Should().Be("Commands");
            harness.OverlayBadgeTexts.Should().Contain(["B", "1"]);
            harness.OverlayBadgeTexts.Should().NotContain("SC", "command-scope Alt mode should not show off-tab Insert chart badges");
            harness.VisibleCommandKeyTips("B").Should().ContainSingle("Borders");
            harness.HandleKeyTip(Key.B);

            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("All Borders").Should().Be("A");
            harness.HandleKeyTip(Key.Escape);

            harness.KeyTipScope.Should().Be("None");
            harness.ActiveMenuIsOpen.Should().BeFalse();
            harness.OverlayBadgeTexts.Should().BeEmpty("Escape should clear any visible keytip badges");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.B);

            harness.HandleKeyTip(Key.A);

            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty("invoking a menu keytip should leave keytip mode fully closed");

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.D1);

            harness.IsToggleChecked("BoldButton").Should().BeTrue();
            harness.KeyTipScope.Should().Be("None");
            harness.OverlayBadgeTexts.Should().BeEmpty("invoking a command keytip should leave keytip mode fully closed");
        });
    }

    [Fact]
    public void FileKeyTip_RoutesThroughBackstageCommandsOnly()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.F);

            harness.StartScreenIsVisible.Should().BeTrue();
            harness.KeyTipScope.Should().Be("Commands");
            harness.OverlayBadgeTexts.Should().Contain(["N", "O", "SH"]);
            harness.OverlayBadgeTexts.Should().NotContain("FG", "covered Home ribbon controls should not participate while Backstage is open");
            harness.VisibleCommandKeyTips("N").Should().ContainSingle().Which.Should().Be("New");
        });
    }

    [Fact]
    public void DirectAltTopLevelKeyTips_OpenTabsAndBackstage()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.HandleDirectTopLevelKeyTip(Key.N).Should().BeTrue();

            harness.SelectedRibbonTabHeader.Should().Be("Insert");
            harness.KeyTipScope.Should().Be("Commands");

            harness.HandleDirectTopLevelKeyTip(Key.F).Should().BeTrue();

            harness.StartScreenIsVisible.Should().BeTrue();
            harness.KeyTipScope.Should().Be("Commands");
            harness.VisibleCommandKeyTips("N").Should().ContainSingle().Which.Should().Be("New");
        });
    }

    [Fact]
    public void CrossTabMenuKeyTips_RouteThroughStaticRibbonMenus()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.P, Key.B);
            harness.SelectedRibbonTabHeader.Should().Be("Page Layout");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Insert Page Break").Should().Be("I");
            harness.ActiveMenuItemGestureText("Remove Page Break").Should().Be("R");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.M, Key.E, Key.C);
            harness.SelectedRibbonTabHeader.Should().Be("Formulas");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Error Checking...").Should().Be("E");
            harness.ActiveMenuItemGestureText("Error Checking Options...").Should().Be("O");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.W, Key.F);
            harness.SelectedRibbonTabHeader.Should().Be("View");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Freeze Panes").Should().Be("F");
            harness.ActiveMenuItemGestureText("Unfreeze All").Should().Be("U");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.W, Key.Q);
            harness.ActiveMenuItemGestureText("100%").Should().Be("1");
            harness.ActiveMenuItemGestureText("Custom...").Should().Be("C");
            harness.HandleKeyTip(Key.Escape);

            harness.OpenRibbonMenu(Key.W, Key.A);
            harness.ActiveMenuItemGestureText("Tiled").Should().Be("T");
            harness.ActiveMenuItemGestureText("Cascade").Should().Be("C");
        });
    }

    [Fact]
    public void HomePasteKeyTip_OpensExcelStylePasteMenu()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.H, Key.V);

            harness.SelectedRibbonTabHeader.Should().Be("Home");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Paste").Should().Be("P");
            harness.ActiveMenuItemGestureText("Values").Should().Be("V");
            harness.ActiveMenuItemGestureText("Formulas").Should().Be("F");
            harness.ActiveMenuItemGestureText("Formatting").Should().Be("R");
            harness.ActiveMenuItemGestureText("Transpose").Should().Be("T");
            harness.ActiveMenuItemGestureText("Paste Special...").Should().Be("S");
        });
    }

    [Fact]
    public void CollapsedRibbonGroupKeyTip_RoutesThroughVisibleOverflowGroup()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SetRibbonWidth(220);

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.H);
            harness.HandleKeyTip(Key.E);

            harness.SelectedRibbonTabHeader.Should().Be("Home");
            harness.KeyTipScope.Should().Be("Commands", "E should be treated as the first character of the visible Editing group keytip ED");
            harness.ActiveMenuIsOpen.Should().BeFalse();

            harness.HandleKeyTip(Key.D);

            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("Find & Select").Should().Be("FD");
        });
    }

    [Fact]
    public void FormulasFunctionLibraryDynamicMenu_IsKeyTipRoutable()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.M);
            harness.HandleKeyTip(Key.L);

            harness.SelectedRibbonTabHeader.Should().Be("Formulas");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("IF").Should().Be("I");
        });
    }

    [Fact]
    public void FormulasUseInFormulaDynamicMenu_IsKeyTipRoutable()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(workbook =>
            {
                var sheet = workbook.Sheets[0];
                workbook.DefineNamedRange(
                    "Sales",
                    new GridRange(
                        new CellAddress(sheet.Id, 1, 1),
                        new CellAddress(sheet.Id, 1, 1)));
            });

            harness.EnterKeyTipScope("TopLevel");
            harness.HandleKeyTip(Key.M);
            harness.HandleKeyTip(Key.I);

            harness.SelectedRibbonTabHeader.Should().Be("Formulas");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuIsOpen.Should().BeTrue();
            harness.ActiveMenuItemGestureText("Sales").Should().Be("S");
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly MethodInfo _enterKeyTipMode;
        private readonly MethodInfo _handleActiveRibbonKeyTip;
        private readonly MethodInfo _tryHandleDirectRibbonKeyTip;
        private readonly MethodInfo _getVisibleKeyTipElements;
        private readonly MethodInfo _updateRibbonCompactMode;
        private readonly Type _scopeType;
        private readonly FieldInfo _scopeField;
        private readonly FieldInfo _activeMenuField;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _enterKeyTipMode = typeof(MainWindow).GetMethod("EnterRibbonKeyTipMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EnterRibbonKeyTipMode");
            _handleActiveRibbonKeyTip = typeof(MainWindow).GetMethod("HandleActiveRibbonKeyTip", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "HandleActiveRibbonKeyTip");
            _tryHandleDirectRibbonKeyTip = typeof(MainWindow).GetMethod("TryHandleDirectRibbonKeyTip", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "TryHandleDirectRibbonKeyTip");
            _getVisibleKeyTipElements = typeof(MainWindow).GetMethod("GetVisibleKeyTipElements", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "GetVisibleKeyTipElements");
            _updateRibbonCompactMode = typeof(MainWindow).GetMethod("UpdateRibbonCompactMode", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "UpdateRibbonCompactMode");
            _scopeType = typeof(MainWindow).GetNestedType("RibbonKeyTipScope", BindingFlags.NonPublic)
                ?? throw new MissingMemberException(nameof(MainWindow), "RibbonKeyTipScope");
            _scopeField = typeof(MainWindow).GetField("_ribbonKeyTipScope", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_ribbonKeyTipScope");
            _activeMenuField = typeof(MainWindow).GetField("_activeRibbonKeyTipMenu", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_activeRibbonKeyTipMenu");
        }

        public string? SelectedRibbonTabHeader =>
            (_window.FindName("RibbonTabs") as TabControl)?.SelectedItem is TabItem tab
                ? tab.Header?.ToString()
                : null;

        public string KeyTipScope => _scopeField.GetValue(_window)?.ToString() ?? "";

        public bool? IsToggleChecked(string name) =>
            (_window.FindName(name) as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked;

        public IReadOnlyList<string> OverlayBadgeTexts =>
            (_window.FindName("KeyTipOverlay") as Canvas)?.Children
                .OfType<Border>()
                .Select(border => (border.Child as TextBlock)?.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Cast<string>()
                .ToList() ?? [];

        public bool ActiveMenuIsOpen => ActiveMenu?.IsOpen == true;

        public bool StartScreenIsVisible =>
            (_window.FindName("StartScreenOverlay") as FrameworkElement)?.Visibility == Visibility.Visible;

        public string? ActiveMenuItemGestureText(string header) =>
            FindActiveMenuItem(header)?.InputGestureText;

        public bool ActiveMenuItemSubmenuIsOpen(string header) =>
            FindActiveMenuItem(header)?.IsSubmenuOpen == true;

        public IReadOnlyList<string> VisibleCommandKeyTips(string keyTip)
        {
            var scope = Enum.Parse(_scopeType, "Commands");
            var elements = ((System.Collections.IEnumerable)_getVisibleKeyTipElements.Invoke(_window, [scope])!)
                .OfType<FrameworkElement>()
                .Where(element => string.Equals(RibbonTooltip.GetKeyTip(element), keyTip, StringComparison.OrdinalIgnoreCase))
                .Select(element => RibbonTooltip.GetTitle(element) ?? element.Name ?? element.GetType().Name)
                .ToList();
            return elements;
        }

        private ContextMenu? ActiveMenu => _activeMenuField.GetValue(_window) as ContextMenu;

        private MenuItem? FindActiveMenuItem(string header) =>
            ActiveMenu is { } menu
                ? EnumerateMenuItems(menu).FirstOrDefault(item => string.Equals(item.Header?.ToString(), header, StringComparison.Ordinal))
                : null;

        public static MainWindowHarness Create(Action<Workbook>? configureWorkbook = null)
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook);

            window.WindowState = WindowState.Normal;
            window.Width = 2400;
            window.Height = 720;
            window.Show();
            if (window.FindName("RibbonTabs") is TabControl ribbonTabs)
                ribbonTabs.Width = 2400;
            window.UpdateLayout();
            PumpDispatcher();
            configureWorkbook?.Invoke(workbookRef.Current);
            return new MainWindowHarness(window);
        }

        public void SetRibbonWidth(double width)
        {
            if (_window.FindName("RibbonTabs") is TabControl ribbonTabs)
            {
                ribbonTabs.Width = width;
                ribbonTabs.SelectedIndex = 1;
            }

            _window.WindowState = WindowState.Normal;
            _window.Width = width;
            _window.UpdateLayout();
            _updateRibbonCompactMode.Invoke(_window, [true]);
            PumpDispatcher();
        }

        public void EnterKeyTipScope(string scope)
        {
            var value = Enum.Parse(_scopeType, scope);
            _enterKeyTipMode.Invoke(_window, [value]);
            PumpDispatcher();
        }

        public void HandleKeyTip(Key key)
        {
            _handleActiveRibbonKeyTip.Invoke(_window, [key]);
            PumpDispatcher();
        }

        public bool HandleDirectTopLevelKeyTip(Key key)
        {
            var handled = (bool)_tryHandleDirectRibbonKeyTip.Invoke(_window, [key])!;
            PumpDispatcher();
            return handled;
        }

        public void OpenRibbonMenu(Key tabKeyTip, params Key[] commandKeyTips)
        {
            EnterKeyTipScope("TopLevel");
            HandleKeyTip(tabKeyTip);
            foreach (var keyTip in commandKeyTips)
                HandleKeyTip(keyTip);

            ActiveMenuIsOpen.Should().BeTrue();
        }

        public void Dispose()
        {
            _window.Close();
            PumpDispatcher();
        }

        private static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl control)
        {
            foreach (var item in control.Items)
            {
                if (item is not MenuItem menuItem)
                    continue;

                yield return menuItem;

                foreach (var child in EnumerateMenuItems(menuItem))
                    yield return child;
            }
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

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }

    private static void RunSta(Action action)
    {
        StaTestRunner.Run(action);
    }
}
