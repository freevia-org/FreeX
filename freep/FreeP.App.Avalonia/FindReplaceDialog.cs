using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.AppServices;
using Free.Shared.Shell.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

internal sealed partial class FindReplaceDialog : FreePDialogWindow
{
    private static readonly AvaloniaCompactDialogChromeStyle DialogChromeStyle =
        AvaloniaCompactDialogChrome.WindowsStyle;

    private readonly FindReplaceDialogSession _session;
    private readonly TextBox _findBox;
    private readonly TextBlock _replaceLabel;
    private readonly TextBox _replaceBox;
    private readonly CheckBox _matchCaseCheck;
    private readonly CheckBox _wholeWordCheck;
    private readonly Button _findNextButton;
    private readonly Button _findPreviousButton;
    private readonly Button _replaceButton;
    private readonly Button _replaceAllButton;
    private readonly TextBlock _statusText;
    private readonly Grid _replaceInputRow;
    private readonly StackPanel _replaceButtonRow;
    private readonly StackPanel _findActionRow;
    internal FindReplaceWorkflowPlan LastWorkflowPlan => _session.LastWorkflowPlan;
    internal bool ShowReplace => _session.ShowReplace;

    public FindReplaceDialog(
        EditingSession editor,
        bool showReplace = false,
        Action? onNavigationOrMutation = null)
    {
        _session = new FindReplaceDialogSession(
            editor,
            showReplace,
            onNavigationOrMutation,
            FreePFindReplacePolicyTextCatalog.BuildTextSpec(UiText.Get));
        var initial = _session.InitialState;
        var surface = _session.Surface;

        Width = 425.3333333333333;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, surface.Schema.AccessibleName);
        AutomationProperties.SetAutomationId(this, surface.Schema.AutomationId);

        var findField = surface.Field(FindReplaceDialogField.Query);
        _findBox = new TextBox
        {
            Text = initial.Query,
            MinWidth = 260,
            Margin = new Thickness(6, 4, 0, 4),
        };
        PresentationDialogControlAdapter.ApplySemantic(_findBox, findField);
        var replacementField = surface.Field(FindReplaceDialogField.Replacement);
        _replaceLabel = BuildLabel(replacementField.Label);
        _replaceBox = new TextBox
        {
            Text = initial.Replacement,
            MinWidth = 260,
            Margin = new Thickness(6, 4, 0, 4),
        };
        PresentationDialogControlAdapter.ApplySemantic(_replaceBox, replacementField);
        _matchCaseCheck = new CheckBox
        {
            Content = surface.OptionLabel(FindReplaceDialogOptionKind.MatchCase),
            IsChecked = initial.MatchCase,
            Margin = new Thickness(0, 0, 12, 0),
        };
        _wholeWordCheck = new CheckBox
        {
            Content = surface.OptionLabel(FindReplaceDialogOptionKind.WholeWord),
            IsChecked = initial.WholeWord,
        };
        PresentationDialogControlAdapter.ApplySemantic(_matchCaseCheck, surface.Field(FindReplaceDialogField.MatchCase));
        PresentationDialogControlAdapter.ApplySemantic(_wholeWordCheck, surface.Field(FindReplaceDialogField.WholeWord));
        _findNextButton = BuildButton(
            surface.Action(FindReplaceDialogAction.FindNext),
            () => ApplyWorkflowPlan(_session.Dispatch(FindReplaceDialogAction.FindNext)));
        _findPreviousButton = BuildButton(
            surface.Action(FindReplaceDialogAction.FindPrevious),
            () => ApplyWorkflowPlan(_session.Dispatch(FindReplaceDialogAction.FindPrevious)));
        _replaceButton = BuildButton(
            surface.Action(FindReplaceDialogAction.ReplaceCurrent),
            () => ApplyWorkflowPlan(_session.Dispatch(FindReplaceDialogAction.ReplaceCurrent)));
        _replaceAllButton = BuildButton(
            surface.Action(FindReplaceDialogAction.ReplaceAll),
            () => ApplyWorkflowPlan(_session.Dispatch(FindReplaceDialogAction.ReplaceAll)));
        var closeButton = BuildButton(
            surface.Action(FindReplaceDialogAction.Close),
            Close);
        _statusText = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        PresentationDialogControlAdapter.ApplySemantic(_statusText, surface.Field(FindReplaceDialogField.Status));

        AvaloniaCompactDialogChrome.ApplyTextBox(_findBox, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyTextBox(_replaceBox, DialogChromeStyle);
        // The default Avalonia toggle template reserves a full control-height row.
        // Find/Replace uses the compact WPF checkbox metric instead, keeping the
        // option row and both subsequent action rows on their shared baselines.
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_matchCaseCheck, DialogChromeStyle);
        AvaloniaCompactDialogChrome.ApplyCompactCheckBox(_wholeWordCheck, DialogChromeStyle);

