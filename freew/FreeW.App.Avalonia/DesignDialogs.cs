using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>
/// AV-DESIGN: Page Borders dialog (Design &gt; Page Background &gt; Page Borders). Collects a line style,
/// colour and width and returns a <see cref="PageBorder"/> on OK, or signals removal. Mirrors the existing
/// Avalonia dialog pattern (non-resizable, owner-centred, result via public properties awaited by the shell).
/// Edge-selection (top/bottom/left/right) is modelled by Word but FreeW's <see cref="PageBorder"/> is a
/// uniform box, so the dialog applies to all four edges (the most common case); per-edge selection is deferred.
/// </summary>
public sealed class PageBordersDialog : Window
{
    private static readonly (string Label, BorderLineStyle Style)[] BorderStyles =
    [
        ("Single", BorderLineStyle.Single),
        ("Dotted", BorderLineStyle.Dotted),
        ("Dashed", BorderLineStyle.Dashed),
        ("Double", BorderLineStyle.Double),
        ("Thick",  BorderLineStyle.Thick),
    ];

    private static readonly (string Label, string Hex)[] Colors =
    [
        ("Black",     "#000000"),
        ("Dark Blue", "#1F3864"),
        ("Blue",      "#2F5496"),
        ("Red",       "#C00000"),
        ("Green",     "#548235"),
        ("Gray",      "#808080"),
    ];

    private readonly ComboBox _style = new() { MinWidth = 180, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _color = new() { MinWidth = 180, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _width = new() { MinWidth = 180, Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>The border the user chose to apply (OK), or null when cancelled / Remove was clicked.</summary>
    public PageBorder? Result { get; private set; }

    /// <summary>True when the user clicked "None" (remove the page border).</summary>
    public bool RemoveRequested { get; private set; }

    public PageBordersDialog(PageBorder? current)
    {
        Title = "Page Borders";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _style.ItemsSource = BorderStyles.Select(s => s.Label).ToList();
        _style.SelectedIndex = Math.Max(0, Array.FindIndex(BorderStyles, s => s.Style == (current?.LineStyle ?? BorderLineStyle.Single)));

        _color.ItemsSource = Colors.Select(c => c.Label).ToList();
        _color.SelectedIndex = Math.Max(0, Array.FindIndex(Colors,
            c => string.Equals(c.Hex, current?.ColorHex, StringComparison.OrdinalIgnoreCase)));

        _width.ItemsSource = new[] { "0.5 pt", "1 pt", "1.5 pt", "2.25 pt", "3 pt", "4.5 pt", "6 pt" };
        _width.SelectedIndex = current is null ? 1 : ClosestWidthIndex(current.WidthPt);
        AvaloniaCompactDialogChrome.ApplyComboBox(_style, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_color, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_width, InsertDialogLayout.ChromeStyle);

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, "Style:", _style);
        InsertDialogLayout.AddLabeledRow(grid, 1, "Color:", _color);
        InsertDialogLayout.AddLabeledRow(grid, 2, "Width:", _width);

        var okButton = InsertDialogLayout.MakeButton("OK", (_, _) =>
        {
            var style = BorderStyles[Math.Max(0, _style.SelectedIndex)].Style;
            var hex = Colors[Math.Max(0, _color.SelectedIndex)].Hex;
            var widthPt = WidthPtForIndex(Math.Max(0, _width.SelectedIndex));
            Result = new PageBorder(hex, widthPt) { LineStyle = style };
            Close();
        });
        var noneButton = InsertDialogLayout.MakeButton("None", (_, _) =>
        {
            RemoveRequested = true;
            Close();
        });
        var cancelButton = InsertDialogLayout.MakeButton("Cancel", (_, _) => Close());
        var btnRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, noneButton, cancelButton], new Thickness(14, 12, 14, 12));

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(btnRow);
        Content = outer;
    }

    private static readonly double[] WidthValues = [0.5, 1.0, 1.5, 2.25, 3.0, 4.5, 6.0];

    private static int ClosestWidthIndex(double pt)
    {
        var best = 1;
        var bestDelta = double.MaxValue;
        for (var i = 0; i < WidthValues.Length; i++)
        {
            var d = Math.Abs(WidthValues[i] - pt);
            if (d < bestDelta) { bestDelta = d; best = i; }
        }
        return best;
    }

