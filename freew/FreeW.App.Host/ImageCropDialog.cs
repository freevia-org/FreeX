using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// Numeric crop dialog: collect left/right/top/bottom crop fractions (0–1) for the selected image.
/// Crop amounts are entered as percentages (0–99.9 %) for readability and converted to fractions
/// (÷ 100) by the dialog. Returns null if the user cancels.
/// </summary>
internal sealed class ImageCropDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _leftBox, _rightBox, _topBox, _bottomBox;
    private (double Left, double Right, double Top, double Bottom)? _result;

    private ImageCropDialog(Window? owner, double left, double right, double top, double bottom)
    {
        var surface = ImageCropDialogPlanner.Surface;
        Owner = owner;
        Title = surface.Title;
        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WpfDialogSurfaceSemantics.Apply(this, surface);

        var state = ImageCropDialogPlanner.BuildInitialState(
            left,
            right,
            top,
            bottom,
            CultureInfo.CurrentCulture);

        static TextBox Box(string text) =>
            new() { Text = text, MinWidth = 80 };

        _leftBox = Box(state.LeftText);
        _rightBox = Box(state.RightText);
        _topBox = Box(state.TopText);
        _bottomBox = Box(state.BottomText);
        WpfDialogSurfaceSemantics.Apply(_leftBox, surface.Field(ImageCropDialogField.Left));
        WpfDialogSurfaceSemantics.Apply(_rightBox, surface.Field(ImageCropDialogField.Right));
        WpfDialogSurfaceSemantics.Apply(_topBox, surface.Field(ImageCropDialogField.Top));
        WpfDialogSurfaceSemantics.Apply(_bottomBox, surface.Field(ImageCropDialogField.Bottom));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 6; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static void Place(Grid g, UIElement el, int row, int col)
        {
            Grid.SetRow(el, row); Grid.SetColumn(el, col); g.Children.Add(el);
        }

        Place(grid, Label(surface.Field(ImageCropDialogField.Left).Label),   0, 0); Place(grid, _leftBox,   0, 1);
        Place(grid, Label(surface.Field(ImageCropDialogField.Right).Label),  1, 0); Place(grid, _rightBox,  1, 1);
        Place(grid, Label(surface.Field(ImageCropDialogField.Top).Label),    2, 0); Place(grid, _topBox,    2, 1);
        Place(grid, Label(surface.Field(ImageCropDialogField.Bottom).Label), 3, 0); Place(grid, _bottomBox, 3, 1);

        var note = new TextBlock
        {
            Text = ImageCropDialogPlanner.Instruction,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Margin = new Thickness(0, 6, 0, 0)
        };
        Grid.SetRow(note, 4); Grid.SetColumnSpan(note, 2); grid.Children.Add(note);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Place(grid, buttons, 5, 1);

        Content = grid;
        DialogFocus.FocusAndSelect(_leftBox);
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) };

    private void Accept()
    {
        if (!ImageCropDialogPlanner.TryBuildResult(
                new ImageCropDialogInput(
                    _leftBox.Text,
                    _rightBox.Text,
                    _topBox.Text,
                    _bottomBox.Text),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            DialogMessageHelper.ShowWarning(
                this,
                validation?.Message ?? ImageCropDialogPlanner.PercentageValidationMessage);
            return;
        }

        _result = (result!.Left, result.Right, result.Top, result.Bottom);
        Close();
    }

    /// <summary>Show the crop dialog. Returns fractions, or null if cancelled.</summary>
    public static (double Left, double Right, double Top, double Bottom)? Prompt(
        Window? owner, double left, double right, double top, double bottom)
    {
        var dialog = new ImageCropDialog(owner, left, right, top, bottom);
        dialog.ShowDialog();
        return dialog._result;
    }
}
