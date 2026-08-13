using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Restrict Editing" pane (Review &gt; Protect &gt; Restrict Editing). The host owns only the
/// WPF controls; shared mode, password, and stop-protection decisions live in
/// <see cref="RestrictEditingDialogPlanner"/>.
/// </summary>
internal sealed class RestrictEditingDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly RadioButton[] _radios;
    private readonly PasswordBox _passwordBox;
    private readonly PasswordBox _confirmBox;
    private readonly RestrictEditingDialogSession _session;
    private readonly RestrictEditingDialogPlan _plan;
    private ProtectionSettings? _result;

    private RestrictEditingDialog(Window? owner, ProtectionSettings current)
    {
        var presentation = RestrictEditingDialogPlanner.Presentation;

        Owner = owner;
        Title = RestrictEditingDialogPlanner.Title;
        Width = presentation.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _session = new RestrictEditingDialogSession(current);
        _plan = _session.InitialPlan;
        _passwordBox = new PasswordBox { MinWidth = 180 };
        _confirmBox = new PasswordBox { MinWidth = 180 };

        var panel = new StackPanel { Margin = new Thickness(presentation.ContentMargin) };
        panel.Children.Add(new TextBlock
        {
            Text = RestrictEditingDialogPlanner.RestrictionPrompt,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, presentation.PromptBottomMargin)
        });

        _radios = new RadioButton[RestrictEditingDialogPlanner.ModeOptions.Count];
        for (var i = 0; i < RestrictEditingDialogPlanner.ModeOptions.Count; i++)
        {
            var option = RestrictEditingDialogPlanner.ModeOptions[i];
            _radios[i] = new RadioButton
            {
                Content = option.Label,
                Margin = new Thickness(0, presentation.ModeOptionVerticalMargin, 0, presentation.ModeOptionVerticalMargin),
                IsChecked = i == _plan.SelectedModeIndex
            };
            panel.Children.Add(_radios[i]);
        }

        if (_plan.ShowStartPasswordFields)
        {
            panel.Children.Add(new Separator
            {
                Margin = new Thickness(0, presentation.PasswordSeparatorTopMargin, 0, presentation.PasswordSeparatorBottomMargin)
            });
            panel.Children.Add(new TextBlock
            {
                Text = RestrictEditingDialogPlanner.OptionalPasswordPrompt,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, presentation.PasswordPromptBottomMargin)
            });
            panel.Children.Add(new TextBlock { Text = RestrictEditingDialogPlanner.PasswordLabel, Margin = new Thickness(0, 0, 0, 2) });
            panel.Children.Add(_passwordBox);
            panel.Children.Add(new TextBlock { Text = RestrictEditingDialogPlanner.ConfirmLabel, Margin = new Thickness(0, 4, 0, 2) });
            panel.Children.Add(_confirmBox);
        }

        var enforce = new Button
        {
            Content = RestrictEditingDialogPlanner.StartButtonText,
            MinWidth = 200,
            Margin = new Thickness(0, presentation.StartActionTopMargin, 0, presentation.ActionButtonBottomMargin),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = _plan.CanStartProtection,
            IsDefault = presentation.DefaultButtonText == RestrictEditingDialogPlanner.StartButtonText
        };
        enforce.Click += (_, _) => Enforce();
        panel.Children.Add(enforce);

        var stop = new Button
        {
            Content = RestrictEditingDialogPlanner.StopButtonText,
            MinWidth = 180,
            Margin = new Thickness(0, 0, 0, presentation.ActionButtonBottomMargin),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = _plan.CanStopProtection,
            IsDefault = presentation.DefaultButtonText == RestrictEditingDialogPlanner.StopButtonText
        };
        stop.Click += async (_, _) => await StopProtectionAsync();
        panel.Children.Add(stop);

        var cancel = new Button
        {
            Content = RestrictEditingDialogPlanner.CancelButtonText,
            MinWidth = 72,
            IsCancel = true,
            Margin = new Thickness(0, presentation.CancelActionTopMargin, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        cancel.Click += (_, _) => _session.Cancel();
        panel.Children.Add(cancel);

        Content = panel;
        Loaded += (_, _) => _radios[0].Focus();
    }

    private void Enforce()
    {
        var outcome = _session.Start(
            SelectedModeIndex(),
            _passwordBox.Password,
            _confirmBox.Password);
        if (!outcome.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ValidationMessage, Title);
            _passwordBox.Focus();
            return;
        }

        _result = outcome.Settings;
        Close();
    }

    private async Task StopProtectionAsync()
    {
        var outcome = await _session.StopAsync((title, prompt) =>
            ValueTask.FromResult(PasswordPromptDialog.Ask(Owner, title, prompt)));
        if (outcome.Kind == RestrictEditingDialogOutcomeKind.Cancelled)
            return;
        if (!outcome.IsAccepted)
        {
            DialogMessageHelper.ShowWarning(this, outcome.ValidationMessage, Title);
            return;
        }

        _result = outcome.Settings;
        Close();
    }

    private int SelectedModeIndex()
    {
        for (var i = 0; i < _radios.Length; i++)
        {
            if (_radios[i].IsChecked == true)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Show the pane seeded with the current protection settings. Returns the new
    /// <see cref="ProtectionSettings"/> (which may include a password hash), or null if cancelled.
    /// A return value of <see cref="ProtectionSettings.Unprotected"/> means protection was stopped.
    /// </summary>
    public static ProtectionSettings? Prompt(Window? owner, ProtectionSettings current)
    {
        var dialog = new RestrictEditingDialog(owner, current);
        dialog.ShowDialog();
        return dialog._result;
    }
}
