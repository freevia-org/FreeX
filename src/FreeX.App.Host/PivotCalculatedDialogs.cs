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
    private readonly ComboBox _existingBox = new();
    private readonly PivotCalculatedFieldSession _session;
    private readonly bool _managesExistingFields;

    public PivotCalculatedFieldResult Result { get; private set; }

    /// <summary>
    /// The add/modify or delete the user confirmed when the dialog was opened for a PivotTable
    /// (see the <c>pivotTable</c> constructor argument); null for the draft-only form.
    /// </summary>
    public PivotCalculatedFieldSubmission? Submission { get; private set; }

    /// <param name="pivotTable">
    /// When supplied, the dialog works like Excel's Insert Calculated Field: it lists the PivotTable's existing
    /// calculated fields so one can be modified, offers Delete, and starts new fields at "= ". The caller applies
    /// <see cref="CreateCommitPlan"/>. Without it the dialog is a plain name/formula draft editor.
    /// </param>
    public PivotCalculatedFieldDialog(
        string name = "",
        string formula = "",
        IEnumerable<string>? fieldNames = null,
        PivotTableModel? pivotTable = null)
    {
        var text = PivotCalculatedFieldSessionText.Default with
        {
            EmptyNameMessage = UiText.Get("PivotCalculated_EnterCalculatedFieldName"),
            EmptyFormulaMessage = UiText.Get("PivotCalculated_EnterCalculatedFieldFormula"),
            SavedStatusFormat = UiText.Get("PivotCalcField_Saved"),
            DeletedStatusFormat = UiText.Get("PivotCalcField_Deleted")
        };
        _managesExistingFields = pivotTable is not null;
        _session = pivotTable is null
            ? PivotCalculatedFieldSession.CreateDraft(name, formula, fieldNames ?? [], text)
            : PivotCalculatedFieldSession.Create(pivotTable, (fieldNames ?? []).ToList(), text);
        Result = CreateResult(name, formula);
        Title = UiText.Get("PivotCalculated_CalculatedField");
        Width = 480;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        _nameBox.Text = _managesExistingFields ? _session.Name : Result.Name;
        _formulaBox.Text = _managesExistingFields ? _session.Formula : Result.Formula;
        _fieldList.ItemsSource = _session.FieldReferences;
        if (_session.FieldReferences.Count > 0)
            _fieldList.SelectedIndex = 0;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
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
        if (_managesExistingFields)
        {
            _existingBox.Items.Add(UiText.Get("PivotCalcField_ExistingNone"));
            foreach (var existingName in _session.ExistingNames)
                _existingBox.Items.Add(existingName);
            _existingBox.SelectedIndex = 0;
            _existingBox.SelectionChanged += (_, _) => LoadExistingField();
            AutomationProperties.SetName(_existingBox, UiText.Get("PivotCalcField_ExistingLabel"));
            PivotDialogLayout.AddLabeledControl(formulaPanel, UiText.Get("PivotCalcField_ExistingLabel"), _existingBox);
        }

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

        var buttonRow = PivotDialogLayout.CreateButtonRow(Accept);
        if (_managesExistingFields)
        {
            var deleteButton = new Button
            {
                Content = UiText.Get("PivotCalcField_Delete"),
                MinWidth = 76,
                Margin = new Thickness(0, 0, 8, 0)
            };
            deleteButton.Click += (_, _) => DeleteField();
            buttonRow.Children.Insert(1, deleteButton);
        }

        stack.Children.Add(buttonRow);
        return stack;
    }

    private void LoadExistingField()
    {
        var selectedName = _existingBox.SelectedIndex > 0
            ? _session.ExistingNames[_existingBox.SelectedIndex - 1]
            : null;
        var draft = _session.SelectExisting(selectedName);
        _nameBox.Text = draft.Name;
        _formulaBox.Text = draft.Formula;
    }

    /// <summary>
    /// The calculated-field list to apply for <see cref="Submission"/> (add/modify or delete) and its status
    /// text, or null when the dialog was not confirmed or was opened without a PivotTable.
    /// </summary>
    public PivotCalculatedFieldCommitPlan? CreateCommitPlan() =>
        _managesExistingFields && Submission is not null ? _session.Commit(Submission) : null;

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
        Submission = plan.Submission;
        DialogResult = true;
    }

    private void DeleteField()
    {
        var plan = _session.PlanDelete(_nameBox.Text);
        if (!plan.Success)
        {
            ShowInvalidInputWarning(plan.Issue!.Message, _nameBox);
            return;
        }

        Submission = plan.Submission;
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
}

