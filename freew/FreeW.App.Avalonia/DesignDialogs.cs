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
/// AV-DESIGN: Page Borders dialog (Design &gt; Page Background &gt; Page Borders). The page-border tab uses
/// the same setting/style/palette/width/art choices and shared result planner as WPF. A page border is a
/// uniform box in the FreeW model, so the paragraph-edge controls from WPF's combined Borders and Shading
/// dialog do not belong on this page-only launcher.
/// </summary>
public sealed class PageBordersDialog : FreeWDialogWindow
{
    private readonly ComboBox _setting = new() { MinWidth = 220 };
    private readonly ComboBox _style = new() { MinWidth = 220 };
    private readonly ComboBox _art = new() { MinWidth = 220 };
    private readonly ComboBox _color = new() { MinWidth = 220 };
    private readonly TextBox _width = new() { MinWidth = 120 };
    private readonly TextBlock _status = new();

    /// <summary>The border the user chose to apply (OK), or null when cancelled / Remove was clicked.</summary>
    public PageBorder? Result { get; private set; }

    /// <summary>True when the user clicked "None" (remove the page border).</summary>
    public bool RemoveRequested { get; private set; }

    public PageBordersDialog(PageBorder? current)
    {
        Title = "Borders and Shading";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _setting.ItemsSource = BordersAndShadingDialogPlanner.SettingNames;
        _setting.SelectedIndex = current is null ? 0 : 1;
        _style.ItemsSource = BordersAndShadingDialogPlanner.LineStyleNames;
        _style.SelectedIndex = BordersAndShadingDialogPlanner.IndexOfLineStyle(current?.LineStyle ?? BorderLineStyle.Single);
        _art.ItemsSource = BordersAndShadingDialogPlanner.ArtBorders.Select(option => option.Label).ToArray();
        _art.SelectedIndex = BordersAndShadingDialogPlanner.ArtIndexFor(current?.ArtId ?? 0);
        _color.ItemsSource = BordersAndShadingDialogPlanner.Palette;
        _color.SelectedIndex = ColorIndex(current?.ColorHex);
        _width.Text = BordersAndShadingDialogPlanner.FormatPoints(current?.WidthPt ?? 1.0, CultureInfo.CurrentCulture);
        AvaloniaCompactDialogChrome.ApplyComboBox(_style, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_setting, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_art, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_color, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_width, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, InsertDialogLayout.ChromeStyle, new Thickness(14, 8, 14, 0));

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, "Setting:", _setting);
        InsertDialogLayout.AddLabeledRow(grid, 1, "Style:", _style);
        InsertDialogLayout.AddLabeledRow(grid, 2, "Art border:", _art);
        InsertDialogLayout.AddLabeledRow(grid, 3, "Color:", _color);
        InsertDialogLayout.AddLabeledRow(grid, 4, "Width (pt):", _width);

        var okButton = InsertDialogLayout.MakeButton("OK", (_, _) =>
        {
            if (!BordersAndShadingDialogPlanner.TryBuildResult(
                    new BordersAndShadingDialogInput(
                        ParagraphSettingIndex: 0,
                        ParagraphLineStyleIndex: 0,
                        ParagraphColorHex: null,
                        ParagraphWidthText: "1",
                        Top: false,
                        Left: false,
                        Bottom: false,
                        Right: false,
                        PageSettingIndex: _setting.SelectedIndex,
                        PageLineStyleIndex: _style.SelectedIndex,
                        PageColorHex: SelectedColor(),
                        PageWidthText: _width.Text,
                        PageArtIndex: _art.SelectedIndex,
                        ShadingColorHex: null,
                        ShadingPatternIndex: 0),
                    CultureInfo.CurrentCulture,
                    out var planned,
                    out var error))
            {
                _status.Text = error ?? BordersAndShadingDialogPlanner.WidthValidationMessage;
                _status.IsVisible = true;
                _width.Focus();
                return;
            }

            Result = planned?.PageBorder;
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
        outer.Children.Add(_status);
        outer.Children.Add(btnRow);
        Content = outer;
    }

    private string? SelectedColor() =>
        _color.SelectedIndex >= 0 && _color.SelectedIndex < BordersAndShadingDialogPlanner.Palette.Count
            ? BordersAndShadingDialogPlanner.Palette[_color.SelectedIndex]
            : null;

    private static int ColorIndex(string? hex)
    {
        for (var i = 0; i < BordersAndShadingDialogPlanner.Palette.Count; i++)
        {
            if (string.Equals(BordersAndShadingDialogPlanner.Palette[i], hex, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }
}

/// <summary>
/// AV-DESIGN: Custom Watermark dialog (Design &gt; Page Background &gt; Watermark &gt; Custom Watermark).
/// Supports text and picture watermarks through the shared <see cref="WatermarkOptionsDialogPlanner"/>.
/// </summary>
public sealed class WatermarkDialog : FreeWDialogWindow
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

        var actionPlans = WatermarkOptionsDialogPlanner.ActionButtons;
        var okPlan = actionPlans[0];
        var okButton = InsertDialogLayout.MakeButton(okPlan.Label, (_, _) => Accept(closeOnSuccess: true));
        okButton.IsDefault = okPlan.IsDefault;
        var noneButton = InsertDialogLayout.MakeButton(actionPlans[1].Label, (_, _) =>
        {
            RemoveRequested = true;
            Close();
        });
        var cancelPlan = actionPlans[2];
        var cancelButton = InsertDialogLayout.MakeButton(cancelPlan.Label, (_, _) => Close());
        cancelButton.IsCancel = cancelPlan.IsCancel;
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
