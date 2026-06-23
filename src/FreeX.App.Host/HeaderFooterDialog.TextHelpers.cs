using System.Windows.Controls;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host;

public partial class HeaderFooterDialog
{
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
        var target = GetActiveTextBox();
        var caretIndex = target.CaretIndex;
        target.Text = HeaderFooterEditorPlanner.InsertToken(target.Text, caretIndex, token);
        target.CaretIndex = caretIndex + token.Length;
        target.Focus();
    }

    private static void SetControlsEnabled(bool isEnabled, params Control[] controls)
    {
        foreach (var control in controls)
            control.IsEnabled = isEnabled;
    }
}
