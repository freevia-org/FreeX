using FluentAssertions;
using Free.Shared.AppServices;

namespace FreeX.App.Services.Tests;

public sealed class StatusBarVisibilityPlannerTests
{
    private sealed class TestTextProvider : IStatusBarTextProvider
    {
        public string GetReadoutFormat(StatusBarReadoutKind kind) => GetReadoutLabel(kind) + ": {0}";

        public string GetReadoutLabel(StatusBarReadoutKind kind) => kind switch
        {
            StatusBarReadoutKind.Average => "Average",
            StatusBarReadoutKind.Count => "Count",
            StatusBarReadoutKind.NumericalCount => "Numerical Count",
            StatusBarReadoutKind.Sum => "Sum",
            StatusBarReadoutKind.Minimum => "Min",
            StatusBarReadoutKind.Maximum => "Max",
            _ => kind.ToString()
        };
    }

    private sealed class TestStatusBarOptions : IStatusBarOptionVisibilityStore
    {
        public bool StatusBarShowCellMode { get; set; }
        public bool StatusBarShowEndMode { get; set; }
        public bool StatusBarShowSelectionMode { get; set; }
        public bool StatusBarShowPageNumber { get; set; }
        public bool StatusBarShowAverage { get; set; }
        public bool StatusBarShowCount { get; set; }
        public bool StatusBarShowNumericalCount { get; set; }
        public bool StatusBarShowMinimum { get; set; }
        public bool StatusBarShowMaximum { get; set; }
        public bool StatusBarShowSum { get; set; }
        public bool StatusBarShowViewShortcuts { get; set; }
        public bool StatusBarShowZoom { get; set; }
        public bool StatusBarShowZoomSlider { get; set; }
    }

    private static readonly TestTextProvider Text = new();

    [Fact]
    public void DefaultProfiles_PreserveWpfAndAvaloniaDifferences()
    {
        StatusBarOptionVisibility.ExcelDefaults.SelectionMode.Should().BeFalse();
        StatusBarOptionVisibility.ExcelDefaults.NumericalCount.Should().BeFalse();
        StatusBarOptionVisibility.ExcelDefaults.Minimum.Should().BeFalse();
        StatusBarOptionVisibility.ExcelDefaults.Maximum.Should().BeFalse();
        StatusBarOptionVisibility.ExcelDefaults.Sum.Should().BeTrue();

        StatusBarOptionVisibility.FullReadoutDefaults.SelectionMode.Should().BeTrue();
        StatusBarOptionVisibility.FullReadoutDefaults.NumericalCount.Should().BeTrue();
        StatusBarOptionVisibility.FullReadoutDefaults.Minimum.Should().BeFalse();
        StatusBarOptionVisibility.FullReadoutDefaults.Maximum.Should().BeFalse();
        StatusBarOptionVisibility.FullReadoutDefaults.Sum.Should().BeTrue();
    }

    [Fact]
    public void ReadoutOptionTag_MapsKindsToCustomizeOptionTags()
    {
        StatusBarVisibilityPlanner.ReadoutOptionTag(StatusBarReadoutKind.Average).Should().Be(StatusBarOptionTags.Average);
        StatusBarVisibilityPlanner.ReadoutOptionTag(StatusBarReadoutKind.Count).Should().Be(StatusBarOptionTags.Count);
        StatusBarVisibilityPlanner.ReadoutOptionTag(StatusBarReadoutKind.NumericalCount).Should().Be(StatusBarOptionTags.NumericalCount);
        StatusBarVisibilityPlanner.ReadoutOptionTag(StatusBarReadoutKind.Sum).Should().Be(StatusBarOptionTags.Sum);
        StatusBarVisibilityPlanner.ReadoutOptionTag(StatusBarReadoutKind.Minimum).Should().Be(StatusBarOptionTags.Minimum);
        StatusBarVisibilityPlanner.ReadoutOptionTag(StatusBarReadoutKind.Maximum).Should().Be(StatusBarOptionTags.Maximum);
    }

    [Fact]
    public void FormatVisibleReadouts_UsesModelOrderAndThreeSpaceSeparator()
    {
        var model = StatsModel();

        var text = StatusBarVisibilityPlanner.FormatVisibleReadouts(
            model,
            StatusBarOptionVisibility.FullReadoutDefaults with { Maximum = true });

        text.Should().Be("Average: 20   Count: 4   Numerical Count: 3   Sum: 60   Max: 30");
    }

    [Fact]
    public void FormatVisibleReadouts_HonorsOptionVisibilityAndSkipsBlankReadouts()
    {
        var model = StatusBarDisplayModelBuilder.Stats(
            StatusBarViewMode.Normal,
            zoomPercent: 100,
            new WorkbookSelectionStats(Sum: 0, Count: 3, NumericalCount: 0, Average: null, Min: null, Max: null),
            Text);
        var options = StatusBarOptionVisibility.FullReadoutDefaults with
        {
            Average = true,
            Sum = true,
            Minimum = true,
            Maximum = true
        };

        var text = StatusBarVisibilityPlanner.FormatVisibleReadouts(model, options);

        text.Should().Be("Count: 3   Numerical Count: 0");
    }

