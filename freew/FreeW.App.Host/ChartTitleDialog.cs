using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// Tiny modal prompt for the chart's display title. Returns the new title (possibly null to clear
/// it) wrapped in a success boolean on OK, or returns accepted=false on cancel.
/// </summary>
internal sealed class ChartTitleDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _titleBox;
    private bool _accepted;
    private string? _title;

    private ChartTitleDialog(Window? owner, string? currentTitle)
    {
        var surface = ChartTitleDialogPlanner.BuildSurface(UiText.Get);
        Owner = owner;
        Title = surface.Title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WpfDialogSurfaceSemantics.Apply(this, surface);

        _titleBox = new TextBox { Text = currentTitle ?? string.Empty, MinWidth = 200 };
        WpfDialogSurfaceSemantics.Apply(_titleBox, surface.Field(ChartTitleDialogField.Title));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock { Text = surface.Field(ChartTitleDialogField.Title).Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetRow(label, 0); Grid.SetColumn(label, 0);
        grid.Children.Add(label);
        Grid.SetRow(_titleBox, 0); Grid.SetColumn(_titleBox, 1);
        grid.Children.Add(_titleBox);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 1); Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_titleBox);
    }

    private void Accept()
    {
        _accepted = true;
        _title = ChartTitleDialogPlanner.BuildResult(_titleBox.Text).NewTitle;
        Close();
    }

    /// <summary>
    /// Show the dialog. Returns (true, newTitle) on OK — newTitle may be null to clear the title —
    /// or (false, _) on cancel.
    /// </summary>
    public static (bool Accepted, string? NewTitle) Prompt(Window? owner, string? currentTitle)
    {
        var dialog = new ChartTitleDialog(owner, currentTitle);
        dialog.ShowDialog();
        return (dialog._accepted, dialog._title);
    }
}
