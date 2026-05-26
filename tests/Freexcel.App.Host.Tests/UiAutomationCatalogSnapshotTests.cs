using FluentAssertions;
using System.IO;
using System.Text.Json;
using System.Windows.Automation;
using System.Xml.Linq;

namespace Freexcel.App.Host.Tests;

public sealed class UiAutomationCatalogSnapshotTests
{
    [Fact]
    [Trait("Category", "UIE2E")]
    public void VisibleControls_MatchCatalogSnapshotExpectations()
    {
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive)
            return;

        using var run = FreexcelUiRun.Start();

        var snapshot = UiAutomationCatalogSnapshot.CaptureVisibleControls(run.ProcessId, run.WindowHandle);
        var expectedTabNames = ReadCatalogTopLevelTabNames();
        var expectedVisibleAutomationIds = ReadExpectedVisibleAutomationIdsFromXaml();

        snapshot.Should().Contain(control => control.ControlType == "Window" && control.Name.Contains("Freexcel", StringComparison.Ordinal));
        snapshot.Count(control => control.ControlType == "Button").Should().BeGreaterThanOrEqualTo(20);
        snapshot.Count(control => control.ControlType == "TabItem").Should().BeGreaterThanOrEqualTo(expectedTabNames.Count);
        snapshot.Count(control => control.ControlType == "Edit").Should().BeGreaterThanOrEqualTo(2);

        snapshot.Select(control => control.Name)
            .Should()
            .Contain(expectedTabNames)
            .And.Contain(["Name Box", "Formula Bar", "Zoom Slider", "Insert Sheet", "Save", "Undo", "Redo"]);

        snapshot.Select(control => control.AutomationId)
            .Should()
            .Contain(expectedVisibleAutomationIds);

        snapshot.Should().Contain(control => control.AutomationId == "FormulaBar" && control.Name == "Formula Bar" && control.ControlType == "Edit");
        snapshot.Should().Contain(control => control.AutomationId == "ZoomSlider" && control.Name == "Zoom Slider" && control.ControlType == "Slider");
        snapshot.Should().Contain(control => control.AutomationId == "AddSheetButton" && control.Name == "Insert Sheet" && control.ControlType == "Button");
    }

    private static IReadOnlyList<string> ReadCatalogTopLevelTabNames()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(WorkspaceFileLocator.Find("docs", "COMMAND_INVENTORY.json")));
        return document.RootElement
            .GetProperty("keyTips")
            .GetProperty("topLevelTabs")
            .EnumerateArray()
            .Select(tab => tab.GetProperty("name").GetString()!)
            .Select(name => name == "File/Backstage" ? "File" : name)
            .ToList();
    }

    private static IReadOnlyList<string> ReadExpectedVisibleAutomationIdsFromXaml()
    {
        var document = XDocument.Load(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var expected = new[]
        {
            "SaveQatBtn",
            "UndoQatBtn",
            "RedoQatBtn",
            "CloseSysBtn",
            "CellAddressBox",
            "FormulaBar",
            "FormulaBarExpandBtn",
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

        declaredNames.Should().Contain(expected);
        return expected;
    }
}

internal static class UiAutomationCatalogSnapshot
{
    public static IReadOnlyList<UiAutomationCatalogControl> CaptureVisibleControls(int processId, IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            throw new ArgumentException("A visible Freexcel window handle is required.", nameof(windowHandle));

        var root = AutomationElement.FromHandle(windowHandle)
            ?? throw new InvalidOperationException("UI Automation could not attach to the Freexcel window.");

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
