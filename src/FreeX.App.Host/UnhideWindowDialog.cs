using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using WorkbookWindowSelectionTarget = FreeX.App.Presentation.Shell.WorkbookWindowSelectionTarget<FreeX.App.Host.IWorkbookWindow>;

namespace FreeX.App.Host;

public sealed record UnhideWindowDialogResult(IWorkbookWindow Window);

public sealed class UnhideWindowDialog : Window
{
    private readonly ListBox _windowBox = new();
    private readonly Button _okButton = new() { Content = UiText.Ok, Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
    private readonly Button _cancelButton = new() { Content = UiText.Cancel, Width = 72, IsCancel = true };

    public UnhideWindowDialogResult? Result { get; private set; }

    public UnhideWindowDialog(IEnumerable<WorkbookWindowSelectionTarget> targets)
    {
        var targetList = targets.ToList();
        var selected = targetList.Count == 0 ? null : targetList[0];
        Result = selected is null ? null : CreateResult(selected);

        Title = UiText.Get("UnhideWindow_Title");
        Width = 340;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _windowBox.ItemsSource = targetList;
        _windowBox.SelectedItem = selected;
        _windowBox.SelectionMode = SelectionMode.Single;
        _windowBox.MinHeight = 64;
        AutomationProperties.SetName(_windowBox, UiText.Get("UnhideWindow_ListAutomationName"));
        AutomationProperties.SetAutomationId(_windowBox, "UnhideWindowList");
        AutomationProperties.SetHelpText(_windowBox, UiText.Get("UnhideWindow_ListHelpText"));
        _windowBox.SelectionChanged += (_, _) => UpdateButtonState();
        _windowBox.MouseDoubleClick += WindowBox_MouseDoubleClick;

        AutomationProperties.SetName(_okButton, UiText.Get("UnhideWindow_OkAutomationName"));
        AutomationProperties.SetAutomationId(_okButton, "UnhideWindowOkButton");
        AutomationProperties.SetHelpText(_okButton, UiText.Get("UnhideWindow_OkHelpText"));
        _okButton.Click += (_, _) => Accept();
        AutomationProperties.SetName(_cancelButton, UiText.Get("UnhideWindow_CancelAutomationName"));
        AutomationProperties.SetAutomationId(_cancelButton, "UnhideWindowCancelButton");
        AutomationProperties.SetHelpText(_cancelButton, UiText.Get("UnhideWindow_CancelHelpText"));

        Content = CreateContent();
        UpdateButtonState();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static UnhideWindowDialogResult CreateResult(WorkbookWindowSelectionTarget target) => new(target.Window);

    private UIElement CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label
        {
            Content = UiText.Get("UnhideWindow_WindowLabel"),
            Target = _windowBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4)
        });
        _windowBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_windowBox);
        stack.Children.Add(CreateButtonRow());
        return stack;
    }

    private void FocusInitialKeyboardTarget()
    {
        _windowBox.Focus();
        Keyboard.Focus(_windowBox);
    }

    private UIElement CreateButtonRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        row.Children.Add(_okButton);
        row.Children.Add(_cancelButton);
        return row;
    }

    private void UpdateButtonState()
    {
        _okButton.IsEnabled = _windowBox.SelectedItem is WorkbookWindowSelectionTarget;
    }

    private bool Accept()
    {
        if (_windowBox.SelectedItem is not WorkbookWindowSelectionTarget target)
            return false;

        Result = CreateResult(target);
        DialogResult = true;
        return true;
    }

    private void WindowBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Accept())
            e.Handled = true;
    }
}
