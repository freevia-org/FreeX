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

    // A preset replaces the whole header/footer, as in Excel: two-part presets fill centre and right.
    private static void ApplyPreset(TextBox left, TextBox center, TextBox right, object? selectedItem)
    {
        if (selectedItem is not ComboBoxItem { Tag: HeaderFooterPresetChoice choice })
            return;

        var sections = choice.Sections;
        left.Text = sections.Left;
        center.Text = sections.Center;
        right.Text = sections.Right;
        center.CaretIndex = center.Text.Length;
        center.Focus();
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
