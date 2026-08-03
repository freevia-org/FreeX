using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Custom Watermark" dialog (Design &gt; Page Background &gt; Watermark &gt; Custom Watermark).
/// Lets the user set or clear a page watermark. Supports two modes:
/// <list type="bullet">
/// <item><b>Text watermark</b> — text, font family, font colour, diagonal/horizontal layout, and
///   semitransparent toggle. Matches the original dialog behaviour.</item>
/// <item><b>Picture watermark</b> — file picker (PNG/JPEG/BMP/GIF/TIFF), scale percentage
///   (Auto or 1–500%), washout (= semitransparent) toggle, and diagonal/horizontal layout.</item>
/// </list>
/// Returns the configured <see cref="WatermarkOptions"/> on OK, null on cancel, or null +
/// <paramref name="removeRequested"/> = true when the user clicks "Remove Watermark".
/// </summary>
internal sealed class WatermarkOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    /// <summary>Sentinel: user clicked "Remove Watermark" — caller should clear any existing watermark.</summary>
    public static readonly WatermarkOptions? Removed = null;

    // Mode selection.
    private readonly RadioButton _textRadio;
    private readonly RadioButton _pictureRadio;

    // Text watermark controls.
    private readonly TextBox _textBox;
    private readonly TextBox _fontBox;
    private readonly TextBox _colorBox;
    private readonly RadioButton _diagonalRadio;
    private readonly RadioButton _horizontalRadio;
    private readonly CheckBox _semitransparentCheck;

    // Picture watermark controls.
    private readonly TextBox _pathBox;
    private readonly TextBox _scaleBox;
    private readonly CheckBox _washoutCheck;
    private readonly RadioButton _picDiagonalRadio;
    private readonly RadioButton _picHorizontalRadio;

    // Panels for show/hide.
    private StackPanel? _textPanel;
    private StackPanel? _picturePanel;

    private byte[]? _pendingImageBytes; // loaded from the picked file

    private WatermarkOptions? _result;
    private bool _accepted;
    private bool _removeClicked;

    private WatermarkOptionsDialog(Window? owner, WatermarkOptions? current)
    {
        Owner = owner;
        Title = WatermarkOptionsDialogPlanner.Title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var state = WatermarkOptionsDialogPlanner.BuildInitialState(current, CultureInfo.CurrentCulture);

        // Mode radios.
        _textRadio    = new RadioButton { Content = WatermarkOptionsDialogPlanner.TextModeLabel,    GroupName = "WmMode", IsChecked = !state.IsPicture };
        _pictureRadio = new RadioButton { Content = WatermarkOptionsDialogPlanner.PictureModeLabel, GroupName = "WmMode", IsChecked = state.IsPicture };

        // Text controls.
        _textBox            = new TextBox { Text = state.Text, MinWidth = 200 };
        _fontBox            = new TextBox { Text = state.FontFamily, MinWidth = 150 };
        _colorBox           = new TextBox { Text = state.FontColorHex, MinWidth = 80 };
        _diagonalRadio      = new RadioButton { Content = WatermarkOptionsDialogPlanner.DiagonalLabel,   IsChecked = !state.TextIsHorizontal, GroupName = "WmLayout" };
        _horizontalRadio    = new RadioButton { Content = WatermarkOptionsDialogPlanner.HorizontalLabel, IsChecked = state.TextIsHorizontal, GroupName = "WmLayout" };
        _semitransparentCheck = new CheckBox { Content = WatermarkOptionsDialogPlanner.SemitransparentLabel, IsChecked = state.TextIsSemitransparent };

        // Picture controls.
        _pendingImageBytes   = current?.ImageBytes;
        _pathBox             = new TextBox { Text = state.PicturePathText, MinWidth = 200, IsReadOnly = true };
        _scaleBox            = new TextBox { Text = state.ScaleText, MinWidth = 80 };
        _washoutCheck        = new CheckBox { Content = WatermarkOptionsDialogPlanner.WashoutLabel, IsChecked = state.PictureWashout };
        _picDiagonalRadio    = new RadioButton { Content = WatermarkOptionsDialogPlanner.DiagonalLabel,   IsChecked = !state.PictureIsHorizontal, GroupName = "PicLayout" };
        _picHorizontalRadio  = new RadioButton { Content = WatermarkOptionsDialogPlanner.HorizontalLabel, IsChecked = state.PictureIsHorizontal, GroupName = "PicLayout" };

        Content = BuildContent();
        SyncPanelVisibility();

        _textRadio.Checked    += (_, _) => SyncPanelVisibility();
        _pictureRadio.Checked += (_, _) => SyncPanelVisibility();

        Loaded += (_, _) => (_textRadio.IsChecked == true ? (UIElement)_textBox : _pathBox).Focus();
    }

    private UIElement BuildContent()
    {
        var root = new StackPanel { Margin = new Thickness(14) };

        // Mode selection.
        var modePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        _textRadio.Margin    = new Thickness(0, 0, 24, 0);
        modePanel.Children.Add(_textRadio);
        modePanel.Children.Add(_pictureRadio);
        root.Children.Add(modePanel);
        root.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 8) });

        // ── Text watermark panel ──────────────────────────────────────────────────────────────────
        _textPanel = new StackPanel();
        _textPanel.Children.Add(new TextBlock
        {
            Text = WatermarkOptionsDialogPlanner.TextModeLabel,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var layoutPanel = new StackPanel { Orientation = Orientation.Horizontal };
        _diagonalRadio.Margin = new Thickness(0, 0, 16, 0);
        layoutPanel.Children.Add(_diagonalRadio);
        layoutPanel.Children.Add(_horizontalRadio);

        _textPanel.Children.Add(LabeledRow(WatermarkOptionsDialogPlanner.TextLabel,         _textBox));
        _textPanel.Children.Add(LabeledRow(WatermarkOptionsDialogPlanner.FontLabel,         _fontBox));
        _textPanel.Children.Add(LabeledRow(WatermarkOptionsDialogPlanner.ColorLabel,  _colorBox));
        _textPanel.Children.Add(LabeledRow(WatermarkOptionsDialogPlanner.LayoutLabel,       layoutPanel));
        _textPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 4) });
        _semitransparentCheck.Margin = new Thickness(0, 4, 0, 0);
        _textPanel.Children.Add(_semitransparentCheck);
        root.Children.Add(_textPanel);

        // ── Picture watermark panel ───────────────────────────────────────────────────────────────
        _picturePanel = new StackPanel();
        _picturePanel.Children.Add(new TextBlock
        {
            Text = WatermarkOptionsDialogPlanner.PictureModeLabel,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // File picker row.
        var fileRow = new StackPanel { Orientation = Orientation.Horizontal };
        _pathBox.MinWidth = 240;
        var browseBtn = new Button { Content = WatermarkOptionsDialogPlanner.SelectPictureButton, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
        browseBtn.Click += (_, _) => BrowseForImage();
        fileRow.Children.Add(_pathBox);
        fileRow.Children.Add(browseBtn);

        var picLayoutPanel = new StackPanel { Orientation = Orientation.Horizontal };
        _picDiagonalRadio.Margin = new Thickness(0, 0, 16, 0);
        picLayoutPanel.Children.Add(_picDiagonalRadio);
        picLayoutPanel.Children.Add(_picHorizontalRadio);

        _picturePanel.Children.Add(LabeledRow(WatermarkOptionsDialogPlanner.ImageFileLabel, fileRow));
        _picturePanel.Children.Add(LabeledRow(WatermarkOptionsDialogPlanner.ScaleLabel, _scaleBox));
        _picturePanel.Children.Add(LabeledRow(WatermarkOptionsDialogPlanner.LayoutLabel, picLayoutPanel));
        _washoutCheck.Margin = new Thickness(0, 6, 0, 0);
        _picturePanel.Children.Add(_washoutCheck);
        root.Children.Add(_picturePanel);

        // ── Buttons ───────────────────────────────────────────────────────────────────────────────
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var actionPlans = WatermarkOptionsDialogPlanner.ActionButtons;
        var okPlan = actionPlans[0];
        var ok = new Button { Content = okPlan.Label, MinWidth = 72, IsDefault = okPlan.IsDefault, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var remove = new Button { Content = actionPlans[1].Label, MinWidth = 130, Margin = new Thickness(0, 0, 8, 0) };
        remove.Click += (_, _) => { _removeClicked = true; Close(); };
        var cancelPlan = actionPlans[2];
        var cancel = new Button { Content = cancelPlan.Label, MinWidth = 72, IsCancel = cancelPlan.IsCancel };
        buttonRow.Children.Add(ok);
        buttonRow.Children.Add(remove);
        buttonRow.Children.Add(cancel);
        root.Children.Add(buttonRow);

        return root;
    }

    private void SyncPanelVisibility()
    {
        if (_textPanel is null || _picturePanel is null)
            return;
        var isPicture = _pictureRadio.IsChecked == true;
        _textPanel.Visibility    = isPicture ? Visibility.Collapsed : Visibility.Visible;
        _picturePanel.Visibility = isPicture ? Visibility.Visible   : Visibility.Collapsed;
    }

    private static UIElement LabeledRow(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lbl = new Label { Content = label, Margin = new Thickness(0, 4, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        return grid;
    }

    private void BrowseForImage()
    {
        var result = WpfFileDialogService.ShowOpenDialog(
            this,
            WatermarkOptionsDialogPlanner.WatermarkImageFilter,
            checkFileExists: true,
            title: WatermarkOptionsDialogPlanner.SelectWatermarkImageTitle);
        if (!result.Chosen || result.FileName is not { Length: > 0 } fileName)
            return;

        try
        {
            _pendingImageBytes = File.ReadAllBytes(fileName);
            _pathBox.Text = WatermarkOptionsDialogPlanner.FormatPickedImageLabel(
                Path.GetFileName(fileName),
                _pendingImageBytes.Length);
        }
        catch (Exception ex)
        {
            DialogMessageHelper.ShowWarning(this, $"Could not read image file: {ex.Message}", Title);
        }
    }

    private void Accept()
    {
        if (_pictureRadio.IsChecked == true)
            AcceptPicture();
        else
            AcceptText();
    }

    private void AcceptText()
    {
        if (!WatermarkOptionsDialogPlanner.TryBuildTextResult(
                new WatermarkTextDialogInput(
                    _textBox.Text,
                    _fontBox.Text,
                    _colorBox.Text,
                    _horizontalRadio.IsChecked == true,
                    _semitransparentCheck.IsChecked == true),
                out var result,
                out var validation))
        {
            DialogMessageHelper.ShowWarning(this, validation?.Message ?? WatermarkOptionsDialogPlanner.TextValidationMessage, Title);
            FocusValidationTarget(validation?.Target);
            return;
        }

        _result = result;
        _accepted = true;
        Close();
    }

    private void AcceptPicture()
    {
        if (!WatermarkOptionsDialogPlanner.TryBuildPictureResult(
                new WatermarkPictureDialogInput(
                    _pendingImageBytes,
                    _scaleBox.Text,
                    _picHorizontalRadio.IsChecked == true,
                    _washoutCheck.IsChecked == true),
                CultureInfo.CurrentCulture,
                out var result,
                out var validation))
        {
            DialogMessageHelper.ShowWarning(this, validation?.Message ?? WatermarkOptionsDialogPlanner.ImageValidationMessage, Title);
            FocusValidationTarget(validation?.Target);
            return;
        }

        _result = result;
        _accepted = true;
        Close();
    }

    private void FocusValidationTarget(WatermarkDialogValidationTarget? target)
    {
        switch (target)
        {
            case WatermarkDialogValidationTarget.Text:
                _textBox.Focus();
                break;
            case WatermarkDialogValidationTarget.Color:
                _colorBox.Focus();
                break;
            case WatermarkDialogValidationTarget.Scale:
                _scaleBox.Focus();
                break;
        }
    }

    /// <summary>
    /// Show the Custom Watermark dialog seeded with <paramref name="current"/> options. Returns:
    /// <list type="bullet">
    ///   <item>A <see cref="WatermarkOptions"/> instance when the user clicked OK.</item>
    ///   <item><c>null</c> with <paramref name="removeRequested"/> = true when the user clicked "Remove Watermark".</item>
    ///   <item><c>null</c> with <paramref name="removeRequested"/> = false when the user cancelled.</item>
    /// </list>
    /// </summary>
    public static WatermarkOptions? Prompt(Window? owner, WatermarkOptions? current, out bool removeRequested)
    {
        var dialog = new WatermarkOptionsDialog(owner, current);
        dialog.ShowDialog();
        removeRequested = dialog._removeClicked;
        return dialog._accepted ? dialog._result : null;
    }
}
