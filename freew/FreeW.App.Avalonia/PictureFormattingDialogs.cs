using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using static FreeW.App.Avalonia.PictureFormattingDialogChrome;

namespace FreeW.App.Avalonia;

internal sealed class ImageAltTextDialog : FreeWDialogWindow
{
    private readonly TextBox _descriptionBox;

    private ImageAltTextDialog(string seed)
    {
        Title = "Alt Text";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _descriptionBox = new TextBox
        {
            Text = seed,
            MinWidth = 360,
            Margin = new Thickness(0, 0, 0, 12),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(_descriptionBox, Style);

        var ok = CreateButton("OK", isDefault: true);
        ok.Click += (_, _) => Close(_descriptionBox.Text ?? string.Empty);
        var cancel = CreateButton("Cancel", isCancel: true);
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = "Description:", Margin = new Thickness(0, 0, 0, 4) },
                _descriptionBox,
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]),
            },
        };

        Opened += (_, _) => FocusAndSelect(_descriptionBox);
        CloseOnEscape(this, () => Close(null));
    }

    public static Task<string?> ShowAsync(Window owner, string seed) =>
        new ImageAltTextDialog(seed).ShowDialog<string?>(owner);
}

internal sealed class ImageSizeDialog : FreeWDialogWindow
{
    private readonly TextBox _widthBox;
    private readonly TextBox _heightBox;
    private readonly CheckBox _lockCheck;
    private readonly TextBlock _status = new();
    private readonly double _aspect;
    private bool _updating;

    private ImageSizeDialog(double widthPt, double heightPt, string title)
    {
        Title = title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var state = ImageSizeDialogPlanner.BuildInitialState(widthPt, heightPt, CultureInfo.CurrentCulture);
        _aspect = state.AspectRatio;
        _widthBox = CreateTextBox(state.WidthText, minWidth: 120);
        _heightBox = CreateTextBox(state.HeightText, minWidth: 120);
        _lockCheck = new CheckBox
        {
            Content = "Lock aspect ratio",
            IsChecked = state.LockAspectRatio,
            Margin = new Thickness(0, 6, 0, 0),
        };
        AvaloniaCompactDialogChrome.ApplyCheckBox(_lockCheck, Style);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Style, new Thickness(0, 6, 0, 0));

        _widthBox.TextChanged += (_, _) => UpdateLockedHeight();
        _heightBox.TextChanged += (_, _) => UpdateLockedWidth();

        var grid = CreateGrid(rows: 5);
        AddField(grid, "Width (pt):", _widthBox, 0);
        AddField(grid, "Height (pt):", _heightBox, 1);
        Place(grid, _lockCheck, 2, 1);
        Grid.SetRow(_status, 3);
        Grid.SetColumnSpan(_status, 2);
        grid.Children.Add(_status);

        var ok = CreateButton("OK", isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = CreateButton("Cancel", isCancel: true);
        cancel.Click += (_, _) => Close(null);
        Place(grid, AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)), 4, 1);

        Content = new Border { Padding = new Thickness(14), Child = grid };
        Opened += (_, _) => FocusAndSelect(_widthBox);
        CloseOnEscape(this, () => Close(null));
    }

    public static Task<ImageSizeDialogResult?> ShowAsync(
        Window owner,
        double widthPt,
        double heightPt,
        string title = "Image Size") =>
        new ImageSizeDialog(widthPt, heightPt, title).ShowDialog<ImageSizeDialogResult?>(owner);

    private void UpdateLockedHeight()
    {
        if (_updating)
            return;
        if (!ImageSizeDialogPlanner.TryBuildLockedHeightText(
                _widthBox.Text,
                _aspect,
                _lockCheck.IsChecked == true,
                CultureInfo.CurrentCulture,
                out var heightText))
        {
            return;
        }

        _updating = true;
        _heightBox.Text = heightText;
        _updating = false;
    }

    private void UpdateLockedWidth()
    {
        if (_updating)
            return;
        if (!ImageSizeDialogPlanner.TryBuildLockedWidthText(
                _heightBox.Text,
                _aspect,
                _lockCheck.IsChecked == true,
                CultureInfo.CurrentCulture,
                out var widthText))
        {
            return;
        }

        _updating = true;
        _widthBox.Text = widthText;
        _updating = false;
    }

    private void Accept()
    {
        if (ImageSizeDialogPlanner.TryBuildResult(
                new ImageSizeDialogInput(_widthBox.Text, _heightBox.Text),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            Close(result);
            return;
        }

        _status.Text = validation?.Message ?? ImageSizeDialogPlanner.PositiveSizeValidationMessage;
        FocusAndSelect(validation?.Field == ImageSizeDialogField.Height ? _heightBox : _widthBox);
    }

}

