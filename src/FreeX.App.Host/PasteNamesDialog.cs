using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FreeX.App.Presentation.DefinedNames;

namespace FreeX.App.Host;

internal enum PasteNamesDialogAction
{
    None,
    InsertName,
    PasteList
}

internal sealed record PasteNamesDialogResult(PasteNamesDialogAction Action, string? Name)
{
    public static PasteNamesDialogResult None { get; } = new(PasteNamesDialogAction.None, null);
}

internal sealed class PasteNamesDialog : Window
{
    private readonly ListView _namesList = new()
    {
        MinHeight = 150,
        SelectionMode = SelectionMode.Single
    };

    private readonly Button _okButton = new()
    {
        Content = UiText.Ok,
        Width = 72,
        Margin = new Thickness(0, 0, 8, 0),
        IsDefault = true
    };

    private readonly Button _pasteListButton = new()
    {
        Content = UiText.Get("PasteNames_PasteList"),
        Width = 92,
        Margin = new Thickness(0, 0, 8, 0)
    };

    private readonly Button _cancelButton = new()
    {
        Content = UiText.Cancel,
        Width = 72,
        IsCancel = true
    };

    private readonly IReadOnlyList<PasteNamesItem> _items;

    public PasteNamesDialogResult Result { get; private set; } = PasteNamesDialogResult.None;

    public PasteNamesDialog(IReadOnlyList<PasteNamesItem> items)
    {
        _items = items;
        Title = UiText.Get("PasteNames_Title");
        Width = 380;
        Height = 300;
        MinWidth = 340;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;

        _namesList.ItemsSource = items;
        _namesList.View = CreateGridView();
        _namesList.SelectionChanged += (_, _) => SyncButtonState();
        _namesList.MouseDoubleClick += (_, _) => AcceptSelectedName();
        _namesList.KeyDown += NamesList_KeyDown;
        AutomationProperties.SetAutomationId(_namesList, "PasteNamesList");
        AutomationProperties.SetName(_namesList, UiText.Get("PasteNames_NamesAutomationName"));

        ConfigureButton(_okButton, "PasteNamesOkButton");
        ConfigureButton(_pasteListButton, "PasteNamesPasteListButton");
        ConfigureButton(_cancelButton, "PasteNamesCancelButton");
        _okButton.Click += (_, _) => AcceptSelectedName();
        _pasteListButton.Click += (_, _) => AcceptPasteList();

        if (items.Count > 0)
            _namesList.SelectedIndex = 0;

        SyncButtonState();
        Content = CreateContent();
        Loaded += (_, _) => FocusInitialTarget();
    }

    private static GridView CreateGridView()
    {
        var view = new GridView();
        view.Columns.Add(new GridViewColumn
        {
            Header = UiText.Get("NamedRange_Name"),
            Width = 130,
            DisplayMemberBinding = new Binding(nameof(PasteNamesItem.Name))
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = UiText.Get("NamedRange_RefersTo"),
            Width = 210,
            DisplayMemberBinding = new Binding(nameof(PasteNamesItem.RefersTo))
        });
        return view;
    }

    private Grid CreateContent()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new Label
        {
            Content = UiText.Get("PasteNames_NamesLabel"),
            Target = _namesList,
            Padding = new Thickness(0, 0, 0, 4)
        };
        root.Children.Add(label);
        Grid.SetRow(label, 0);

        root.Children.Add(_namesList);
        Grid.SetRow(_namesList, 1);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttons.Children.Add(_okButton);
        buttons.Children.Add(_pasteListButton);
        buttons.Children.Add(_cancelButton);
        root.Children.Add(buttons);
        Grid.SetRow(buttons, 2);

        return root;
    }

    private static void ConfigureButton(Button button, string automationId)
    {
        var content = button.Content as string ?? string.Empty;
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, UiText.CreateAutomationName(content));
    }

    private void NamesList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        AcceptSelectedName();
        e.Handled = true;
    }

    private void AcceptSelectedName()
    {
        if (DefinedNameUiPolicy.PlanPasteNamesSelection(_items, _namesList.SelectedIndex).SelectedItem
            is not { } item)
        {
            return;
        }

        Result = new PasteNamesDialogResult(PasteNamesDialogAction.InsertName, item.Name);
        DialogResult = true;
    }

    private void AcceptPasteList()
    {
        Result = new PasteNamesDialogResult(PasteNamesDialogAction.PasteList, null);
        DialogResult = true;
    }

    private void SyncButtonState()
    {
        var plan = DefinedNameUiPolicy.PlanPasteNamesSelection(_items, _namesList.SelectedIndex);
        _okButton.IsEnabled = plan.CanInsertName;
        _pasteListButton.IsEnabled = plan.CanPasteList;
    }

    private void FocusInitialTarget()
    {
        if (_namesList.Items.Count > 0)
        {
            _namesList.Focus();
            Keyboard.Focus(_namesList);
            return;
        }

        _cancelButton.Focus();
        Keyboard.Focus(_cancelButton);
    }
}
