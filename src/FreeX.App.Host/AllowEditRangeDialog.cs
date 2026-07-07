using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record AllowEditRangeSelectionRequest(
    string CurrentText,
    bool CollapseDialog = true);

public sealed class AllowEditRangeDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly TextBox _rangeBox = new();
    private readonly PasswordBox _rangePasswordBox = new();
    private readonly ListBox _existingRangesBox = new();
    private readonly Button _newRangeButton = new() { Content = UiText.Get("AllowEditRange_NewButton"), Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _modifyRangeButton = new() { Content = UiText.Get("AllowEditRange_ModifyButton"), Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _deleteRangeButton = new() { Content = UiText.Get("AllowEditRange_DeleteButton"), Width = 82, Margin = new Thickness(0, 0, 6, 0) };
    private readonly Button _permissionsButton = new() { Content = UiText.Get("AllowEditRange_PermissionsButton"), Width = 104 };
    private readonly Action<AllowEditRangeSelectionRequest>? _requestRangeSelection;
    private readonly IReadOnlyDictionary<GridRange, string?> _existingRangePasswords;
    private GridRange? _rangeBeingModified;

    public GridRange Range { get; private set; }
    public AllowEditRangeResult Result { get; private set; } = CreateClearResult();
    public AllowEditRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    /// <summary>
    /// The hash of the range-specific password typed into this dialog for the range in
    /// <see cref="Result"/> (Excel's Allow Users to Edit Ranges "Range Password", distinct from the
    /// sheet password) -- already hashed via
    /// <see cref="ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash"/> so callers never see or
    /// persist the typed plaintext. Null means "no password" (the range stays freely editable once
    /// reached). Only meaningful when <see cref="RangePasswordChanged"/> is true; callers wire it
    /// into <c>Sheet.AllowEditRangePasswords</c> alongside applying <see cref="Result"/>.
    /// </summary>
    public string? RangePassword { get; private set; }

    /// <summary>
    /// True when the user actually typed into the password box this time round (an add/new range
    /// always counts as changed). False on a modify where the box was left blank, meaning "keep
    /// whatever password (if any) the range already had" — mirrors Excel, which never redisplays or
    /// silently clears an existing range password just because the dialog reopened.
    /// </summary>
    public bool RangePasswordChanged { get; private set; }

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
        Action<AllowEditRangeSelectionRequest>? requestRangeSelection = null,
        IReadOnlyDictionary<GridRange, string?>? existingRangePasswords = null)
    {
        _sheetId = sheetId;
        _requestRangeSelection = requestRangeSelection;
        _existingRangePasswords = existingRangePasswords is null
            ? []
            : new Dictionary<GridRange, string?>(existingRangePasswords);
        Title = UiText.Get("AllowEditRange_Title");
        Width = 430;
        Height = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        // Layout mirrors the Avalonia/Linux shell (the agreed parity target): a single vertical flow of
        // intro text, a "Ranges" group whose New/Modify/Delete/Permissions buttons sit UNDER the list,
        // then the range label + box stacked vertically, the example hint, and an [OK][Cancel] row.
        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("AllowEditRange_Intro"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _existingRangesBox.ItemsSource = AllowEditRangePlanner.BuildExistingRangeItems(existingRanges);
        AutomationProperties.SetName(_existingRangesBox, UiText.Get("AllowEditRange_ExistingRangesAutomationName"));
        AutomationProperties.SetAutomationId(_existingRangesBox, "AllowEditRangeExistingRangesList");
        AutomationProperties.SetHelpText(_existingRangesBox, UiText.Get("AllowEditRange_ExistingRangesHelpText"));
        _existingRangesBox.MinHeight = 80;
        _existingRangesBox.SelectionMode = SelectionMode.Single;
        _existingRangesBox.SelectionChanged += ExistingRangesBox_SelectionChanged;
        _existingRangesBox.MouseDoubleClick += ExistingRangesBox_MouseDoubleClick;

        _newRangeButton.Click += NewRange_Click;
        _modifyRangeButton.Click += ModifySelectedRange_Click;
        _deleteRangeButton.Click += DeleteSelectedRange_Click;
        _permissionsButton.IsEnabled = false;
        AutomationProperties.SetName(_newRangeButton, UiText.Get("AllowEditRange_NewAutomationName"));
        AutomationProperties.SetAutomationId(_newRangeButton, "AllowEditRangeNewButton");
        AutomationProperties.SetHelpText(_newRangeButton, UiText.Get("AllowEditRange_NewHelpText"));
        AutomationProperties.SetName(_modifyRangeButton, UiText.Get("AllowEditRange_ModifyAutomationName"));
        AutomationProperties.SetAutomationId(_modifyRangeButton, "AllowEditRangeModifyButton");
        AutomationProperties.SetHelpText(_modifyRangeButton, UiText.Get("AllowEditRange_ModifyHelpText"));
        AutomationProperties.SetName(_deleteRangeButton, UiText.Get("AllowEditRange_DeleteAutomationName"));
        AutomationProperties.SetAutomationId(_deleteRangeButton, "AllowEditRangeDeleteButton");
        AutomationProperties.SetHelpText(_deleteRangeButton, UiText.Get("AllowEditRange_DeleteHelpText"));
        AutomationProperties.SetName(_permissionsButton, UiText.Get("AllowEditRange_PermissionsAutomationName"));
        AutomationProperties.SetAutomationId(_permissionsButton, "AllowEditRangePermissionsButton");
        AutomationProperties.SetHelpText(_permissionsButton, UiText.Get("AllowEditRange_PermissionsHelpText"));

        // New/Modify/Delete/Permissions sit in a left-aligned row directly under the list.
        var rangeButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        rangeButtons.Children.Add(_newRangeButton);
        rangeButtons.Children.Add(_modifyRangeButton);
        rangeButtons.Children.Add(_deleteRangeButton);
        rangeButtons.Children.Add(_permissionsButton);

        var existingPanel = new StackPanel { Margin = new Thickness(8) };
        existingPanel.Children.Add(_existingRangesBox);
        existingPanel.Children.Add(rangeButtons);
        var existingGroup = new GroupBox
        {
            Header = UiText.Get("AllowEditRange_ExistingRangesLabel"),
            Content = existingPanel,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(existingGroup);

        // Range label + box stacked vertically (no nested "Range" group box, no inline "..." picker),
        // matching the Linux layout.
        _rangeBox.Text = defaultRange;
        AutomationProperties.SetName(_rangeBox, UiText.Get("AllowEditRange_RangeAutomationName"));
        AutomationProperties.SetAutomationId(_rangeBox, "AllowEditRangeBox");
        AutomationProperties.SetHelpText(_rangeBox, UiText.Get("AllowEditRange_RangeHelpText"));
        root.Children.Add(new Label
        {
            Content = UiText.Get("AllowEditRange_RangeLabel"),
            Target = _rangeBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(_rangeBox);
        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("AllowEditRange_Example"),
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 6, 0, 10)
        });

        // Range-specific password (Excel's per-range "Range Password", distinct from the sheet
        // password): optional, so an empty box means the range stays freely editable once reached.
        AutomationProperties.SetName(_rangePasswordBox, UiText.Get("Protection_PasswordAutomationName"));
        AutomationProperties.SetAutomationId(_rangePasswordBox, "AllowEditRangePasswordBox");
        AutomationProperties.SetHelpText(_rangePasswordBox, UiText.Get("Protection_PasswordHelpText"));
        root.Children.Add(new Label
        {
            Content = UiText.Get("Protection_Password"),
            Target = _rangePasswordBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(_rangePasswordBox);
        root.Children.Add(DialogButtonRowFactory.Create(Accept, buttonWidth: 84));

        Content = root;
        UpdateRangeButtons();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void RangePicker_Click(object sender, RoutedEventArgs e)
    {
        RangeSelectionRequest = CreateRangeSelectionRequest(_rangeBox.Text);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        FocusRangeInput();
    }

    public static AllowEditRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    public void ApplyRangeSelection(string rangeText)
    {
        _rangeBox.Text = rangeText;
        FocusRangeInput();
    }

    /// <summary>True when <paramref name="range"/> already carries its own range password.</summary>
    public bool HasExistingPassword(GridRange range) =>
        _existingRangePasswords.TryGetValue(range, out var stored) && !string.IsNullOrEmpty(stored);

    public static AllowEditRangeResult CreateAddResult(GridRange range) =>
        AllowEditRangePlanner.CreateAddResult(range);

    public static AllowEditRangeResult CreateModifyResult(GridRange originalRange, GridRange updatedRange) =>
        AllowEditRangePlanner.CreateModifyResult(originalRange, updatedRange);

    public static AllowEditRangeResult CreateRemoveResult(GridRange range) =>
        AllowEditRangePlanner.CreateRemoveResult(range);

    public static AllowEditRangeResult CreateClearResult() =>
        AllowEditRangePlanner.CreateClearResult();

    private void Accept()
    {
        if (!AllowEditRangePlanner.TryParseRange(_rangeBox.Text, _sheetId, out var range))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("AllowEditRange_InvalidRangeMessage"), Title);
            FocusRangeInput();
            return;
        }

        Range = range;
        var isModify = _rangeBeingModified is not null;
        Result = _rangeBeingModified is { } originalRange
            ? CreateModifyResult(originalRange, range)
            : CreateAddResult(range);

        var typedPassword = string.IsNullOrEmpty(_rangePasswordBox.Password) ? null : _rangePasswordBox.Password;
        // Hash the typed plaintext immediately -- never store or hand callers the raw password (see
        // ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash for why the unambiguous hash-only
        // overload is required here rather than the round-tripping ToLegacyPasswordHash).
        RangePassword = typedPassword is null
            ? null
            : ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash(typedPassword);
        // Adding a brand-new range always "changes" its password (from nothing to whatever was
        // typed, including nothing). Modifying an existing range only counts as a password change
        // when the user actually typed something — a blank box means "leave the stored password
        // (if any) alone", since the box can never show the real existing password back to them.
        RangePasswordChanged = !isModify || typedPassword is not null;
        DialogResult = true;
    }

    private void NewRange_Click(object sender, RoutedEventArgs e)
    {
        _rangeBeingModified = null;
        _existingRangesBox.SelectedItem = null;
        _rangePasswordBox.Password = string.Empty;
        FocusRangeInput();
    }

    private void ModifySelectedRange_Click(object sender, RoutedEventArgs e)
        => TryLoadSelectedRangeForModification();

    private void DeleteSelectedRange_Click(object sender, RoutedEventArgs e)
        => TryDeleteSelectedRange();

    private bool TryLoadSelectedRangeForModification()
    {
        if (_existingRangesBox.SelectedItem is not string selected ||
            !AllowEditRangePlanner.TryParseRange(selected, _sheetId, out var range))
            return false;

        _rangeBeingModified = range;
        _rangeBox.Text = selected;
        // Mirrors Excel: an existing range password is never redisplayed (only its hash is known),
        // so the box is always cleared here. See RangePasswordChanged for how a blank box on Accept
        // is interpreted as "leave the stored password alone" rather than "clear it".
        _rangePasswordBox.Password = string.Empty;
        FocusRangeInput();
        return true;
    }

    private bool TryDeleteSelectedRange()
    {
        if (_existingRangesBox.SelectedItem is not string selected ||
            !AllowEditRangePlanner.TryParseRange(selected, _sheetId, out var range))
            return false;

        Range = range;
        Result = CreateRemoveResult(range);
        DialogResult = true;
        return true;
    }

    private void ExistingRangesBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryLoadSelectedRangeForModification())
            e.Handled = true;
    }

    private void ExistingRangesBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rangeBeingModified is not null &&
            (_existingRangesBox.SelectedItem is not string selected ||
             !AllowEditRangePlanner.TryParseRange(selected, _sheetId, out var selectedRange) ||
             selectedRange != _rangeBeingModified))
        {
            _rangeBeingModified = null;
        }

        UpdateRangeButtons();
    }

    private void UpdateRangeButtons()
    {
        var state = AllowEditRangePlanner.BuildButtonState(
            _existingRangesBox.Items.Count,
            _existingRangesBox.SelectedItem is not null);
        _modifyRangeButton.IsEnabled = state.CanModifySelectedRange;
        _deleteRangeButton.IsEnabled = state.CanDeleteSelectedRange;
        _permissionsButton.IsEnabled = state.CanUsePermissions;
    }

    private void FocusInitialKeyboardTarget()
    {
        FocusRangeInput();
    }

    private void FocusRangeInput()
    {
        DialogFocus.FocusAndSelect(_rangeBox);
    }
}