public sealed class PivotCalculatedItemDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly ListBox _fieldList = new() { Height = 80 };
    private readonly ListBox _itemList = new() { Height = 80 };
    private readonly TextBox _nameBox = new();
    private readonly TextBox _formulaBox = new();
    private readonly ComboBox _existingBox = new();
    private readonly PivotCalculatedItemSession _session;
    private readonly bool _managesExistingItems;
    private bool _loading = true;
    private bool _reloadingExistingItems;

    public PivotCalculatedItemResult Result { get; private set; }

    /// <summary>
    /// The add/modify or delete the user confirmed when the dialog was opened for a PivotTable
    /// (see the <c>pivotTable</c> constructor argument); null for the draft-only form.
    /// </summary>
    public PivotCalculatedItemSubmission? Submission { get; private set; }

    /// <summary>
    /// Set when the PivotTable has no row or column field a calculated item could attach to; the caller shows
    /// the message instead of the dialog. Always null for the draft-only form.
    /// </summary>
    public PivotCalculatedWorkflowIssue? OpenIssue => _managesExistingItems ? _session.OpenIssue : null;

    /// <param name="pivotTable">
    /// When supplied, the dialog works like Excel's Insert Calculated Item: the source fields are the
    /// PivotTable's row and column fields, the selected field's existing calculated items can be modified,
    /// Delete is offered, and new items start at "= ". The caller applies <see cref="CreateCommitPlan"/>.
    /// Without it the dialog is a plain name/formula draft editor.
    /// </param>
    public PivotCalculatedItemDialog(
        IEnumerable<string> fieldNames,
        int selectedSourceFieldIndex = 0,
        string name = "",
        string formula = "",
        IReadOnlyDictionary<int, IEnumerable<string>>? itemNamesBySourceFieldIndex = null,
        PivotTableModel? pivotTable = null)
    {
        var text = PivotCalculatedItemSessionText.Default with
        {
            EmptyNameMessage = UiText.Get("PivotCalculated_EnterCalculatedItemName"),
            EmptyFormulaMessage = UiText.Get("PivotCalculated_EnterCalculatedItemFormula"),
            NoSourceFieldMessage = UiText.Get("PivotCalcItem_NoField"),
            SavedStatusFormat = UiText.Get("PivotCalcItem_Saved"),
            DeletedStatusFormat = UiText.Get("PivotCalcItem_Deleted")
        };
        _managesExistingItems = pivotTable is not null;
        if (pivotTable is null)
        {
            _session = PivotCalculatedItemSession.CreateDraft(
                fieldNames,
                selectedSourceFieldIndex,
                name,
                formula,
                itemNamesBySourceFieldIndex,
                text);
        }
        else
        {
            _session = PivotCalculatedItemSession.Create(
                pivotTable,
                fieldNames.ToList(),
                text,
                itemNamesBySourceFieldIndex: itemNamesBySourceFieldIndex);
            _session.SelectSourceField(selectedSourceFieldIndex, startNew: true);
        }

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
        _loading = false;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
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
        _fieldBox.SelectionChanged += (_, _) => OnSourceFieldChanged();
        PivotDialogLayout.AddLabeledControl(itemPanel, UiText.Get("PivotCalculated_SourceFieldLabel"), _fieldBox);
        if (_managesExistingItems)
        {
            _existingBox.SelectionChanged += (_, _) => LoadExistingItem();
            AutomationProperties.SetName(_existingBox, UiText.Get("PivotCalcItem_ExistingLabel"));
            PivotDialogLayout.AddLabeledControl(itemPanel, UiText.Get("PivotCalcItem_ExistingLabel"), _existingBox);
        }

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

        var buttonRow = PivotDialogLayout.CreateButtonRow(Accept);
        if (_managesExistingItems)
        {
            var deleteButton = new Button
            {
                Content = UiText.Get("PivotCalcItem_Delete"),
                MinWidth = 76,
                Margin = new Thickness(0, 0, 8, 0)
            };
            deleteButton.Click += (_, _) => DeleteItem();
            buttonRow.Children.Insert(1, deleteButton);
        }

        stack.Children.Add(buttonRow);
        return stack;
    }

    /// <summary>
    /// The calculated-item list to apply for <see cref="Submission"/> (add/modify or delete) and its status
    /// text, or null when the dialog was not confirmed or was opened without a PivotTable.
    /// </summary>
    public PivotCalculatedItemCommitPlan? CreateCommitPlan() =>
        _managesExistingItems && Submission is not null ? _session.Commit(Submission) : null;

    private void OnSourceFieldChanged()
    {
        // Switching the source field in the PivotTable form starts a new item for that field.
        if (_managesExistingItems && !_loading && _fieldBox.SelectedItem is PivotCalculatedItemPlanner.CalculatedItemField field)
        {
            var draft = _session.SelectSourceField(field.SourceFieldIndex, startNew: true);
            _nameBox.Text = draft.Name;
            _formulaBox.Text = draft.Formula;
        }

        RefreshItemList();
    }

    private void ReloadExistingItems()
    {
        if (!_managesExistingItems)
            return;

        _reloadingExistingItems = true;
        try
        {
            _existingBox.Items.Clear();
            _existingBox.Items.Add(UiText.Get("PivotCalcItem_ExistingNone"));
            foreach (var existingName in _session.ExistingNames)
                _existingBox.Items.Add(existingName);
            _existingBox.SelectedIndex = 0;
        }
        finally
        {
            _reloadingExistingItems = false;
        }
    }

    private void LoadExistingItem()
    {
        if (_reloadingExistingItems)
            return;

        var selectedName = _existingBox.SelectedIndex > 0
            ? _session.ExistingNames[_existingBox.SelectedIndex - 1]
            : null;
        var draft = _session.SelectExisting(selectedName);
        _nameBox.Text = draft.Name;
        _formulaBox.Text = draft.Formula;
    }

    private void DeleteItem()
    {
        SyncSelectedSourceField();
        var plan = _session.PlanDelete(_nameBox.Text);
        if (!plan.Success)
        {
            ShowInvalidInputWarning(plan.Issue!.Message, _nameBox);
            return;
        }

        Submission = plan.Submission;
        DialogResult = true;
    }

    private void Load(PivotCalculatedItemResult result)
    {
        _fieldBox.SelectedItem = _session.Fields.FirstOrDefault(
            field => field.SourceFieldIndex == result.SourceFieldIndex) ?? _session.Fields.FirstOrDefault();
        _nameBox.Text = _managesExistingItems ? _session.Name : result.Name;
        _formulaBox.Text = _managesExistingItems ? _session.Formula : result.Formula;
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
        Submission = plan.Submission;
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
        ReloadExistingItems();
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
}
