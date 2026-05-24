using System.Windows.Controls;

namespace Freexcel.App.Host;

public partial class HeaderFooterDialog
{
    public static string InsertToken(string text, int caretIndex, string token)
    {
        var boundedCaretIndex = Math.Clamp(caretIndex, 0, text.Length);
        return text.Insert(boundedCaretIndex, token);
    }

    private static void ApplyPreset(TextBox target, object? selectedItem)
    {
        if (selectedItem is not ComboBoxItem { Tag: string preset })
            return;

        target.Text = preset;
        target.CaretIndex = target.Text.Length;
        target.Focus();
    }

    private void InsertTokenIntoActiveBox(string token)
    {
        var target = _activeTextBox ?? HeaderCenterBox;
        var caretIndex = target.CaretIndex;
        target.Text = InsertToken(target.Text, caretIndex, token);
        target.CaretIndex = caretIndex + token.Length;
        target.Focus();
    }

    private static void SetControlsEnabled(bool isEnabled, params Control[] controls)
    {
        foreach (var control in controls)
            control.IsEnabled = isEnabled;
    }
}
