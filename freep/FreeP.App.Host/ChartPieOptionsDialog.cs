using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style first-slice and doughnut-hole options dialog.</summary>
public sealed class ChartPieOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartPieOptionsPlanner _planner;
    private readonly TextBox _angleBox;
    private readonly TextBox _holeBox;
    private readonly ComboBox? _ofPieTypeCombo;
    private readonly ComboBox? _ofPieSplitTypeCombo;
    private readonly TextBox? _ofPieSplitPositionBox;
    private readonly TextBox? _ofPieSizeBox;
    private readonly TextBox? _ofPieCustomPointsBox;
    private readonly TextBox? _ofPieGapWidthBox;
    private readonly CheckBox? _ofPieSeriesLinesCheck;

    public ChartPieOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        if (chart.ChartType is not (ChartType.Pie or ChartType.Doughnut or ChartType.OfPie))
            throw new InvalidOperationException("Select a pie, doughnut, or pie-of-pie chart before editing pie options.");

        _planner = ChartPieOptionsPlanner.FromChart(chart);
        var surface = ChartPieOptionsPlanner.BuildSurfacePlan();
        Title = surface.Title;
        Width = ChartPieOptionsPlanner.DefaultDialogWidth;
        Height = chart.ChartType == ChartType.OfPie ? ChartPieOptionsPlanner.DefaultDialogHeight : 250;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _angleBox = new TextBox
        {
            Text = (_planner.FirstSliceAngleDegrees ?? 0).ToString(CultureInfo.CurrentCulture),
            MinWidth = 150,
        };
        _holeBox = new TextBox
        {
            Text = _planner.DoughnutHolePercent.ToString(CultureInfo.CurrentCulture),
            MinWidth = 150,
            IsEnabled = chart.ChartType == ChartType.Doughnut,
        };

        if (chart.ChartType == ChartType.OfPie)
        {
            _ofPieTypeCombo = new ComboBox { ItemsSource = new[] { "Pie", "Bar" }, SelectedIndex = _planner.OfPieType == OfPieType.Bar ? 1 : 0, MinWidth = 150 };
            _ofPieSplitTypeCombo = new ComboBox { ItemsSource = new[] { "Auto", "Custom", "Percent", "Position", "Value" }, SelectedIndex = (int)_planner.OfPieSplitType, MinWidth = 150 };
            _ofPieSplitPositionBox = new TextBox { Text = (_planner.OfPieSplitPosition ?? 0).ToString(CultureInfo.CurrentCulture), MinWidth = 150 };
            _ofPieSizeBox = new TextBox { Text = _planner.OfPieSecondPieSizePercent.ToString(CultureInfo.CurrentCulture), MinWidth = 150 };
            _ofPieCustomPointsBox = new TextBox { Text = string.Join(",", _planner.OfPieCustomPointIndices), MinWidth = 150 };
            _ofPieGapWidthBox = new TextBox { Text = _planner.OfPieGapWidthPercent?.ToString(CultureInfo.CurrentCulture) ?? string.Empty, MinWidth = 150 };
            _ofPieSeriesLinesCheck = new CheckBox { IsChecked = _planner.OfPieSeriesLines, MinWidth = 150 };
        }

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            Close,
            new Thickness(8, 14, 8, 8));

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FirstSliceAngleLabel, _angleBox, 220));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.DoughnutHoleLabel, _holeBox, 220));
        if (chart.ChartType == ChartType.OfPie)
        {
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieTypeLabel, _ofPieTypeCombo!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieSplitTypeLabel, _ofPieSplitTypeCombo!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieSplitPositionLabel, _ofPieSplitPositionBox!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieSecondPieSizeLabel, _ofPieSizeBox!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieCustomPointIndicesLabel, _ofPieCustomPointsBox!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieGapWidthLabel, _ofPieGapWidthBox!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieSeriesLinesLabel, _ofPieSeriesLinesCheck!, 220));
        }
        content.Children.Add(new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartPieOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(int? firstSliceAngleDegrees, int doughnutHolePercent)
    {
        _angleBox.Text = (firstSliceAngleDegrees ?? 0).ToString(CultureInfo.CurrentCulture);
        _holeBox.Text = doughnutHolePercent.ToString(CultureInfo.CurrentCulture);
    }

    internal void SetOfPieOptionsForTests(OfPieType type, OfPieSplitType splitType, double? splitPosition, int secondPieSizePercent, string customPointIndices, int? gapWidthPercent = null, bool seriesLines = false)
    {
        if (_ofPieTypeCombo is null)
            throw new InvalidOperationException("The selected chart is not an OfPie chart.");
        _ofPieTypeCombo.SelectedIndex = type == OfPieType.Bar ? 1 : 0;
        _ofPieSplitTypeCombo!.SelectedIndex = (int)splitType;
        _ofPieSplitPositionBox!.Text = (splitPosition ?? 0).ToString(CultureInfo.CurrentCulture);
        _ofPieSizeBox!.Text = secondPieSizePercent.ToString(CultureInfo.CurrentCulture);
        _ofPieCustomPointsBox!.Text = customPointIndices;
        _ofPieGapWidthBox!.Text = (gapWidthPercent?.ToString(CultureInfo.CurrentCulture)) ?? string.Empty;
        _ofPieSeriesLinesCheck!.IsChecked = seriesLines;
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartPieOptions(BuildCommitPlanForTests());
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetFirstSliceAngleDegrees(ParseAngle(_angleBox.Text));
        _planner.SetDoughnutHolePercent(ParseHole(_holeBox.Text));
        if (_ofPieTypeCombo is null)
            return;

        _planner.SetOfPieType(_ofPieTypeCombo.SelectedIndex == 1 ? OfPieType.Bar : OfPieType.Pie);
        _planner.SetOfPieSplitType((OfPieSplitType)Math.Clamp(_ofPieSplitTypeCombo!.SelectedIndex, 0, 4));
        _planner.SetOfPieSplitPosition(ParseOptionalDouble(_ofPieSplitPositionBox!.Text));
        _planner.SetOfPieSecondPieSizePercent(ParseOfPieSize(_ofPieSizeBox!.Text));
        _planner.SetOfPieCustomPointIndices(ParsePointIndices(_ofPieCustomPointsBox!.Text));
        _planner.SetOfPieGapWidthPercent(ParseOptionalInt(_ofPieGapWidthBox!.Text, 0, 500, "Secondary plot gap width"));
        _planner.SetOfPieSeriesLines(_ofPieSeriesLinesCheck!.IsChecked == true);
    }

    private static int ParseAngle(string? text)
    {
        return ChartDialogOptionProjection.ParseRequiredInt(
            text,
            CultureInfo.CurrentCulture,
            value => value is >= 0 and <= 359,
            "First slice angle must be a whole number from 0 to 359.");
    }

    private static int ParseHole(string? text)
    {
        return ChartDialogOptionProjection.ParseRequiredInt(
            text,
            CultureInfo.CurrentCulture,
            value => value is >= 10 and <= 90,
            "Doughnut hole must be a whole number from 10 to 90.");
    }

    private static double? ParseOptionalDouble(string? text)
    {
        return ChartDialogOptionProjection.ParseOptionalDouble(
            text,
            CultureInfo.CurrentCulture,
            value => value >= 0,
            "OfPie split position must be a non-negative number or blank.");
    }

    private static int ParseOfPieSize(string? text)
    {
        return ChartDialogOptionProjection.ParseRequiredInt(
            text,
            CultureInfo.CurrentCulture,
            value => value is >= 5 and <= 200,
            "Secondary plot size must be a whole number from 5 to 200.");
    }

    private static int? ParseOptionalInt(string? text, int min, int max, string label)
    {
        return ChartDialogOptionProjection.ParseOptionalInt(
            text,
            CultureInfo.CurrentCulture,
            value => value >= min && value <= max,
            $"{label} must be a whole number from {min} to {max}, or blank.");
    }

    private static IReadOnlyList<int> ParsePointIndices(string? text)
    {
        return ChartDialogOptionProjection.ParseNonNegativeIntList(
            text,
            CultureInfo.CurrentCulture,
            "Custom secondary points must be non-negative whole numbers separated by commas.");
    }
}
