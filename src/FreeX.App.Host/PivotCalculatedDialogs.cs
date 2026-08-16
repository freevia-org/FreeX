using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;
using PivotCalculatedFieldResult = FreeX.App.Presentation.PivotUI.PivotCalculatedFieldPlanner.PivotCalculatedFieldResult;
using PivotCalculatedItemResult = FreeX.App.Presentation.PivotUI.PivotCalculatedItemPlanner.PivotCalculatedItemResult;

namespace FreeX.App.Host;

public sealed class PivotCalculatedFieldDialog : Window
{
    private readonly TextBox _nameBox = new();
    private readonly TextBox _formulaBox = new();
    private readonly ListBox _fieldList = new() { Height = 92 };
    private readonly PivotCalculatedFieldSession _session;

    public PivotCalculatedFieldResult Result { get; private set; }

    public PivotCalculatedFieldDialog(string name = "", string formula = "", IEnumerable<string>? fieldNames = null)
    {
        var text = PivotCalculatedFieldSessionText.Default with
        {
            EmptyNameMessage = UiText.Get("PivotCalculated_EnterCalculatedFieldName"),
            EmptyFormulaMessage = UiText.Get("PivotCalculated_EnterCalculatedFieldFormula")
        };
        _session = PivotCalculatedFieldSession.CreateDraft(name, formula, fieldNames ?? [], text);
        Result = CreateResult(name, formula);
        Title = UiText.Get("PivotCalculated_CalculatedField");
        Width = 480;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        _nameBox.Text = Result.Name;
        _formulaBox.Text = Result.Formula;
        _fieldList.ItemsSource = _session.FieldReferences;
        if (_session.FieldReferences.Count > 0)
            _fieldList.SelectedIndex = 0;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
            ApplyAutomationNames();
    }

    public static PivotCalculatedFieldResult CreateResult(string name, string formula)
    {
        var draft = PivotCalculatedDraft.Normalize(name, formula);
        return new PivotCalculatedFieldResult(draft.Name, draft.Formula);
    }

    public static string InsertFormulaReference(string formula, string reference, int selectionStart, int selectionLength) =>
        PivotCalculatedFieldPlanner.InsertReference(formula, reference, selectionStart, selectionLength).Formula;

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        var formulaPanel = PivotDialogLayout.CreateGroupPanel();
        AddTextBox(formulaPanel, UiText.Get("PivotCalculated_NameLabel"), _nameBox);
        AddTextBox(formulaPanel, UiText.Get("PivotCalculated_FormulaLabel"), _formulaBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotCalculated_NameAndFormulaGroup"), formulaPanel));

        var fieldsPanel = PivotDialogLayout.CreateGroupPanel();
        AutomationProperties.SetName(_fieldList, UiText.Get("PivotCalculated_AvailableFields"));
        PivotDialogLayout.AddLabeledControl(fieldsPanel, UiText.Get("PivotCalculated_AvailableFieldsLabel"), _fieldList);
        var insertFieldButton = new Button
        {
            Content = UiText.Get("PivotCalculated_InsertField"),
            Width = 110,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        insertFieldButton.Click += (_, _) => InsertSelectedField();
        _fieldList.MouseDoubleClick += FieldList_MouseDoubleClick;
        fieldsPanel.Children.Add(insertFieldButton);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotCalculated_FieldsGroup"), fieldsPanel));

        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return stack;
    }

    private void Accept()
    {
        var plan = _session.PlanSave(_nameBox.Text, _formulaBox.Text);
        if (!plan.Success)
        {
            var issue = plan.Issue!;
            ShowInvalidInputWarning(
                issue.Message,
                issue.Target == PivotCalculatedInputTarget.Formula ? _formulaBox : _nameBox);
            return;
        }

        var result = plan.Submission!.Result!;
        Result = result;
        DialogResult = true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return false;
    }

    private void FocusInitialKeyboardTarget()
    {
        _nameBox.Focus();
        _nameBox.SelectAll();
        Keyboard.Focus(_nameBox);
    }

    private void FieldList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (InsertSelectedField())
            e.Handled = true;
    }

    private bool InsertSelectedField()
    {
        if (_fieldList.SelectedItem is not string fieldName)
            return false;

        InsertFormulaText(fieldName);
        return true;
    }

