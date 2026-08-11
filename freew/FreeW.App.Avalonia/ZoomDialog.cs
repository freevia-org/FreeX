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
/// validation, focus recovery, and result resolution live in <see cref="ZoomDialogSession"/> so it
/// matches the WPF dialog policy.
/// </para>
/// </summary>
internal sealed class ZoomDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;
    private static readonly ZoomDialogTextSpec Text = ZoomDialogPlanner.Text;

    private readonly RadioButton _pageWidthButton = Preset(Text.PageWidthLabel);
    private readonly RadioButton _textWidthButton = Preset(Text.TextWidthLabel);
    private readonly RadioButton _wholePageButton = Preset(Text.WholePageLabel);
    private readonly RadioButton _customButton = Preset(Text.PercentLabel);
    private readonly TextBox _percentBox = new()
    {
        Width = 64,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _status = new();
    private readonly ZoomDialogFitFactors _fitFactors;
    private readonly ZoomDialogSession _session;
    private static readonly Free.Shared.Shell.DialogFocusPlan<string> FocusPlan = FreeWDialogFocusPlanner.Zoom;

    /// <summary>The scale the user accepted (1.0 == 100%), or <c>null</c> if cancelled.</summary>
    public double? Result { get; private set; }

    public ZoomDialog(double currentScale, ZoomDialogFitFactors fitFactors)
    {
        ArgumentNullException.ThrowIfNull(fitFactors);

        Title = Text.Title;
        Width = 280;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        AvaloniaCompactDialogChrome.ApplyTextBox(_percentBox, DialogChromeStyle);
        AutomationProperties.SetAutomationId(_percentBox, FocusPlan.InitialFocusTarget);
        AvaloniaCompactDialogChrome.ApplyValidationStatus(_status, DialogChromeStyle, new Thickness(16, 8, 16, 0));

        _fitFactors = fitFactors;
        _session = new ZoomDialogSession(currentScale);
        var plan = _session.InitialPlan;
        _percentBox.Text = plan.CustomPercentText;
        _percentBox.GotFocus += (_, _) => SelectCustom();
        _percentBox.TextChanged += (_, _) =>
        {
            _session.UpdateCustomPercentText(_percentBox.Text);
            _customButton.IsChecked = true;
        };

        _pageWidthButton.IsCheckedChanged += (_, _) => SelectFitWhenChecked(_pageWidthButton, ZoomDialogFitOption.PageWidth);
        _textWidthButton.IsCheckedChanged += (_, _) => SelectFitWhenChecked(_textWidthButton, ZoomDialogFitOption.TextWidth);
        _wholePageButton.IsCheckedChanged += (_, _) => SelectFitWhenChecked(_wholePageButton, ZoomDialogFitOption.WholePage);
        _customButton.IsCheckedChanged += (_, _) =>
        {
            if (_customButton.IsChecked == true)
                _session.SelectCustom();
        };

        var customRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _customButton,
                _percentBox,
                new TextBlock
                {
                    Text = Text.PercentSuffix,
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
            var button = Preset(ZoomDialogPlanner.FormatPresetLabel(preset.Percent));
            button.IsChecked = preset.IsSelected;
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true)
                    _session.SelectPreset(preset.Percent);
            };
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
        TryResolveScale(_session.PlanAcceptance(_fitFactors), out scale, out error);

    private void Accept()
    {
        _status.IsVisible = false;
        var acceptance = _session.PlanAcceptance(_fitFactors);
        if (!acceptance.IsAccepted)
        {
            _status.Text = acceptance.Validation!.Message;
            _status.IsVisible = true;
            ApplyControlState(acceptance.ControlState);
            Focus(acceptance.Validation.FocusTarget);
            return;
        }

        Result = acceptance.Result;
        Close();
    }

    private static bool TryResolveScale(
        ZoomDialogAcceptance acceptance,
        out double scale,
        out ZoomDialogValidationError? error)
    {
        scale = acceptance.Result ?? default;
        error = acceptance.Validation?.Error;
        return acceptance.IsAccepted;
    }

    private void SelectCustom()
    {
        _session.SelectCustom();
        _customButton.IsChecked = true;
    }

    private void SelectFitWhenChecked(RadioButton button, ZoomDialogFitOption fitOption)
    {
        if (button.IsChecked == true)
            _session.SelectFit(fitOption);
    }

    private void ApplyControlState(ZoomDialogControlState state)
    {
        if (state.IsCustomSelected)
            _customButton.IsChecked = true;
    }

    private void Focus(ZoomDialogFocusTarget target)
    {
        if (target == ZoomDialogFocusTarget.CustomPercent)
            FocusPercent();
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
