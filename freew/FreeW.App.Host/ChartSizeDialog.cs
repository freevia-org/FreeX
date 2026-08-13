using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// Modal prompt for a chart's width and height in points.
/// Returns a record on OK, or null on cancel.
/// </summary>
internal sealed class ChartSizeDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;
    private (double WidthPt, double HeightPt)? _result;

    private ChartSizeDialog(Window? owner, double widthPt, double heightPt)
    {
        var surface = ChartSizeDialogPlanner.BuildSurface(UiText.Get);
        Owner = owner;
        Title = surface.Title;
        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WpfDialogSurfaceSemantics.Apply(this, surface);

        var state = ChartSizeDialogPlanner.BuildInitialState(widthPt, heightPt, CultureInfo.CurrentCulture);

        _widthBox = new TextBox
        {
            Text = state.WidthText,
            MinWidth = 120
        };
        _heightBox = new TextBox
        {
            Text = state.HeightText,
            MinWidth = 120
        };
        WpfDialogSurfaceSemantics.Apply(_widthBox, surface.Field(ChartSizeDialogField.Width));
        WpfDialogSurfaceSemantics.Apply(_heightBox, surface.Field(ChartSizeDialogField.Height));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, surface.Field(ChartSizeDialogField.Width).Label, _widthBox);
        AddRow(grid, 1, surface.Field(ChartSizeDialogField.Height).Label, _heightBox);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Grid.SetRow(buttons, 2); Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        Content = grid;
        DialogFocus.FocusAndSelect(_widthBox);
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
        if (!ChartSizeDialogPlanner.TryBuildResult(
            new ChartSizeDialogInput(_widthBox.Text, _heightBox.Text),
            CultureInfo.CurrentCulture,
            UiText.Get,
            out var result,
            out var validation))
        {
            DialogMessageHelper.ShowWarning(
                this,
                validation!.Message);
            return;
        }

        _result = (result!.WidthPt, result.HeightPt);
        Close();
    }

    public static (double WidthPt, double HeightPt)? Prompt(Window? owner, double widthPt, double heightPt)
    {
        var dialog = new ChartSizeDialog(owner, widthPt, heightPt);
        dialog.ShowDialog();
        return dialog._result;
    }
}
