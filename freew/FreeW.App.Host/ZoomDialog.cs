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
/// custom percentage interaction, validation, focus recovery, and result resolution live in the shared
/// presentation session.
/// </para>
/// </summary>
internal sealed class ZoomDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private static readonly ZoomDialogTextSpec Text = ZoomDialogPlanner.Text;
    private readonly RadioButton _customButton =
        new() { Content = Text.PercentLabel, GroupName = "Zoom", VerticalAlignment = VerticalAlignment.Center };
    private readonly RadioButton _pageWidthButton = new() { Content = Text.PageWidthLabel, GroupName = "Zoom" };
    private readonly RadioButton _textWidthButton = new() { Content = Text.TextWidthLabel, GroupName = "Zoom" };
    private readonly RadioButton _wholePageButton = new() { Content = Text.WholePageLabel, GroupName = "Zoom" };
    private readonly TextBox _percentBox = new() { Width = 64 };

    private readonly ZoomDialogFitFactors _fitFactors;
    private readonly ZoomDialogSession _session;
    private double? _result;
    private static readonly Free.Shared.Shell.DialogFocusPlan<string> FocusPlan = FreeWDialogFocusPlanner.Zoom;

    private ZoomDialog(Window? owner, double currentFactor, double pageWidthFactor, double textWidthFactor, double wholePageFactor)
    {
        Owner = owner;
        Title = Text.Title;
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _fitFactors = new ZoomDialogFitFactors(pageWidthFactor, textWidthFactor, wholePageFactor);
        _session = new ZoomDialogSession(currentFactor);

        var plan = _session.InitialPlan;
        _percentBox.Text = plan.CustomPercentText;
        AutomationProperties.SetName(_percentBox, Text.CustomPercentAutomationName);
        AutomationProperties.SetAutomationId(_percentBox, FocusPlan.InitialFocusTarget);
        _percentBox.GotKeyboardFocus += (_, _) => SelectCustom();
        _percentBox.TextChanged += (_, _) => _session.UpdateCustomPercentText(_percentBox.Text);

        _pageWidthButton.Checked += (_, _) => _session.SelectFit(ZoomDialogFitOption.PageWidth);
        _textWidthButton.Checked += (_, _) => _session.SelectFit(ZoomDialogFitOption.TextWidth);
        _wholePageButton.Checked += (_, _) => _session.SelectFit(ZoomDialogFitOption.WholePage);
        _customButton.Checked += (_, _) => _session.SelectCustom();

        Content = BuildContent(plan);
        Loaded += (_, _) => FocusPercent();
    }

    // The radio column: the fixed presets, then the page-relative fits, then the custom % row. The button
    // matching the current zoom (a preset if one matches exactly, else Percent:) starts checked.
    private UIElement BuildContent(ZoomDialogPlan plan)
    {
        var group = new GroupBox
        {
            Header = Text.GroupLabel,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var stack = new StackPanel();
        foreach (var preset in plan.Presets)
        {
            var button = new RadioButton
            {
                Content = ZoomDialogPlanner.FormatPresetLabel(preset.Percent),
                GroupName = "Zoom",
                IsChecked = preset.IsSelected,
                Margin = new Thickness(0, 0, 0, 4)
            };
            button.Checked += (_, _) => _session.SelectPreset(preset.Percent);
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
        customRow.Children.Add(new TextBlock { Text = Text.PercentSuffix, VerticalAlignment = VerticalAlignment.Center });
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
        var acceptance = _session.PlanAcceptance(_fitFactors);
        if (!acceptance.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, acceptance.Validation!.Message, Title);
            ApplyControlState(acceptance.ControlState);
            Focus(acceptance.Validation.FocusTarget);
            return;
        }

        _result = acceptance.Result;
        Close();
    }

    private void SelectCustom()
    {
        _session.SelectCustom();
        _customButton.IsChecked = true;
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