internal sealed class ImageBorderDialog : FreeWDialogWindow
{
    private readonly TextBox _colorBox;
    private readonly TextBox _widthBox;
    private readonly ComboBox _dashBox;
    private readonly TextBlock _status = new();

    private ImageBorderDialog(string? colorHex, double widthPt, string? dash)
    {
        Title = "Picture Border";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var state = ImageBorderDialogPlanner.BuildInitialState(
            colorHex,
            widthPt,
            dash,
            CultureInfo.CurrentCulture);
        _colorBox = CreateTextBox(state.ColorText, minWidth: 80);
        _widthBox = CreateTextBox(state.WidthText, minWidth: 80);
        _dashBox = new ComboBox
        {
            MinWidth = 100,
            ItemsSource = ImageBorderDialogPlanner.DashItems.Select(item => item.Label).ToArray(),
            SelectedIndex = state.DashIndex,
        };
        AvaloniaCompactDialogChrome.ApplyComboBox(_dashBox, Style);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Style, new Thickness(0, 6, 0, 0));

        var grid = CreateGrid(rows: 6);
        AddField(grid, "Color (hex, empty = no border):", _colorBox, 0);
        AddField(grid, "Width (pt):", _widthBox, 1);
        AddField(grid, "Style:", _dashBox, 2);

        var note = new TextBlock
        {
            Text = "Color: 6-digit RGB hex, e.g. 000000 for black. Leave blank to remove the border.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10,
            Margin = new Thickness(0, 6, 0, 0),
        };
        Grid.SetRow(note, 3);
        Grid.SetColumnSpan(note, 2);
        grid.Children.Add(note);
        Grid.SetRow(_status, 4);
        Grid.SetColumnSpan(_status, 2);
        grid.Children.Add(_status);

        var ok = CreateButton("OK", isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = CreateButton("Cancel", isCancel: true);
        cancel.Click += (_, _) => Close(null);
        Place(grid, AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)), 5, 1);

        Content = new Border { Padding = new Thickness(14), Child = grid };
        Opened += (_, _) => FocusAndSelect(_colorBox);
        CloseOnEscape(this, () => Close(null));
    }

    public static Task<ImageBorderDialogResult?> ShowAsync(
        Window owner,
        string? colorHex,
        double widthPt,
        string? dash) =>
        new ImageBorderDialog(colorHex, widthPt, dash).ShowDialog<ImageBorderDialogResult?>(owner);

    private void Accept()
    {
        if (ImageBorderDialogPlanner.TryBuildResult(
                new ImageBorderDialogInput(_colorBox.Text, _widthBox.Text, _dashBox.SelectedIndex),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            Close(result);
            return;
        }

        _status.Text = validation?.Message ?? ImageBorderDialogPlanner.ColorValidationMessage;
        FocusAndSelect(validation?.Field == ImageBorderDialogField.Width ? _widthBox : _colorBox);
    }

}

internal static class PictureFormattingDialogChrome
{
    public static AvaloniaCompactDialogChromeStyle Style { get; } = AvaloniaCompactDialogChrome.WindowsStyle;

    public static TextBox CreateTextBox(string text, double minWidth)
    {
        var box = new TextBox { Text = text, MinWidth = minWidth };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, Style);
        return box;
    }

    public static Button CreateButton(string content, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button { Content = content, IsDefault = isDefault, IsCancel = isCancel };
        AvaloniaCompactDialogChrome.ApplyButton(button, Style, minWidth: 72, isDefault: isDefault);
        return button;
    }

    public static void CloseOnEscape(Window window, Action close) =>
        window.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            close();
            e.Handled = true;
        };

    public static Grid CreateGrid(int rows)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < rows; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    public static void AddField(Grid grid, string label, Control field, int row)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, row == 0 ? 0 : 4, 8, 0),
        };
        Place(grid, text, row, 0);
        Place(grid, field, row, 1);
    }

    public static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    public static void FocusAndSelect(TextBox box)
    {
        box.Focus();
        box.SelectAll();
    }
}
