using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// Dialog for Picture Format > Adjust: brightness (-100..100), contrast (-100..100),
/// saturation (0..400, 100=normal), and transparency (0..100). Returns null on cancel.
/// </summary>
internal sealed class ImageAdjustDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _brightnessBox, _contrastBox, _saturationBox, _transparencyBox;
    private (double Brightness, double Contrast, double Saturation, double Transparency)? _result;

    private ImageAdjustDialog(Window? owner,
        double brightnessPct, double contrastPct, double saturationPct, double transparencyPct)
    {
        var surface = ImageAdjustDialogPlanner.DetailedSurface;
        Owner = owner;
        Title = surface.Title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ImageChartDialogSurfaceSemantics.Apply(this, surface);

        var state = ImageAdjustDialogPlanner.BuildInitialState(
            brightnessPct,
            contrastPct,
            saturationPct,
            transparencyPct,
            CultureInfo.CurrentCulture);

        _brightnessBox = Box(state.BrightnessText);
        _contrastBox = Box(state.ContrastText);
        _saturationBox = Box(state.SaturationText);
        _transparencyBox = Box(state.TransparencyText);
        ImageChartDialogSurfaceSemantics.Apply(_brightnessBox, surface.Field(ImageAdjustDialogField.Brightness));
        ImageChartDialogSurfaceSemantics.Apply(_contrastBox, surface.Field(ImageAdjustDialogField.Contrast));
        ImageChartDialogSurfaceSemantics.Apply(_saturationBox, surface.Field(ImageAdjustDialogField.Saturation));
        ImageChartDialogSurfaceSemantics.Apply(_transparencyBox, surface.Field(ImageAdjustDialogField.Transparency));

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 7; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static void Place(Grid g, UIElement el, int row, int col)
        { Grid.SetRow(el, row); Grid.SetColumn(el, col); g.Children.Add(el); }

        Place(grid, Label("Corrections"), 0, 0);
        Place(grid, new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) }, 0, 1);

        Place(grid, Label(surface.Field(ImageAdjustDialogField.Brightness).Label), 1, 0); Place(grid, _brightnessBox,   1, 1);
        Place(grid, Label(surface.Field(ImageAdjustDialogField.Contrast).Label),   2, 0); Place(grid, _contrastBox,     2, 1);

        var sep = new Separator { Margin = new Thickness(0, 6, 0, 4) };
        Grid.SetRow(sep, 3); Grid.SetColumnSpan(sep, 2); grid.Children.Add(sep);

        Place(grid, Label("Color"), 3, 0);

        Place(grid, Label(surface.Field(ImageAdjustDialogField.Saturation).Label),   4, 0); Place(grid, _saturationBox,   4, 1);
        Place(grid, Label(surface.Field(ImageAdjustDialogField.Transparency).Label), 5, 0); Place(grid, _transparencyBox, 5, 1);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Place(grid, buttons, 6, 1);

        Content = grid;
        DialogFocus.FocusAndSelect(_brightnessBox);
    }

    private static TextBox Box(string text) =>
        new() { Text = text, MinWidth = 80 };

    private static TextBlock Label(string text) =>
        new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) };

    private void Accept()
    {
        if (!ImageAdjustDialogPlanner.TryBuildResult(
                new ImageAdjustDialogInput(
                    _brightnessBox.Text,
                    _contrastBox.Text,
                    _saturationBox.Text,
                    _transparencyBox.Text),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            DialogMessageHelper.ShowWarning(
                this,
                validation?.Message ?? ImageAdjustDialogPlanner.BrightnessValidationMessage);
            return;
        }

        _result = (result!.Brightness, result.Contrast, result.Saturation, result.Transparency);
        Close();
    }

    /// <summary>Show the dialog. Returns (brightness, contrast, saturation, transparency), or null if cancelled.</summary>
    public static (double Brightness, double Contrast, double Saturation, double Transparency)? Prompt(
        Window? owner, double brightnessPct, double contrastPct, double saturationPct, double transparencyPct)
    {
        var dialog = new ImageAdjustDialog(owner, brightnessPct, contrastPct, saturationPct, transparencyPct);
        dialog.ShowDialog();
        return dialog._result;
    }
}
