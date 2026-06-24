using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.Core.Model;
using Microsoft.Win32;

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
        Title = "Printed Watermark";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var isPicture = current?.IsPicture ?? false;
        var seed = current ?? new WatermarkOptions("DRAFT");

        // Mode radios.
        _textRadio    = new RadioButton { Content = "Text watermark",    GroupName = "WmMode", IsChecked = !isPicture };
        _pictureRadio = new RadioButton { Content = "Picture watermark", GroupName = "WmMode", IsChecked = isPicture };

        // Text controls.
        _textBox            = new TextBox { Text = isPicture ? "DRAFT" : seed.Text, MinWidth = 200 };
        _fontBox            = new TextBox { Text = seed.FontFamily, MinWidth = 150 };
        _colorBox           = new TextBox { Text = seed.FontColorHex, MinWidth = 80 };
        _diagonalRadio      = new RadioButton { Content = "Diagonal",   IsChecked = seed.Layout == WatermarkLayout.Diagonal, GroupName = "WmLayout" };
        _horizontalRadio    = new RadioButton { Content = "Horizontal", IsChecked = seed.Layout == WatermarkLayout.Horizontal, GroupName = "WmLayout" };
        _semitransparentCheck = new CheckBox { Content = "Semitransparent", IsChecked = seed.Opacity < 1.0 };

        // Picture controls.
        _pendingImageBytes   = current?.ImageBytes;
        _pathBox             = new TextBox { Text = "(choose an image file…)", MinWidth = 200, IsReadOnly = true };
        if (_pendingImageBytes is { Length: > 0 })
            _pathBox.Text = $"(image loaded — {_pendingImageBytes.Length / 1024} KB)";
        _scaleBox            = new TextBox { Text = (current?.ScalePct ?? 0).ToString(), MinWidth = 80 };
        _washoutCheck        = new CheckBox { Content = "Washout (semitransparent)", IsChecked = isPicture ? seed.Opacity < 1.0 : true };
        _picDiagonalRadio    = new RadioButton { Content = "Diagonal",   IsChecked = seed.Layout == WatermarkLayout.Diagonal, GroupName = "PicLayout" };
        _picHorizontalRadio  = new RadioButton { Content = "Horizontal", IsChecked = seed.Layout == WatermarkLayout.Horizontal, GroupName = "PicLayout" };

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
            Text = "Text watermark",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var layoutPanel = new StackPanel { Orientation = Orientation.Horizontal };
        _diagonalRadio.Margin = new Thickness(0, 0, 16, 0);
        layoutPanel.Children.Add(_diagonalRadio);
        layoutPanel.Children.Add(_horizontalRadio);

        _textPanel.Children.Add(LabeledRow("Text:",         _textBox));
        _textPanel.Children.Add(LabeledRow("Font:",         _fontBox));
        _textPanel.Children.Add(LabeledRow("Color (hex):",  _colorBox));
        _textPanel.Children.Add(LabeledRow("Layout:",       layoutPanel));
        _textPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 4) });
        _semitransparentCheck.Margin = new Thickness(0, 4, 0, 0);
        _textPanel.Children.Add(_semitransparentCheck);
        root.Children.Add(_textPanel);

        // ── Picture watermark panel ───────────────────────────────────────────────────────────────
        _picturePanel = new StackPanel();
        _picturePanel.Children.Add(new TextBlock
        {
            Text = "Picture watermark",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // File picker row.
        var fileRow = new StackPanel { Orientation = Orientation.Horizontal };
        _pathBox.MinWidth = 240;
        var browseBtn = new Button { Content = "Select Picture…", MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
        browseBtn.Click += (_, _) => BrowseForImage();
        fileRow.Children.Add(_pathBox);
        fileRow.Children.Add(browseBtn);

        var picLayoutPanel = new StackPanel { Orientation = Orientation.Horizontal };
        _picDiagonalRadio.Margin = new Thickness(0, 0, 16, 0);
        picLayoutPanel.Children.Add(_picDiagonalRadio);
        picLayoutPanel.Children.Add(_picHorizontalRadio);

        _picturePanel.Children.Add(LabeledRow("Image file:", fileRow));
        _picturePanel.Children.Add(LabeledRow("Scale (%, 0=Auto):", _scaleBox));
        _picturePanel.Children.Add(LabeledRow("Layout:", picLayoutPanel));
        _washoutCheck.Margin = new Thickness(0, 6, 0, 0);
        _picturePanel.Children.Add(_washoutCheck);
        root.Children.Add(_picturePanel);

        // ── Buttons ───────────────────────────────────────────────────────────────────────────────
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "OK", MinWidth = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var remove = new Button { Content = "Remove Watermark", MinWidth = 130, Margin = new Thickness(0, 0, 8, 0) };
        remove.Click += (_, _) => { _removeClicked = true; Close(); };
        var cancel = new Button { Content = "Cancel", MinWidth = 72, IsCancel = true };
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
        var dlg = new OpenFileDialog
        {
            Title = "Select a watermark image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            _pendingImageBytes = File.ReadAllBytes(dlg.FileName);
            _pathBox.Text = $"{Path.GetFileName(dlg.FileName)} ({_pendingImageBytes.Length / 1024} KB)";
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
        var text = _textBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            DialogMessageHelper.ShowWarning(this, "Enter watermark text, or click 'Remove Watermark' to clear.", Title);
            _textBox.Focus();
            return;
        }

        var font = _fontBox.Text.Trim();
        if (string.IsNullOrEmpty(font))
            font = "Calibri";

        var color = _colorBox.Text.Trim();
        if (!color.StartsWith('#'))
            color = "#" + color;
        try { ColorConverter.ConvertFromString(color); }
        catch
        {
            DialogMessageHelper.ShowWarning(this, "Enter a valid colour hex value (e.g. #808080).", Title);
            _colorBox.Focus();
            return;
        }

        var layout  = _horizontalRadio.IsChecked == true ? WatermarkLayout.Horizontal : WatermarkLayout.Diagonal;
        var opacity = _semitransparentCheck.IsChecked == true ? 0.3 : 1.0;

        _result = new WatermarkOptions(text)
        {
            FontFamily   = font,
            FontColorHex = color,
            Layout       = layout,
            Opacity      = opacity,
        };
        _accepted = true;
        Close();
    }

    private void AcceptPicture()
    {
        if (_pendingImageBytes is not { Length: > 0 })
        {
            DialogMessageHelper.ShowWarning(this, "Select an image file for the picture watermark.", Title);
            return;
        }

        var scaleText = _scaleBox.Text.Trim();
        if (!int.TryParse(scaleText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.CurrentCulture, out var scale)
            || scale < 0 || scale > 500)
        {
            DialogMessageHelper.ShowWarning(this, "Scale must be 0 (Auto) or 1–500.", Title);
            _scaleBox.Focus();
            return;
        }

        var layout  = _picHorizontalRadio.IsChecked == true ? WatermarkLayout.Horizontal : WatermarkLayout.Diagonal;
        var opacity = _washoutCheck.IsChecked == true ? 0.3 : 1.0;

        // Use a placeholder text so the text fields round-trip without confusion.
        _result = new WatermarkOptions(string.Empty)
        {
            FontFamily   = "Calibri",
            FontColorHex = "#808080",
            Layout       = layout,
            Opacity      = opacity,
            ImageBytes   = _pendingImageBytes,
            ScalePct     = scale,
        };
        _accepted = true;
        Close();
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
