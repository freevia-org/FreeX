using System.Globalization;
using System.Windows;
using System.Windows.Controls;

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
        Owner = owner;
        Title = "Crop Picture";
        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        static TextBox Box(double fraction) =>
            new() { Text = (fraction * 100).ToString("0.#", CultureInfo.CurrentCulture), MinWidth = 80 };

        _leftBox   = Box(left);
        _rightBox  = Box(right);
        _topBox    = Box(top);
        _bottomBox = Box(bottom);

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 6; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static void Place(Grid g, UIElement el, int row, int col)
        {
            Grid.SetRow(el, row); Grid.SetColumn(el, col); g.Children.Add(el);
        }

        Place(grid, Label("Left (%):"),   0, 0); Place(grid, _leftBox,   0, 1);
        Place(grid, Label("Right (%):"),  1, 0); Place(grid, _rightBox,  1, 1);
        Place(grid, Label("Top (%):"),    2, 0); Place(grid, _topBox,    2, 1);
        Place(grid, Label("Bottom (%):"), 3, 0); Place(grid, _bottomBox, 3, 1);

        var note = new TextBlock
        {
            Text = "Enter the percentage of width/height to remove from each edge (0–99).",
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
        static bool TryFrac(TextBox box, out double frac)
        {
            frac = 0;
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var pct))
                return false;
            if (pct < 0 || pct >= 100)
                return false;
            frac = pct / 100.0;
            return true;
        }

        if (!TryFrac(_leftBox, out var l) || !TryFrac(_rightBox, out var r) ||
            !TryFrac(_topBox, out var t)  || !TryFrac(_bottomBox, out var b))
        {
            DialogMessageHelper.ShowWarning(this, "Each crop value must be a percentage between 0 and 99.");
            return;
        }
        if (l + r >= 1.0 || t + b >= 1.0)
        {
            DialogMessageHelper.ShowWarning(this, "Left + Right and Top + Bottom must each total less than 100%.");
            return;
        }
        _result = (l, r, t, b);
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
