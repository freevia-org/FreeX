using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.AppServices;
using Free.Shared.Ribbon.Avalonia;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Tests that the Avalonia footer renders from the shared neutral <see cref="StatusBarViewModel"/>
/// (built by the shared <see cref="StatusBarDisplayModelBuilder"/>) and that the "Customize Status Bar"
/// right-click menu is built from the neutral <see cref="StatusBarCustomizeContextMenuPlanner"/>.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FreeXStatusBarRendererPlannerAvaloniaTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static Task RunOnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static readonly IStatusBarTextProvider TextProvider =
        new ResourceKeyStatusBarTextProvider(UiText.Get);

    private static Dictionary<string, bool> DefaultVisibility() =>
        StatusBarVisibilityPlanner.CreateDefaultOptionVisibility(StatusBarOptionVisibility.ExcelDefaults);

    private static StatusBarViewModel BuildModel(
        WorkbookSelectionStats stats,
        int zoomPercent,
        string readyText,
        WorksheetViewMode viewMode = WorksheetViewMode.Normal) =>
        FreeXStatusBarRendererPlanner.BuildModel(
            stats,
            zoomPercent,
            readyText,
            viewMode,
            TextProvider);

    // A representative multi-cell selection: Count=4, Numerical Count=3, Sum/Avg/Min/Max numeric.
    private static WorkbookSelectionStats SampleStats() =>
        new(Sum: 60, Count: 4, NumericalCount: 3, Average: 20, Min: 10, Max: 30);

    [Fact]
    public void ReadyText_ResolvesSharedResourceKey()
    {
        Assert.Equal(UiText.Get(StatusBarTextResourceKeys.ReadyText), TextProvider.GetReadyText());
    }

    [Fact]
    public void BuildModel_ProducesSharedStatsModel_ForRepresentativeSelection()
    {
        var model = BuildModel(
            SampleStats(),
            zoomPercent: 100,
            readyText: "Ready",
            viewMode: WorksheetViewMode.PageBreakPreview);

        Assert.Equal(StatusBarViewMode.PageBreak, model.ViewMode);
        Assert.Equal(100, model.ZoomPercent);
        Assert.True(model.AreStatsVisible);
        Assert.False(model.IsReadyVisible);

        // The model carries the shared builder's readouts in order with the portable resource provider's labels.
        Assert.Equal("Average: 20", model.FindReadout(StatusBarReadoutKind.Average)!.Value.Value);
        Assert.Equal("Count: 4", model.FindReadout(StatusBarReadoutKind.Count)!.Value.Value);
        Assert.Equal("Numerical Count: 3", model.FindReadout(StatusBarReadoutKind.NumericalCount)!.Value.Value);
        Assert.Equal("Sum: 60", model.FindReadout(StatusBarReadoutKind.Sum)!.Value.Value);
        Assert.Equal("Min: 10", model.FindReadout(StatusBarReadoutKind.Minimum)!.Value.Value);
        Assert.Equal("Max: 30", model.FindReadout(StatusBarReadoutKind.Maximum)!.Value.Value);
    }

    [Fact]
    public void BuildModel_EmptySelection_ProducesReadyModel()
    {
        var empty = new WorkbookSelectionStats(0, 0, 0, null, null, null);
        var model = BuildModel(
            empty,
            zoomPercent: 80,
            readyText: "Ready",
            viewMode: WorksheetViewMode.PageLayout);

        Assert.Equal(StatusBarViewMode.PageLayout, model.ViewMode);
        Assert.True(model.IsReadyVisible);
        Assert.False(model.AreStatsVisible);
        Assert.Equal("Ready", model.ReadyText);
        Assert.Empty(model.Readouts);
    }

    [Fact]
    public void BuildReadyText_UsesSharedInputPromptPlanner()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(address, address),
            ShowInputMessage = true,
            PromptTitle = "Input",
            PromptMessage = "Use a number"
        });

        var text = FreeXStatusBarRendererPlanner.BuildReadyText(sheet, address, TextProvider);

        Assert.Equal("Input: Use a number", text);
    }

    [Fact]
    public void FormatVisibleReadouts_JoinsDefaultVisibleReadouts_InWpfOrder()
    {
        var visibility = DefaultVisibility();
        var model = BuildModel(SampleStats(), zoomPercent: 100, readyText: "Ready");

        var text = FreeXStatusBarRendererPlanner.BuildRendererPlan(model, visibility).VisibleReadoutText;

        // Defaults mirror WPF: Average, Count, Sum are on; Numerical Count/Min/Max are off.
        Assert.Equal("Average: 20   Count: 4   Sum: 60", text);
    }

    [Fact]
    public void FormatVisibleReadouts_HonorsPerOptionToggles()
    {
        var visibility = DefaultVisibility();
        visibility["Average"] = false;
        visibility["NumericalCount"] = true;
        visibility["Sum"] = false;
        visibility["Maximum"] = true;
        var model = BuildModel(SampleStats(), zoomPercent: 100, readyText: "Ready");

        var text = FreeXStatusBarRendererPlanner.BuildRendererPlan(model, visibility).VisibleReadoutText;

        // Average + Sum hidden, Maximum re-enabled.
        Assert.Equal("Count: 4   Numerical Count: 3   Max: 30", text);
    }

    [Fact]
    public void FormatVisibleReadouts_EmptyForReadyModel()
    {
        var visibility = DefaultVisibility();
        var ready = BuildModel(
            new WorkbookSelectionStats(0, 0, 0, null, null, null),
            zoomPercent: 100,
            readyText: "Ready");

        Assert.Equal("", FreeXStatusBarRendererPlanner.BuildRendererPlan(ready, visibility).VisibleReadoutText);
    }

    [Fact]
    public void BuildPresentation_UsesSharedStatusBarPlan()
    {
        var visibility = DefaultVisibility();
        visibility["NumericalCount"] = true;
        visibility["Sum"] = false;
        var model = BuildModel(SampleStats(), zoomPercent: 90, readyText: "Ready");

        var plan = FreeXStatusBarRendererPlanner.BuildRendererPlan(model, visibility);

        Assert.True(plan.IsElementVisible(StatusBarPresentationElement.StatsPanel));
        Assert.True(plan.IsElementVisible(StatusBarPresentationElement.ZoomText));
        Assert.Equal(90, plan.ZoomPercent);
        Assert.Contains(plan.ReadoutElements, readout => readout.Kind == StatusBarReadoutKind.Sum && readout.Text == "Sum: 60");
        Assert.Equal("Average: 20   Count: 4   Numerical Count: 3", plan.VisibleReadoutText);
        Assert.Equal("Average: 20; Count: 4; Numerical Count: 3", plan.StatsPanelAutomationText);
    }

    [Fact]
    public void BuildRendererPlan_UsesSharedStatusBarRendererPlan()
    {
        var visibility = DefaultVisibility();
        visibility["NumericalCount"] = true;
        visibility["Sum"] = false;
        var model = BuildModel(SampleStats(), zoomPercent: 90, readyText: "Ready");

        var plan = FreeXStatusBarRendererPlanner.BuildRendererPlan(model, visibility);

        Assert.False(plan.ReadyTextVisible);
        Assert.Equal("Average: 20   Count: 4   Numerical Count: 3", plan.VisibleReadoutText);
        Assert.True(plan.VisibleReadoutTextVisible);
        Assert.Equal(90, plan.ZoomPercent);
        Assert.True(plan.IsElementVisible(StatusBarPresentationElement.StatsPanel));
        Assert.True(plan.IsElementVisible(StatusBarPresentationElement.ZoomText));
        Assert.True(plan.IsElementVisible(StatusBarPresentationElement.ZoomSlider));
        Assert.Contains(
            plan.ReadoutElements,
            readout => readout.Kind == StatusBarReadoutKind.Sum && readout.Text == "Sum: 60");
    }

    [Fact]
    public Task CustomizeMenu_IsBuiltFromPlanner_WithExpectedToggleItems() => RunOnUiThread(() =>
    {
        var options = DefaultVisibility();
        var registered = new Dictionary<string, MenuItem>(StringComparer.Ordinal);

        var menu = AvaloniaStatusBarCustomizeMenu.Build(
            optionTag => options.TryGetValue(optionTag, out var v) && v,
            (optionTag, isChecked) => options[optionTag] = isChecked,
            registered);

        var items = ((IEnumerable<Control>)menu.ItemsSource!).ToList();

        // The plan opens with a disabled title, a separator, then the toggle groups.
        var title = Assert.IsType<MenuItem>(items[0]);
        Assert.Equal("Customize Status Bar", title.Header);
        Assert.False(title.IsEnabled);
        Assert.IsType<Separator>(items[1]);

        // Every checkable toggle from the planner is registered with the expected OptionTag set.
        var expectedTags = StatusBarCustomizeContextMenuPlanner.BuildStatusBarCustomizeCommands()
            .Where(c => c.IsCheckable)
            .Select(c => c.OptionTag)
            .ToHashSet();
        Assert.Equal(expectedTags, registered.Keys.ToHashSet());

        // The toggle items are checkboxes reflecting the supplied option state.
        Assert.True(registered["Sum"].IsChecked);     // default-on
        Assert.False(registered["NumericalCount"].IsChecked); // default-off
        Assert.False(registered["Minimum"].IsChecked); // default-off
        Assert.All(registered.Values, item => Assert.Equal(MenuItemToggleType.CheckBox, item.ToggleType));

        // Headers mirror the WPF host's StatusBar_* resource values.
        Assert.Equal("Cell Mode", registered["CellMode"].Header);
        Assert.Equal("Numerical Count", registered["NumericalCount"].Header);
        Assert.Equal("Zoom Slider", registered["ZoomSlider"].Header);
    });

    [Fact]
    public Task CustomizeMenu_ToggleClick_FlipsOptionState() => RunOnUiThread(() =>
    {
        var options = DefaultVisibility();
        var registered = new Dictionary<string, MenuItem>(StringComparer.Ordinal);

        var menu = AvaloniaStatusBarCustomizeMenu.Build(
            optionTag => options.TryGetValue(optionTag, out var v) && v,
            (optionTag, isChecked) => options[optionTag] = isChecked,
            registered);
        _ = menu; // keep the menu rooted while we drive its item

        // Sum starts on; clicking the checkbox toggles it off and routes through onToggle.
        var sumItem = registered["Sum"];
        Assert.True(sumItem.IsChecked);
        sumItem.IsChecked = false;
        sumItem.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        Assert.False(options["Sum"]);
    });
}
