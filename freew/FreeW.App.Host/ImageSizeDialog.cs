using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace FreeW.App.Host;

/// <summary>
/// Modal dialog for setting an inline image's width and height in points, with a lock-aspect-ratio
/// toggle. When the lock is active, editing either dimension recomputes the other to preserve the
/// original aspect ratio. Returns the chosen (widthPt, heightPt) pair, or null if the user cancels.
/// </summary>
internal sealed class ImageSizeDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;
    private readonly CheckBox _lockCheck;
    private readonly double _aspect; // height / width of the original image
    private bool _updating;          // re-entry guard when syncing width↔height
    private (double Width, double Height)? _result;

    private ImageSizeDialog(Window? owner, double currentWidthPt, double currentHeightPt)
    {
        Owner = owner;
        Title = "Image Size";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _aspect = currentWidthPt > 0 ? currentHeightPt / currentWidthPt : 1.0;

        _widthBox  = new TextBox { Text = currentWidthPt.ToString("0.##", CultureInfo.CurrentCulture), MinWidth = 120 };
        _heightBox = new TextBox { Text = currentHeightPt.ToString("0.##", CultureInfo.CurrentCulture), MinWidth = 120 };
        _lockCheck = new CheckBox { Content = "Lock aspect ratio", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };

        _widthBox.TextChanged  += OnWidthChanged;
        _heightBox.TextChanged += OnHeightChanged;

        var grid = new Grid { Margin = new Thickness(14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        static void Place(Grid g, UIElement el, int row, int col)
        {
            Grid.SetRow(el, row); Grid.SetColumn(el, col); g.Children.Add(el);
        }

        Place(grid, new TextBlock { Text = "Width (pt):",  VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, 0, 0);
        Place(grid, _widthBox,  0, 1);
        Place(grid, new TextBlock { Text = "Height (pt):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) }, 1, 0);
        Place(grid, _heightBox, 1, 1);
        Place(grid, _lockCheck, 2, 1);

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0));
        Place(grid, buttons, 3, 1);

        Content = grid;
        DialogFocus.FocusAndSelect(_widthBox);
    }

    private void OnWidthChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        if (_lockCheck.IsChecked == true && double.TryParse(_widthBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var w) && w > 0)
        {
            _updating = true;
            _heightBox.Text = (w * _aspect).ToString("0.##", CultureInfo.CurrentCulture);
            _updating = false;
        }
    }

    private void OnHeightChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        if (_lockCheck.IsChecked == true && double.TryParse(_heightBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var h) && h > 0 && _aspect > 0)
        {
            _updating = true;
            _widthBox.Text = (h / _aspect).ToString("0.##", CultureInfo.CurrentCulture);
            _updating = false;
        }
    }

    private void Accept()
    {
        var okW = double.TryParse(_widthBox.Text,  NumberStyles.Float, CultureInfo.CurrentCulture, out var width)  && width  > 0;
        var okH = double.TryParse(_heightBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var height) && height > 0;
        if (okW && okH)
        {
            _result = (width, height);
            Close();
        }
        else
        {
            DialogMessageHelper.ShowWarning(this, "Enter positive values for both width and height (in points).");
        }
    }

    /// <summary>
    /// Show the dialog. Returns the chosen (widthPt, heightPt), or null if the user cancels.
    /// </summary>
    public static (double Width, double Height)? Prompt(Window? owner, double currentWidthPt, double currentHeightPt)
    {
        var dialog = new ImageSizeDialog(owner, currentWidthPt, currentHeightPt);
        dialog.ShowDialog();
        return dialog._result;
    }
}