    private static double WidthPtForIndex(int index) =>
        index >= 0 && index < WidthValues.Length ? WidthValues[index] : 1.0;
}

/// <summary>
/// AV-DESIGN: Custom Watermark dialog (Design &gt; Page Background &gt; Watermark &gt; Custom Watermark).
/// Supports text and picture watermarks through the shared <see cref="WatermarkOptionsDialogPlanner"/>.
/// </summary>
public sealed class WatermarkDialog : Window
{
    private static readonly FilePickerFileType WatermarkImageFileType =
        AvaloniaFilePickerTypeAdapter.CreateFileType(
            "Image files",
            ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.tif", "*.tiff"],
            ["image/png", "image/jpeg", "image/bmp", "image/gif", "image/tiff"]);

    private readonly RadioButton _textMode;
    private readonly RadioButton _pictureMode;
    private readonly TextBox _text;
    private readonly TextBox _font;
    private readonly TextBox _color;
    private readonly RadioButton _diagonal;
    private readonly RadioButton _horizontal;
    private readonly CheckBox _semitransparent;
    private readonly TextBox _pathBox;
    private readonly TextBox _scaleBox;
    private readonly RadioButton _pictureDiagonal;
    private readonly RadioButton _pictureHorizontal;
    private readonly CheckBox _washout;
    private readonly TextBlock _status = new();
    private readonly StackPanel _textPanel;
    private readonly StackPanel _picturePanel;

    private byte[]? _pendingImageBytes;

    /// <summary>The watermark to apply (OK), or null when cancelled / Remove was clicked.</summary>
    public WatermarkOptions? Result { get; private set; }

    /// <summary>True when the user chose "No watermark" (remove).</summary>
    public bool RemoveRequested { get; private set; }

