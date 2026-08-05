using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed class ChartBubbleOptionsDialog : Window
{
    private readonly EditingSession _editor;
    private readonly ChartBubbleOptionsPlanner _planner;
    private readonly TextBox _scaleBox;
    private readonly ComboBox _sizeRepresentsCombo;
    private readonly CheckBox _negativeBubblesCheck;

    internal ChartBubbleOptionsDialog(EditingSession editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        var chart = editor.SelectedChart
            ?? throw new InvalidOperationException("No chart is currently selected.");
        if (chart.ChartType != ChartType.Bubble)
            throw new InvalidOperationException("Select a bubble chart before editing bubble options.");

        _planner = ChartBubbleOptionsPlanner.FromChart(chart);
        var surface = ChartBubbleOptionsPlanner.BuildSurfacePlan();

        Title = surface.Title;
        Width = ChartBubbleOptionsPlanner.DefaultDialogWidth;
        Height = ChartBubbleOptionsPlanner.DefaultDialogHeight;
        MinWidth = 360;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

        _scaleBox = new TextBox { Text = _planner.BubbleScalePercent.ToString(CultureInfo.CurrentCulture), MinWidth = 150 };
        _sizeRepresentsCombo = new ComboBox
        {
            ItemsSource = ChartBubbleOptionsPlanner.SizeRepresentsOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = FindSizeRepresentsIndex(_planner.SizeRepresents),
            MinWidth = 150,
        };
        _negativeBubblesCheck = new CheckBox { Content = surface.ShowNegativeBubblesLabel, IsChecked = _planner.ShowNegativeBubbles };

        var buttons = ChartOptionsDialogChrome.CreateActionRow(
            surface.OkLabel,
            OnOk,
            surface.CancelLabel,
            () => Close(false));

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Spacing = 8,
            Children =
            {
                ChartOptionsDialogChrome.CreateRow(surface.BubbleScaleLabel, _scaleBox, 190),
                ChartOptionsDialogChrome.CreateRow(surface.SizeRepresentsLabel, _sizeRepresentsCombo, 190),
                _negativeBubblesCheck,
                new TextBlock { Text = surface.Hint, Opacity = 0.7, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                buttons,
            },
        };
    }

    internal ChartBubbleOptions BuildCommitPlanForTests()
    {
        UpdatePlannerFromControls();
        return _planner.BuildCommitPlan();
    }

    internal void SetOptionsForTests(int bubbleScalePercent, BubbleSizeRepresentation sizeRepresents, bool showNegativeBubbles)
    {
        _scaleBox.Text = bubbleScalePercent.ToString(CultureInfo.CurrentCulture);
        _sizeRepresentsCombo.SelectedIndex = FindSizeRepresentsIndex(sizeRepresents);
        _negativeBubblesCheck.IsChecked = showNegativeBubbles;
    }

    private void OnOk()
    {
        try
        {
            _editor.ApplyChartBubbleOptions(BuildCommitPlanForTests());
            Close(true);
        }
        catch (FormatException)
        {
            // Keep the dialog open so the user can correct the scale.
        }
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetBubbleScalePercent(ParseScale(_scaleBox.Text));
        _planner.SetSizeRepresents(ChartDialogOptionProjection.ValueAtOrDefault(
            ChartBubbleOptionsPlanner.SizeRepresentsOptions,
            _sizeRepresentsCombo.SelectedIndex,
            option => option.Value,
            BubbleSizeRepresentation.Area));
        _planner.SetShowNegativeBubbles(_negativeBubblesCheck.IsChecked == true);
    }

    private static int ParseScale(string? text)
    {
        return ChartDialogOptionProjection.ParseRequiredInt(
            text,
            CultureInfo.CurrentCulture,
            value => value is >= 0 and <= 300,
            "Bubble scale must be a whole number from 0 to 300.");
    }

    private static int FindSizeRepresentsIndex(BubbleSizeRepresentation value) =>
        ChartDialogOptionProjection.FindIndex(
            ChartBubbleOptionsPlanner.SizeRepresentsOptions,
            value,
            option => option.Value);
}
