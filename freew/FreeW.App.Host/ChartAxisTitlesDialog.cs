using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// Modal prompt for the chart's category axis title and value axis title.
/// Returns a record on OK, or null on cancel.
/// </summary>
internal sealed class ChartAxisTitlesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _catBox;
    private readonly TextBox _valBox;
    private (string? CategoryTitle, string? ValueTitle)? _result;

    private ChartAxisTitlesDialog(Window? owner, string? currentCategory, string? currentValue)
    {
        var surface = ChartAxisTitlesDialogPlanner.BuildSurface(UiText.Get);
        Owner = owner;
        Title = surface.Title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WpfDialogSurfaceSemantics.Apply(this, surface);

        _catBox = new TextBox { Text = currentCategory ?? string.Empty, MinWidth = 200 };
        _valBox = new TextBox { Text = currentValue ?? string.Empty, MinWidth = 200 };
        WpfDialogSurfaceSemantics.Apply(_catBox, surface.Field(ChartAxisTitlesDialogField.Category));
        WpfDialogSurfaceSemantics.Apply(_valBox, surface.Field(ChartAxisTitlesDialogField.Value));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, surface.Field(ChartAxisTitlesDialogField.Category).Label, _catBox);
        AddRow(grid, 1, surface.Field(ChartAxisTitlesDialogField.Value).Label, _valBox);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 3); Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_catBox);
    }

    private static void AddRow(Grid grid, int row, string label, TextBox box)
    {
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 6) };
        Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
        grid.Children.Add(lbl);
        Grid.SetRow(box, row); Grid.SetColumn(box, 1);
        box.Margin = new Thickness(0, 0, 0, 6);
        grid.Children.Add(box);
    }

    private void Accept()
    {
        var result = ChartAxisTitlesDialogPlanner.BuildResult(_catBox.Text, _valBox.Text);
        _result = (result.CategoryTitle, result.ValueTitle);
        Close();
    }

    public static (string? CategoryTitle, string? ValueTitle)? Prompt(Window? owner, string? currentCategory, string? currentValue)
    {
        var dialog = new ChartAxisTitlesDialog(owner, currentCategory, currentValue);
        dialog.ShowDialog();
        return dialog._result;
    }
}
