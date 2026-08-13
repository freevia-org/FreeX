using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Behavioural harness for the backstage rail, now hosted on the shared
/// <see cref="Free.Shared.Shell.Wpf.BackstageFrame"/>. Replaces the old source-text rail assertions
/// (literal <c>x:Name</c>s, handler-name strings) with automation-tree queries against a live MainWindow:
/// the tests open the backstage and assert the rail exposes the expected AutomationIds, KeyTips, localized
/// names and pane-swap behaviour — refactor-proof and reusable, the de-brittling pattern this pilot proves.
/// </summary>
internal sealed class BackstageRailHarness : IDisposable
{
    private readonly MainWindow _window;
    private readonly object _frame;

    private BackstageRailHarness(MainWindow window, object frame)
    {
        _window = window;
        _frame = frame;
    }

    public static BackstageRailHarness Create()
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

        var frame = typeof(MainWindow)
            .GetField("_backstageFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)
            ?? throw new InvalidOperationException("MainWindow did not build a BackstageFrame.");

        return new BackstageRailHarness(window, frame);
    }

    /// <summary>Open the backstage (the production File-screen entry point) and lay it out.</summary>
    public void OpenBackstage()
    {
        _window.Activate();
        Invoke("ShowStartScreen");
        _window.UpdateLayout();
        PumpDispatcher();
    }

    public bool IsBackstageVisible =>
        ((UIElement)_window.FindName("StartScreenOverlay")).Visibility == Visibility.Visible;

    public string? CurrentEntryId =>
        (string?)_frame.GetType()
            .GetProperty("CurrentEntryId", BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(_frame);

    /// <summary>Invoke a private MainWindow method by name (e.g. ShowInfoView/ShowPrintView/HideStartScreen).</summary>
    public void Invoke(string methodName)
    {
        typeof(MainWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!
            .Invoke(_window, null);
        _window.UpdateLayout();
        PumpDispatcher();
    }

    /// <summary>
    /// The rail nav buttons (back arrow + top + bottom entries) realized in the frame's visual tree —
    /// i.e. the buttons that live on the coloured sidebar, excluding anything inside the content host (the
    /// reparented panes, e.g. recent-file rows, also carry Backstage* automation ids).
    /// </summary>
    public IReadOnlyList<Button> RailButtons()
    {
        var contentHost = ContentHost();
        return Descendants(_frame as DependencyObject)
            .OfType<Button>()
            .Where(button => AutomationProperties.GetAutomationId(button).StartsWith("Backstage", StringComparison.Ordinal))
            .Where(button => contentHost is null || !IsDescendantOf(button, contentHost))
            .ToList();
    }

    private ContentControl? ContentHost() =>
        Descendants(_frame as DependencyObject)
            .OfType<ContentControl>()
            .FirstOrDefault(host => host is not Button && host.Content is UIElement);

    private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    public Button? RailButton(string automationId) =>
        RailButtons().FirstOrDefault(button =>
            AutomationProperties.GetAutomationId(button) == automationId);

    public string KeyTip(Button button) => RibbonTooltip.GetKeyTip(button) ?? string.Empty;

    public string AutomationName(Button button) => AutomationProperties.GetName(button);

    public string AutomationHelpText(Button button) => AutomationProperties.GetHelpText(button);

    public string TooltipTitle(Button button) => RibbonTooltip.GetTitle(button) ?? string.Empty;

    public string TooltipDescription(Button button) => RibbonTooltip.GetDescription(button) ?? string.Empty;

    /// <summary>Click a rail button through the real WPF invoke path (so its command/pane logic runs).</summary>
    public void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        _window.UpdateLayout();
        PumpDispatcher();
    }

    /// <summary>True when the frame's content host currently shows the given named pane element.</summary>
    public bool ContentHostShows(string paneName)
    {
        var pane = _window.FindName(paneName) as UIElement;
        if (pane is null)
            return false;
        return Descendants(_frame as DependencyObject)
            .OfType<ContentControl>()
            .Any(host => ReferenceEquals(host.Content, pane));
    }

    public bool IsRailButtonFocused(string automationId) =>
        RailButton(automationId) is { } button &&
        (button.IsFocused ||
         button.IsKeyboardFocused ||
         ReferenceEquals(Keyboard.FocusedElement, button) ||
         ReferenceEquals(FocusManager.GetFocusedElement(_window), button) ||
         ReferenceEquals(FocusManager.GetFocusedElement(FocusScopeOf(button)), button));

    private static DependencyObject FocusScopeOf(DependencyObject node)
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (FocusManager.GetIsFocusScope(current))
                return current;
        }
        return node;
    }

    public void FocusRailButton(string automationId)
    {
        var button = RailButton(automationId)
            ?? throw new InvalidOperationException($"Rail button '{automationId}' not found.");
        _window.Activate();
        FocusManager.SetFocusedElement(_window, button);
        button.Focus();
        Keyboard.Focus(button);
        PumpDispatcher();
    }

    /// <summary>Send an arrow/Home/End key to the frame so its rail navigation handler runs.</summary>
    public void PressKeyOnFrame(Key key)
    {
        var frameElement = (UIElement)_frame;
        var source = PresentationSource.FromVisual((Visual)_frame)
            ?? throw new InvalidOperationException("BackstageFrame has no presentation source.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        frameElement.RaiseEvent(args);
        PumpDispatcher();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
    {
        if (root is null)
            yield break;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    public void Dispose()
    {
        MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
        PumpDispatcher();
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
