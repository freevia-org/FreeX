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
        var text = AltTextDialogPlanner.ResolveText(UiText.Get);
        Title = text.Title;
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

        var ok = CreateButton(text.OkLabel, isDefault: true);
        ok.Click += (_, _) => Close(_descriptionBox.Text ?? string.Empty);
        var cancel = CreateButton(text.CancelLabel, isCancel: true);
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = text.DescriptionLabel, Margin = new Thickness(0, 0, 0, 4) },
                _descriptionBox,
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]),
            },
        };

        Opened += (_, _) => AvaloniaCompactDialogChrome.FocusAndSelect(_descriptionBox);
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
        var surface = ImageSizeDialogPlanner.Surface;
        Title = title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        ImageChartDialogSurfaceSemantics.Apply(this, surface with { AutomationName = title });

        var state = ImageSizeDialogPlanner.BuildInitialState(widthPt, heightPt, CultureInfo.CurrentCulture);
        _aspect = state.AspectRatio;
        _widthBox = CreateTextBox(state.WidthText, minWidth: 120);
        _heightBox = CreateTextBox(state.HeightText, minWidth: 120);
        _lockCheck = new CheckBox
        {
            Content = surface.Field(ImageSizeDialogField.LockAspectRatio).Label,
            IsChecked = state.LockAspectRatio,
            Margin = new Thickness(0, 6, 0, 0),
        };
        ImageChartDialogSurfaceSemantics.Apply(_widthBox, surface.Field(ImageSizeDialogField.Width));
        ImageChartDialogSurfaceSemantics.Apply(_heightBox, surface.Field(ImageSizeDialogField.Height));
        ImageChartDialogSurfaceSemantics.Apply(_lockCheck, surface.Field(ImageSizeDialogField.LockAspectRatio));
        AvaloniaCompactDialogChrome.ApplyCheckBox(_lockCheck, Style);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Style, new Thickness(0, 6, 0, 0));
        ImageChartDialogSurfaceSemantics.ApplyValidation(_status, surface);

        _widthBox.TextChanged += (_, _) => UpdateLockedHeight();
        _heightBox.TextChanged += (_, _) => UpdateLockedWidth();

        var grid = AvaloniaLabeledFormRow.CreateCompactGrid(rows: 5);
        AvaloniaLabeledFormRow.AddCompact(grid, surface.Field(ImageSizeDialogField.Width).Label, _widthBox, 0);
        AvaloniaLabeledFormRow.AddCompact(grid, surface.Field(ImageSizeDialogField.Height).Label, _heightBox, 1);
        AvaloniaLabeledFormRow.Place(grid, _lockCheck, 2, 1);
        Grid.SetRow(_status, 3);
        Grid.SetColumnSpan(_status, 2);
        grid.Children.Add(_status);

        var ok = CreateButton("OK", isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = CreateButton("Cancel", isCancel: true);
        cancel.Click += (_, _) => Close(null);
        AvaloniaLabeledFormRow.Place(grid, AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)), 4, 1);

        Content = new Border { Padding = new Thickness(14), Child = grid };
        Opened += (_, _) => AvaloniaCompactDialogChrome.FocusAndSelect(_widthBox);
        CloseOnEscape(this, () => Close(null));
    }

    public static Task<ImageSizeDialogResult?> ShowAsync(
        Window owner,
        double widthPt,
        double heightPt,
        string title = ImageSizeDialogPlanner.DefaultTitle) =>
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
        AvaloniaCompactDialogChrome.FocusAndSelect(validation?.Field == ImageSizeDialogField.Height ? _heightBox : _widthBox);
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
        var surface = ImageBorderDialogPlanner.Surface;
        Title = surface.Title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        ImageChartDialogSurfaceSemantics.Apply(this, surface);

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
        ImageChartDialogSurfaceSemantics.Apply(_colorBox, surface.Field(ImageBorderDialogField.Color));
        ImageChartDialogSurfaceSemantics.Apply(_widthBox, surface.Field(ImageBorderDialogField.Width));
        ImageChartDialogSurfaceSemantics.Apply(_dashBox, surface.Field(ImageBorderDialogField.Style));
        AvaloniaCompactDialogChrome.ApplyComboBox(_dashBox, Style);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, Style, new Thickness(0, 6, 0, 0));
        ImageChartDialogSurfaceSemantics.ApplyValidation(_status, surface);

        var grid = AvaloniaLabeledFormRow.CreateCompactGrid(rows: 6);
        AvaloniaLabeledFormRow.AddCompact(grid, surface.Field(ImageBorderDialogField.Color).Label, _colorBox, 0);
        AvaloniaLabeledFormRow.AddCompact(grid, surface.Field(ImageBorderDialogField.Width).Label, _widthBox, 1);
        AvaloniaLabeledFormRow.AddCompact(grid, surface.Field(ImageBorderDialogField.Style).Label, _dashBox, 2);

        var note = new TextBlock
        {
            Text = surface.SupportingText,
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
        AvaloniaLabeledFormRow.Place(grid, AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)), 5, 1);

        Content = new Border { Padding = new Thickness(14), Child = grid };
        Opened += (_, _) => AvaloniaCompactDialogChrome.FocusAndSelect(_colorBox);
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
        AvaloniaCompactDialogChrome.FocusAndSelect(validation?.Field == ImageBorderDialogField.Width ? _widthBox : _colorBox);
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

}
