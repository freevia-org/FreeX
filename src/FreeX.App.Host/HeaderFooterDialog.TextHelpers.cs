using System.Windows.Controls;
using FreeX.App.Presentation.PageLayout;

namespace FreeX.App.Host;

public partial class HeaderFooterDialog
{
    private static void PopulatePresetBox(
        ComboBox comboBox,
        IReadOnlyList<HeaderFooterPresetChoice> choices)
    {
        foreach (var choice in choices)
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Content = UiText.Get(choice.EditorLabelResourceKey),
                Tag = choice
            });
        }
    }

    private static void ApplyPreset(TextBox target, object? selectedItem)
    {
        if (selectedItem is not ComboBoxItem { Tag: HeaderFooterPresetChoice choice })
            return;

        target.Text = choice.Value;
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
