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
        var state = BordersAndShadingDialogPlanner.BuildPageBordersInitialState(
            current,
            CultureInfo.CurrentCulture);

        Title = BordersAndShadingDialogPlanner.Title;
        Width = 430;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _setting.ItemsSource = BordersAndShadingDialogPlanner.SettingNames;
        _setting.SelectedIndex = state.SettingIndex;
        _style.ItemsSource = BordersAndShadingDialogPlanner.LineStyleNames;
        _style.SelectedIndex = state.LineStyleIndex;
        _art.ItemsSource = BordersAndShadingDialogPlanner.ArtBorders.Select(option => option.Label).ToArray();
        _art.SelectedIndex = state.ArtIndex;
        _color.ItemsSource = BordersAndShadingDialogPlanner.Palette;
        _color.SelectedIndex = state.ColorIndex;
        _width.Text = state.WidthText;
        AvaloniaCompactDialogChrome.ApplyComboBox(_style, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_setting, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_art, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyComboBox(_color, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_width, InsertDialogLayout.ChromeStyle);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, InsertDialogLayout.ChromeStyle, new Thickness(14, 8, 14, 0));

        var grid = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        InsertDialogLayout.AddLabeledRow(grid, 0, BordersAndShadingDialogPlanner.SettingLabel, _setting);
        InsertDialogLayout.AddLabeledRow(grid, 1, BordersAndShadingDialogPlanner.StyleLabel, _style);
        InsertDialogLayout.AddLabeledRow(grid, 2, BordersAndShadingDialogPlanner.ArtBorderLabel, _art);
        InsertDialogLayout.AddLabeledRow(grid, 3, BordersAndShadingDialogPlanner.ColorLabel, _color);
        InsertDialogLayout.AddLabeledRow(grid, 4, BordersAndShadingDialogPlanner.WidthLabel, _width);

        var okButton = InsertDialogLayout.MakeButton(BordersAndShadingDialogPlanner.AcceptButtonLabel, (_, _) =>
        {
            var acceptance = BordersAndShadingDialogPlanner.SubmitPageBorders(
                new PageBordersDialogInput(
                    _setting.SelectedIndex,
                    _style.SelectedIndex,
                    _color.SelectedIndex,
                    _width.Text,
                    _art.SelectedIndex),
                CultureInfo.CurrentCulture);
            if (!acceptance.IsAccepted)
            {
                _status.Text = acceptance.ValidationMessage ?? BordersAndShadingDialogPlanner.WidthValidationMessage;
                _status.IsVisible = true;
                _width.Focus();
                return;
            }

            Result = acceptance.PageBorder;
            Close();
        });
        var noneButton = InsertDialogLayout.MakeButton(BordersAndShadingDialogPlanner.RemovePageBorderButtonLabel, (_, _) =>
        {
            RemoveRequested = true;
            Close();
        });
        var cancelButton = InsertDialogLayout.MakeButton(BordersAndShadingDialogPlanner.CancelButtonLabel, (_, _) => Close());
        var btnRow = AvaloniaCompactDialogChrome.CreateActionRow([okButton, noneButton, cancelButton], new Thickness(14, 12, 14, 12));

        var outer = new StackPanel();
        outer.Children.Add(grid);
        outer.Children.Add(_status);
        outer.Children.Add(btnRow);
        Content = outer;
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

    private readonly WatermarkOptionsDialogSession _session;

    /// <summary>The watermark to apply (OK), or null when cancelled / Remove was clicked.</summary>
    public WatermarkOptions? Result { get; private set; }

    /// <summary>True when the user chose "No watermark" (remove).</summary>
    public bool RemoveRequested { get; private set; }

    public WatermarkDialog(WatermarkOptions? current)
    {
        _session = new WatermarkOptionsDialogSession(current, CultureInfo.CurrentCulture);
        var state = _session.InitialState;

        Title = WatermarkOptionsDialogPlanner.Title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

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
            var acceptance = _session.Remove();
            RemoveRequested = acceptance.RemoveRequested;
            Result = acceptance.Result;
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
        var grid = AvaloniaLabeledFormRow.CreateCompactGrid(0);
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

        var grid = AvaloniaLabeledFormRow.CreateCompactGrid(0);
        InsertDialogLayout.AddLabeledRow(grid, 0, WatermarkOptionsDialogPlanner.ImageFileLabel, fileRow);
        InsertDialogLayout.AddLabeledRow(grid, 1, WatermarkOptionsDialogPlanner.ScaleLabel, _scaleBox);
        InsertDialogLayout.AddLabeledRow(grid, 2, WatermarkOptionsDialogPlanner.LayoutLabel, CreateRadioRow(_pictureDiagonal, _pictureHorizontal));

        var panel = new StackPanel { Margin = new Thickness(14, 0, 14, 0) };
        panel.Children.Add(grid);
        panel.Children.Add(_washout);
        return panel;
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
        _session.SelectMode(isPicture ? WatermarkDialogMode.Picture : WatermarkDialogMode.Text);
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
            ShowValidation(WatermarkOptionsDialogPlanner.FormatImageReadFailure(ex.Message), _pathBox);
        }
    }

    private void LoadPictureImage(string fileName, byte[] imageBytes)
    {
        var import = _session.ImportImage(fileName, imageBytes);
        _pathBox.Text = import.DisplayLabel;
        ClearValidation();
    }

    private bool Accept(bool closeOnSuccess)
    {
        _session.SelectMode(
            _pictureMode.IsChecked == true ? WatermarkDialogMode.Picture : WatermarkDialogMode.Text);
        var acceptance = _session.Submit(new WatermarkOptionsDialogSubmission(
            _text.Text,
            _font.Text,
            _color.Text,
            _horizontal.IsChecked == true,
            _semitransparent.IsChecked == true,
            _scaleBox.Text,
            _pictureHorizontal.IsChecked == true,
            _washout.IsChecked == true));
        if (!acceptance.IsAccepted)
        {
            ShowValidation(
                acceptance.Validation?.Message ?? WatermarkOptionsDialogPlanner.TextValidationMessage,
                FocusTarget(acceptance.Validation?.Target));
            return false;
        }

        Result = acceptance.Result;
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
