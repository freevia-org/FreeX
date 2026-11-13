using FluentAssertions;
using System.IO;
using System.Text.Json;
using System.Windows.Automation;
using System.Xml.Linq;

namespace FreeX.App.Host.Tests;

internal static class UiAutomationCatalogSnapshotHarness
{
    public static void Run(FreeXUiRun run)
    {
        VisibleShellControls_ExposeExpectedAutomationPatterns(run);
        VisibleDialogEntryPointControls_ExposeInvokePattern(run);
    }


    private static void VisibleDialogEntryPointControls_ExposeInvokePattern(FreeXUiRun run)
    {
        var root = AutomationElement.FromHandle(run.WindowHandle)
            ?? throw new InvalidOperationException("UI Automation could not attach to the FreeX window.");

        SelectTab(root, run.ProcessId, UiText.Get("MainWindow_Header_Formulas"));
        AssertVisibleButtonExposesInvokePattern(root, run.ProcessId, "FormulasInsertFunctionButton", UiText.Get("MainWindow_AutomationName_InsertFunction"));

        SelectTab(root, run.ProcessId, UiText.Get("MainWindow_Header_File"));
        AssertVisibleButtonExposesInvokePattern(root, run.ProcessId, "BackstageAccountButton", UiText.Get("MainWindow_AutomationName_Account"));
        AssertVisibleButtonExposesInvokePattern(root, run.ProcessId, "BackstageOptionsButton", UiText.Get("MainWindow_AutomationName_Options"));

        SelectTab(root, run.ProcessId, UiText.Get("MainWindow_Header_Home"));
    }

    private static void VisibleShellControls_ExposeExpectedAutomationPatterns(FreeXUiRun run)
    {
        var root = AutomationElement.FromHandle(run.WindowHandle)
            ?? throw new InvalidOperationException("UI Automation could not attach to the FreeX window.");

        AssertVisibleElementExposesPattern(root, run.ProcessId, AutomationElement.NameProperty, UiText.Get("MainWindow_Header_Home"), ControlType.TabItem, SelectionItemPattern.Pattern);
        AssertVisibleElementExposesPattern(root, run.ProcessId, AutomationElement.NameProperty, UiText.Get("MainWindow_Header_Insert"), ControlType.TabItem, SelectionItemPattern.Pattern);
        AssertVisibleElementExposesPattern(root, run.ProcessId, AutomationElement.AutomationIdProperty, "SaveQatBtn", ControlType.Button, InvokePattern.Pattern);
        AssertVisibleElementExposesPattern(root, run.ProcessId, AutomationElement.AutomationIdProperty, "UndoQatBtn", ControlType.Button, InvokePattern.Pattern);
        AssertVisibleElementExposesPattern(root, run.ProcessId, AutomationElement.AutomationIdProperty, "RedoQatBtn", ControlType.Button, InvokePattern.Pattern);
        AssertVisibleElementExposesPattern(root, run.ProcessId, AutomationElement.AutomationIdProperty, "AddSheetButton", ControlType.Button, InvokePattern.Pattern);
        AssertVisibleElementExposesPattern(root, run.ProcessId, AutomationElement.AutomationIdProperty, "ZoomSlider", ControlType.Slider, RangeValuePattern.Pattern);
    }


    private static IReadOnlyList<string> ReadExpectedVisibleAutomationIdsFromXaml()
    {
        var document = DialogSourceTestSupport.LoadHostXamlDocument("MainWindow.xaml");
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var dynamicQuickAccessToolbarIds = QuickAccessToolbarCatalog.DefaultCommandIds
            .Select(id => QuickAccessToolbarCatalog.TryGet(id, out var command) ? command.AutomationId : null)
            .Where(id => id is not null)
            .Cast<string>()
            .ToArray();
        var expectedDeclaredNames = new[]
        {
            "CloseSysBtn",
            "VerticalScroll",
            "HorizontalScroll",
            "AddSheetButton",
            "StatusZoomOutButton",
            "ZoomSlider",
            "StatusZoomInButton",
        };

        var declaredNames = document
            .Descendants()
            .Select(element => element.Attribute(x + "Name")?.Value)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        declaredNames.Should().Contain(expectedDeclaredNames);
        return dynamicQuickAccessToolbarIds.Concat(expectedDeclaredNames).ToArray();
    }

