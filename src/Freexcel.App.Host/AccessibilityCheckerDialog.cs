using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;

namespace Freexcel.App.Host;

public sealed class AccessibilityCheckerDialog : Window
{
    private readonly TextBox _messageBox = new();

    public AccessibilityCheckerDialog(IReadOnlyList<AccessibilityIssue> issues)
    {
        Title = "Accessibility Checker";
        Width = 520;
        Height = issues.Count == 0 ? 170 : 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Content = CreateContent(CreateMessage(issues));
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static string CreateMessage(IReadOnlyList<AccessibilityIssue> issues) =>
        issues.Count == 0
            ? "No accessibility issues found."
            : AccessibilityIssueFormatter.Format(issues);

    private StackPanel CreateContent(string message)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        _messageBox.Text = message;
        _messageBox.IsReadOnly = true;
        _messageBox.AcceptsReturn = true;
        _messageBox.TextWrapping = TextWrapping.Wrap;
        _messageBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _messageBox.MinHeight = 80;
        _messageBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_messageBox);
        stack.Children.Add(DialogButtonRowFactory.Create(() => Window.GetWindow(stack)!.DialogResult = true, buttonWidth: 76));
        return stack;
    }

    private void FocusInitialKeyboardTarget()
    {
        _messageBox.Focus();
        Keyboard.Focus(_messageBox);
    }
}
