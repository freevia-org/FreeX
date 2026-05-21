using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum ProtectionDialogMode
{
    Protect,
    Unprotect
}

public sealed record ProtectionDialogResult(ProtectionDialogMode Mode, string? Password);

public static class ProtectionDialogPlanner
{
    private static readonly string[] DefaultSheetPermissions =
    [
        "Select locked cells",
        "Select unlocked cells",
        "Format cells",
        "Format columns",
        "Format rows",
        "Insert columns",
        "Insert rows",
        "Insert hyperlinks",
        "Delete columns",
        "Delete rows",
        "Sort",
        "Use AutoFilter",
        "Use PivotTable reports",
        "Edit objects",
        "Edit scenarios"
    ];

    public static ProtectionDialogResult CreateSheetResult(Sheet sheet, string? password) =>
        sheet.IsProtected
            ? new ProtectionDialogResult(ProtectionDialogMode.Unprotect, null)
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, password);

    public static ProtectionDialogResult CreateSheetResult(Sheet sheet, string? password, string? confirmation) =>
        sheet.IsProtected || PasswordsMatch(password, confirmation)
            ? CreateSheetResult(sheet, password)
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, null);

    public static ProtectionDialogResult CreateWorkbookResult(Workbook workbook, string? password) =>
        workbook.IsStructureProtected
            ? new ProtectionDialogResult(ProtectionDialogMode.Unprotect, null)
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, password);

    public static IReadOnlyList<string> GetDefaultSheetPermissions() => DefaultSheetPermissions;

    public static bool PasswordsMatch(string? password, string? confirmation) =>
        string.Equals(password ?? "", confirmation ?? "", StringComparison.Ordinal);

    public static bool TryParseAllowEditRange(string text, SheetId sheetId, out GridRange range) =>
        ProtectionInputParser.TryParseAllowEditRange(text, sheetId, out range);
}

public sealed class PasswordProtectionDialog : Window
{
    private readonly PasswordBox _passwordBox = new();
    private readonly PasswordBox _confirmationBox = new();
    private readonly bool _requiresConfirmation;

    public string? Password { get; private set; }

    public PasswordProtectionDialog(string title, string prompt)
    {
        _requiresConfirmation = title.StartsWith("Protect ", StringComparison.OrdinalIgnoreCase);
        Title = title;
        Width = title.Equals("Protect Sheet", StringComparison.OrdinalIgnoreCase) ? 430 : 360;
        Height = title.Equals("Protect Sheet", StringComparison.OrdinalIgnoreCase) ? 500 : 210;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new Label { Content = prompt, Target = _passwordBox, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(_passwordBox);
        if (_requiresConfirmation)
        {
            root.Children.Add(new Label { Content = "_Confirm password:", Target = _confirmationBox, Margin = new Thickness(0, 8, 0, 4) });
            root.Children.Add(_confirmationBox);
        }

        if (title.Equals("Protect Sheet", StringComparison.OrdinalIgnoreCase))
            AddSheetPermissionChecklist(root);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        root.Children.Add(buttons);
        var ok = new Button { Content = "_OK", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "_Cancel", Width = 72, IsCancel = true });

        Content = root;
    }

    private static void AddSheetPermissionChecklist(Panel root)
    {
        root.Children.Add(new TextBlock
        {
            Text = "Allow all users of this worksheet to:",
            Margin = new Thickness(0, 14, 0, 6)
        });

        var checklist = new StackPanel();
        var scroll = new ScrollViewer
        {
            Content = checklist,
            Height = 230,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(scroll);

        foreach (var permission in ProtectionDialogPlanner.GetDefaultSheetPermissions())
        {
            checklist.Children.Add(new CheckBox
            {
                Content = permission,
                IsChecked = permission is "Select locked cells" or "Select unlocked cells",
                Margin = new Thickness(0, 0, 0, 4)
            });
        }
    }

    private void Accept()
    {
        if (_requiresConfirmation && !ProtectionDialogPlanner.PasswordsMatch(_passwordBox.Password, _confirmationBox.Password))
        {
            MessageBox.Show(this, "The confirmation password does not match.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Password = _passwordBox.Password;
        DialogResult = true;
    }
}

public sealed class AllowEditRangeDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _rangeBox = new();

    public GridRange Range { get; private set; }

    public AllowEditRangeDialog(SheetId sheetId, string defaultRange)
    {
        _sheetId = sheetId;
        Title = "Allow Users to Edit Ranges";
        Width = 360;
        Height = 150;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new Label { Content = "_Range:", Target = _rangeBox, Margin = new Thickness(0, 0, 0, 6) });
        _rangeBox.Text = defaultRange;
        root.Children.Add(_rangeBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        root.Children.Add(buttons);
        var ok = new Button { Content = "_OK", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "_Cancel", Width = 72, IsCancel = true });

        Content = root;
    }

    private void Accept()
    {
        if (!ProtectionDialogPlanner.TryParseAllowEditRange(_rangeBox.Text, _sheetId, out var range))
        {
            MessageBox.Show(this, "Enter a valid range.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Range = range;
        DialogResult = true;
    }
}
