using System.Windows.Controls;

namespace Free.Shared.Shell;

public static class ComboBoxTextEditingExtensions
{
    public static void SelectAll(this ComboBox comboBox)
    {
        if (TryGetEditableTextBox(comboBox, out var textBox))
            textBox.SelectAll();
    }

    public static bool IsEditableTextUndoEnabled(this ComboBox comboBox) =>
        !TryGetEditableTextBox(comboBox, out var textBox) || textBox.IsUndoEnabled;

    public static void SetEditableTextUndoEnabled(this ComboBox comboBox, bool enabled)
    {
        if (TryGetEditableTextBox(comboBox, out var textBox))
            textBox.IsUndoEnabled = enabled;
    }

    private static bool TryGetEditableTextBox(ComboBox comboBox, out TextBox textBox)
    {
        comboBox.ApplyTemplate();
        textBox = comboBox.Template.FindName("PART_EditableTextBox", comboBox) as TextBox
            ?? null!;
        return textBox is not null;
    }
}
