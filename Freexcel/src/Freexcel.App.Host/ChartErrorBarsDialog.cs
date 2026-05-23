using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

using static Freexcel.App.Host.ChartDialogHelpers;

namespace Freexcel.App.Host;

public sealed record ChartErrorBarsDialogResult(
    bool ShowErrorBars,
    ChartErrorBarKind Kind,
    ChartErrorBarDirection Direction,
    double Value,
    bool EndCaps)
{
    public ChartLayoutOptions ToOptions() => new(
        ShowErrorBars: ShowErrorBars,
        ErrorBarKind: Kind,
        ErrorBarDirection: Direction,
        ErrorBarValue: Value,
        ErrorBarEndCaps: EndCaps);
}

public sealed class ChartErrorBarsDialog : Window
{
    private readonly CheckBox _showBox = new() { Content = "_Show error bars" };
    private readonly CheckBox _endCapsBox = new() { Content = "_End caps" };
    private readonly ComboBox _kindBox = new();
    private readonly ComboBox _directionBox = new();
    private readonly TextBox _valueBox = new();

    public ChartErrorBarsDialogResult Result { get; private set; }

    public ChartErrorBarsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "Format Error Bars";
        Width = 360;
        Height = 290;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static ChartErrorBarsDialogResult FromChart(ChartModel chart) => CreateResult(
        chart.ShowErrorBars,
        chart.ErrorBarKind,
        chart.ErrorBarDirection,
        chart.ErrorBarValue,
        chart.ErrorBarEndCaps);

    public static ChartErrorBarsDialogResult CreateResult(
        bool showErrorBars,
        ChartErrorBarKind kind,
        ChartErrorBarDirection direction,
        double value,
        bool endCaps) =>
        new(
            showErrorBars,
            Enum.IsDefined(kind) ? kind : ChartErrorBarKind.StandardError,
            Enum.IsDefined(direction) ? direction : ChartErrorBarDirection.Both,
            Math.Clamp(double.IsFinite(value) ? value : 5, 0, 1000),
            endCaps);

    private StackPanel CreateContent()
    {
        var root = ChartDialogHelpers.DialogStack();
        var stack = new StackPanel();
        ChartDialogHelpers.AddCheck(stack, _showBox);
        ChartDialogHelpers.AddCombo(stack, "Type", _kindBox, Enum.GetValues<ChartErrorBarKind>());
        ChartDialogHelpers.AddCombo(stack, "Direction", _directionBox, Enum.GetValues<ChartErrorBarDirection>());
        ChartDialogHelpers.AddNumericText(stack, "Value", _valueBox, "Enter the error amount or percentage.");
        ChartDialogHelpers.AddCheck(stack, _endCapsBox);
        root.Children.Add(CreateGroupBox("Error Amount", stack));
        root.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return root;
    }

    private void Load(ChartErrorBarsDialogResult result)
    {
        _showBox.IsChecked = result.ShowErrorBars;
        _kindBox.SelectedItem = result.Kind;
        _directionBox.SelectedItem = result.Direction;
        _valueBox.Text = result.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _endCapsBox.IsChecked = result.EndCaps;
    }

    private void Accept()
    {
        Result = CreateResult(
            _showBox.IsChecked == true,
            ChartDialogHelpers.Selected(_kindBox, ChartErrorBarKind.StandardError),
            ChartDialogHelpers.Selected(_directionBox, ChartErrorBarDirection.Both),
            ChartDialogHelpers.ParseDouble(_valueBox.Text, 5),
            _endCapsBox.IsChecked == true);
        DialogResult = true;
    }
}
