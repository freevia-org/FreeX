using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// A two-tab Font dialog matching the Home > Font dialog-launcher in Word. The <b>Font</b> tab
/// covers family, size, style (bold/italic/underline/single and double strikethrough), colour, and character effects
/// (small caps, all caps, superscript, subscript). The <b>Advanced</b> tab surfaces the OpenType
/// and character-spacing fields already on <see cref="RunFormatting"/>:
/// character spacing, kerning threshold, raised/lowered position, ligatures, stylistic set, number
/// form, and number spacing. Applies to the selection via the command bus through
/// <see cref="DocumentView.ApplyFontFormatting"/>.
/// </summary>
internal static class FontDialog
{
    /// <summary>
    /// Show the Font dialog seeded from <paramref name="current"/>. Returns the edited
    /// <see cref="RunFormatting"/>, or null if cancelled.
    /// </summary>
    public static RunFormatting? Prompt(Window? owner, RunFormatting current)
    {
        RunFormatting? result = null;
        var session = FontDialogPlanner.CreateSession(current, CultureInfo.CurrentCulture);
        var state = session.InitialState;
        var text = FontDialogPlanner.Text;

        var dialog = new Window
        {
            Title = text.Title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        var familyBox = new TextBox
        {
            Text = state.FontFamilyText,
            MinWidth = 200,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var sizeBox = new ComboBox { MinWidth = 80, IsEditable = true, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var size in FontDialogPlanner.SizeChoices)
            sizeBox.Items.Add(size.Label);
        sizeBox.Text = state.FontSizeText;

        var boldCheck      = new CheckBox { Content = text.BoldLabel,             IsChecked = state.Bold,          Margin = new Thickness(0, 0, 12, 4) };
        var italicCheck    = new CheckBox { Content = text.ItalicLabel,           IsChecked = state.Italic,        Margin = new Thickness(0, 0, 12, 4) };
        var underlineCheck = new CheckBox { Content = text.UnderlineLabel,        IsChecked = state.Underline,     Margin = new Thickness(0, 0, 12, 4) };
        var strikeCheck    = new CheckBox { Content = text.StrikethroughLabel,    IsChecked = state.Strikethrough, Margin = new Thickness(0, 0, 12, 4) };
        var doubleStrikeCheck = new CheckBox { Content = text.DoubleStrikethroughLabel, IsChecked = state.DoubleStrikethrough, Margin = new Thickness(0, 0, 12, 4) };
        var hiddenCheck    = new CheckBox { Content = text.HiddenLabel,           IsChecked = state.Hidden,        Margin = new Thickness(0, 0, 12, 4) };
        var smallCapsCheck = new CheckBox { Content = text.SmallCapsLabel,        IsChecked = state.SmallCaps,     Margin = new Thickness(0, 0, 12, 4) };
        var allCapsCheck   = new CheckBox { Content = text.AllCapsLabel,          IsChecked = state.AllCaps,       Margin = new Thickness(0, 0, 12, 4) };
        var superCheck     = new CheckBox { Content = text.SuperscriptLabel,      IsChecked = state.Superscript,   Margin = new Thickness(0, 0, 12, 4) };
        var subCheck       = new CheckBox { Content = text.SubscriptLabel,        IsChecked = state.Subscript,     Margin = new Thickness(0, 0, 0, 4) };

        superCheck.Checked += (_, _) =>
        {
            var alignment = session.PlanVerticalAlignmentToggle(
                superCheck.IsChecked == true,
                subCheck.IsChecked == true,
                FontDialogVerticalAlignmentToggle.Superscript,
                superCheck.IsChecked);
            superCheck.IsChecked = alignment.Superscript;
            subCheck.IsChecked = alignment.Subscript;
        };
        subCheck.Checked += (_, _) =>
        {
            var alignment = session.PlanVerticalAlignmentToggle(
                superCheck.IsChecked == true,
                subCheck.IsChecked == true,
                FontDialogVerticalAlignmentToggle.Subscript,
                subCheck.IsChecked);
            superCheck.IsChecked = alignment.Superscript;
            subCheck.IsChecked = alignment.Subscript;
        };

        var colorBox = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var color in FontDialogPlanner.ColorChoices)
            colorBox.Items.Add(color.Label);
        colorBox.SelectedIndex = state.ColorIndex;

        var fontPanel = new StackPanel { Margin = new Thickness(10) };
        FontRow(fontPanel, text.FontFamilyLabel, familyBox);
        FontRow(fontPanel, text.FontSizeLabel, sizeBox);
        FontRow(fontPanel, text.ColorLabel, colorBox);
        fontPanel.Children.Add(new TextBlock { Text = text.StyleLabel, Margin = new Thickness(0, 4, 0, 2) });
        var effectsWrap = new WrapPanel();
        foreach (var cb in new[] { boldCheck, italicCheck, underlineCheck, strikeCheck, doubleStrikeCheck, hiddenCheck, smallCapsCheck, allCapsCheck, superCheck, subCheck })
            effectsWrap.Children.Add(cb);
        fontPanel.Children.Add(effectsWrap);

        var spacingBox = NumberTextBox(state.CharacterSpacingText ?? string.Empty);
        var kerningBox = new TextBox
        {
            Text = state.KerningMinSizeText,
            MinWidth = 100,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var positionBox = NumberTextBox(state.PositionText ?? string.Empty);

        var ligatureBox = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var ligature in FontDialogPlanner.LigatureChoices)
            ligatureBox.Items.Add(ligature.Label);
        ligatureBox.SelectedIndex = state.LigatureIndex;

        var stylisticBox = new TextBox
        {
            Text = state.StylisticSetText,
            MinWidth = 100,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = FontDialogPlanner.StylisticSetToolTip,
        };

        var numberFormBox = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var numberForm in FontDialogPlanner.NumberFormChoices)
            numberFormBox.Items.Add(numberForm.Label);
        numberFormBox.SelectedIndex = state.NumberFormIndex;

        var numberSpacingBox = new ComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var numberSpacing in FontDialogPlanner.NumberSpacingChoices)
            numberSpacingBox.Items.Add(numberSpacing.Label);
        numberSpacingBox.SelectedIndex = state.NumberSpacingIndex;

