using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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
        var isProtectSheet = IsProtectSheetTitle(title);
        _requiresConfirmation = title.StartsWith("Protect ", StringComparison.OrdinalIgnoreCase) ||
            title.StartsWith(UiText.Get("Protection_ProtectTitlePrefix"), StringComparison.OrdinalIgnoreCase);
        Title = title;
        Width = isProtectSheet ? 430 : 360;
        Height = isProtectSheet ? 540 : 250;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AutomationProperties.SetName(_passwordBox, "Protection password");
        AutomationProperties.SetAutomationId(_passwordBox, "ProtectionPasswordBox");
        AutomationProperties.SetHelpText(_passwordBox, "Enter the optional password for protecting the sheet or workbook.");

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock
        {
            Text = isProtectSheet
                ? UiText.Get("Protection_ProtectWorksheetContents")
                : prompt,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var passwordGroup = new GroupBox { Header = UiText.Get("Protection_Password"), Margin = new Thickness(0, 0, 0, 10) };
        var passwordPanel = new StackPanel { Margin = new Thickness(8, 6, 8, 8) };
        passwordPanel.Children.Add(new Label { Content = prompt, Target = _passwordBox, Margin = new Thickness(0, 0, 0, 4) });
        passwordPanel.Children.Add(_passwordBox);
        passwordPanel.Children.Add(new TextBlock
        {
            Text = UiText.Get("Protection_CautionLostOrForgottenPasswordsCannotBeRecovered"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 8, 0, 0)
        });
        passwordGroup.Content = passwordPanel;
        root.Children.Add(passwordGroup);

        if (isProtectSheet)
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
            Header = UiText.Get("Protection_AllowAllUsersOfThisWorksheetTo"),
            Content = scroll,
            ToolTip = UiText.Get("Protection_ChooseWhichProtectedSheetActionsRemainAvailable"),
            Margin = new Thickness(0, 0, 0, 0)
        };
        root.Children.Add(group);

        var defaultSelectedPermissions = ProtectionDialogPlanner.GetDefaultSelectedSheetPermissions().ToHashSet(StringComparer.Ordinal);
        foreach (var permission in ProtectionDialogPlanner.GetDefaultSheetPermissions())
        {
            var box = new CheckBox
            {
                Content = permission,
                IsChecked = defaultSelectedPermissions.Contains(permission),
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

    private static bool IsProtectSheetTitle(string title) =>
        title.Equals("Protect Sheet", StringComparison.OrdinalIgnoreCase) ||
        title.Equals(UiText.Get("Protection_ProtectSheetTitle"), StringComparison.OrdinalIgnoreCase);
}

public sealed class ConfirmPasswordDialog : Window
{
    private readonly string _password;
    private readonly PasswordBox _confirmationBox = new();

    public ConfirmPasswordDialog(string password)
    {
        _password = password;
        Title = UiText.Get("Protection_ConfirmPassword");
        Width = 360;
        Height = 170;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AutomationProperties.SetName(_confirmationBox, "Confirm protection password");
        AutomationProperties.SetAutomationId(_confirmationBox, "ConfirmProtectionPasswordBox");
        AutomationProperties.SetHelpText(_confirmationBox, "Reenter the password to confirm protection.");

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("Protection_ReenterPasswordToProceed"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new Label { Content = UiText.Get("Protection_Password2"), Target = _confirmationBox, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(_confirmationBox);
        root.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 12, 0, 0)));
        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void Accept()
    {
        if (!ProtectionDialogPlanner.PasswordsMatch(_password, _confirmationBox.Password))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("Protection_TheConfirmationPasswordDoesNotMatch"), Title);
            FocusConfirmationInput();
            return;
        }

        DialogResult = true;
    }

    private void FocusConfirmationInput()
    {
        _confirmationBox.Focus();
        _confirmationBox.SelectAll();
        Keyboard.Focus(_confirmationBox);
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusConfirmationInput();
    }
}
