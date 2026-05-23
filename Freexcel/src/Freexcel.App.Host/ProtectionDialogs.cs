using System.Windows;
using System.Windows.Controls;
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

    private static readonly string[] DefaultSelectedSheetPermissions =
    [
        "Select locked cells",
        "Select unlocked cells"
    ];

    public static ProtectionDialogResult CreateSheetResult(Sheet sheet, string? password) =>
        CreateSheetResult(sheet, password, DefaultSelectedSheetPermissions);

    public static ProtectionDialogResult CreateSheetResult(
        Sheet sheet,
        string? password,
        IReadOnlyList<string> selectedSheetPermissions) =>
        sheet.IsProtected
            ? new ProtectionDialogResult(ProtectionDialogMode.Unprotect, null, [])
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, password, selectedSheetPermissions);

    public static ProtectionDialogResult CreateSheetResult(Sheet sheet, string? password, string? confirmation) =>
        sheet.IsProtected || PasswordsMatch(password, confirmation)
            ? CreateSheetResult(sheet, password)
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, null, DefaultSelectedSheetPermissions);

    public static ProtectionDialogResult CreateWorkbookResult(Workbook workbook, string? password) =>
        workbook.IsStructureProtected
            ? new ProtectionDialogResult(ProtectionDialogMode.Unprotect, null, [])
            : new ProtectionDialogResult(ProtectionDialogMode.Protect, password, []);

    public static IReadOnlyList<string> GetDefaultSheetPermissions() => DefaultSheetPermissions;

    public static IReadOnlyList<string> GetDefaultSelectedSheetPermissions() => DefaultSelectedSheetPermissions;

    public static bool PasswordsMatch(string? password, string? confirmation) =>
        string.Equals(password ?? "", confirmation ?? "", StringComparison.Ordinal);

    public static bool TryParseAllowEditRange(string text, SheetId sheetId, out GridRange range) =>
        ProtectionInputParser.TryParseAllowEditRange(text, sheetId, out range);
}

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
            ToolTip = "These selections are returned with the Protect Sheet result; current enforcement is limited to locked cells and allowed edit ranges.",
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
    }

    private void Accept()
    {
        if (!ProtectionDialogPlanner.PasswordsMatch(_password, _confirmationBox.Password))
        {
            MessageBox.Show(this, "The confirmation password does not match.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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
        Width = 430;
        Height = 230;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock
        {
            Text = "Specify a worksheet range that can be edited while the sheet is protected.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        var group = new GroupBox { Header = "Range", Margin = new Thickness(0, 0, 0, 10) };
        var rangePanel = new DockPanel { Margin = new Thickness(8) };
        rangePanel.Children.Add(new Label { Content = "_Range:", Target = _rangeBox, Margin = new Thickness(0, 0, 8, 0) });
        var rangePicker = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Select editable range"
        };
        System.Windows.Automation.AutomationProperties.SetName(rangePicker, "Select editable range");
        rangePicker.Click += RangePicker_Click;
        DockPanel.SetDock(rangePicker, Dock.Right);
        rangePanel.Children.Add(rangePicker);
        _rangeBox.Text = defaultRange;
        rangePanel.Children.Add(_rangeBox);
        group.Content = rangePanel;
        root.Children.Add(group);
        root.Children.Add(new TextBlock
        {
            Text = "Use an A1-style range, for example A1:C10.",
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 72));

        Content = root;
    }

    private void RangePicker_Click(object sender, RoutedEventArgs e)
    {
        _rangeBox.Focus();
        _rangeBox.SelectAll();
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
