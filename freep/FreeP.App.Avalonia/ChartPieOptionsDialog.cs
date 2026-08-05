using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartPieOptionsDialog : Window
{
    private readonly ChartPieOptionsDialogSession _session;
    private readonly TextBox _angleBox;
    private readonly TextBox _holeBox;
    private readonly ComboBox? _ofPieTypeCombo;
    private readonly ComboBox? _ofPieSplitTypeCombo;
    private readonly TextBox? _ofPieSplitPositionBox;
    private readonly TextBox? _ofPieSizeBox;
    private readonly TextBox? _ofPieCustomPointsBox;
    private readonly TextBox? _ofPieGapWidthBox;
    private readonly CheckBox? _ofPieSeriesLinesCheck;

    internal ChartPieOptionsDialog(EditingSession editor)
    {
        _session = new ChartPieOptionsDialogSession(editor);
        var state = _session.State;
        var surface = ChartPieOptionsPlanner.BuildSurfacePlan();
        Title = surface.Title;
        Width = ChartPieOptionsPlanner.DefaultDialogWidth;
        Height = state.IsOfPie ? ChartPieOptionsPlanner.DefaultDialogHeight : 250;
        MinWidth = 380;
        MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _angleBox = new TextBox { Text = Format(state.FirstSliceAngleDegrees ?? 0), MinWidth = 150 };
        _holeBox = new TextBox
        {
            Text = Format(state.DoughnutHolePercent),
            MinWidth = 150,
            IsEnabled = state.IsDoughnut,
        };

        if (state.IsOfPie)
        {
            _ofPieTypeCombo = new ComboBox { ItemsSource = state.OfPieTypeOptions, SelectedIndex = state.OfPieTypeIndex, MinWidth = 150 };
            _ofPieSplitTypeCombo = new ComboBox { ItemsSource = state.OfPieSplitTypeOptions, SelectedIndex = state.OfPieSplitTypeIndex, MinWidth = 150 };
            _ofPieSplitPositionBox = new TextBox { Text = Format(state.OfPieSplitPosition ?? 0), MinWidth = 150 };
            _ofPieSizeBox = new TextBox { Text = Format(state.OfPieSecondPieSizePercent), MinWidth = 150 };
            _ofPieCustomPointsBox = new TextBox { Text = string.Join(",", state.OfPieCustomPointIndices), MinWidth = 150 };
            _ofPieGapWidthBox = new TextBox { Text = Format(state.OfPieGapWidthPercent), MinWidth = 150 };
            _ofPieSeriesLinesCheck = new CheckBox { IsChecked = state.OfPieSeriesLines, MinWidth = 150 };
        }

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            () => Close(false));

        var content = new StackPanel { Margin = new Thickness(14), Spacing = 8 };
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.FirstSliceAngleLabel, _angleBox, 220));
        content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.DoughnutHoleLabel, _holeBox, 220));
        if (state.IsOfPie)
        {
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieTypeLabel, _ofPieTypeCombo!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieSplitTypeLabel, _ofPieSplitTypeCombo!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieSplitPositionLabel, _ofPieSplitPositionBox!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieSecondPieSizeLabel, _ofPieSizeBox!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieCustomPointIndicesLabel, _ofPieCustomPointsBox!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieGapWidthLabel, _ofPieGapWidthBox!, 220));
            content.Children.Add(ChartOptionsDialogChrome.CreateRow(surface.OfPieSeriesLinesLabel, _ofPieSeriesLinesCheck!, 220));
        }
        content.Children.Add(new TextBlock { Text = surface.Hint, Opacity = 0.7, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap });
        content.Children.Add(buttons);
        Content = content;
    }

    internal ChartPieOptions BuildCommitPlanForTests() =>
        _session.BuildCommitPlan(ReadInput(), CultureInfo.CurrentCulture);

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
        _ofPieGapWidthBox!.Text = gapWidthPercent?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        _ofPieSeriesLinesCheck!.IsChecked = seriesLines;
    }

    private void OnOk()
    {
        var result = _session.TryCommit(ReadInput(), CultureInfo.CurrentCulture);
        if (result.Succeeded)
            Close(true);
    }

    private ChartPieOptionsDialogInput ReadInput() => new(
        _angleBox.Text,
        _holeBox.Text,
        _ofPieTypeCombo?.SelectedIndex ?? 0,
        _ofPieSplitTypeCombo?.SelectedIndex ?? 0,
        _ofPieSplitPositionBox?.Text,
        _ofPieSizeBox?.Text,
        _ofPieCustomPointsBox?.Text,
        _ofPieGapWidthBox?.Text,
        _ofPieSeriesLinesCheck?.IsChecked == true);

    private static string Format(double? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);

    private static string Format(int? value) =>
        ChartDialogOptionProjection.Format(value, CultureInfo.CurrentCulture);
}
