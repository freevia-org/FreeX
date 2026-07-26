using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

/// <summary>PowerPoint-style bubble chart sizing options dialog.</summary>
public sealed class ChartBubbleOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly EditingSession _editor;
    private readonly ChartBubbleOptionsPlanner _planner;
    private readonly TextBox _scaleBox;
    private readonly ComboBox _sizeRepresentsCombo;
    private readonly CheckBox _negativeBubblesCheck;

    public ChartBubbleOptionsDialog(EditingSession editor)
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
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _scaleBox = new TextBox { Text = _planner.BubbleScalePercent.ToString(CultureInfo.CurrentCulture), MinWidth = 150 };
        _sizeRepresentsCombo = new ComboBox
        {
            ItemsSource = ChartBubbleOptionsPlanner.SizeRepresentsOptions,
            DisplayMemberPath = nameof(ChartBubbleSizeRepresentationOption.Label),
            SelectedIndex = FindSizeRepresentsIndex(_planner.SizeRepresents),
            MinWidth = 150,
        };
        _negativeBubblesCheck = new CheckBox
        {
            Content = surface.ShowNegativeBubblesLabel,
            IsChecked = _planner.ShowNegativeBubbles,
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8, 14, 8, 8),
        };
        var ok = new Button { Content = surface.OkLabel, IsDefault = true, MinWidth = 80, Margin = new Thickness(4) };
        var cancel = new Button { Content = surface.CancelLabel, IsCancel = true, MinWidth = 80, Margin = new Thickness(4) };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(MakeRow(surface.BubbleScaleLabel, _scaleBox));
        content.Children.Add(MakeRow(surface.SizeRepresentsLabel, _sizeRepresentsCombo));
        content.Children.Add(_negativeBubblesCheck);
        content.Children.Add(new TextBlock { Text = surface.Hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Opacity = 0.7 });
        content.Children.Add(buttons);
        Content = content;
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
            DialogResult = true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdatePlannerFromControls()
    {
        _planner.SetBubbleScalePercent(ParseScale(_scaleBox.Text));
        if (_sizeRepresentsCombo.SelectedItem is ChartBubbleSizeRepresentationOption size)
            _planner.SetSizeRepresents(size.Value);
        _planner.SetShowNegativeBubbles(_negativeBubblesCheck.IsChecked == true);
    }

    private static int ParseScale(string? text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value is >= 0 and <= 300)
            return value;
        throw new FormatException("Bubble scale must be a whole number from 0 to 300.");
    }

    private static int FindSizeRepresentsIndex(BubbleSizeRepresentation value) =>
        Math.Max(0, ChartBubbleOptionsPlanner.SizeRepresentsOptions
            .Select((option, index) => (option, index))
            .FirstOrDefault(item => item.option.Value == value).index);

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new Label { Content = label, Width = 190, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}
