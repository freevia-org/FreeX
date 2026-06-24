using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Custom Watermark" dialog (Design &gt; Page Background &gt; Watermark &gt; Custom Watermark).
/// Lets the user set or clear a page watermark with full options: text, font family, font colour,
/// diagonal vs horizontal layout, and semitransparent vs opaque (opacity). Returns the configured
/// <see cref="WatermarkOptions"/> on OK, null on cancel, or a sentinel <see cref="Removed"/> value
/// when the user clicks "Remove Watermark".
///
/// <para>
/// The resulting value is applied by the ribbon command through
/// <see cref="FreeW.App.Host.Editing.DocumentView.SetWatermarkOptions"/> which commits the change to
/// the model, triggers a re-render (so the watermark shows immediately), and marks the document dirty
/// (so the new options persist on save via docProps/custom.xml).
/// </para>
/// </summary>
internal sealed class WatermarkOptionsDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    /// <summary>
    /// Sentinel: the user clicked "No watermark" / "Remove Watermark" — the caller should clear any
    /// existing watermark.
    /// </summary>
    public static readonly WatermarkOptions? Removed = null; // null signals removal

    private readonly TextBox _textBox;
    private readonly TextBox _fontBox;
    private readonly TextBox _colorBox;
    private readonly RadioButton _diagonalRadio;
    private readonly RadioButton _horizontalRadio;
    private readonly CheckBox _semitransparentCheck;

    private WatermarkOptions? _result; // null == remove/cancelled (distinguished by _accepted)
    private bool _accepted;
    private bool _removeClicked;

    private WatermarkOptionsDialog(Window? owner, WatermarkOptions? current)
    {
        Owner = owner;
        Title = "Printed Watermark";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        // Seed fields from current options (or defaults).
        var seed = current ?? new WatermarkOptions("DRAFT");
        _textBox = new TextBox { Text = current?.Text ?? "DRAFT", MinWidth = 200 };
        _fontBox = new TextBox { Text = seed.FontFamily, MinWidth = 150 };
        _colorBox = new TextBox { Text = seed.FontColorHex, MinWidth = 80 };
        _diagonalRadio = new RadioButton { Content = "Diagonal", IsChecked = seed.Layout == WatermarkLayout.Diagonal, GroupName = "WmLayout" };
        _horizontalRadio = new RadioButton { Content = "Horizontal", IsChecked = seed.Layout == WatermarkLayout.Horizontal, GroupName = "WmLayout" };
        _semitransparentCheck = new CheckBox { Content = "Semitransparent", IsChecked = seed.Opacity < 1.0 };

        Content = BuildContent();
        Loaded += (_, _) => _textBox.Focus();
        _textBox.SelectAll();
    }

    private UIElement BuildContent()
    {
        static Label Lbl(string text) => new() { Content = text, Margin = new Thickness(0, 4, 8, 0), VerticalAlignment = VerticalAlignment.Center };

        UIElement Row(string label, UIElement control)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(Lbl(label), 0);
            Grid.SetColumn(control, 1);
            grid.Children.Add(Lbl(label));
            grid.Children.Add(control);
            return grid;
        }

        // Layout row: two radio buttons in a horizontal stack.
        var layoutPanel = new StackPanel { Orientation = Orientation.Horizontal };
        _diagonalRadio.Margin = new Thickness(0, 0, 16, 0);
        layoutPanel.Children.Add(_diagonalRadio);
        layoutPanel.Children.Add(_horizontalRadio);

        // Color: text box (hex). A colour picker would be ideal but plain hex matches Word's field.
        var panel = new StackPanel { Margin = new Thickness(14) };

        panel.Children.Add(new TextBlock
        {
            Text = "Text watermark",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        panel.Children.Add(Row("Text:", _textBox));
        panel.Children.Add(Row("Font:", _fontBox));
        panel.Children.Add(Row("Color (hex):", _colorBox));
        panel.Children.Add(Row("Layout:", layoutPanel));
        panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 4) });
        _semitransparentCheck.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(_semitransparentCheck);

        // Action buttons: OK / Remove Watermark / Cancel
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "OK", MinWidth = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();

        var remove = new Button { Content = "Remove Watermark", MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        remove.Click += (_, _) => { _removeClicked = true; Close(); };

        var cancel = new Button { Content = "Cancel", MinWidth = 72, IsCancel = true };
        buttonRow.Children.Add(ok);
        buttonRow.Children.Add(remove);
        buttonRow.Children.Add(cancel);
        panel.Children.Add(buttonRow);

        return panel;
    }

    private void Accept()
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
        // Validate the hex: attempt a conversion to confirm it is parseable.
        try { ColorConverter.ConvertFromString(color); }
        catch
        {
            DialogMessageHelper.ShowWarning(this, "Enter a valid colour hex value (e.g. #808080).", Title);
            _colorBox.Focus();
            return;
        }

        var layout = _horizontalRadio.IsChecked == true ? WatermarkLayout.Horizontal : WatermarkLayout.Diagonal;
        var opacity = _semitransparentCheck.IsChecked == true ? 0.3 : 1.0;

        _result = new WatermarkOptions(text)
        {
            FontFamily = font,
            FontColorHex = color,
            Layout = layout,
            Opacity = opacity
        };
        _accepted = true;
        Close();
    }

    /// <summary>
    /// Show the Custom Watermark dialog seeded with <paramref name="current"/> options. Returns:
    /// <list type="bullet">
    ///   <item>A <see cref="WatermarkOptions"/> instance when the user clicked OK.</item>
    ///   <item>
    ///     <c>null</c> with <paramref name="removeRequested"/> = true when the user clicked
    ///     "Remove Watermark".
    ///   </item>
    ///   <item>
    ///     <c>null</c> with <paramref name="removeRequested"/> = false when the user cancelled (no change).
    ///   </item>
    /// </list>
    /// </summary>
    public static WatermarkOptions? Prompt(Window? owner, WatermarkOptions? current, out bool removeRequested)
    {
        var dialog = new WatermarkOptionsDialog(owner, current);
        dialog.ShowDialog();
        removeRequested = dialog._removeClicked;
        return dialog._accepted ? dialog._result : (dialog._removeClicked ? null : null);
        // Note: _accepted and _removeClicked are mutually exclusive; cancelled = neither.
    }
}