    public WatermarkDialog(WatermarkOptions? current)
    {
        var state = WatermarkOptionsDialogPlanner.BuildInitialState(current, CultureInfo.CurrentCulture);

        Title = WatermarkOptionsDialogPlanner.Title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _pendingImageBytes = current?.ImageBytes;
        _textMode = new RadioButton
        {
            Content = WatermarkOptionsDialogPlanner.TextModeLabel,
            IsChecked = !state.IsPicture,
            GroupName = "WatermarkMode",
            Margin = new Thickness(0, 0, 22, 0),
        };
        _pictureMode = new RadioButton
        {
            Content = WatermarkOptionsDialogPlanner.PictureModeLabel,
            IsChecked = state.IsPicture,
            GroupName = "WatermarkMode",
        };
        _text = new TextBox
        {
            Text = state.Text,
            MinWidth = 240,
            PlaceholderText = WatermarkOptionsDialogPlanner.DefaultText,
            Margin = new Thickness(0, 6, 0, 0),
        };
        _font = new TextBox
        {
            Text = state.FontFamily,
            MinWidth = 240,
            Margin = new Thickness(0, 6, 0, 0),
        };
        _color = new TextBox
        {
            Text = state.FontColorHex,
            MinWidth = 240,
            Margin = new Thickness(0, 6, 0, 0),
        };
        _diagonal = new RadioButton
        {
            Content = WatermarkOptionsDialogPlanner.DiagonalLabel,
            IsChecked = !state.TextIsHorizontal,
            GroupName = "WatermarkTextLayout",
            Margin = new Thickness(0, 6, 16, 0),
        };
        _horizontal = new RadioButton
        {
            Content = WatermarkOptionsDialogPlanner.HorizontalLabel,
            IsChecked = state.TextIsHorizontal,
            GroupName = "WatermarkTextLayout",
            Margin = new Thickness(0, 6, 0, 0),
        };
        _semitransparent = new CheckBox
        {
            Content = WatermarkOptionsDialogPlanner.SemitransparentLabel,
            IsChecked = state.TextIsSemitransparent,
            Margin = new Thickness(0, 8, 0, 0),
        };
        _pathBox = new TextBox
        {
            Text = state.PicturePathText,
            IsReadOnly = true,
            MinWidth = 240,
            Margin = new Thickness(0, 6, 0, 0),
        };
        _scaleBox = new TextBox
        {
            Text = state.ScaleText,
            MinWidth = 120,
            Margin = new Thickness(0, 6, 0, 0),
        };
        _pictureDiagonal = new RadioButton
        {
            Content = WatermarkOptionsDialogPlanner.DiagonalLabel,
            IsChecked = !state.PictureIsHorizontal,
            GroupName = "WatermarkPictureLayout",
            Margin = new Thickness(0, 6, 16, 0),
        };
        _pictureHorizontal = new RadioButton
        {
            Content = WatermarkOptionsDialogPlanner.HorizontalLabel,
            IsChecked = state.PictureIsHorizontal,
            GroupName = "WatermarkPictureLayout",
            Margin = new Thickness(0, 6, 0, 0),
        };
        _washout = new CheckBox
        {
            Content = WatermarkOptionsDialogPlanner.WashoutLabel,
            IsChecked = state.PictureWashout,
            Margin = new Thickness(0, 8, 0, 0),
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(_text, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_font, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_color, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_pathBox, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_scaleBox, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_textMode, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_pictureMode, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_diagonal, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_horizontal, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_pictureDiagonal, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyRadioButton(_pictureHorizontal, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCheckBox(_semitransparent, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCheckBox(_washout, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, InsertDialogLayout.ChromeStyle, new Thickness(14, 8, 14, 0));

        _textPanel = BuildTextPanel();
        _picturePanel = BuildPicturePanel();
        _textMode.IsCheckedChanged += (_, _) => SyncModePanels();
        _pictureMode.IsCheckedChanged += (_, _) => SyncModePanels();

        var modeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 12, 14, 8),
        };
        modeRow.Children.Add(_textMode);
        modeRow.Children.Add(_pictureMode);

        var okButton = InsertDialogLayout.MakeButton(WatermarkOptionsDialogPlanner.OkButton, (_, _) => Accept(closeOnSuccess: true));
        var noneButton = InsertDialogLayout.MakeButton(WatermarkOptionsDialogPlanner.RemoveWatermarkButton, (_, _) =>
        {
            RemoveRequested = true;
            Close();
        });
        var cancelButton = InsertDialogLayout.MakeButton(WatermarkOptionsDialogPlanner.CancelButton, (_, _) => Close());
        var btnRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, noneButton, cancelButton], new Thickness(14, 12, 14, 12));

        var outer = new StackPanel();
        outer.Children.Add(modeRow);
        outer.Children.Add(_textPanel);
        outer.Children.Add(_picturePanel);
        outer.Children.Add(_status);
        outer.Children.Add(btnRow);
        Content = outer;
        SyncModePanels();
    }

