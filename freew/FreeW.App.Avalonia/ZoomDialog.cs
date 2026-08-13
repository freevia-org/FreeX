using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Avalonia;

/// <summary>
/// AV-VIEW: Zoom dialog (modal). Mirrors Word's View > Zoom dialog: fixed preset radio buttons,
/// page-relative fit options, and a custom-percent box.
///
/// <para>
/// The dialog stays Avalonia-only chrome: preset/default selection, custom percentage parsing,
/// validation, and result resolution live in <see cref="ZoomDialogPlanner"/> so it matches the WPF
/// dialog policy.
/// </para>
/// </summary>
internal sealed class ZoomDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly RadioButton _pageWidthButton = Preset("Page width");
    private readonly RadioButton _textWidthButton = Preset("Text width");
    private readonly RadioButton _wholePageButton = Preset("Whole page");
    private readonly RadioButton _customButton = Preset("Percent:");
    private readonly TextBox _percentBox = new()
    {
        Width = 64,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _status = new();
    private readonly List<(RadioButton Button, int Percent)> _presetButtons = [];
    private readonly ZoomDialogFitFactors _fitFactors;
    private static readonly DialogFocusPlan FocusPlan = FreeWDialogFocusPlanner.Zoom;

    /// <summary>The scale the user accepted (1.0 == 100%), or <c>null</c> if cancelled.</summary>
    public double? Result { get; private set; }

    public ZoomDialog(double currentScale, ZoomDialogFitFactors fitFactors)
    {
        _fitFactors = fitFactors;
        Title = "Zoom";
        Width = 280;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        AvaloniaCompactDialogChrome.ApplyTextBox(_percentBox, DialogChromeStyle);
        AutomationProperties.SetAutomationId(_percentBox, FocusPlan.InitialFocusTargetAutomationId);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(16, 8, 16, 0));

        var plan = ZoomDialogPlanner.Build(currentScale);
        _percentBox.Text = plan.CustomPercentText;
        _percentBox.GotFocus += (_, _) => _customButton.IsChecked = true;
        _percentBox.TextChanged += (_, _) => _customButton.IsChecked = true;

        var customRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _customButton,
                _percentBox,
                new TextBlock
                {
                    Text = "%",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0),
                },
            },
        };

        var presets = new StackPanel
        {
            Margin = new Thickness(16, 14, 16, 0),
            Spacing = 2,
        };
        foreach (var preset in plan.Presets)
        {
            var button = Preset($"{preset.Percent}%");
            button.IsChecked = preset.IsSelected;
            _presetButtons.Add((button, preset.Percent));
            presets.Children.Add(button);
        }

        presets.Children.Add(_pageWidthButton);
        presets.Children.Add(_textWidthButton);
        presets.Children.Add(_wholePageButton);
        _customButton.IsChecked = plan.InitialChoice == ZoomDialogInitialChoice.Custom;
        presets.Children.Add(customRow);

        var ok = new Button { Content = "OK", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 72, isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => { Result = null; Close(); };

        var buttons = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(16, 12, 16, 14));

        DockPanel.SetDock(buttons, global::Avalonia.Controls.Dock.Bottom);
        Content = new DockPanel
        {
            LastChildFill = true,
            Children = { buttons, new StackPanel { Children = { presets, _status } } },
        };
        Opened += (_, _) => FocusPercent();
    }

    /// <summary>
    /// Maps the currently-selected radio (or the custom box) to a scale factor. Exposed via
    /// <see cref="Result"/> after OK; also used directly by tests to verify the mapping without a UI.
    /// </summary>
    internal double ResolveScale()
    {
        if (TryResolveScale(out var scale, out var error))
            return scale;

        throw new InvalidOperationException(ZoomDialogPlanner.ValidationMessageFor(error));
    }

    internal bool TryResolveScale(out double scale, out ZoomDialogValidationError? error) =>
        ZoomDialogPlanner.TryCreateResult(BuildSelectionRequest(), _fitFactors, out scale, out error);

    private void Accept()
    {
        _status.IsVisible = false;
        if (!TryResolveScale(out var scale, out var error))
        {
            _status.Text = ZoomDialogPlanner.ValidationMessageFor(error);
            _status.IsVisible = true;
            _customButton.IsChecked = true;
            FocusPercent();
            return;
        }

        Result = scale;
        Close();
    }

    private ZoomDialogSelectionRequest BuildSelectionRequest() => new ZoomDialogSelectionRequest(
        GetSelectedFitOption(),
        GetSelectedPresetPercent(),
        _percentBox.Text);

    private ZoomDialogFitOption? GetSelectedFitOption()
    {
        if (_pageWidthButton.IsChecked == true)
            return ZoomDialogFitOption.PageWidth;
        if (_textWidthButton.IsChecked == true)
            return ZoomDialogFitOption.TextWidth;
        if (_wholePageButton.IsChecked == true)
            return ZoomDialogFitOption.WholePage;

        return null;
    }

    private int? GetSelectedPresetPercent()
    {
        foreach (var (button, percent) in _presetButtons)
        {
            if (button.IsChecked == true)
                return percent;
        }

        return null;
    }

    private static RadioButton Preset(string label)
    {
        var button = new RadioButton
        {
            Content = label,
            GroupName = "zoom",
            VerticalAlignment = VerticalAlignment.Center,
        };
        AvaloniaCompactDialogChrome.ApplyRadioButton(button, DialogChromeStyle);
        return button;
    }

    private void FocusPercent()
    {
        if (FocusPlan.SelectAllOnFocus)
            AvaloniaCompactDialogChrome.FocusAndSelect(_percentBox);
        else
            _percentBox.Focus();
    }
}