        _findBox.TextChanged += (_, _) => ApplyWorkflowPlan(_session.SetQuery(_findBox.Text));
        _replaceBox.TextChanged += (_, _) => ApplyWorkflowPlan(_session.SetReplacement(_replaceBox.Text));
        _matchCaseCheck.IsCheckedChanged += (_, _) =>
            ApplyWorkflowPlan(_session.SetMatchCase(_matchCaseCheck.IsChecked == true));
        _wholeWordCheck.IsCheckedChanged += (_, _) =>
            ApplyWorkflowPlan(_session.SetWholeWord(_wholeWordCheck.IsChecked == true));
        _findBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;

            ApplyWorkflowPlan(_session.Dispatch(FindReplaceDialogAction.FindNext));
            e.Handled = true;
        };

        _replaceInputRow = BuildInputRow(_replaceLabel, _replaceBox);
        _replaceButtonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [_replaceButton, _replaceAllButton],
            new Thickness(0, 1, 0, 0));
        _replaceButtonRow.Spacing = 4;

        _findActionRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [_findNextButton, _findPreviousButton, closeButton],
            new Thickness(0, 1, 0, 0));

        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                BuildInputRow(BuildLabel(findField.Label), _findBox),
                _replaceInputRow,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 4, 0, 4),
                    Children = { _matchCaseCheck, _wholeWordCheck },
                },
                _replaceButtonRow,
                _findActionRow,
                _statusText,
            },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;

            Close();
            e.Handled = true;
        };
        Opened += (_, _) =>
        {
            _findBox.Focus();
            _findBox.SelectAll();
        };
        ApplyWorkflowPlan(_session.LastWorkflowPlan);
    }

    internal void ShowReplaceMode(bool show)
    {
        ApplyWorkflowPlan(_session.SetShowReplace(show));
    }

    private FindReplaceWorkflowPlan ApplyWorkflowPlan(FindReplaceWorkflowPlan plan)
    {
        // Keep the client surface aligned with the WPF dialog after its content has
        // been normalized to the shared 24px field and button metrics.  These are
        // deliberately mode-specific because the replacement field/action row is
        // genuinely absent in Find mode, not merely hidden visually.
        Height = plan.ShowReplace ? 192 : 130;
        _replaceInputRow.IsVisible = plan.ShowReplace;
        _replaceButtonRow.IsVisible = plan.ShowReplace;
        // In Replace mode the preceding action row supplies the three pixels of
        // separation that WPF gets from its Grid row.  In Find mode that row is
        // absent, so the active action row itself uses the compact one-pixel gap.
        _findActionRow.Margin = new Thickness(0, plan.ShowReplace ? 4 : 1, 0, 0);
        Title = plan.Title;
        _statusText.Text = plan.StatusText;
        _statusText.Foreground = plan.StatusKind switch
        {
            FindReplacePolicyStatusKind.NoMatches or FindReplacePolicyStatusKind.NoReplacements =>
                new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            FindReplacePolicyStatusKind.Match or FindReplacePolicyStatusKind.Replacements =>
                new SolidColorBrush(Color.FromRgb(0x1B, 0x7E, 0x30)),
            _ => new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
        };
        _findNextButton.IsEnabled = plan.CanSearch;
        _findPreviousButton.IsEnabled = plan.CanSearch;
        _replaceButton.IsEnabled = plan.CanReplace;
        _replaceAllButton.IsEnabled = plan.CanReplaceAll;
        return plan;
    }

    private static Grid BuildInputRow(TextBlock label, Control field)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                // WPF's 90px label plus its 6px trailing label margin positions
                // the text field at 102px from the dialog content edge.  Reserve
                // the same space in the Avalonia grid so the two native renderers
                // start their fields on the same vertical guide.
                new ColumnDefinition { Width = new GridLength(96) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        Grid.SetColumn(field, 1);
        row.Children.Add(label);
        row.Children.Add(field);
        return row;
    }

    private static TextBlock BuildLabel(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 4, 6, 4),
        MinWidth = 90,
    };

    private static Button BuildButton(
        PresentationDialogActionPlan<FindReplaceDialogAction> plan,
        Action action)
    {
        var button = new Button
        {
            Content = plan.Label,
            IsDefault = plan.IsDefault,
            IsCancel = plan.IsCancel,
        };
        AutomationProperties.SetName(button, plan.AccessibleName);
        AutomationProperties.SetAutomationId(button, plan.AutomationId);
        AvaloniaCompactDialogChrome.ApplyButton(
            button,
            DialogChromeStyle,
            minWidth: 80,
            isDefault: plan.IsDefault);
        button.Click += (_, _) => action();
        return button;
    }

}