    internal void SelectPictureWatermarkForTests(
        byte[] imageBytes,
        string fileName,
        string scaleText,
        bool isHorizontal,
        bool isWashout)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        _pictureMode.IsChecked = true;
        LoadPictureImage(fileName, imageBytes);
        _scaleBox.Text = scaleText;
        _pictureHorizontal.IsChecked = isHorizontal;
        _pictureDiagonal.IsChecked = !isHorizontal;
        _washout.IsChecked = isWashout;
        SyncModePanels();
    }

    internal bool AcceptForTests() => Accept(closeOnSuccess: false);

    private StackPanel BuildTextPanel()
    {
        var grid = CreateLabeledGrid();
        InsertDialogLayout.AddLabeledRow(grid, 0, WatermarkOptionsDialogPlanner.TextLabel, _text);
        InsertDialogLayout.AddLabeledRow(grid, 1, WatermarkOptionsDialogPlanner.FontLabel, _font);
        InsertDialogLayout.AddLabeledRow(grid, 2, WatermarkOptionsDialogPlanner.ColorLabel, _color);
        InsertDialogLayout.AddLabeledRow(grid, 3, WatermarkOptionsDialogPlanner.LayoutLabel, CreateRadioRow(_diagonal, _horizontal));

        var panel = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
        panel.Children.Add(grid);
        panel.Children.Add(_semitransparent);
        return panel;
    }

    private StackPanel BuildPicturePanel()
    {
        var browseButton = InsertDialogLayout.MakeButton(WatermarkOptionsDialogPlanner.SelectPictureButton, async (_, _) => await BrowseForImageAsync());
        var fileRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };
        fileRow.Children.Add(_pathBox);
        fileRow.Children.Add(browseButton);

        var grid = CreateLabeledGrid();
        InsertDialogLayout.AddLabeledRow(grid, 0, WatermarkOptionsDialogPlanner.ImageFileLabel, fileRow);
        InsertDialogLayout.AddLabeledRow(grid, 1, WatermarkOptionsDialogPlanner.ScaleLabel, _scaleBox);
        InsertDialogLayout.AddLabeledRow(grid, 2, WatermarkOptionsDialogPlanner.LayoutLabel, CreateRadioRow(_pictureDiagonal, _pictureHorizontal));

        var panel = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
        panel.Children.Add(grid);
        panel.Children.Add(_washout);
        return panel;
    }

    private static Grid CreateLabeledGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static StackPanel CreateRadioRow(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls)
            row.Children.Add(control);
        return row;
    }

    private void SyncModePanels()
    {
        var isPicture = _pictureMode.IsChecked == true;
        _textPanel.IsVisible = !isPicture;
        _picturePanel.IsVisible = isPicture;
    }

    private async Task BrowseForImageAsync()
    {
        try
        {
            using var file = await AvaloniaFilePickerService.PickSingleOpenFileAsync(
                StorageProvider,
                AvaloniaFilePickerOpenRequest.FromFileTypes(
                    WatermarkOptionsDialogPlanner.SelectWatermarkImageTitle,
                    [WatermarkImageFileType]));
            if (file is null)
                return;

            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            LoadPictureImage(file.Name, memory.ToArray());
        }
        catch (Exception ex)
        {
            ShowValidation($"Could not read image file: {ex.Message}", _pathBox);
        }
    }

    private void LoadPictureImage(string fileName, byte[] imageBytes)
    {
        _pendingImageBytes = imageBytes;
        _pathBox.Text = WatermarkOptionsDialogPlanner.FormatPickedImageLabel(fileName, imageBytes.Length);
        ClearValidation();
    }

    private bool Accept(bool closeOnSuccess)
    {
        if (_pictureMode.IsChecked == true)
            return AcceptPicture(closeOnSuccess);

        return AcceptText(closeOnSuccess);
    }

    private bool AcceptText(bool closeOnSuccess)
    {
        if (!WatermarkOptionsDialogPlanner.TryBuildTextResult(
                new WatermarkTextDialogInput(
                    _text.Text,
                    _font.Text,
                    _color.Text,
                    _horizontal.IsChecked == true,
                    _semitransparent.IsChecked == true),
                out var result,
                out var validation))
        {
            ShowValidation(validation?.Message ?? WatermarkOptionsDialogPlanner.TextValidationMessage, FocusTarget(validation?.Target));
            return false;
        }

        Result = result;
        ClearValidation();
        if (closeOnSuccess)
            Close();
        return true;
    }

    private bool AcceptPicture(bool closeOnSuccess)
    {
        if (!WatermarkOptionsDialogPlanner.TryBuildPictureResult(
                new WatermarkPictureDialogInput(
                    _pendingImageBytes,
                    _scaleBox.Text,
                    _pictureHorizontal.IsChecked == true,
                    _washout.IsChecked == true),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            ShowValidation(validation?.Message ?? WatermarkOptionsDialogPlanner.ImageValidationMessage, FocusTarget(validation?.Target));
            return false;
        }

        Result = result;
        ClearValidation();
        if (closeOnSuccess)
            Close();
        return true;
    }

    private Control? FocusTarget(WatermarkDialogValidationTarget? target) =>
        target switch
        {
            WatermarkDialogValidationTarget.Text => _text,
            WatermarkDialogValidationTarget.Color => _color,
            WatermarkDialogValidationTarget.Image => _pathBox,
            WatermarkDialogValidationTarget.Scale => _scaleBox,
            _ => null,
        };

    private void ShowValidation(string message, Control? focusTarget)
    {
        _status.Text = message;
        _status.IsVisible = true;
        focusTarget?.Focus();
    }

    private void ClearValidation()
    {
        _status.Text = string.Empty;
        _status.IsVisible = false;
    }
}