    private void InsertFormulaText(string reference)
    {
        _session.UpdateDraft(_nameBox.Text, _formulaBox.Text);
        var inserted = _session.InsertReference(
            reference,
            _formulaBox.SelectionStart,
            _formulaBox.SelectionLength);
        _formulaBox.Text = inserted.Formula;
        _formulaBox.Focus();
        _formulaBox.SelectionStart = inserted.CaretIndex;
        _formulaBox.SelectionLength = 0;
    }

    private static void AddTextBox(Panel stack, string label, TextBox textBox)
    {
        PivotDialogLayout.AddLabeledControl(stack, label, textBox);
    }

    /// <summary>
    /// Screen-reader names for this dialog's controls. Ported from the abandoned
    /// codex/dialog-parity-loop branch, whose paths predate the Freexcel -> FreeX rename.
    /// </summary>
    private void ApplyAutomationNames()
    {
        AutomationProperties.SetName(_nameBox, "Calculated field name");
        AutomationProperties.SetName(_formulaBox, "Calculated field formula");
        AutomationProperties.SetName(_nameBox, "Calculated item name");
        AutomationProperties.SetName(_formulaBox, "Calculated item formula");
    }
}

public sealed class PivotCalculatedItemDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly ListBox _fieldList = new() { Height = 80 };
    private readonly ListBox _itemList = new() { Height = 80 };
    private readonly TextBox _nameBox = new();
    private readonly TextBox _formulaBox = new();
    private readonly PivotCalculatedItemSession _session;

    public PivotCalculatedItemResult Result { get; private set; }

    public PivotCalculatedItemDialog(
        IEnumerable<string> fieldNames,
        int selectedSourceFieldIndex = 0,
        string name = "",
        string formula = "",
        IReadOnlyDictionary<int, IEnumerable<string>>? itemNamesBySourceFieldIndex = null)
    {
        var text = PivotCalculatedItemSessionText.Default with
        {
            EmptyNameMessage = UiText.Get("PivotCalculated_EnterCalculatedItemName"),
            EmptyFormulaMessage = UiText.Get("PivotCalculated_EnterCalculatedItemFormula")
        };
        _session = PivotCalculatedItemSession.CreateDraft(
            fieldNames,
            selectedSourceFieldIndex,
            name,
            formula,
            itemNamesBySourceFieldIndex,
            text);
        Result = CreateResult(
            _session.SelectedSourceFieldIndex,
            name,
            formula);

        Title = UiText.Get("PivotCalculated_CalculatedItem");
        Width = 500;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
            ApplyAutomationNames();
    }

    public static PivotCalculatedItemResult CreateResult(
        int sourceFieldIndex,
        string name,
        string formula)
    {
        var draft = PivotCalculatedDraft.Normalize(name, formula);
        return new PivotCalculatedItemResult(
            Math.Max(0, sourceFieldIndex),
            draft.Name,
            draft.Formula);
    }

    public static string InsertFormulaReference(string formula, string reference, int selectionStart, int selectionLength) =>
        PivotCalculatedItemPlanner.InsertReference(formula, reference, selectionStart, selectionLength).Formula;

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        var itemPanel = PivotDialogLayout.CreateGroupPanel();
        _fieldBox.ItemsSource = _session.Fields;
        _fieldBox.DisplayMemberPath = nameof(PivotCalculatedItemPlanner.CalculatedItemField.Caption);
        _fieldBox.SelectionChanged += (_, _) => RefreshItemList();
        PivotDialogLayout.AddLabeledControl(itemPanel, UiText.Get("PivotCalculated_SourceFieldLabel"), _fieldBox);
        AddTextBox(itemPanel, UiText.Get("PivotCalculated_NameLabel"), _nameBox);
        AddTextBox(itemPanel, UiText.Get("PivotCalculated_ItemFormulaLabel"), _formulaBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotCalculated_FieldAndItemGroup"), itemPanel));

        var insertPanel = PivotDialogLayout.CreateGroupPanel();
        _fieldList.ItemsSource = _session.Fields;
        _fieldList.DisplayMemberPath = nameof(PivotCalculatedItemPlanner.CalculatedItemField.Caption);
        _fieldList.MouseDoubleClick += FieldList_MouseDoubleClick;
        AutomationProperties.SetName(_fieldList, UiText.Get("PivotCalculated_AvailableFields"));
        PivotDialogLayout.AddLabeledControl(insertPanel, UiText.Get("PivotCalculated_AvailableFieldsLabel"), _fieldList);
        insertPanel.Children.Add(CreateInsertButton(UiText.Get("PivotCalculated_InsertField"), () => InsertSelectedField()));
        AutomationProperties.SetName(_itemList, UiText.Get("PivotCalculated_AvailableItems"));
        PivotDialogLayout.AddLabeledControl(insertPanel, UiText.Get("PivotCalculated_AvailableItemsLabel"), _itemList);
        _itemList.MouseDoubleClick += ItemList_MouseDoubleClick;
        insertPanel.Children.Add(CreateInsertButton(UiText.Get("PivotCalculated_InsertItem"), () => InsertSelectedItem()));
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotCalculated_InsertIntoFormulaGroup"), insertPanel));

        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return stack;
    }

    private void Load(PivotCalculatedItemResult result)
    {
        _fieldBox.SelectedItem = _session.Fields.FirstOrDefault(
            field => field.SourceFieldIndex == result.SourceFieldIndex) ?? _session.Fields.FirstOrDefault();
        _nameBox.Text = result.Name;
        _formulaBox.Text = result.Formula;
        _fieldList.SelectedItem = _fieldBox.SelectedItem;
        if (_fieldList.SelectedItem is null && _session.Fields.Count > 0)
            _fieldList.SelectedIndex = 0;
        RefreshItemList();
    }

    private void Accept()
    {
        SyncSelectedSourceField();
        var plan = _session.PlanSave(_nameBox.Text, _formulaBox.Text);
        if (!plan.Success)
        {
            var issue = plan.Issue!;
            ShowInvalidInputWarning(
                issue.Message,
                issue.Target == PivotCalculatedInputTarget.Formula ? _formulaBox : _nameBox);
            return;
        }

        var result = plan.Submission!.Result!;
        Result = result;
        DialogResult = true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return false;
    }

    private void FocusInitialKeyboardTarget()
    {
        _nameBox.Focus();
        _nameBox.SelectAll();
        Keyboard.Focus(_nameBox);
    }

    private void RefreshItemList()
    {
        SyncSelectedSourceField();
        _itemList.ItemsSource = _session.ItemReferences;
        _itemList.SelectedIndex = _session.ItemReferences.Count > 0 ? 0 : -1;
    }

    private void SyncSelectedSourceField()
    {
        if (_fieldBox.SelectedItem is PivotCalculatedItemPlanner.CalculatedItemField selectedField)
            _session.SelectSourceField(selectedField.SourceFieldIndex);
    }

    private void FieldList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (InsertSelectedField())
            e.Handled = true;
    }

    private void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (InsertSelectedItem())
            e.Handled = true;
    }

    private bool InsertSelectedField()
    {
        var selectedField = _fieldList.SelectedItem as PivotCalculatedItemPlanner.CalculatedItemField
            ?? _fieldBox.SelectedItem as PivotCalculatedItemPlanner.CalculatedItemField;
        if (selectedField is null)
            return false;

        InsertFormulaText(selectedField.Caption);
        return true;
    }

    private bool InsertSelectedItem()
    {
        if (_itemList.SelectedItem is not string itemName)
            return false;

        InsertFormulaText(itemName);
        return true;
    }

    private void InsertFormulaText(string reference)
    {
        _session.UpdateDraft(_nameBox.Text, _formulaBox.Text);
        var inserted = _session.InsertReference(
            reference,
            _formulaBox.SelectionStart,
            _formulaBox.SelectionLength);
        _formulaBox.Text = inserted.Formula;
        _formulaBox.Focus();
        _formulaBox.SelectionStart = inserted.CaretIndex;
        _formulaBox.SelectionLength = 0;
    }

    private static Button CreateInsertButton(string content, Action action)
    {
        var button = new Button
        {
            Content = content,
            Width = 110,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static void AddTextBox(Panel stack, string label, TextBox textBox)
    {
        PivotDialogLayout.AddLabeledControl(stack, label, textBox);
    }

    /// <summary>
    /// Screen-reader names for this dialog's controls. Ported from the abandoned
    /// codex/dialog-parity-loop branch, whose paths predate the Freexcel -> FreeX rename.
    /// </summary>
    private void ApplyAutomationNames()
    {
        AutomationProperties.SetName(_fieldBox, "Source field");
    }
}
