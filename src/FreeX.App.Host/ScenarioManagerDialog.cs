using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.ScenarioManager;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public enum ScenarioManagerRangeSelectionTarget
{
    ChangingCells,
    ResultCells
}

public sealed record ScenarioManagerRangeSelectionRequest(
    ScenarioManagerRangeSelectionTarget Target,
    string CurrentText,
    bool CollapseDialog = true);

public sealed partial class ScenarioManagerDialog : Window
{
    private readonly ListBox _scenarioList = new();
    private readonly TextBox _newNameBox = new();
    private readonly TextBox _changingCellsBox = new();
    private readonly TextBox _resultCellsBox = new();
    private readonly TextBox _commentBox = new();
    private readonly CheckBox _lockedBox = new() { Content = UiText.Get("ScenarioManager_PreventChanges"), IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _hiddenBox = new() { Content = UiText.Get("ScenarioManager_Hide"), Margin = new Thickness(0, 0, 0, 8) };
    private readonly string _defaultScenarioName;
    private readonly SheetId? _currentSheetId;
    private readonly Func<string, SheetId?>? _resolveSheetIdByName;
    private readonly Action<ScenarioManagerRangeSelectionRequest>? _requestRangeSelection;
    private Button? _addButton;
    private Button? _editButton;
    private Button? _deleteButton;
    private Button? _showButton;

    public ScenarioManagerAction SelectedAction { get; private set; } = ScenarioManagerAction.Show;
    public string? SelectedScenarioName { get; private set; }
    public string? NewScenarioName { get; private set; }
    public string? ChangingCellsText { get; private set; }
    public string? ResultCellsText { get; private set; }
    public string? CommentText { get; private set; }
    public bool ScenarioHidden { get; private set; }
    public bool ScenarioLocked { get; private set; }
    public ScenarioManagerRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public ScenarioManagerDialog(
        Workbook workbook,
        SheetId? currentSheetId = null,
        Func<string, SheetId?>? resolveSheetIdByName = null,
        Action<ScenarioManagerRangeSelectionRequest>? requestRangeSelection = null)
    {
        _currentSheetId = currentSheetId;
        _resolveSheetIdByName = resolveSheetIdByName;
        _requestRangeSelection = requestRangeSelection;
        Title = UiText.Get("ScenarioManager_ScenarioManager");
        Width = 360;
        Height = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _defaultScenarioName = ScenarioManagerPlanner.GetDefaultScenarioName(workbook.Scenarios.Count);

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(body, 0);
        root.Children.Add(body);

        var left = new StackPanel();
        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        left.Children.Add(new Label { Content = UiText.Get("ScenarioManager_Scenarios"), Target = _scenarioList, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        AutomationProperties.SetName(_scenarioList, UiText.Get("ScenarioManager_Scenarios2"));
        AutomationProperties.SetAutomationId(_scenarioList, FreeXAutomationIdCatalog.ScenarioManager.ScenarioList);
        AutomationProperties.SetHelpText(_scenarioList, UiText.Get("ScenarioManager_SelectAScenarioToShowEditOrDelete"));
        _scenarioList.ItemsSource = ScenarioManagerDialogPlanner.BuildItems(workbook);
        _scenarioList.DisplayMemberPath = nameof(ScenarioManagerDialogItem.Name);
        _scenarioList.SelectionChanged += (_, _) => UpdateSelectionState();
        _scenarioList.MouseDoubleClick += ScenarioList_MouseDoubleClick;
        _scenarioList.SelectedIndex = _scenarioList.Items.Count > 0 ? 0 : -1;
        _scenarioList.Height = 118;
        left.Children.Add(_scenarioList);

        var editor = new GroupBox
        {
            Header = UiText.Get("ScenarioManager_AddEditScenario"),
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(8)
        };
        left.Children.Add(editor);

        var fields = new Grid();
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.Content = fields;

        AddField(fields, row: 0, UiText.Get("ScenarioManager_ScenarioName"), _newNameBox);
        _newNameBox.Text = _defaultScenarioName;
        AddReferenceField(
            fields,
            row: 1,
            UiText.Get("ScenarioManager_ChangingCells"),
            _changingCellsBox,
            "Select changing cells range",
            ScenarioManagerRangeSelectionTarget.ChangingCells);
        AddReferenceField(
            fields,
            row: 2,
            UiText.Get("ScenarioManager_ResultCells"),
            _resultCellsBox,
            "Select result cells range",
            ScenarioManagerRangeSelectionTarget.ResultCells);
        AddField(fields, row: 3, UiText.Get("ScenarioManager_Comment"), _commentBox);
        AddCheckBox(fields, row: 4, _lockedBox);
        AddCheckBox(fields, row: 5, _hiddenBox);
        AutomationProperties.SetName(_newNameBox, UiText.Get("ScenarioManager_ScenarioNameAutomationName"));
        AutomationProperties.SetAutomationId(_newNameBox, FreeXAutomationIdCatalog.ScenarioManager.WpfScenarioNameBox);
        AutomationProperties.SetHelpText(_newNameBox, UiText.Get("ScenarioManager_EnterTheScenarioNameToAddOrEdit"));
        AutomationProperties.SetName(_changingCellsBox, UiText.Get("ScenarioManager_ChangingCellsAutomationName"));
        AutomationProperties.SetAutomationId(_changingCellsBox, FreeXAutomationIdCatalog.ScenarioManager.ChangingCellsBox);
        AutomationProperties.SetHelpText(_changingCellsBox, UiText.Get("ScenarioManager_EnterTheWorksheetCellsWhoseValuesChangeInTheScenario"));
        AutomationProperties.SetName(_resultCellsBox, UiText.Get("ScenarioManager_ResultCellsAutomationName"));
        AutomationProperties.SetAutomationId(_resultCellsBox, FreeXAutomationIdCatalog.ScenarioManager.ResultCellsBox);
        AutomationProperties.SetHelpText(_resultCellsBox, UiText.Get("ScenarioManager_EnterOptionalResultCellsToIncludeInAScenarioSummary"));
        AutomationProperties.SetName(_commentBox, UiText.Get("ScenarioManager_CommentAutomationName"));
        AutomationProperties.SetAutomationId(_commentBox, FreeXAutomationIdCatalog.ScenarioManager.CommentBox);
        AutomationProperties.SetHelpText(_commentBox, UiText.Get("ScenarioManager_EnterAnOptionalCommentForTheScenario"));
        AutomationProperties.SetName(_lockedBox, UiText.Get("ScenarioManager_PreventChangesAutomationName"));
        AutomationProperties.SetAutomationId(_lockedBox, FreeXAutomationIdCatalog.ScenarioManager.WpfPreventChangesBox);
        AutomationProperties.SetHelpText(_lockedBox, UiText.Get("ScenarioManager_PreventChangesToTheScenarioWhenTheSheetIsProtected"));
        AutomationProperties.SetName(_hiddenBox, UiText.Get("ScenarioManager_HideAutomationName"));
        AutomationProperties.SetAutomationId(_hiddenBox, FreeXAutomationIdCatalog.ScenarioManager.WpfHideBox);
        AutomationProperties.SetHelpText(_hiddenBox, UiText.Get("ScenarioManager_HideTheScenarioWhenTheSheetIsProtected"));

        var sideButtons = new StackPanel { Margin = new Thickness(10, 20, 0, 0) };
        Grid.SetColumn(sideButtons, 1);
        body.Children.Add(sideButtons);
        _addButton = AddActionButton(sideButtons, UiText.Get("ScenarioManager_Add"), ScenarioManagerAction.Add, isDefault: _scenarioList.Items.Count == 0);
        _editButton = AddActionButton(sideButtons, UiText.Get("ScenarioManager_Edit"), ScenarioManagerAction.Edit, isEnabled: false);
        _deleteButton = AddActionButton(sideButtons, UiText.Get("ScenarioManager_Delete"), ScenarioManagerAction.Delete, isEnabled: false);
        _showButton = AddActionButton(sideButtons, UiText.Get("ScenarioManager_Show"), ScenarioManagerAction.Show, isEnabled: _scenarioList.SelectedItem is not null, isDefault: _scenarioList.SelectedItem is not null);
        AddActionButton(sideButtons, UiText.Get("ScenarioManager_Summary"), ScenarioManagerAction.Report);
        UpdateSelectionState();

        var closeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(closeRow, 1);
        root.Children.Add(closeRow);
        var closeButton = new Button { Content = UiText.Get("ScenarioManager_Close"), Width = 72, IsCancel = true };
        AutomationProperties.SetName(closeButton, UiText.Get("ScenarioManager_CloseAutomationName"));
        AutomationProperties.SetAutomationId(closeButton, FreeXAutomationIdCatalog.ScenarioManager.CloseButton);
        AutomationProperties.SetHelpText(closeButton, UiText.Get("ScenarioManager_CloseTheScenarioManagerDialog"));
        closeRow.Children.Add(closeButton);

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private static void AddField(Grid grid, int row, string label, Control field)
    {
        var text = new Label
        {
            Content = label,
            Target = field,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        field.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private void AddReferenceField(
        Grid grid,
        int row,
        string label,
        TextBox field,
        string automationName,
        ScenarioManagerRangeSelectionTarget target)
    {
        var text = new Label
        {
            Content = label,
            Target = field,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        var editor = DialogReferencePicker.CreateEditor(
            field,
            automationName,
            requestSelection: request => RequestRangeSelection(target, request));
        editor.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
    }

    private static void AddCheckBox(Grid grid, int row, CheckBox checkBox)
    {
        Grid.SetRow(checkBox, row);
        Grid.SetColumn(checkBox, 1);
        grid.Children.Add(checkBox);
    }

    private Button AddActionButton(Panel panel, string label, ScenarioManagerAction action, bool isEnabled = true, bool isDefault = false)
    {
        var button = new Button { Content = label, Width = 82, Margin = new Thickness(0, 0, 0, 6), IsEnabled = isEnabled, IsDefault = isDefault };
        AutomationProperties.SetName(button, GetActionAutomationName(action));
        AutomationProperties.SetAutomationId(button, FreeXAutomationIdCatalog.ScenarioManager.WpfActionButton(action));
        AutomationProperties.SetHelpText(button, GetActionHelpText(action));
        button.Click += (_, _) => Accept(action);
        panel.Children.Add(button);
        return button;
    }

    private static string GetActionAutomationName(ScenarioManagerAction action) =>
        action switch
        {
            ScenarioManagerAction.Add => UiText.Get("ScenarioManager_AddScenarioAutomationName"),
            ScenarioManagerAction.Edit => UiText.Get("ScenarioManager_EditScenarioAutomationName"),
            ScenarioManagerAction.Delete => UiText.Get("ScenarioManager_DeleteScenarioAutomationName"),
            ScenarioManagerAction.Show => UiText.Get("ScenarioManager_ShowScenarioAutomationName"),
            ScenarioManagerAction.Report => UiText.Get("ScenarioManager_ScenarioSummaryAutomationName"),
            _ => UiText.Get("ScenarioManager_SaveScenarioAutomationName")
        };

    private static string GetActionHelpText(ScenarioManagerAction action) =>
        action switch
        {
            ScenarioManagerAction.Add => UiText.Get("ScenarioManager_AddAScenarioUsingTheScenarioFields"),
            ScenarioManagerAction.Edit => UiText.Get("ScenarioManager_EditTheSelectedScenarioUsingTheScenarioFields"),
            ScenarioManagerAction.Delete => UiText.Get("ScenarioManager_DeleteTheSelectedScenario"),
            ScenarioManagerAction.Show => UiText.Get("ScenarioManager_ApplyTheSelectedScenarioToTheWorkbook"),
            ScenarioManagerAction.Report => UiText.Get("ScenarioManager_CreateAScenarioSummaryReport"),
            _ => UiText.Get("ScenarioManager_SaveTheScenarioUsingTheScenarioFields")
        };

    private void FocusInitialKeyboardTarget()
    {
        Control target = _scenarioList.Items.Count > 0 ? _scenarioList : _newNameBox;
        target.Focus();
        Keyboard.Focus(target);
    }

    private void UpdateSelectionState()
    {
        var selected = _scenarioList.SelectedItem as ScenarioManagerDialogItem;
        if (ScenarioManagerDialogPlanner.ProjectSelectionFields(
                selected,
                _newNameBox.Text,
                _defaultScenarioName) is { } fields)
        {
            ApplySelectionFields(fields);
        }

        var hasSelection = selected is not null;
        if (_addButton is not null) _addButton.IsDefault = !hasSelection;
        if (_editButton is not null) _editButton.IsEnabled = hasSelection;
        if (_deleteButton is not null) _deleteButton.IsEnabled = hasSelection;
        if (_showButton is not null)
        {
            _showButton.IsEnabled = hasSelection;
            _showButton.IsDefault = hasSelection;
        }
    }

    private bool AcceptSelectedScenario()
    {
        if (_scenarioList.SelectedItem is null)
        {
            FocusInitialKeyboardTarget();
            return false;
        }

        Accept(ScenarioManagerAction.Show);
        return true;
    }

    private void ScenarioList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AcceptSelectedScenario())
            e.Handled = true;
    }

    private void Accept(ScenarioManagerAction action)
    {
        if (ScenarioManagerDialogPlanner.ValidateAcceptRequest(
                action,
                _newNameBox.Text,
                _changingCellsBox.Text,
                _resultCellsBox.Text,
                _currentSheetId,
                _resolveSheetIdByName) is { } failure)
        {
            var presentation = ScenarioManagerDialogPlanner
                .DescribeValidationFailure(failure);
            ShowInvalidInputWarning(
                presentation.Message.Resolve(UiText.Get, UiText.Format),
                GetValidationTarget(presentation.FocusTarget));
            return;
        }

        ApplyAcceptResult(ScenarioManagerDialogPlanner.ProjectAcceptResult(
            action,
            _scenarioList.SelectedItem as ScenarioManagerDialogItem,
            _newNameBox.Text,
            _changingCellsBox.Text,
            _resultCellsBox.Text,
            _commentBox.Text,
            _lockedBox.IsChecked == true,
            _hiddenBox.IsChecked == true));
        DialogResult = true;
    }

    private void ApplySelectionFields(ScenarioManagerDialogSelectionFields fields)
    {
        _newNameBox.Text = fields.ScenarioName;
        _changingCellsBox.Text = fields.ChangingCellsText;
        _resultCellsBox.Text = fields.ResultCellsText;
        _commentBox.Text = fields.CommentText;
        _lockedBox.IsChecked = fields.Locked;
        _hiddenBox.IsChecked = fields.Hidden;
    }

    private void ApplyAcceptResult(ScenarioManagerDialogAcceptResult result)
    {
        SelectedAction = result.Action;
        SelectedScenarioName = result.SelectedScenarioName;
        NewScenarioName = result.NewScenarioName;
        ChangingCellsText = result.ChangingCellsText;
        ResultCellsText = result.ResultCellsText;
        CommentText = result.CommentText;
        ScenarioLocked = result.Locked;
        ScenarioHidden = result.Hidden;
    }

    public static ScenarioManagerRangeSelectionRequest CreateRangeSelectionRequest(
        ScenarioManagerRangeSelectionTarget target,
        string currentText) =>
        new(target, currentText.Trim(), CollapseDialog: true);

    private void RequestRangeSelection(
        ScenarioManagerRangeSelectionTarget target,
        DialogReferencePickerRequest request)
    {
        RangeSelectionRequest = CreateRangeSelectionRequest(target, request.CurrentText);
        _requestRangeSelection?.Invoke(RangeSelectionRequest);
        FocusRangeSelectionInput(request.Target);
    }

    public void ApplyRangeSelection(ScenarioManagerRangeSelectionTarget target, string rangeText)
    {
        var textBox = target == ScenarioManagerRangeSelectionTarget.ResultCells
            ? _resultCellsBox
            : _changingCellsBox;
        textBox.Text = rangeText;
        FocusRangeSelectionInput(textBox);
    }

    private static void FocusRangeSelectionInput(TextBox textBox)
    {
        DialogFocus.FocusAndSelect(textBox);
    }

    private TextBox GetValidationTarget(ScenarioManagerDialogValidationField field) =>
        field switch
        {
            ScenarioManagerDialogValidationField.ScenarioName => _newNameBox,
            ScenarioManagerDialogValidationField.ChangingCells => _changingCellsBox,
            ScenarioManagerDialogValidationField.ResultCells => _resultCellsBox,
            _ => _newNameBox
        };

    private void ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
    }
}