        var advPanel = new StackPanel { Margin = new Thickness(10) };
        FontRow(advPanel, text.CharacterSpacingLabel, spacingBox);
        FontRow(advPanel, text.KerningLabel, kerningBox);
        FontRow(advPanel, text.PositionLabel, positionBox);
        FontRow(advPanel, text.LigaturesLabel, ligatureBox);
        FontRow(advPanel, text.StylisticSetLabel, stylisticBox);
        FontRow(advPanel, text.NumberFormLabel, numberFormBox);
        FontRow(advPanel, text.NumberSpacingLabel, numberSpacingBox);

        var tabs = new TabControl { Margin = new Thickness(0) };
        tabs.Items.Add(new TabItem { Header = text.FontTab, Content = new ScrollViewer { Content = fontPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = text.AdvancedTab, Content = new ScrollViewer { Content = advPanel,  VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

        void Accept()
        {
            var acceptance = session.PlanAcceptance(new FontDialogControlState(
                familyBox.Text,
                sizeBox.Text,
                colorBox.SelectedIndex,
                boldCheck.IsChecked,
                italicCheck.IsChecked,
                underlineCheck.IsChecked,
                strikeCheck.IsChecked,
                smallCapsCheck.IsChecked == true,
                allCapsCheck.IsChecked == true,
                superCheck.IsChecked == true,
                subCheck.IsChecked == true,
                spacingBox.Text,
                kerningBox.Text,
                positionBox.Text,
                ligatureBox.SelectedIndex,
                stylisticBox.Text,
                numberFormBox.SelectedIndex,
                numberSpacingBox.SelectedIndex,
                doubleStrikeCheck.IsChecked,
                hiddenCheck.IsChecked));

            if (!acceptance.IsAccepted)
            {
                DialogMessageHelper.ShowWarning(
                    dialog,
                    acceptance.ErrorMessage ?? string.Empty);
                return;
            }

            result = acceptance.Result!.Formatting;
            dialog.DialogResult = true;
        }

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 10, 0, 0));

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(tabs);
        root.Children.Add(buttons);
        dialog.Content = root;

        familyBox.Focus();
        return dialog.ShowDialog() == true ? result : null;
    }

    private static void FontRow(Panel panel, string label, UIElement control)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 2),
        });
        if (control is FrameworkElement fe)
            fe.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(control);
    }

    private static TextBox NumberTextBox(string text) => new()
    {
        Text = text,
        MinWidth = 100,
        Margin = new Thickness(0, 0, 0, 8),
    };
}
