using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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

        Title = "Go To";
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
            Content = "_Go to:",
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

        _historyList.ToolTip = "Recent references and defined names";
        _historyList.MinHeight = 130;
        _historyList.Margin = new Thickness(0, 24, 0, 0);
        _historyList.SelectionChanged += (_, _) =>
        {
            if (_historyList.SelectedItem is string reference)
                _addressBox.Text = reference;
        };
        _historyList.SelectedIndex = 0;
        Grid.SetRow(_historyList, 1);
        Grid.SetColumnSpan(_historyList, 2);
        root.Children.Add(_historyList);

        var referenceLabel = new Label
        {
            Content = "_Reference:",
            Target = _addressBox,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 8, 8, 12)
        };
        Grid.SetRow(referenceLabel, 2);
        root.Children.Add(referenceLabel);

        _addressBox.Text = defaultAddress;
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

        var special = new Button { Content = "S_pecial...", Width = 86, Margin = new Thickness(0, 0, 8, 0) };
        special.Click += (_, _) => OpenSpecialDialog();
        buttons.Children.Add(special);
        var ok = new Button { Content = "_OK", Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(new Button { Content = "_Cancel", Width = 72, IsCancel = true });

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private void FocusInitialKeyboardTarget()
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
            MessageBox.Show(this, "Reference is not valid.", "Go To", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedRange = range;
        SelectedAddress = range.Start;
        SelectedSpecialKind = null;
        SelectedSpecialOptions = null;
        DialogResult = true;
    }

}
