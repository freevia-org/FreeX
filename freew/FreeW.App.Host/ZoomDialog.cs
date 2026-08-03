using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Zoom" dialog (View &gt; Zoom). Offers the fixed magnification presets (200 / 100 / 75 %), the
/// page-relative fit options (Page width / Text width / Whole page) and a custom percentage box, and
/// returns the chosen zoom factor (1.0 == 100%) to drive <c>DocumentView.ZoomLevel</c>, or null if cancelled.
///
/// <para>
/// The dialog stays WPF-only chrome: fit factors are handed in by the host, while preset selection,
/// custom percentage parsing, validation, and result resolution live in the shared presentation planner.
/// </para>
/// </summary>
internal sealed class ZoomDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly RadioButton _customButton =
        new() { Content = "Percent:", GroupName = "Zoom", VerticalAlignment = VerticalAlignment.Center };
    private readonly RadioButton _pageWidthButton = new() { Content = "Page width", GroupName = "Zoom" };
    private readonly RadioButton _textWidthButton = new() { Content = "Text width", GroupName = "Zoom" };
    private readonly RadioButton _wholePageButton = new() { Content = "Whole page", GroupName = "Zoom" };
    private readonly TextBox _percentBox = new() { Width = 64 };
    private readonly List<(RadioButton Button, int Percent)> _presetButtons = [];

    private readonly ZoomDialogFitFactors _fitFactors;
    private double? _result;
    private static readonly DialogFocusPlan FocusPlan = FreeWDialogFocusPlanner.Zoom;

    private ZoomDialog(Window? owner, double currentFactor, double pageWidthFactor, double textWidthFactor, double wholePageFactor)
    {
        Owner = owner;
        Title = "Zoom";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _fitFactors = new ZoomDialogFitFactors(pageWidthFactor, textWidthFactor, wholePageFactor);

        var plan = ZoomDialogPlanner.Build(currentFactor);
        _percentBox.Text = plan.CustomPercentText;
        AutomationProperties.SetName(_percentBox, "Custom zoom percent");
        AutomationProperties.SetAutomationId(_percentBox, FocusPlan.InitialFocusTargetAutomationId);
        _percentBox.GotKeyboardFocus += (_, _) => _customButton.IsChecked = true;

        Content = BuildContent(plan);
        Loaded += (_, _) => FocusPercent();
    }

    // The radio column: the fixed presets, then the page-relative fits, then the custom % row. The button
    // matching the current zoom (a preset if one matches exactly, else Percent:) starts checked.
    private UIElement BuildContent(ZoomDialogPlan plan)
    {
        var group = new GroupBox
        {
            Header = "Zoom to",
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var stack = new StackPanel();
        foreach (var preset in plan.Presets)
        {
            var button = new RadioButton
            {
                Content = $"{preset.Percent}%",
                GroupName = "Zoom",
                IsChecked = preset.IsSelected,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _presetButtons.Add((button, preset.Percent));
            stack.Children.Add(button);
        }

        _pageWidthButton.Margin = new Thickness(0, 0, 0, 4);
        _textWidthButton.Margin = new Thickness(0, 0, 0, 4);
        _wholePageButton.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(_pageWidthButton);
        stack.Children.Add(_textWidthButton);
        stack.Children.Add(_wholePageButton);

        var customRow = new StackPanel { Orientation = Orientation.Horizontal };
        _customButton.IsChecked = plan.InitialChoice == ZoomDialogInitialChoice.Custom;
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
        var request = new ZoomDialogSelectionRequest(
            GetSelectedFitOption(),
            GetSelectedPresetPercent(),
            _percentBox.Text);
        if (!ZoomDialogPlanner.TryCreateResult(request, _fitFactors, out var result, out var error))
        {
            DialogMessageHelper.ShowWarning(this, ResolveValidationError(error), Title);
            _customButton.IsChecked = true;
            FocusPercent();
            return;
        }

        _result = result;
        Close();
    }

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

    private static string ResolveValidationError(ZoomDialogValidationError? error) =>
        error switch
        {
            ZoomDialogValidationError.WholePercentRequired => "Enter a whole zoom percentage.",
            _ => "Enter a whole zoom percentage."
        };

    private void FocusPercent()
    {
        if (FocusPlan.SelectAllOnFocus)
            DialogFocus.FocusAndSelect(_percentBox);
        else
            DialogFocus.Focus(_percentBox);
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
