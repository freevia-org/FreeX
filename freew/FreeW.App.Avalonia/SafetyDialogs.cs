using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

internal sealed class RestrictEditingDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle with
        {
            CompactRadioButtonHeight = RestrictEditingDialogPlanner.Presentation.RadioButtonHeight,
            TextBoxHeight = RestrictEditingDialogPlanner.Presentation.TextBoxHeight,
        };
    private readonly ProtectionSettings _currentProtection;
    private readonly RestrictEditingDialogPlan _plan;
    private readonly RadioButton[] _radios;
    private readonly TextBox _passwordBox = CreatePasswordBox();
    private readonly TextBox _confirmBox = CreatePasswordBox();
    private readonly TextBlock _validation = new();
    private readonly Func<Window, string, string, Task<string?>> _askPassword;

    public ProtectionSettings? Result { get; private set; }

    public RestrictEditingDialog(ProtectionSettings current)
        : this(current, PasswordPromptDialog.ShowAsync)
    {
    }

    internal RestrictEditingDialog(
        ProtectionSettings current,
        Func<Window, string, string, Task<string?>> askPassword)
    {
        _currentProtection = current;
        _plan = RestrictEditingDialogPlanner.BuildPlan(current);
        _askPassword = askPassword ?? throw new ArgumentNullException(nameof(askPassword));
        var presentation = RestrictEditingDialogPlanner.Presentation;

        Title = RestrictEditingDialogPlanner.Title;
        Width = presentation.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var body = new StackPanel
        {
            Margin = new Thickness(presentation.ContentMargin),
        };

        body.Children.Add(new TextBlock
        {
            Text = RestrictEditingDialogPlanner.RestrictionPrompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, presentation.PromptBottomMargin),
        });

        _radios = new RadioButton[RestrictEditingDialogPlanner.ModeOptions.Count];
        for (var i = 0; i < RestrictEditingDialogPlanner.ModeOptions.Count; i++)
        {
            var option = RestrictEditingDialogPlanner.ModeOptions[i];
            var radio = new RadioButton
            {
                Content = option.Label,
                GroupName = "RestrictEditingMode",
                IsChecked = i == _plan.SelectedModeIndex,
                IsEnabled = _plan.CanStartProtection,
                Margin = new Thickness(0, presentation.ModeOptionVerticalMargin, 0, presentation.ModeOptionVerticalMargin),
            };
            AvaloniaCompactDialogChrome.ApplyCompactRadioButton(radio, DialogChromeStyle);
            _radios[i] = radio;
            body.Children.Add(radio);
        }

        if (presentation.ShowStatusText)
            body.Children.Add(new TextBlock { Text = _plan.StatusText });

        if (_plan.ShowStartPasswordFields)
        {
            body.Children.Add(new Separator
            {
                Margin = new Thickness(0, presentation.PasswordSeparatorTopMargin, 0, presentation.PasswordSeparatorBottomMargin)
            });
            body.Children.Add(new TextBlock
            {
                Text = RestrictEditingDialogPlanner.OptionalPasswordPrompt,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, presentation.PasswordPromptBottomMargin),
            });
            AddPasswordField(body, RestrictEditingDialogPlanner.PasswordLabel, _passwordBox);
            AddPasswordField(body, RestrictEditingDialogPlanner.ConfirmLabel, _confirmBox);
        }

        AvaloniaCompactDialogChrome.ApplyValidationStatus(_validation, DialogChromeStyle, new Thickness(0, 4, 0, 0));
        body.Children.Add(_validation);

        var start = new Button
        {
            Content = RestrictEditingDialogPlanner.StartButtonText,
            IsDefault = presentation.DefaultButtonText == RestrictEditingDialogPlanner.StartButtonText,
            IsEnabled = _plan.CanStartProtection,
            Margin = new Thickness(0, presentation.StartActionTopMargin, 0, presentation.ActionButtonBottomMargin),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AvaloniaCompactDialogChrome.ApplyButton(
            start,
            DialogChromeStyle,
            minWidth: 200,
            isDefault: presentation.DefaultButtonText == RestrictEditingDialogPlanner.StartButtonText);
        start.Click += (_, _) => StartProtection();

        var stop = new Button
        {
            Content = RestrictEditingDialogPlanner.StopButtonText,
            IsDefault = presentation.DefaultButtonText == RestrictEditingDialogPlanner.StopButtonText,
            IsEnabled = _plan.CanStopProtection,
            Margin = new Thickness(0, 0, 0, presentation.ActionButtonBottomMargin),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AvaloniaCompactDialogChrome.ApplyButton(
            stop,
            DialogChromeStyle,
            minWidth: 180,
            isDefault: presentation.DefaultButtonText == RestrictEditingDialogPlanner.StopButtonText);
        stop.Click += async (_, _) => await StopProtectionAsync();

        var cancel = new Button { Content = RestrictEditingDialogPlanner.CancelButtonText, IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => Close();

        cancel.Margin = new Thickness(0, presentation.CancelActionTopMargin, 0, 0);
        cancel.HorizontalAlignment = HorizontalAlignment.Right;
        body.Children.Add(start);
        body.Children.Add(stop);
        body.Children.Add(cancel);
        Content = body;
        Opened += (_, _) =>
        {
            // FreeWDialogWindow reapplies the default compact chrome to descendants on Opened;
            // restore the WPF authority input height after that host-wide pass.
            AvaloniaCompactDialogChrome.ApplyTextBox(_passwordBox, DialogChromeStyle);
            AvaloniaCompactDialogChrome.ApplyTextBox(_confirmBox, DialogChromeStyle);
            _radios[0].Focus();
        };
    }

    private void StartProtection()
    {
        if (!RestrictEditingDialogPlanner.TryCreateStartSettings(
            SelectedMode(),
            _passwordBox.Text,
            _confirmBox.Text,
            out var settings,
            out var validationMessage))
        {
            ShowValidation(validationMessage);
            _passwordBox.Focus();
            return;
        }

        Result = settings;
        Close();
    }

    private async Task StopProtectionAsync()
    {
        string? password = null;
        if (_currentProtection.HasPassword)
        {
            password = await _askPassword(
                this,
                "Stop Protection",
                RestrictEditingDialogPlanner.StopPasswordPrompt);
            if (password is null)
                return;
        }

        if (!RestrictEditingDialogPlanner.TryCreateStopSettings(
            _currentProtection,
            password,
            out var settings,
            out var validationMessage))
        {
            ShowValidation(validationMessage);
            return;
        }

        Result = settings;
        Close();
    }

    internal Task StopProtectionForTestAsync() => StopProtectionAsync();

    private ProtectionMode SelectedMode()
    {
        for (var i = 0; i < _radios.Length; i++)
        {
            if (_radios[i].IsChecked == true)
                return RestrictEditingDialogPlanner.ModeOptions[i].Mode;
        }

        return ProtectionMode.ReadOnly;
    }

    private void ShowValidation(string? message)
    {
        _validation.Text = message;
        _validation.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private static TextBox CreatePasswordBox()
    {
        var box = new TextBox
        {
            MinWidth = 180,
            PasswordChar = '*',
        };
        AvaloniaCompactDialogChrome.ApplyTextBox(box, DialogChromeStyle);
        return box;
    }

    private static void AddPasswordField(Panel body, string label, TextBox box)
    {
        body.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 2, 0, 0),
        });
        body.Children.Add(box);
    }
}

