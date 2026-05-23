using System.Windows;
using System.Windows.Controls;

using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record PivotCalculatedFieldDialogResult(string Name, string Formula)
{
    public PivotCalculatedFieldModel ToModel() => new(Name, Formula);
}

public sealed class PivotCalculatedFieldDialog : Window
{
    private readonly TextBox _nameBox = new();
    private readonly TextBox _formulaBox = new();
    private readonly ListBox _fieldList = new() { Height = 92 };
    private readonly IReadOnlyList<string> _fields;

    public PivotCalculatedFieldDialogResult Result { get; private set; }

    public PivotCalculatedFieldDialog(string name = "", string formula = "", IEnumerable<string>? fieldNames = null)
    {
        _fields = CreateFieldNames(fieldNames ?? []);
        Result = CreateResult(name, formula);
        Title = "Calculated Field";
        Width = 480;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        _nameBox.Text = Result.Name;
        _formulaBox.Text = Result.Formula;
        _fieldList.ItemsSource = _fields;
        if (_fields.Count > 0)
            _fieldList.SelectedIndex = 0;
    }

    public static PivotCalculatedFieldDialogResult CreateResult(string name, string formula) =>
        new(name.Trim(), formula.Trim());

    public static string InsertFormulaReference(string formula, string reference, int selectionStart, int selectionLength) =>
        PivotFormulaInsertion.InsertFormulaToken(formula, reference, selectionStart, selectionLength);

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        var formulaPanel = PivotDialogLayout.CreateGroupPanel();
        AddTextBox(formulaPanel, "_Name", _nameBox);
        AddTextBox(formulaPanel, "_Formula:", _formulaBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Name and formula", formulaPanel));

        var fieldsPanel = PivotDialogLayout.CreateGroupPanel();
        PivotDialogLayout.AddLabeledControl(fieldsPanel, "Available _fields", _fieldList);
        var insertFieldButton = new Button
        {
            Content = "Insert _Field",
            Width = 110,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        insertFieldButton.Click += (_, _) => InsertSelectedField();
        _fieldList.MouseDoubleClick += (_, _) => InsertSelectedField();
        fieldsPanel.Children.Add(insertFieldButton);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Fields", fieldsPanel));

        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return stack;
    }

    private void Accept()
    {
        Result = CreateResult(_nameBox.Text, _formulaBox.Text);
        DialogResult = true;
    }

    private void InsertSelectedField()
    {
        if (_fieldList.SelectedItem is not string fieldName)
            return;

        InsertFormulaText(fieldName);
    }

    private void InsertFormulaText(string reference)
    {
        var inserted = InsertFormulaReference(
            _formulaBox.Text,
            reference,
            _formulaBox.SelectionStart,
            _formulaBox.SelectionLength);
        var caretIndex = Math.Min(inserted.Length, _formulaBox.SelectionStart + reference.Length);
        _formulaBox.Text = inserted;
        _formulaBox.Focus();
        _formulaBox.SelectionStart = caretIndex;
        _formulaBox.SelectionLength = 0;
    }

    private static IReadOnlyList<string> CreateFieldNames(IEnumerable<string> fieldNames) =>
        fieldNames
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void AddTextBox(Panel stack, string label, TextBox textBox)
    {
        PivotDialogLayout.AddLabeledControl(stack, label, textBox);
    }
}

public sealed record PivotCalculatedItemDialogResult(
    string SourceFieldName,
    int SourceFieldIndex,
    string Name,
    string Formula)
{
    public PivotCalculatedItemModel ToModel() => new(SourceFieldIndex, Name, Formula);
}

public sealed class PivotCalculatedItemDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly ListBox _fieldList = new() { Height = 80 };
    private readonly ListBox _itemList = new() { Height = 80 };
    private readonly TextBox _nameBox = new();
    private readonly TextBox _formulaBox = new();
    private readonly IReadOnlyList<PivotCalculatedItemSourceFieldOption> _fields;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<string>> _itemsBySourceFieldIndex;

    public PivotCalculatedItemDialogResult Result { get; private set; }

