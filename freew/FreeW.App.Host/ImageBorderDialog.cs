using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// Dialog for setting a picture border color (6-digit hex), line width (points), and dash style.
/// An empty color field clears the border. Returns null if the user cancels.
/// </summary>
internal sealed class ImageBorderDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _colorBox, _widthBox;
    private readonly ComboBox _dashBox;
    private (string? Color, double Width, string? Dash)? _result;

    private ImageBorderDialog(Window? owner, string? colorHex, double widthPt, string? dash)
    {
        var surface = ImageBorderDialogPlanner.Surface;
        Owner = owner;
        Title = surface.Title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ImageChartDialogSurfaceSemantics.Apply(this, surface);

        var state = ImageBorderDialogPlanner.BuildInitialState(
            colorHex,
            widthPt,
            dash,
            CultureInfo.CurrentCulture);

        _colorBox = new TextBox { Text = state.ColorText, MinWidth = 80 };
        _widthBox = new TextBox { Text = state.WidthText, MinWidth = 80 };
        _dashBox = new ComboBox { MinWidth = 100 };
        foreach (var style in ImageBorderDialogPlanner.DashItems)
            _dashBox.Items.Add(style.Label);
        _dashBox.SelectedIndex = state.DashIndex;
        ImageChartDialogSurfaceSemantics.Apply(_colorBox, surface.Field(ImageBorderDialogField.Color));
        ImageChartDialogSurfaceSemantics.Apply(_widthBox, surface.Field(ImageBorderDialogField.Width));
        ImageChartDialogSurfaceSemantics.Apply(_dashBox, surface.Field(ImageBorderDialogField.Style));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static void Place(Grid g, UIElement el, int row, int col)
        {
            Grid.SetRow(el, row); Grid.SetColumn(el, col); g.Children.Add(el);
        }

        Place(grid, Label(surface.Field(ImageBorderDialogField.Color).Label), 0, 0); Place(grid, _colorBox, 0, 1);
        Place(grid, Label(surface.Field(ImageBorderDialogField.Width).Label), 1, 0); Place(grid, _widthBox, 1, 1);
        Place(grid, Label(surface.Field(ImageBorderDialogField.Style).Label), 2, 0); Place(grid, _dashBox,  2, 1);

        var note = new TextBlock
        {
            Text = surface.SupportingText,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetRow(note, 3); Grid.SetColumnSpan(note, 2); grid.Children.Add(note);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Place(grid, buttons, 4, 1);

        Content = grid;
        DialogFocus.FocusAndSelect(_colorBox);
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) };

    private void Accept()
    {
        if (!ImageBorderDialogPlanner.TryBuildResult(
                new ImageBorderDialogInput(_colorBox.Text, _widthBox.Text, _dashBox.SelectedIndex),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            DialogMessageHelper.ShowWarning(
                this,
                validation?.Message ?? ImageBorderDialogPlanner.ColorValidationMessage);
            return;
        }

        _result = (result!.Color, result.Width, result.Dash);
        Close();
    }

    /// <summary>Show the dialog. Returns (colorHex, widthPt, dash), or null if cancelled.</summary>
    public static (string? Color, double Width, string? Dash)? Prompt(
        Window? owner, string? colorHex, double widthPt, string? dash)
    {
        var dialog = new ImageBorderDialog(owner, colorHex, widthPt, dash);
        dialog.ShowDialog();
        return dialog._result;
    }
}
