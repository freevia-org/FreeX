using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace FreeW.App.Avalonia;

/// <summary>
/// AV-VIEW: Zoom dialog (modal). Mirrors Word's View → Zoom dialog: a set of preset radio buttons
/// (200% / 100% / 75% / Page width / Text width / Whole page) plus a custom-percent box. The chosen
/// zoom is returned to the caller as a scale factor (1.0 == 100%) so <c>MainWindow.ApplyZoom</c> can
/// apply it through the same path as the quick zoom-in / zoom-out / 100% commands.
///
/// <para>
/// "Page width", "Text width" and "Whole page" map to representative fixed scales here (the shell does
/// not yet expose viewport metrics to compute exact fit factors); the custom box and the numeric
/// presets are exact. The dialog returns <c>null</c> on Cancel so the current zoom is left untouched.
/// </para>
/// </summary>
internal sealed class ZoomDialog : Window
{
    // Representative "fit" scales for the page-relative presets. These mirror typical Word values for
    // a Letter page in a ~1000px workspace; an exact fit-to-viewport computation is deferred.
    internal const double PageWidthScale = 1.25;
    internal const double TextWidthScale = 1.5;
    internal const double WholePageScale = 0.6;

    private readonly RadioButton _r200      = Preset("200%");
    private readonly RadioButton _r100      = Preset("100%");
    private readonly RadioButton _r75       = Preset("75%");
    private readonly RadioButton _rPageWide = Preset("Page width");
    private readonly RadioButton _rTextWide = Preset("Text width");
    private readonly RadioButton _rWhole    = Preset("Whole page");
    private readonly RadioButton _rCustom   = Preset("Percent:");
    private readonly NumericUpDown _percent = new()
    {
        Minimum = 50,
        Maximum = 300,
        Increment = 10,
        Width = 90,
        FormatString = "0",
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>The scale the user accepted (1.0 == 100%), or <c>null</c> if cancelled.</summary>
    public double? Result { get; private set; }

    public ZoomDialog(double currentScale)
    {
        Title = "Zoom";
        Width = 280;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _percent.Value = (decimal)System.Math.Round(currentScale * 100);

        // Select the radio matching the current scale, else fall back to the custom box.
        var pct = (int)System.Math.Round(currentScale * 100);
        switch (pct)
        {
            case 200: _r200.IsChecked = true; break;
            case 100: _r100.IsChecked = true; break;
            case 75:  _r75.IsChecked = true;  break;
            default:  _rCustom.IsChecked = true; break;
        }

        // Editing the custom percent switches selection to the custom radio.
        _percent.ValueChanged += (_, _) => _rCustom.IsChecked = true;

        var customRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _rCustom, _percent },
        };

        var presets = new StackPanel
        {
            Margin = new Thickness(16, 14, 16, 0),
            Spacing = 2,
            Children = { _r200, _r100, _r75, _rPageWide, _rTextWide, _rWhole, customRow },
        };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 72 };
        ok.Click += (_, _) => { Result = ResolveScale(); Close(); };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => { Result = null; Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 12, 16, 14),
            Children = { ok, cancel },
        };

        DockPanel.SetDock(buttons, global::Avalonia.Controls.Dock.Bottom);
        Content = new DockPanel
        {
            LastChildFill = true,
            Children = { buttons, presets },
        };
    }

    /// <summary>
    /// Maps the currently-selected radio (or the custom box) to a scale factor. Exposed via
    /// <see cref="Result"/> after OK; also used directly by tests to verify the mapping without a UI.
    /// </summary>
    internal double ResolveScale()
    {
        if (_r200.IsChecked == true) return 2.0;
        if (_r100.IsChecked == true) return 1.0;
        if (_r75.IsChecked == true)  return 0.75;
        if (_rPageWide.IsChecked == true) return PageWidthScale;
        if (_rTextWide.IsChecked == true) return TextWidthScale;
        if (_rWhole.IsChecked == true)    return WholePageScale;
        // Custom percent (default).
        var pct = (double)(_percent.Value ?? 100m);
        return pct / 100.0;
    }

    private static RadioButton Preset(string label) => new()
    {
        Content = label,
        GroupName = "zoom",
        VerticalAlignment = VerticalAlignment.Center,
    };
}
