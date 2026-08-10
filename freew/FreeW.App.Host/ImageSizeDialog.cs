using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

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
    private readonly double _aspect;
    private bool _updating;          // re-entry guard when syncing width↔height
    private (double Width, double Height)? _result;

    private ImageSizeDialog(Window? owner, double currentWidthPt, double currentHeightPt)
    {
        var surface = ImageSizeDialogPlanner.Surface;
        Owner = owner;
        Title = surface.Title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ImageChartDialogSurfaceSemantics.Apply(this, surface);

        var state = ImageSizeDialogPlanner.BuildInitialState(
            currentWidthPt,
            currentHeightPt,
            CultureInfo.CurrentCulture);

        _aspect = state.AspectRatio;

        _widthBox = new TextBox { Text = state.WidthText, MinWidth = 120 };
        _heightBox = new TextBox { Text = state.HeightText, MinWidth = 120 };
        _lockCheck = new CheckBox { Content = surface.Field(ImageSizeDialogField.LockAspectRatio).Label, IsChecked = state.LockAspectRatio, Margin = new Thickness(0, 6, 0, 0) };
        ImageChartDialogSurfaceSemantics.Apply(_widthBox, surface.Field(ImageSizeDialogField.Width));
        ImageChartDialogSurfaceSemantics.Apply(_heightBox, surface.Field(ImageSizeDialogField.Height));
        ImageChartDialogSurfaceSemantics.Apply(_lockCheck, surface.Field(ImageSizeDialogField.LockAspectRatio));

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

        Place(grid, new TextBlock { Text = surface.Field(ImageSizeDialogField.Width).Label,  VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, 0, 0);
        Place(grid, _widthBox,  0, 1);
        Place(grid, new TextBlock { Text = surface.Field(ImageSizeDialogField.Height).Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) }, 1, 0);
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
        if (ImageSizeDialogPlanner.TryBuildLockedHeightText(
                _widthBox.Text,
                _aspect,
                _lockCheck.IsChecked == true,
                CultureInfo.CurrentCulture,
                out var heightText))
        {
            _updating = true;
            _heightBox.Text = heightText;
            _updating = false;
        }
    }

    private void OnHeightChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        if (ImageSizeDialogPlanner.TryBuildLockedWidthText(
                _heightBox.Text,
                _aspect,
                _lockCheck.IsChecked == true,
                CultureInfo.CurrentCulture,
                out var widthText))
        {
            _updating = true;
            _widthBox.Text = widthText;
            _updating = false;
        }
    }

    private void Accept()
    {
        if (!ImageSizeDialogPlanner.TryBuildResult(
                new ImageSizeDialogInput(_widthBox.Text, _heightBox.Text),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            DialogMessageHelper.ShowWarning(
                this,
                validation?.Message ?? ImageSizeDialogPlanner.PositiveSizeValidationMessage);
            return;
        }

        _result = (result!.Width, result.Height);
        Close();
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
