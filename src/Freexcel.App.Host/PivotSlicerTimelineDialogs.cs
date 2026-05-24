using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Freexcel.App.Host;

public sealed record InsertSlicerDialogResult(string FieldName, string SlicerName);

public sealed class InsertSlicerDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly TextBox _nameBox = new();

    public InsertSlicerDialogResult Result { get; private set; }

    public InsertSlicerDialog(IEnumerable<string> fieldNames, string? selectedField = null)
    {
        var fields = fieldNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        var field = fields.FirstOrDefault(name => string.Equals(name, selectedField, StringComparison.OrdinalIgnoreCase))
            ?? fields.FirstOrDefault()
            ?? "";
        Result = CreateResult(field, $"{field} Slicer");
        Title = "Insert Slicer";
        Width = 410;
        Height = 270;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateFieldNameContent(fields, field, Result.SlicerName, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static InsertSlicerDialogResult CreateResult(string fieldName, string slicerName) =>
        new(fieldName.Trim(), slicerName.Trim());

    private void Accept()
    {
        Result = CreateResult(_fieldBox.Text, _nameBox.Text);
        DialogResult = true;
    }

    private StackPanel CreateFieldNameContent(IReadOnlyList<string> fields, string field, string name, Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };

        var fieldPanel = PivotDialogLayout.CreateGroupPanel();
        _fieldBox.ItemsSource = fields;
        _fieldBox.Text = field;
        _fieldBox.IsEditable = true;
        PivotDialogLayout.AddLabeledControl(fieldPanel, "_Field to connect", _fieldBox);
        _nameBox.Text = name;
        PivotDialogLayout.AddLabeledControl(fieldPanel, "Slicer _caption", _nameBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Choose fields", fieldPanel));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(accept));
        return stack;
    }

    private void FocusInitialKeyboardTarget()
    {
        _fieldBox.Focus();
        Keyboard.Focus(_fieldBox);
    }
}

public sealed record InsertTimelineDialogResult(string DateFieldName, string TimelineName);

public sealed class InsertTimelineDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly TextBox _nameBox = new();

    public InsertTimelineDialogResult Result { get; private set; }

    public InsertTimelineDialog(IEnumerable<string> fieldNames, string? selectedField = null)
    {
        var fields = fieldNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        var field = fields.FirstOrDefault(name => string.Equals(name, selectedField, StringComparison.OrdinalIgnoreCase))
            ?? fields.FirstOrDefault()
            ?? "";
        Result = CreateResult(field, $"{field} Timeline");
        Title = "Insert Timeline";
        Width = 410;
        Height = 270;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };

        var fieldPanel = PivotDialogLayout.CreateGroupPanel();
        _fieldBox.ItemsSource = fields;
        _fieldBox.Text = field;
        _fieldBox.IsEditable = true;
        PivotDialogLayout.AddLabeledControl(fieldPanel, "_Date field to connect", _fieldBox);
        _nameBox.Text = Result.TimelineName;
        PivotDialogLayout.AddLabeledControl(fieldPanel, "Timeline _caption", _nameBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Choose date fields", fieldPanel));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static InsertTimelineDialogResult CreateResult(string dateFieldName, string timelineName) =>
        new(dateFieldName.Trim(), timelineName.Trim());

    private void Accept()
    {
        Result = CreateResult(_fieldBox.Text, _nameBox.Text);
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _fieldBox.Focus();
        Keyboard.Focus(_fieldBox);
    }
}
