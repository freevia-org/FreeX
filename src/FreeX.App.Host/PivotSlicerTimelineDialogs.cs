using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeX.App.Host;

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
        Result = CreateResult(field, UiText.Format("PivotSlicerTimeline_DefaultSlicerName", field));
        Title = UiText.Get("PivotSlicerTimeline_InsertSlicer");
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

    public static bool TryCreateResult(
        string? fieldName,
        string? slicerName,
        out InsertSlicerDialogResult result,
        out string? error)
    {
        result = CreateResult(fieldName ?? "", slicerName ?? "");
        if (string.IsNullOrWhiteSpace(result.FieldName))
        {
            error = UiText.Get("PivotSlicerTimeline_SelectFieldToConnect");
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.SlicerName))
        {
            error = UiText.Get("PivotSlicerTimeline_EnterSlicerCaption");
            return false;
        }

        error = null;
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_fieldBox.Text, _nameBox.Text, out var result, out var error))
        {
            ShowInvalidInputWarning(error ?? UiText.Get("PivotSlicerTimeline_EnterSlicerOptions"), string.IsNullOrWhiteSpace(_fieldBox.Text) ? _fieldBox : _nameBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private StackPanel CreateFieldNameContent(IReadOnlyList<string> fields, string field, string name, Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };

        var fieldPanel = PivotDialogLayout.CreateGroupPanel();
        _fieldBox.ItemsSource = fields;
        _fieldBox.Text = field;
        _fieldBox.IsEditable = true;
        PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get("PivotSlicerTimeline_FieldToConnectLabel"), _fieldBox);
        _nameBox.Text = name;
        PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get("PivotSlicerTimeline_SlicerCaptionLabel"), _nameBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotSlicerTimeline_ChooseFieldsGroup"), fieldPanel));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(accept));
        return stack;
    }

    private void FocusInitialKeyboardTarget()
    {
        _fieldBox.Focus();
        Keyboard.Focus(_fieldBox);
    }

    private void ShowInvalidInputWarning(string message, Control target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        target.Focus();
        if (target is TextBox textBox)
            textBox.SelectAll();
        Keyboard.Focus(target);
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
        Result = CreateResult(field, UiText.Format("PivotSlicerTimeline_DefaultTimelineName", field));
        Title = UiText.Get("PivotSlicerTimeline_InsertTimeline");
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
        PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get("PivotSlicerTimeline_DateFieldToConnectLabel"), _fieldBox);
        _nameBox.Text = Result.TimelineName;
        PivotDialogLayout.AddLabeledControl(fieldPanel, UiText.Get("PivotSlicerTimeline_TimelineCaptionLabel"), _nameBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotSlicerTimeline_ChooseDateFieldsGroup"), fieldPanel));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static InsertTimelineDialogResult CreateResult(string dateFieldName, string timelineName) =>
        new(dateFieldName.Trim(), timelineName.Trim());

    public static bool TryCreateResult(
        string? dateFieldName,
        string? timelineName,
        out InsertTimelineDialogResult result,
        out string? error)
    {
        result = CreateResult(dateFieldName ?? "", timelineName ?? "");
        if (string.IsNullOrWhiteSpace(result.DateFieldName))
        {
            error = UiText.Get("PivotSlicerTimeline_SelectDateFieldToConnect");
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.TimelineName))
        {
            error = UiText.Get("PivotSlicerTimeline_EnterTimelineCaption");
            return false;
        }

        error = null;
        return true;
    }

    private void Accept()
    {
        if (!TryCreateResult(_fieldBox.Text, _nameBox.Text, out var result, out var error))
        {
            ShowInvalidInputWarning(error ?? UiText.Get("PivotSlicerTimeline_EnterTimelineOptions"), string.IsNullOrWhiteSpace(_fieldBox.Text) ? _fieldBox : _nameBox);
            return;
        }

        Result = result;
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _fieldBox.Focus();
        Keyboard.Focus(_fieldBox);
    }

    private void ShowInvalidInputWarning(string message, Control target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        target.Focus();
        if (target is TextBox textBox)
            textBox.SelectAll();
        Keyboard.Focus(target);
    }
}
