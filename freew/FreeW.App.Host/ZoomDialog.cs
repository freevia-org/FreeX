using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Zoom" dialog (View &gt; Zoom). Offers the fixed magnification presets (200 / 100 / 75 %), the
/// page-relative fit options (Page width / Text width / Whole page) and a custom percentage box, and
/// returns the chosen zoom factor (1.0 == 100%) to drive <c>DocumentView.ZoomLevel</c>, or null if cancelled.
///
/// <para>
/// The dialog stays WPF-only chrome: the fit factors are pre-computed by the host from the live page
/// geometry + viewport via the pure <see cref="ZoomFit"/> helper and handed in, so the dialog itself just
/// picks one. Custom percentages are parsed and clamped to the supported zoom range
/// (<see cref="ZoomLevels.Min"/>..<see cref="ZoomLevels.Max"/>) — the same range as the status-bar slider.
/// </para>
/// </summary>
internal sealed class ZoomDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    // The fixed magnification presets Word offers in the Zoom dialog, as whole percentages.
    private static readonly int[] Presets = [200, 100, 75];

    private readonly RadioButton _customButton =
        new() { Content = "Percent:", GroupName = "Zoom", VerticalAlignment = VerticalAlignment.Center };
    private readonly RadioButton _pageWidthButton = new() { Content = "Page width", GroupName = "Zoom" };
    private readonly RadioButton _textWidthButton = new() { Content = "Text width", GroupName = "Zoom" };
    private readonly RadioButton _wholePageButton = new() { Content = "Whole page", GroupName = "Zoom" };
    private readonly TextBox _percentBox = new() { Width = 64 };
    private readonly List<(RadioButton Button, int Percent)> _presetButtons = [];

    private readonly double _pageWidthFactor;
    private readonly double _textWidthFactor;
    private readonly double _wholePageFactor;
    private double? _result;

    private ZoomDialog(Window? owner, double currentFactor, double pageWidthFactor, double textWidthFactor, double wholePageFactor)
    {
        Owner = owner;
        Title = "Zoom";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _pageWidthFactor = pageWidthFactor;
        _textWidthFactor = textWidthFactor;
        _wholePageFactor = wholePageFactor;

        var currentPercent = ZoomLevels.ToPercent(currentFactor);
        _percentBox.Text = currentPercent.ToString(CultureInfo.CurrentCulture);
        AutomationProperties.SetName(_percentBox, "Custom zoom percent");
        AutomationProperties.SetAutomationId(_percentBox, "ZoomCustomPercentBox");
        _percentBox.GotKeyboardFocus += (_, _) => _customButton.IsChecked = true;

        Content = BuildContent(currentPercent);
        DialogFocus.FocusAndSelect(_percentBox);
    }

    // The radio column: the fixed presets, then the page-relative fits, then the custom % row. The button
    // matching the current zoom (a preset if one matches exactly, else Percent:) starts checked.
    private UIElement BuildContent(int currentPercent)
    {
        var group = new GroupBox
        {
            Header = "Zoom to",
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var stack = new StackPanel();
        var matchedPreset = false;
        foreach (var percent in Presets)
        {
            var button = new RadioButton
            {
                Content = $"{percent}%",
                GroupName = "Zoom",
                IsChecked = percent == currentPercent,
                Margin = new Thickness(0, 0, 0, 4)
            };
            matchedPreset |= percent == currentPercent;
            _presetButtons.Add((button, percent));
            stack.Children.Add(button);
        }

        _pageWidthButton.Margin = new Thickness(0, 0, 0, 4);
        _textWidthButton.Margin = new Thickness(0, 0, 0, 4);
        _wholePageButton.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(_pageWidthButton);
        stack.Children.Add(_textWidthButton);
        stack.Children.Add(_wholePageButton);

        var customRow = new StackPanel { Orientation = Orientation.Horizontal };
        _customButton.IsChecked = !matchedPreset;
        customRow.Children.Add(_customButton);
        _percentBox.Margin = new Thickness(6, 0, 4, 0);
        customRow.Children.Add(_percentBox);
        customRow.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(customRow);
        group.Content = stack;

        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(group);
        // Reuse the shared OK/Cancel button row (accelerators, automation names, shell strings; Cancel is
        // IsCancel so Esc/Cancel closes). Single source of truth shared with FreeX/FreeW dialogs.
        root.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72));
        return root;
    }

    private void Accept()
    {
        if (_pageWidthButton.IsChecked == true) { _result = _pageWidthFactor; Close(); return; }
        if (_textWidthButton.IsChecked == true) { _result = _textWidthFactor; Close(); return; }
        if (_wholePageButton.IsChecked == true) { _result = _wholePageFactor; Close(); return; }

        foreach (var (button, percent) in _presetButtons)
        {
            if (button.IsChecked == true) { _result = ZoomLevels.FromPercent(percent); Close(); return; }
        }

        // Custom percent: parse a whole percentage and clamp to the supported range.
        if (!int.TryParse(_percentBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var custom))
        {
            DialogMessageHelper.ShowWarning(this, "Enter a whole zoom percentage.", Title);
            DialogFocus.FocusAndSelect(_percentBox);
            return;
        }

        _result = ZoomLevels.FromPercent(custom);
        Close();
    }

    /// <summary>
    /// Show the Zoom dialog seeded with the current zoom and the host-computed fit factors (Page width /
    /// Text width / Whole page). Returns the chosen zoom factor (1.0 == 100%), or null if cancelled.
    /// </summary>
    public static double? Prompt(
        Window? owner,
        double currentFactor,
        double pageWidthFactor,
        double textWidthFactor,
        double wholePageFactor)
    {
        var dialog = new ZoomDialog(owner, currentFactor, pageWidthFactor, textWidthFactor, wholePageFactor);
        dialog.ShowDialog();
        return dialog._result;
    }
}