internal sealed class DocumentInspectorDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;
    private readonly CheckBox _comments;
    private readonly CheckBox _revisions;
    private readonly CheckBox _properties;
    private readonly CheckBox _bookmarks;

    public InspectorRemovalChoice? Choice { get; private set; }

    public DocumentInspectorDialog(InspectionResult result)
    {
        Title = "Document Inspector";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var body = new StackPanel
        {
            Margin = new Thickness(16, 14, 16, 0),
            Spacing = 6,
        };

        if (result.IsClean)
        {
            body.Children.Add(new TextBlock
            {
                Text = "No comments, revisions, document properties, or bookmarks were found.",
                TextWrapping = TextWrapping.Wrap,
            });
        }

        _comments = AddCheck(body, "Comments", result.Comments);
        _revisions = AddCheck(body, "Revisions", result.Revisions);
        _properties = AddCheck(body, "Document properties", result.NonEmptyProperties);
        _bookmarks = AddCheck(body, "Bookmarks", result.Bookmarks);

        var remove = new Button { Content = result.IsClean ? "OK" : "Remove", IsDefault = true };
        AvaloniaCompactDialogChrome.ApplyButton(remove, DialogChromeStyle, minWidth: 72, isDefault: true);
        remove.Click += (_, _) =>
        {
            Choice = result.IsClean
                ? new InspectorRemovalChoice(false, false, false, false)
                : new InspectorRemovalChoice(
                    _comments.IsChecked == true,
                    _revisions.IsChecked == true,
                    _properties.IsChecked == true,
                    _bookmarks.IsChecked == true);
            Close();
        };

        IReadOnlyList<Control> controls = result.IsClean
            ? [remove]
            : [remove, CreateCancelButton()];
        var buttons = AvaloniaCompactDialogChrome.CreateActionRow(controls, new Thickness(16, 12, 16, 14));
        DockPanel.SetDock(buttons, Dock.Bottom);

        Content = new DockPanel { LastChildFill = true, Children = { buttons, body } };
    }

    private static CheckBox AddCheck(Panel body, string label, int count)
    {
        var box = new CheckBox
        {
            Content = $"{label}: {count}",
            IsChecked = count > 0,
            IsEnabled = count > 0,
        };
        AvaloniaCompactDialogChrome.ApplyCheckBox(box, DialogChromeStyle);
        body.Children.Add(box);
        return box;
    }

    private Button CreateCancelButton()
    {
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(cancel, DialogChromeStyle, minWidth: 72);
        cancel.Click += (_, _) => Close();
        return cancel;
    }
}