    [Fact]
    public void Build_PreservesWpfVisibilityBehaviorForSeparateStatControls()
    {
        var model = StatusBarDisplayModelBuilder.Stats(
            StatusBarViewMode.Normal,
            zoomPercent: 100,
            new WorkbookSelectionStats(Sum: 0, Count: 3, NumericalCount: 0, Average: null, Min: null, Max: null),
            Text);
        var options = StatusBarOptionVisibility.ExcelDefaults with
        {
            Average = true,
            Count = false,
            NumericalCount = false,
            Sum = false,
            Minimum = false,
            Maximum = false,
            Zoom = false,
            ZoomSlider = true
        };

        var plan = StatusBarVisibilityPlanner.Build(
            model,
            options,
            hasPageNumberText: true,
            fallbackAutomationText: "Customize Status Bar");

        plan.StatsPanelVisible.Should().BeTrue();
        plan.AverageVisible.Should().BeTrue();
        plan.VisibleReadoutText.Should().BeEmpty();
        plan.AutomationText.Should().Be("Customize Status Bar");
        plan.PageNumberVisible.Should().BeFalse();
        plan.ZoomControlsVisible.Should().BeTrue();
        plan.InteractiveControlsVisible.Should().BeTrue();
    }

    [Fact]
    public void Build_HidesStatsPanelWhenEveryStatisticOptionIsOff()
    {
        var options = StatusBarOptionVisibility.ExcelDefaults with
        {
            Average = false,
            Count = false,
            NumericalCount = false,
            Sum = false,
            Minimum = false,
            Maximum = false
        };

        var plan = StatusBarVisibilityPlanner.Build(StatsModel(), options, fallbackAutomationText: "Customize Status Bar");

        plan.StatsPanelVisible.Should().BeFalse();
        plan.VisibleReadoutText.Should().BeEmpty();
        plan.AutomationText.Should().Be("Customize Status Bar");
    }

    [Fact]
    public void Build_FormatsAutomationTextWithSemicolons()
    {
        var plan = StatusBarVisibilityPlanner.Build(
            StatsModel(),
            StatusBarOptionVisibility.FullReadoutDefaults with { Average = false, Sum = false, Maximum = true },
            fallbackAutomationText: "Customize Status Bar");

        plan.AutomationText.Should().Be("Count: 4; Numerical Count: 3; Max: 30");
    }

    [Fact]
    public void Build_ReadyModelUsesCellModeAndPageTextInputs()
    {
        var model = StatusBarDisplayModelBuilder.Ready(StatusBarViewMode.PageLayout, zoomPercent: 125, "Ready");
        var options = StatusBarOptionVisibility.ExcelDefaults with
        {
            CellMode = true,
            PageNumber = true
        };

        var withPageText = StatusBarVisibilityPlanner.Build(model, options, hasPageNumberText: true);
        var withoutCellMode = StatusBarVisibilityPlanner.Build(model, options with { CellMode = false }, hasPageNumberText: true);
        var withoutPageText = StatusBarVisibilityPlanner.Build(model, options, hasPageNumberText: false);

        withPageText.ReadyTextVisible.Should().BeTrue();
        withPageText.PageNumberVisible.Should().BeTrue();
        withPageText.StatsPanelVisible.Should().BeFalse();
        withoutCellMode.ReadyTextVisible.Should().BeFalse();
        withoutPageText.PageNumberVisible.Should().BeFalse();
    }

    [Fact]
    public void DictionaryHelpers_RoundTripOptionState()
    {
        var dictionary = StatusBarVisibilityPlanner.CreateDefaultOptionVisibility(StatusBarOptionVisibility.FullReadoutDefaults);

        StatusBarVisibilityPlanner.IsOptionVisible(dictionary, StatusBarOptionTags.NumericalCount).Should().BeTrue();
        StatusBarVisibilityPlanner.IsOptionVisible(dictionary, "Unknown").Should().BeFalse();
        StatusBarVisibilityPlanner.FromOptionVisibility(dictionary).Should().Be(StatusBarOptionVisibility.FullReadoutDefaults);
        StatusBarOptionVisibility.ExcelDefaults
            .With(StatusBarOptionTags.Maximum, true)
            .Maximum.Should().BeTrue();
    }

    [Fact]
    public void OptionStoreHelpers_ProjectApplyAndToggleBySharedOptionTag()
    {
        var store = new TestStatusBarOptions();

        StatusBarOptionVisibilityStore.ApplyVisibility(store, StatusBarOptionVisibility.FullReadoutDefaults with
        {
            PageNumber = true,
            Minimum = true,
            Zoom = false
        });

        var visibility = StatusBarOptionVisibilityStore.ToVisibility(store);
        visibility.SelectionMode.Should().BeTrue();
        visibility.PageNumber.Should().BeTrue();
        visibility.NumericalCount.Should().BeTrue();
        visibility.Minimum.Should().BeTrue();
        visibility.Maximum.Should().BeFalse();
        visibility.Zoom.Should().BeFalse();

        StatusBarOptionVisibilityStore
            .TrySetOption(store, StatusBarOptionTags.Maximum, true)
            .Should()
            .BeTrue();
        StatusBarOptionVisibilityStore
            .TrySetOption(store, "NotAStatusBarOption", true)
            .Should()
            .BeFalse();

        StatusBarOptionVisibilityStore.ToVisibility(store).Maximum.Should().BeTrue();
    }

    private static StatusBarViewModel StatsModel() =>
        StatusBarDisplayModelBuilder.Stats(
            StatusBarViewMode.Normal,
            zoomPercent: 100,
            new WorkbookSelectionStats(Sum: 60, Count: 4, NumericalCount: 3, Average: 20, Min: 10, Max: 30),
            Text);
}
