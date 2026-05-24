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

public sealed record AllowEditRangeSelectionRequest(
    string CurrentText,
    bool CollapseDialog = true);

public enum AllowEditRangeDialogAction
{
    Add,
    Remove,
    Clear
}

public sealed record AllowEditRangeDialogResult(AllowEditRangeDialogAction Action, GridRange? Range);

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
            return;
        }

        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _confirmationBox.Focus();
        Keyboard.Focus(_confirmationBox);
    }
}

public sealed class AllowEditRangeDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _rangeBox = new();
    private readonly ListBox _existingRangesBox = new();
    private readonly Button _deleteRangeButton = new() { Content = "_Delete", Width = 76, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _clearRangesButton = new() { Content = "Clear _All", Width = 76 };
    private readonly Action<AllowEditRangeSelectionRequest>? _requestRangeSelection;

    public GridRange Range { get; private set; }
    public AllowEditRangeDialogResult Result { get; private set; } = CreateClearResult();
    public AllowEditRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public AllowEditRangeDialog(
        SheetId sheetId,
        string defaultRange,
        Action<AllowEditRangeSelectionRequest>? requestRangeSelection)
        : this(sheetId, defaultRange, existingRanges: null, requestRangeSelection)
    {
    }

    public AllowEditRangeDialog(
        SheetId sheetId,
        string defaultRange,
        IReadOnlyList<GridRange>? existingRanges = null,
        Action<AllowEditRangeSelectionRequest>? requestRangeSelection = null)
    {
        _sheetId = sheetId;
        _requestRangeSelection = requestRangeSelection;
        Title = "Allow Users to Edit Ranges";
        Width = 430;
        Height = 360;
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

        var existingGroup = new GroupBox { Header = "Ranges unlocked by password", Margin = new Thickness(0, 0, 0, 10) };
        var existingPanel = new DockPanel { Margin = new Thickness(8) };
        _existingRangesBox.ItemsSource = existingRanges?.Select(range => range.ToString()).ToList() ?? [];
        _existingRangesBox.MinHeight = 80;
        _existingRangesBox.SelectionMode = SelectionMode.Single;
        _existingRangesBox.SelectionChanged += (_, _) => UpdateRangeButtons();
        existingPanel.Children.Add(_existingRangesBox);
        var rangeButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _deleteRangeButton.Click += DeleteSelectedRange_Click;
        _clearRangesButton.Click += ClearAllRanges_Click;
        rangeButtons.Children.Add(_deleteRangeButton);
        rangeButtons.Children.Add(_clearRangesButton);
        DockPanel.SetDock(rangeButtons, Dock.Bottom);
        existingPanel.Children.Add(rangeButtons);
        existingGroup.Content = existingPanel;
        root.Children.Add(existingGroup);

        var group = new GroupBox { Header = "Range", Margin = new Thickness(0, 0, 0, 10) };
        var rangePanel = new DockPanel { Margin = new Thickness(8) };
        rangePanel.Children.Add(new Label { Content = "_Range:", Target = _rangeBox, Margin = new Thickness(0, 0, 8, 0) });
        var rangePicker = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Collapse dialog and select editable range"
        };
        System.Windows.Automation.AutomationProperties.SetName(rangePicker, "Select editable range");
        System.Windows.Automation.AutomationProperties.SetHelpText(
            rangePicker,
            "Collapse dialog and select the editable range from the worksheet.");
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
        UpdateRangeButtons();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void RangePicker_Click(object sender, RoutedEventArgs e)
    {
        RangeSelectionRequest = CreateRangeSelectionRequest(_rangeBox.Text);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        _rangeBox.Focus();
        _rangeBox.SelectAll();
    }

    public static AllowEditRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    public static AllowEditRangeDialogResult CreateAddResult(GridRange range) =>
        new(AllowEditRangeDialogAction.Add, range);

    public static AllowEditRangeDialogResult CreateRemoveResult(GridRange range) =>
        new(AllowEditRangeDialogAction.Remove, range);

    public static AllowEditRangeDialogResult CreateClearResult() =>
        new(AllowEditRangeDialogAction.Clear, null);

    private void Accept()
    {
        if (!ProtectionDialogPlanner.TryParseAllowEditRange(_rangeBox.Text, _sheetId, out var range))
        {
            MessageBox.Show(this, "Enter a valid range.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Range = range;
        Result = CreateAddResult(range);
        DialogResult = true;
    }

    private void DeleteSelectedRange_Click(object sender, RoutedEventArgs e)
    {
        if (_existingRangesBox.SelectedItem is not string selected ||
            !ProtectionDialogPlanner.TryParseAllowEditRange(selected, _sheetId, out var range))
            return;

        Range = range;
        Result = CreateRemoveResult(range);
        DialogResult = true;
    }

    private void ClearAllRanges_Click(object sender, RoutedEventArgs e)
    {
        Result = CreateClearResult();
        DialogResult = true;
    }

    private void UpdateRangeButtons()
    {
        var hasRanges = _existingRangesBox.Items.Count > 0;
        _deleteRangeButton.IsEnabled = hasRanges && _existingRangesBox.SelectedItem is not null;
        _clearRangesButton.IsEnabled = hasRanges;
    }

    private void FocusInitialKeyboardTarget()
    {
        _rangeBox.Focus();
        _rangeBox.SelectAll();
        Keyboard.Focus(_rangeBox);
    }
}
