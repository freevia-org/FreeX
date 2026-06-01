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
        VisibleControls_MatchCatalogSnapshotExpectations(run);
        VisibleShellControls_ExposeExpectedAutomationPatterns(run);
        VisibleDialogEntryPointControls_ExposeInvokePattern(run);
    }

    private static void VisibleControls_MatchCatalogSnapshotExpectations(FreeXUiRun run)
    {
        var snapshot = CaptureVisibleControlsWhen(
            run,
            controls => controls.Any(control => control.AutomationId == "SaveQatBtn") &&
                controls.Any(control => control.AutomationId == "AddSheetButton") &&
                controls.Any(control => control.AutomationId == "ZoomSlider"),
            "the stable shell UI Automation peers to be ready");
        var expectedTabNames = ReadCatalogTopLevelTabNames();
        var expectedVisibleAutomationIds = ReadExpectedVisibleAutomationIdsFromXaml();

        snapshot.Should().Contain(control => control.ControlType == "Window" && control.Name.Contains("FreeX", StringComparison.Ordinal));
        snapshot.Count(control => control.ControlType == "Button").Should().BeGreaterThanOrEqualTo(20);
        snapshot.Count(control => control.ControlType == "TabItem").Should().BeGreaterThanOrEqualTo(expectedTabNames.Count);

        snapshot.Select(control => control.Name)
            .Should()
            .Contain(expectedTabNames)
            .And.Contain([
                UiText.Get("MainWindow_AutomationName_ZoomSlider"),
                UiText.Get("MainWindow_AutomationName_InsertSheet"),
                UiText.Get("MainWindow_AutomationName_Save"),
                UiText.Get("MainWindow_AutomationName_Undo"),
                UiText.Get("MainWindow_AutomationName_Redo")]);

        snapshot.Select(control => control.AutomationId)
            .Should()
            .Contain(expectedVisibleAutomationIds);

        snapshot.Should().Contain(control => control.AutomationId == "ZoomSlider" && control.Name == UiText.Get("MainWindow_AutomationName_ZoomSlider") && control.ControlType == "Slider");
        snapshot.Should().Contain(control => control.AutomationId == "AddSheetButton" && control.Name == UiText.Get("MainWindow_AutomationName_InsertSheet") && control.ControlType == "Button");
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

    private static IReadOnlyList<string> ReadCatalogTopLevelTabNames()
    {
        var resourceKeyByCatalogName = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["File"] = "MainWindow_Header_File",
            ["Home"] = "MainWindow_Header_Home",
            ["Insert"] = "MainWindow_Header_Insert",
            ["Draw"] = "MainWindow_Header_Draw",
            ["Page Layout"] = "MainWindow_Header_PageLayout",
            ["Formulas"] = "MainWindow_Header_Formulas",
            ["Data"] = "MainWindow_Header_Data",
            ["Review"] = "MainWindow_Header_Review",
            ["View"] = "MainWindow_Header_View",
            ["Help"] = "MainWindow_Header_Help"
        };

        using var document = JsonDocument.Parse(File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_INVENTORY.json")));
        return document.RootElement
            .GetProperty("keyTips")
            .GetProperty("topLevelTabs")
            .EnumerateArray()
            .Select(tab => tab.GetProperty("name").GetString()!)
            .Select(name => name == "File/Backstage" ? "File" : name)
            .Select(name => UiText.Get(resourceKeyByCatalogName[name]))
            .ToList();
    }

    private static IReadOnlyList<string> ReadExpectedVisibleAutomationIdsFromXaml()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "MainWindow.xaml"));
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