    public PivotCalculatedItemDialog(
        IEnumerable<string> fieldNames,
        int selectedSourceFieldIndex = 0,
        string name = "",
        string formula = "",
        IReadOnlyDictionary<int, IEnumerable<string>>? itemNamesBySourceFieldIndex = null)
    {
        _fields = CreateFieldOptions(fieldNames);
        _itemsBySourceFieldIndex = CreateItemOptions(itemNamesBySourceFieldIndex);
        var selectedField = _fields.FirstOrDefault(field => field.Index == Math.Max(0, selectedSourceFieldIndex))
            ?? _fields.FirstOrDefault();
        Result = CreateResult(selectedField?.Name ?? "", selectedField?.Index ?? 0, name, formula);

        Title = "Calculated Item";
        Width = 500;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static PivotCalculatedItemDialogResult CreateResult(
        string sourceFieldName,
        int sourceFieldIndex,
        string name,
        string formula) =>
        new(sourceFieldName.Trim(), Math.Max(0, sourceFieldIndex), name.Trim(), formula.Trim());

    public static string InsertFormulaReference(string formula, string reference, int selectionStart, int selectionLength) =>
        PivotFormulaInsertion.InsertFormulaToken(formula, reference, selectionStart, selectionLength);

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        var itemPanel = PivotDialogLayout.CreateGroupPanel();
        _fieldBox.ItemsSource = _fields;
        _fieldBox.DisplayMemberPath = nameof(PivotCalculatedItemSourceFieldOption.Name);
        _fieldBox.SelectionChanged += (_, _) => RefreshItemList();
        PivotDialogLayout.AddLabeledControl(itemPanel, "Source _field", _fieldBox);
        AddTextBox(itemPanel, "_Name", _nameBox);
        AddTextBox(itemPanel, "Item _formula", _formulaBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Field and item", itemPanel));

        var insertPanel = PivotDialogLayout.CreateGroupPanel();
        _fieldList.ItemsSource = _fields;
        _fieldList.DisplayMemberPath = nameof(PivotCalculatedItemSourceFieldOption.Name);
        _fieldList.MouseDoubleClick += (_, _) => InsertSelectedField();
        PivotDialogLayout.AddLabeledControl(insertPanel, "Available _fields", _fieldList);
        insertPanel.Children.Add(CreateInsertButton("Insert _Field", InsertSelectedField));
        PivotDialogLayout.AddLabeledControl(insertPanel, "Available _items", _itemList);
        _itemList.MouseDoubleClick += (_, _) => InsertSelectedItem();
        insertPanel.Children.Add(CreateInsertButton("Insert _Item", InsertSelectedItem));
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Insert into formula", insertPanel));

        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return stack;
    }

    private void Load(PivotCalculatedItemDialogResult result)
    {
        _fieldBox.SelectedItem = _fields.FirstOrDefault(field => field.Index == result.SourceFieldIndex)
            ?? _fields.FirstOrDefault();
        _nameBox.Text = result.Name;
        _formulaBox.Text = result.Formula;
        _fieldList.SelectedItem = _fieldBox.SelectedItem;
        if (_fieldList.SelectedItem is null && _fields.Count > 0)
            _fieldList.SelectedIndex = 0;
        RefreshItemList();
    }

    private void Accept()
    {
        var selectedField = _fieldBox.SelectedItem as PivotCalculatedItemSourceFieldOption;
        Result = CreateResult(
            selectedField?.Name ?? "",
            selectedField?.Index ?? 0,
            _nameBox.Text,
            _formulaBox.Text);
        DialogResult = true;
    }

    private void RefreshItemList()
    {
        var selectedField = _fieldBox.SelectedItem as PivotCalculatedItemSourceFieldOption;
        var items = selectedField is not null &&
            _itemsBySourceFieldIndex.TryGetValue(selectedField.Index, out var sourceItems)
                ? sourceItems
                : [];
        _itemList.ItemsSource = items;
        _itemList.SelectedIndex = items.Count > 0 ? 0 : -1;
    }

    private void InsertSelectedField()
    {
        var selectedField = _fieldList.SelectedItem as PivotCalculatedItemSourceFieldOption
            ?? _fieldBox.SelectedItem as PivotCalculatedItemSourceFieldOption;
        if (selectedField is null)
            return;

        InsertFormulaText(selectedField.Name);
    }

    private void InsertSelectedItem()
    {
        if (_itemList.SelectedItem is not string itemName)
            return;

        InsertFormulaText(itemName);
    }

    private void InsertFormulaText(string reference)
    {
        var inserted = InsertFormulaReference(
            _formulaBox.Text,
            reference,
            _formulaBox.SelectionStart,
            _formulaBox.SelectionLength);
        var caretIndex = Math.Min(inserted.Length, _formulaBox.SelectionStart + reference.Length);
        _formulaBox.Text = inserted;
        _formulaBox.Focus();
        _formulaBox.SelectionStart = caretIndex;
        _formulaBox.SelectionLength = 0;
    }

    private static IReadOnlyList<PivotCalculatedItemSourceFieldOption> CreateFieldOptions(IEnumerable<string> fieldNames) =>
        fieldNames
            .Select((name, index) => new PivotCalculatedItemSourceFieldOption(index, name.Trim()))
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToList();

    private static IReadOnlyDictionary<int, IReadOnlyList<string>> CreateItemOptions(
        IReadOnlyDictionary<int, IEnumerable<string>>? itemNamesBySourceFieldIndex) =>
        itemNamesBySourceFieldIndex?.ToDictionary(
            pair => Math.Max(0, pair.Key),
            pair => (IReadOnlyList<string>)pair.Value
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()) ?? new Dictionary<int, IReadOnlyList<string>>();

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

    private sealed record PivotCalculatedItemSourceFieldOption(int Index, string Name);
}

file static class PivotFormulaInsertion
{
    public static string InsertFormulaToken(string formula, string reference, int selectionStart, int selectionLength)
    {
        var safeFormula = formula ?? "";
        var safeReference = reference ?? "";
        var start = Math.Clamp(selectionStart, 0, safeFormula.Length);
        var length = Math.Clamp(selectionLength, 0, safeFormula.Length - start);
        return safeFormula.Remove(start, length).Insert(start, safeReference);
    }
}
