using System.Windows;
using System.Windows.Controls;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed class ChartExSeriesLayoutDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ChartExSeriesLayoutDialogSession _session;
    private readonly ComboBox _seriesCombo;
    private readonly ComboBox _layoutCombo;

    public ChartExSeriesLayoutDialog(EditingSession editor)
    {
        _session = new ChartExSeriesLayoutDialogSession(editor);
        Title = ChartExSeriesLayoutPlanner.DialogTitle;
        Width = 430;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _seriesCombo = new ComboBox
        {
            ItemsSource = _session.SeriesOptions.Select(option => option.Label).ToArray(),
            SelectedIndex = 0,
            MinWidth = 260,
        };
        _seriesCombo.SelectionChanged += (_, _) => LoadLayoutChoices();
        _layoutCombo = new ComboBox { MinWidth = 260 };
        LoadLayoutChoices();

        var ok = new Button { Content = ChartExSeriesLayoutPlanner.OkLabel, IsDefault = true, MinWidth = 80, Margin = new Thickness(4) };
        var cancel = new Button { Content = ChartExSeriesLayoutPlanner.CancelLabel, IsCancel = true, MinWidth = 80, Margin = new Thickness(4) };
        ok.Click += (_, _) => OnOk();
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(MakeRow(ChartExSeriesLayoutPlanner.SeriesLabel, _seriesCombo));
        content.Children.Add(MakeRow(ChartExSeriesLayoutPlanner.LayoutLabel, _layoutCombo));
        content.Children.Add(buttons);
        Content = content;
    }

    internal int SelectedSeriesIndexForTests => _session.SelectedSeriesIndex;
    internal string? SelectedLayoutIdForTests => _session.LayoutIdAt(_layoutCombo.SelectedIndex);

    internal void ApplyForTests()
    {
        if (!_session.TryApply(_layoutCombo.SelectedIndex, out var error))
            throw new ArgumentException(error);
    }

    private void OnOk()
    {
        if (!_session.TryApply(_layoutCombo.SelectedIndex, out var error))
        {
            MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void LoadLayoutChoices()
    {
        var selection = _session.SelectSeries(_seriesCombo.SelectedIndex);
        _layoutCombo.ItemsSource = selection.LayoutChoices
            .Select(choice => choice.Label)
            .ToArray();
        _layoutCombo.SelectedIndex = selection.LayoutIndex;
    }

    private static StackPanel MakeRow(string label, Control control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(new Label { Content = label, Width = 80, VerticalContentAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}