internal sealed class AccessibilityReportDialog : FreeWDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle = AvaloniaCompactDialogChrome.WindowsStyle;

    public AccessibilityReportDialog(AccessibilityReport report)
    {
        Title = "Accessibility Checker";
        Width = 460;
        MaxHeight = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        var outer = new StackPanel
        {
            Margin = new Thickness(16, 14, 16, 8),
        };

        outer.Children.Add(new TextBlock
        {
            Text = report.IsClean
                ? "No accessibility issues found."
                : $"{report.ErrorCount} error(s), {report.WarningCount} warning(s), {report.TipCount} tip(s).",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!report.IsClean)
        {
            var list = new StackPanel();
            AddGroup(list, "Errors", AccessibilitySeverity.Error, report, Color.FromRgb(0xC0, 0x00, 0x00));
            AddGroup(list, "Warnings", AccessibilitySeverity.Warning, report, Color.FromRgb(0xB8, 0x6A, 0x00));
            AddGroup(list, "Tips", AccessibilitySeverity.Tip, report, Color.FromRgb(0x40, 0x40, 0x40));

            outer.Children.Add(new ScrollViewer
            {
                MaxHeight = 420,
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            });
        }

        var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true };
        AvaloniaCompactDialogChrome.ApplyButton(ok, DialogChromeStyle, minWidth: 84, isDefault: true);
        ok.Click += (_, _) => Close();
        outer.Children.Add(AvaloniaCompactDialogChrome.CreateActionRow([ok], new Thickness(0, 12, 0, 4)));

        Content = outer;
    }

    private static void AddGroup(
        StackPanel parent,
        string heading,
        AccessibilitySeverity severity,
        AccessibilityReport report,
        Color accent)
    {
        var issues = report.Issues.Where(issue => issue.Severity == severity).ToArray();
        if (issues.Length == 0)
            return;

        parent.Children.Add(new TextBlock
        {
            Text = $"{heading} ({issues.Length})",
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(0, 8, 0, 2),
        });

        foreach (var issue in issues)
            parent.Children.Add(new TextBlock
            {
                Text = $"\u2022  {issue.Message}",
                Margin = new Thickness(8, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap,
            });
    }
}