    private static void AssertVisibleButtonExposesInvokePattern(
        AutomationElement root,
        int processId,
        string automationId,
        string expectedName)
    {
        var element = root.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId),
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));

        element.Should().NotBeNull($"visible dialog entry point {automationId} should be present in UIA");
        element!.Current.Name.Should().Be(expectedName);
        element.Current.ControlType.Should().Be(ControlType.Button);
        element.TryGetCurrentPattern(InvokePattern.Pattern, out _)
            .Should()
            .BeTrue($"{expectedName} should expose UIA InvokePattern");
    }

    private static void AssertVisibleElementExposesPattern(
        AutomationElement root,
        int processId,
        AutomationProperty property,
        object value,
        ControlType controlType,
        AutomationPattern pattern)
    {
        var element = FindVisibleElement(root, processId, property, value);

        element.Should().NotBeNull($"visible UIA element {value} should be present");
        element!.Current.ControlType.Should().Be(controlType);
        element.TryGetCurrentPattern(pattern, out _)
            .Should()
            .BeTrue($"{value} should expose {pattern.ProgrammaticName}");
    }

    private static void SelectTab(AutomationElement root, int processId, string tabName)
    {
        var tab = FindVisibleElement(root, processId, AutomationElement.NameProperty, tabName)
            ?? throw new InvalidOperationException($"Could not find visible tab '{tabName}' through UI Automation.");

        tab.Current.ControlType.Should().Be(ControlType.TabItem);
        tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)
            .Should()
            .BeTrue($"{tabName} tab should expose SelectionItemPattern");

        ((SelectionItemPattern)pattern).Select();
        WaitFor(() => tab.Current.IsKeyboardFocusable || !tab.Current.IsOffscreen, $"tab '{tabName}' to remain visible after selection");
    }

    private static AutomationElement? FindVisibleElement(AutomationElement root, int processId, AutomationProperty property, object value) =>
        root.FindFirst(
            TreeScope.Descendants,
            new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
                new PropertyCondition(property, value),
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));

    private static void WaitFor(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            Thread.Sleep(50);
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }

    private static IReadOnlyList<UiAutomationCatalogControl> CaptureVisibleControlsWhen(
        FreeXUiRun run,
        Func<IReadOnlyList<UiAutomationCatalogControl>, bool> condition,
        string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        IReadOnlyList<UiAutomationCatalogControl> snapshot = [];
        while (DateTime.UtcNow < deadline)
        {
            snapshot = UiAutomationCatalogSnapshot.CaptureVisibleControls(run.ProcessId, run.WindowHandle);
            if (condition(snapshot))
                return snapshot;

            Thread.Sleep(50);
        }

        throw new TimeoutException($"Timed out waiting for {description}.");
    }
}

internal static class UiAutomationCatalogSnapshot
{
    public static IReadOnlyList<UiAutomationCatalogControl> CaptureVisibleControls(int processId, IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            throw new ArgumentException("A visible FreeX window handle is required.", nameof(windowHandle));

        var root = AutomationElement.FromHandle(windowHandle)
            ?? throw new InvalidOperationException("UI Automation could not attach to the FreeX window.");

        var visibleProcessElement = new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false));

        var controls = new List<UiAutomationCatalogControl> { ToSnapshotControl(root) };
        controls.AddRange(
            root.FindAll(TreeScope.Descendants, visibleProcessElement)
                .Cast<AutomationElement>()
                .Select(ToSnapshotControl));

        return controls
            .Where(control => control.AutomationId.Length > 0 || control.Name.Length > 0)
            .Distinct()
            .OrderBy(control => control.ControlType, StringComparer.Ordinal)
            .ThenBy(control => control.AutomationId, StringComparer.Ordinal)
            .ThenBy(control => control.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static UiAutomationCatalogControl ToSnapshotControl(AutomationElement element)
    {
        var current = element.Current;
        return new UiAutomationCatalogControl(
            current.AutomationId ?? string.Empty,
            current.Name ?? string.Empty,
            current.ControlType.ProgrammaticName.Replace("ControlType.", string.Empty, StringComparison.Ordinal));
    }
}

internal sealed record UiAutomationCatalogControl(string AutomationId, string Name, string ControlType);
