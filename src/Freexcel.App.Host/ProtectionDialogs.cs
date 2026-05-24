using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum ProtectionDialogMode
{
    Protect,
    Unprotect
}

public sealed record ProtectionDialogResult(
    ProtectionDialogMode Mode,
    string? Password,
    IReadOnlyList<string> SelectedSheetPermissions);

public sealed class PasswordProtectionDialog : Window
{
    private readonly PasswordBox _passwordBox = new();
    private readonly List<CheckBox> _sheetPermissionBoxes = [];
    private readonly bool _requiresConfirmation;

    public string? Password { get; private set; }
    public IReadOnlyList<string> SelectedSheetPermissions { get; private set; } =
        ProtectionDialogPlanner.GetDefaultSelectedSheetPermissions();

    public PasswordProtectionDialog(string title, string prompt)
    {
        _requiresConfirmation = title.StartsWith("Protect ", StringComparison.OrdinalIgnoreCase);
        Title = title;
        Width = title.Equals("Protect Sheet", StringComparison.OrdinalIgnoreCase) ? 430 : 360;
        Height = title.Equals("Protect Sheet", StringComparison.OrdinalIgnoreCase) ? 540 : 250;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock
        {
            Text = title.Equals("Protect Sheet", StringComparison.OrdinalIgnoreCase)
                ? "Protect worksheet and contents of locked cells"
                : prompt,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var passwordGroup = new GroupBox { Header = "Password", Margin = new Thickness(0, 0, 0, 10) };
        var passwordPanel = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };
        passwordPanel.Children.Add(new Label { Content = prompt, Target = _passwordBox, Margin = new Thickness(0, 0, 0, 4) });
        passwordPanel.Children.Add(_passwordBox);
        passwordPanel.Children.Add(new TextBlock
        {
            Text = "Caution: lost or forgotten passwords cannot be recovered.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 8, 0, 0)
        });
        passwordGroup.Content = passwordPanel;
        root.Children.Add(passwordGroup);

        if (title.Equals("Protect Sheet", StringComparison.OrdinalIgnoreCase))
            AddSheetPermissionChecklist(root);

        root.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0)));

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void AddSheetPermissionChecklist(Panel root)
    {
        var checklist = new StackPanel();
        var scroll = new ScrollViewer
        {
            Content = checklist,
            Height = 230,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var group = new GroupBox
        {
            Header = "Allow all users of this worksheet to:",
            Content = scroll,
            ToolTip = "Choose which protected-sheet actions remain available.",
            Margin = new Thickness(0, 0, 0, 0)
        };
        root.Children.Add(group);

        foreach (var permission in ProtectionDialogPlanner.GetDefaultSheetPermissions())
        {
            var box = new CheckBox
            {
                Content = permission,
                IsChecked = permission is "Select locked cells" or "Select unlocked cells",
                Margin = new Thickness(0, 0, 0, 4)
            };
            _sheetPermissionBoxes.Add(box);
            checklist.Children.Add(box);
        }
    }

    private void Accept()
    {
        if (_requiresConfirmation && !string.IsNullOrEmpty(_passwordBox.Password))
        {
            var confirmationDialog = new ConfirmPasswordDialog(_passwordBox.Password) { Owner = this };
            if (confirmationDialog.ShowDialog() != true)
                return;
        }

        Password = _passwordBox.Password;
        SelectedSheetPermissions = _sheetPermissionBoxes
            .Where(box => box.IsChecked == true)
            .Select(box => box.Content?.ToString() ?? "")
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .ToList();
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _passwordBox.Focus();
        Keyboard.Focus(_passwordBox);
    }
}

public sealed class ConfirmPasswordDialog : Window
{
    private readonly string _password;
    private readonly PasswordBox _confirmationBox = new();

    public ConfirmPasswordDialog(string password)
    {
        _password = password;
        Title = "Confirm Password";
        Width = 360;
        Height = 170;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock
        {
            Text = "Reenter password to proceed.",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new Label { Content = "_Password:", Target = _confirmationBox, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(_confirmationBox);
        root.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0)));
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void Accept()
    {
        if (!ProtectionDialogPlanner.PasswordsMatch(_password, _confirmationBox.Password))
        {
            MessageBox.Show(this, "The confirmation password does not match.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusConfirmationInput();
            return;
        }

        DialogResult = true;
    }

    private void FocusConfirmationInput()
    {
        _confirmationBox.Focus();
        Keyboard.Focus(_confirmationBox);
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusConfirmationInput();
    }
}
