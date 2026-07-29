using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartProtectionOptionsDialog : Window
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = new(FontFamily.Default);
    private readonly EditingSession _editor;
    private readonly ChartProtectionOptionsPlanner _planner;
    private readonly ComboBox _chartObjectCombo;
    private readonly ComboBox _dataCombo;
    private readonly ComboBox _formattingCombo;
    private readonly ComboBox _selectionCombo;

    internal ChartProtectionOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        _planner = ChartProtectionOptionsPlanner.FromChart(chart);
        var surface = ChartProtectionOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartProtectionOptionsPlanner.DefaultDialogWidth;
        Height = ChartProtectionOptionsPlanner.DefaultDialogHeight;
        MinWidth = 380;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _chartObjectCombo = BuildBooleanCombo(_planner.ChartObject);
        _dataCombo = BuildBooleanCombo(_planner.Data);
        _formattingCombo = BuildBooleanCombo(_planner.Formatting);
        _selectionCombo = BuildBooleanCombo(_planner.Selection);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                MakeButton(surface.OkLabel, true, OnOk),
                MakeButton(surface.CancelLabel, false, () => Close(false)),
            },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                MakeRow(surface.ChartObjectLabel, _chartObjectCombo),
                MakeRow(surface.DataLabel, _dataCombo),
                MakeRow(surface.FormattingLabel, _formattingCombo),
                MakeRow(surface.SelectionLabel, _selectionCombo),
                new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 },
                buttons,
            },
        };
    }

    internal ChartProtectionOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(bool? chartObject, bool? data, bool? formatting, bool? selection)
    {
        _chartObjectCombo.SelectedIndex = FindBooleanIndex(chartObject);
        _dataCombo.SelectedIndex = FindBooleanIndex(data);
        _formattingCombo.SelectedIndex = FindBooleanIndex(formatting);
        _selectionCombo.SelectedIndex = FindBooleanIndex(selection);
    }

    private void OnOk()
    {
        _editor.ApplyChartProtectionOptions(BuildCommitPlanForTests());
        Close(true);
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetChartObject(ReadBoolean(_chartObjectCombo));
        _planner.SetData(ReadBoolean(_dataCombo));
        _planner.SetFormatting(ReadBoolean(_formattingCombo));
        _planner.SetSelection(ReadBoolean(_selectionCombo));
    }

    private static ComboBox BuildBooleanCombo(bool? value) => new()
    {
        ItemsSource = ChartProtectionOptionsPlanner.BooleanOptions.Select(option => option.Label).ToArray(),
        SelectedIndex = FindBooleanIndex(value),
        MinWidth = 180,
    };

    private static bool? ReadBoolean(ComboBox combo)
    {
        var index = combo.SelectedIndex;
        return index >= 0 && index < ChartProtectionOptionsPlanner.BooleanOptions.Count
            ? ChartProtectionOptionsPlanner.BooleanOptions[index].Value
            : null;
    }

    private static int FindBooleanIndex(bool? value) => Math.Max(0,
        ChartProtectionOptionsPlanner.BooleanOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static Control MakeRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180, *") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private static Button MakeButton(string label, bool isDefault, Action action)
    {
        var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyButton(button, DialogChromeStyle, minWidth: 80, isDefault: isDefault);
        button.Click += (_, _) => action();
        return button;
    }
}
