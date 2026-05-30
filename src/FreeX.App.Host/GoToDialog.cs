using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class GoToDialog : Window
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyDictionary<string, GridRange> _definedNames;
    private readonly TextBox _addressBox = new();
    private readonly ListBox _historyList = new();

    public CellAddress SelectedAddress { get; private set; }
    public GridRange? SelectedRange { get; private set; }
    public GoToSpecialKind? SelectedSpecialKind { get; private set; }
    public GoToSpecialOptions? SelectedSpecialOptions { get; private set; }

    public GoToDialog(
        SheetId sheetId,
        string defaultAddress = "A1",
        IReadOnlyDictionary<string, GridRange>? definedNames = null,
        IEnumerable<string>? recentReferences = null)
    {
        _sheetId = sheetId;
        _definedNames = definedNames is null
            ? new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, GridRange>(definedNames, StringComparer.OrdinalIgnoreCase);

        Title = UiText.Get("GoTo_GoTo");
        Width = 420;
        Height = 320;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var historyLabel = new Label
        {
            Content = UiText.Get("GoTo_GoTo2"),
            Target = _historyList,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 4)
        };
        Grid.SetRow(historyLabel, 0);
        Grid.SetColumnSpan(historyLabel, 2);
        root.Children.Add(historyLabel);

        foreach (var reference in BuildReferenceChoices(defaultAddress, recentReferences, _definedNames.Keys))
            _historyList.Items.Add(reference);

        _historyList.ToolTip = UiText.Get("GoTo_RecentReferencesAndDefinedNames");
        AutomationProperties.SetName(_historyList, UiText.Get("GoTo_GoTo"));
        AutomationProperties.SetHelpText(_historyList, UiText.Get("GoTo_ListsRecentReferencesAndDefinedNamesAvailableForNavigation"));
        _historyList.MinHeight = 130;
        _historyList.Margin = new Thickness(0, 24, 0, 0);
        _historyList.SelectionChanged += (_, _) =>
        {
            if (_historyList.SelectedItem is string reference)
                _addressBox.Text = reference;
        };
        _historyList.MouseDoubleClick += (_, _) => Accept();
        _historyList.SelectedIndex = 0;
        Grid.SetRow(_historyList, 1);
        Grid.SetColumnSpan(_historyList, 2);
        root.Children.Add(_historyList);

        var referenceLabel = new Label
        {
            Content = UiText.Get("GoTo_Reference"),
            Target = _addressBox,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 8, 8, 12)
        };
        Grid.SetRow(referenceLabel, 2);
        root.Children.Add(referenceLabel);

        _addressBox.Text = defaultAddress;
        AutomationProperties.SetName(_addressBox, UiText.Get("GoTo_Reference2"));
        AutomationProperties.SetHelpText(_addressBox, UiText.Get("GoTo_EnterACellReferenceRangeOrDefinedNameToNavigateTo"));
        _addressBox.Margin = new Thickness(0, 8, 0, 12);
        Grid.SetRow(_addressBox, 2);
        Grid.SetColumn(_addressBox, 1);
        root.Children.Add(_addressBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        Grid.SetRow(buttons, 3);
        Grid.SetColumnSpan(buttons, 2);
        root.Children.Add(buttons);

        var special = new Button { Content = UiText.Get("GoTo_Special"), Width = 86, Margin = new Thickness(0, 0, 8, 0) };
        special.Click += (_, _) => OpenSpecialDialog();
        buttons.Children.Add(special);
        var ok = new Button { Content = UiText.Ok, Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = UiText.Cancel, Width = 72, IsCancel = true });

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget() =>
        FocusReferenceInput();

    private void FocusReferenceInput()
    {
        _addressBox.Focus();
        _addressBox.SelectAll();
        Keyboard.Focus(_addressBox);
    }

    private void OpenSpecialDialog()
    {
        var dialog = new GoToSpecialDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        SelectedSpecialKind = dialog.SelectedKind;
        SelectedSpecialOptions = dialog.SelectedOptions;
        DialogResult = true;
    }

    private void Accept()
    {
        if (!TryParseReferenceRange(_addressBox.Text, _sheetId, _definedNames, out var range))
        {
            DialogMessageHelper.ShowWarning(this, UiText.Get("GoTo_ReferenceIsNotValid"), UiText.Get("GoTo_GoTo"));
            FocusReferenceInput();
            return;
        }

        SelectedRange = range;
        SelectedAddress = range.Start;
        SelectedSpecialKind = null;
        SelectedSpecialOptions = null;
        DialogResult = true;
    }

}
