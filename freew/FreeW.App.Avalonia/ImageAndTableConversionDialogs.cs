using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

internal sealed class ImageCropDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly TextBox _leftBox;
    private readonly TextBox _rightBox;
    private readonly TextBox _topBox;
    private readonly TextBox _bottomBox;
    private readonly TextBlock _status = new();

    private ImageCropDialog(double left, double right, double top, double bottom)
    {
        var surface = ImageCropDialogPlanner.Surface;
        Title = surface.Title;
        Width = 300;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        ImageChartDialogSurfaceSemantics.Apply(this, surface);

        var state = ImageCropDialogPlanner.BuildInitialState(
            left,
            right,
            top,
            bottom,
            CultureInfo.CurrentCulture);
        _leftBox = MakeBox(state.LeftText);
        _rightBox = MakeBox(state.RightText);
        _topBox = MakeBox(state.TopText);
        _bottomBox = MakeBox(state.BottomText);
        ImageChartDialogSurfaceSemantics.Apply(_leftBox, surface.Field(ImageCropDialogField.Left));
        ImageChartDialogSurfaceSemantics.Apply(_rightBox, surface.Field(ImageCropDialogField.Right));
        ImageChartDialogSurfaceSemantics.Apply(_topBox, surface.Field(ImageCropDialogField.Top));
        ImageChartDialogSurfaceSemantics.Apply(_bottomBox, surface.Field(ImageCropDialogField.Bottom));
        AvaloniaCompactDialogChrome.ApplyValidationStatus(
            _status,
            DialogChromeStyle,
            new Thickness(0, 6, 0, 0));
        ImageChartDialogSurfaceSemantics.ApplyValidation(_status, surface);

        var grid = AvaloniaLabeledFormRow.CreateCompactGrid(4);

        AddField(grid, surface.Field(ImageCropDialogField.Left).Label, _leftBox, 0);
        AddField(grid, surface.Field(ImageCropDialogField.Right).Label, _rightBox, 1);
        AddField(grid, surface.Field(ImageCropDialogField.Top).Label, _topBox, 2);
        AddField(grid, surface.Field(ImageCropDialogField.Bottom).Label, _bottomBox, 3);

        var ok = new Button { Content = "OK", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(14),
            Children =
            {
                grid,
                new TextBlock
                {
                    Text = "Enter the percentage of width/height to remove from each edge (0-99).",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 10,
                    Margin = new Thickness(0, 6, 0, 0),
                },
                _status,
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 12, 0, 0)),
            },
        };

        Opened += (_, _) =>
        {
            AvaloniaCompactDialogChrome.FocusAndSelect(_leftBox);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Close(null);
            e.Handled = true;
        };
    }

    public static Task<ImageCropDialogResult?> ShowAsync(
        Window owner,
        double left,
        double right,
        double top,
        double bottom) =>
        new ImageCropDialog(left, right, top, bottom).ShowDialog<ImageCropDialogResult?>(owner);

    private void Accept()
    {
        if (ImageCropDialogPlanner.TryBuildResult(
                new ImageCropDialogInput(_leftBox.Text, _rightBox.Text, _topBox.Text, _bottomBox.Text),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            Close(result);
            return;
        }

        _status.Text = validation?.Message ?? ImageCropDialogPlanner.PercentageValidationMessage;
        FocusValidationField(validation?.Field);
    }

    private void FocusValidationField(ImageCropDialogField? field)
    {
        var box = field switch
        {
            ImageCropDialogField.Right => _rightBox,
            ImageCropDialogField.Top => _topBox,
            ImageCropDialogField.Bottom => _bottomBox,
            _ => _leftBox,
        };
        AvaloniaCompactDialogChrome.FocusAndSelect(box);
    }

    private static TextBox MakeBox(string text)
    {
        var box = new TextBox { Text = text, MinWidth = 80 };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        return box;
    }

    private static void AddField(Grid grid, string label, Control field, int row)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 0),
        };
        AvaloniaLabeledFormRow.Place(grid, text, row, 0);
        AvaloniaLabeledFormRow.Place(grid, field, row, 1);
    }
}

internal sealed class TableTextConversionDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;
    private readonly ListBox _choices;

    private TableTextConversionDialog(string title)
    {
        var text = TableTextConversionDialogPlanner.ResolveText(UiText.Get);
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _choices = new ListBox
        {
            MinWidth = 240,
            MinHeight = 90,
            ItemsSource = text.Choices.Select(choice => choice.Label).ToArray(),
            SelectedIndex = TableTextConversionDialogPlanner.DefaultChoiceIndex,
            Margin = new Thickness(0, 0, 0, 12),
        };
        AvaloniaCompactDialogChrome.ApplyListBox(_choices, DialogChromeStyle);
        _choices.DoubleTapped += (_, _) => Accept();

        var ok = new Button { Content = "OK", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock
                {
                    Text = text.PromptLabel,
                    Margin = new Thickness(0, 0, 0, 4),
                },
                _choices,
                AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]),
            },
        };

        Opened += (_, _) => _choices.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            Close(null);
            e.Handled = true;
        };
    }

    public static Task<char?> ShowAsync(Window owner, string title) =>
        new TableTextConversionDialog(title).ShowDialog<char?>(owner);

    private void Accept()
    {
        if (TableTextConversionDialogPlanner.DelimiterAt(_choices.SelectedIndex) is { } delimiter)
            Close(delimiter);
    }
}
