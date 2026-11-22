using System.Globalization;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AutoFilterCriteriaFlyoutTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompoundConditionsApplyAndReopenForWorksheetOrTable(bool useTable)
    {
        await Session.Dispatch(() =>
        {
            using var fixture = new FilterFixture(
                [new NumberValue(10), new NumberValue(20), new NumberValue(30), new NumberValue(40)],
                useTable);
            var panel = fixture.Open();
            var options = AutoFilterMenuPlanner.CreateCriteriaOptions(
                AutoFilterMenuFilterKind.Number,
                InvariantAutoFilterMenuTextProvider.Instance);
            var secondOptions = AutoFilterDialogCriteriaPlanner.GetSecondRowCriteriaOptions(options);
            var firstIndex = options.ToList().FindIndex(option => option.CriteriaPrefix == ">");
            var secondIndex = secondOptions.ToList().FindIndex(option => option.CriteriaPrefix == "<");
            var firstOperator = Find<ComboBox>(panel, "AutoFilterCriteriaOperatorBox");
            var secondOperator = Find<ComboBox>(panel, "AutoFilterSecondCriteriaOperatorBox");
            firstOperator.SelectedIndex = firstIndex;
            Find<TextBox>(panel, "AutoFilterCriteriaValueBox").Text = "15";
            secondOperator.SelectedIndex = secondIndex;
            Find<TextBox>(panel, "AutoFilterSecondCriteriaValueBox").Text = "35";
            Assert.Equal("and:>15|<35", RawCriteria(panel).Text);
            Click(panel, "AutoFilterOkButton");
            Assert.Equal([2u, 5u], fixture.Sheet.FilterHiddenRows.Order());

            panel = fixture.Open();
            Assert.Equal(firstIndex, Find<ComboBox>(panel, "AutoFilterCriteriaOperatorBox").SelectedIndex);
            Assert.Equal(secondIndex, Find<ComboBox>(panel, "AutoFilterSecondCriteriaOperatorBox").SelectedIndex);
            Assert.Equal("15", Find<TextBox>(panel, "AutoFilterCriteriaValueBox").Text);
            Assert.Equal("35", Find<TextBox>(panel, "AutoFilterSecondCriteriaValueBox").Text);
            var connector = Find<ComboBox>(panel, "AutoFilterCriteriaConnectorBox");
            Assert.Equal(0, connector.SelectedIndex);
            connector.SelectedIndex = 1;
            Assert.Equal("or:>15|<35", RawCriteria(panel).Text);
            Click(panel, "AutoFilterCancelButton");
            Assert.Equal([2u, 5u], fixture.Sheet.FilterHiddenRows.Order());

            panel = fixture.Open();
            Find<ComboBox>(panel, "AutoFilterCriteriaConnectorBox").SelectedIndex = 1;
            Click(panel, "AutoFilterOkButton");
            Assert.Empty(fixture.Sheet.FilterHiddenRows);

            panel = fixture.Open();
            Assert.Equal(1, Find<ComboBox>(panel, "AutoFilterCriteriaConnectorBox").SelectedIndex);
            RawCriteria(panel).Text = "=20";
            Click(panel, "AutoFilterOkButton");
            Assert.Equal([2u, 4u, 5u], fixture.Sheet.FilterHiddenRows.Order());

            panel = fixture.Open();
            var valueToAdd = panel.GetLogicalDescendants().OfType<CheckBox>().Single(box => Equals(box.Tag, "40"));
            valueToAdd.IsChecked = true;
            Click(panel, "AutoFilterOkButton");
            Assert.Equal([2u, 4u], fixture.Sheet.FilterHiddenRows.Order());
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DatePresetFiltersTheColumnAndReopensAsAnEditableDate()
    {
        await Session.Dispatch(() =>
        {
            var today = DateTime.Today;
            using var fixture = new FilterFixture(
                [DateTimeValue.FromDateTime(today.AddDays(-1)),
                 DateTimeValue.FromDateTime(today),
                 DateTimeValue.FromDateTime(today.AddDays(1))]);
            var panel = fixture.Open();
            var datePreset = Find<ComboBox>(panel, "AutoFilterDatePresetBox");
            var firstOperator = Find<ComboBox>(panel, "AutoFilterCriteriaOperatorBox");
            datePreset.SelectedIndex = 1;
            Assert.False(firstOperator.IsEnabled);
            var secondOperatorRow = Assert.IsAssignableFrom<Control>(
                Find<ComboBox>(panel, "AutoFilterSecondCriteriaOperatorBox").Parent);
            Assert.False(secondOperatorRow.IsVisible);
            var expectedCriteria = AutoFilterDialogCriteriaPlanner.BuildDatePresetCriteriaText("Today", today);
            Assert.Equal(expectedCriteria, RawCriteria(panel).Text);
            Click(panel, "AutoFilterOkButton");
            Assert.Equal([2u, 4u], fixture.Sheet.FilterHiddenRows.Order());

            panel = fixture.Open();
            Assert.Equal(0, Find<ComboBox>(panel, "AutoFilterDatePresetBox").SelectedIndex);
            Assert.Equal(expectedCriteria, RawCriteria(panel).Text);
            Assert.Equal(expectedCriteria["date=".Length..], Find<TextBox>(panel, "AutoFilterCriteriaValueBox").Text);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SecondOperatorHasNoNoneRowAndNeedsAValueLikeWpf()
    {
        await Session.Dispatch(() =>
        {
            using var fixture = new FilterFixture([new NumberValue(10), new NumberValue(20)]);
            var panel = fixture.Open();
            var options = AutoFilterMenuPlanner.CreateCriteriaOptions(
                AutoFilterMenuFilterKind.Number,
                InvariantAutoFilterMenuTextProvider.Instance);
            var secondOptions = AutoFilterDialogCriteriaPlanner.GetSecondRowCriteriaOptions(options);
            var secondOperator = Find<ComboBox>(panel, "AutoFilterSecondCriteriaOperatorBox");
            Assert.Equal(secondOptions.Count, secondOperator.ItemCount);
            Assert.Equal(0, secondOperator.SelectedIndex);
            Assert.True(Find<ComboBox>(panel, "AutoFilterCriteriaConnectorBox").IsEnabled);

            Find<ComboBox>(panel, "AutoFilterCriteriaOperatorBox").SelectedIndex =
                options.ToList().FindIndex(option => option.CriteriaPrefix == ">");
            Find<TextBox>(panel, "AutoFilterCriteriaValueBox").Text = "15";
            Assert.Equal(">15", RawCriteria(panel).Text);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BetweenAndTopBottomUseWpfHorizontalLabelledRows()
    {
        await Session.Dispatch(() =>
        {
            using var fixture = new FilterFixture([new NumberValue(10), new NumberValue(20)]);
            var panel = fixture.Open();
            var options = AutoFilterMenuPlanner.CreateCriteriaOptions(
                AutoFilterMenuFilterKind.Number,
                InvariantAutoFilterMenuTextProvider.Instance);
            var selector = Find<ComboBox>(panel, "AutoFilterCriteriaOperatorBox");
            var minimum = Find<TextBox>(panel, "AutoFilterCriteriaValueBox");
            var maximum = Find<TextBox>(panel, "AutoFilterCriteriaMaximumBox");
            var texts = () => panel.GetLogicalDescendants().OfType<TextBlock>()
                .Where(block => block.IsVisible).Select(block => block.Text).ToList();

            selector.SelectedIndex = options.ToList().FindIndex(option => AutoFilterDialogCriteriaPlanner.IsBetweenOption(option));
            Assert.True(maximum.IsVisible);
            Assert.Same(minimum.Parent, maximum.Parent);
            Assert.Contains(UiText.Get("AutoFilter_MinimumLabel"), texts());
            Assert.Contains(UiText.Get("AutoFilter_MaximumLabel"), texts());

            selector.SelectedIndex = options.ToList().FindIndex(option => AutoFilterDialogCriteriaPlanner.IsTopBottomOption(option));
            Assert.False(maximum.IsVisible);
            Assert.Equal(54, minimum.Width);
            Assert.Equal("10", minimum.Text);
            Assert.Contains(UiText.Get("AutoFilter_ShowLabel"), texts());
            Assert.Contains(UiText.Get("AutoFilter_Items"), texts());
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ColorPanelGroupsSwatchesBySectionLikeWpf()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var create = typeof(MainWindow).GetMethod(
                    "CreateAutoFilterColorPanel", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var options = new List<AutoFilterColorOption>
                {
                    new("#00B050", AutoFilterColorFilterKind.FontColor, new CellColor(0, 176, 80)),
                    new("#FFC000", AutoFilterColorFilterKind.CellFillColor, new CellColor(255, 192, 0)),
                    new("No Fill", AutoFilterColorFilterKind.NoFill, null),
                };
                var root = Assert.IsAssignableFrom<Control>(
                    create.Invoke(window, [options, new Action<AutoFilterColorOption>(_ => { }), "AutoFilter_FilterByColor"]));
                var headers = root.GetLogicalDescendants().OfType<TextBlock>()
                    .Select(block => block.Text).ToList();
                Assert.Contains(UiText.Get("AutoFilter_FontColor"), headers);
                Assert.Contains(UiText.Get("AutoFilter_CellColor"), headers);
                var buttons = root.GetLogicalDescendants().OfType<Button>().ToList();
                Assert.Equal(3, buttons.Count);
                Assert.All(buttons, button =>
                {
                    Assert.Equal(92, button.Width);
                    Assert.Equal(24, button.Height);
                });
                var buttonTexts = buttons
                    .Select(button => button.GetLogicalDescendants().OfType<TextBlock>().Single().Text).ToList();
                Assert.Equal(["#00B050", "#FFC000", UiText.Get("AutoFilter_NoFill")], buttonTexts);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Today", "date=2026-01-12")]
    [InlineData("This Month", "datebetween:2026-01-01:2026-01-31")]
    public void DatePresetsUseTheIsoCalendarRequiredByTheFilterParser(string preset, string expected)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
            Assert.Equal(expected, AutoFilterDialogCriteriaPlanner.BuildDatePresetCriteriaText(
                preset, new DateTime(2026, 1, 12)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static TextBox RawCriteria(Control panel) => Find<TextBox>(panel, "AutoFilterCriteriaBox_1_1");

    private static void Click(Control panel, string id) =>
        Find<Button>(panel, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Find<T>(Control panel, string id) where T : Control =>
        panel.GetLogicalDescendants().OfType<T>()
            .Single(control => AutomationProperties.GetAutomationId(control) == id);

    private sealed class FilterFixture : IDisposable
    {
        private readonly MainWindow _window = new([]);
        private static readonly MethodInfo OpenFlyout = typeof(MainWindow).GetMethod(
            "OpenAutoFilterFlyout", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo CurrentFlyout = typeof(MainWindow).GetField(
            "_autoFilterFlyout", BindingFlags.Instance | BindingFlags.NonPublic)!;

        public Sheet Sheet { get; }

        public FilterFixture(ScalarValue[] values, bool useTable = false)
        {
            Sheet = _window.Session.ActiveSheet;
            Sheet.SetCell(new CellAddress(Sheet.Id, 1, 1), new TextValue("Filter"));
            for (var index = 0; index < values.Length; index++)
                Sheet.SetCell(new CellAddress(Sheet.Id, (uint)(index + 2), 1), values[index]);

            var range = new GridRange(new CellAddress(Sheet.Id, 1, 1), new CellAddress(Sheet.Id, (uint)(values.Length + 1), 1));
            if (useTable)
            {
                Sheet.StructuredTables.Add(new StructuredTableModel
                {
                    Id = 1,
                    Name = "FilterTable",
                    DisplayName = "FilterTable",
                    Range = range,
                    HasAutoFilter = true,
                });
            }
            else
            {
                Sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
            }

            _window.Show();
        }

        public Control Open()
        {
            OpenFlyout.Invoke(_window, [_window, new CellAddress(Sheet.Id, 1, 1)]);
            return Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(CurrentFlyout.GetValue(_window)).Content);
        }

        public void Dispose()
        {
            (CurrentFlyout.GetValue(_window) as Flyout)?.Hide();
            _window.AllowCloseWithoutDirtyPromptForParityCapture();
            _window.Close();
        }
    }
}
