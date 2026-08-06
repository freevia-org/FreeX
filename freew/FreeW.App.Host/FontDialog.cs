using System.Globalization;
using System.Windows;
using System.Windows.Automation;
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
        var surface = FontDialogPlanner.Surface;

        var dialog = new Window
        {
            Title = surface.Title,
            Width = surface.WindowWidth,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        var familyBox = new TextBox
        {
            Text = state.FontFamilyText,
            MinWidth = surface.Field(FontDialogFieldKind.FontFamily).MinWidth,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var sizeBox = new ComboBox { MinWidth = surface.Field(FontDialogFieldKind.FontSize).MinWidth, IsEditable = true, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var size in FontDialogPlanner.SizeChoices)
            sizeBox.Items.Add(size.Label);
        sizeBox.Text = state.FontSizeText;

        var effects = surface.Effects.ToDictionary(
            spec => spec.Kind,
            spec => new CheckBox
            {
                Content = spec.Label,
                IsChecked = state.EffectValue(spec.Kind),
                IsThreeState = spec.IsThreeState,
                Margin = new Thickness(0, 0, spec.Kind == FontDialogEffectKind.Subscript ? 0 : 12, 4),
            });
        foreach (var spec in surface.Effects)
            AutomationProperties.SetAutomationId(effects[spec.Kind], spec.AutomationId);
        var superCheck = effects[FontDialogEffectKind.Superscript];
        var subCheck = effects[FontDialogEffectKind.Subscript];

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

        var colorBox = new ComboBox { MinWidth = surface.Field(FontDialogFieldKind.Color).MinWidth, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var color in FontDialogPlanner.ColorChoices)
            colorBox.Items.Add(color.Label);
        colorBox.SelectedIndex = state.ColorIndex;

        var fontPanel = new StackPanel { Margin = new Thickness(10) };
        var fields = new Dictionary<FontDialogFieldKind, UIElement>
        {
            [FontDialogFieldKind.FontFamily] = familyBox,
            [FontDialogFieldKind.FontSize] = sizeBox,
            [FontDialogFieldKind.Color] = colorBox,
        };
        foreach (var kind in surface.Tabs[0].Fields)
            FontRow(fontPanel, surface.Field(kind).Label, fields[kind]);
        fontPanel.Children.Add(new TextBlock { Text = surface.EffectsSectionLabel, Margin = new Thickness(0, 4, 0, 2) });
        var effectsWrap = new WrapPanel();
        foreach (var spec in surface.Effects)
            effectsWrap.Children.Add(effects[spec.Kind]);
        fontPanel.Children.Add(effectsWrap);

        var spacingBox = NumberTextBox(state.CharacterSpacingText ?? string.Empty);
        var kerningBox = new TextBox
        {
            Text = state.KerningMinSizeText,
            MinWidth = surface.Field(FontDialogFieldKind.Kerning).MinWidth,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var positionBox = NumberTextBox(state.PositionText ?? string.Empty);

        var ligatureBox = new ComboBox { MinWidth = surface.Field(FontDialogFieldKind.Ligatures).MinWidth, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var ligature in FontDialogPlanner.LigatureChoices)
            ligatureBox.Items.Add(ligature.Label);
        ligatureBox.SelectedIndex = state.LigatureIndex;

        var stylisticBox = new TextBox
        {
            Text = state.StylisticSetText,
            MinWidth = surface.Field(FontDialogFieldKind.StylisticSet).MinWidth,
            Margin = new Thickness(0, 0, 0, 8),
            ToolTip = surface.Field(FontDialogFieldKind.StylisticSet).ToolTip,
        };

        var numberFormBox = new ComboBox { MinWidth = surface.Field(FontDialogFieldKind.NumberForm).MinWidth, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var numberForm in FontDialogPlanner.NumberFormChoices)
            numberFormBox.Items.Add(numberForm.Label);
        numberFormBox.SelectedIndex = state.NumberFormIndex;

        var numberSpacingBox = new ComboBox { MinWidth = surface.Field(FontDialogFieldKind.NumberSpacing).MinWidth, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var numberSpacing in FontDialogPlanner.NumberSpacingChoices)
            numberSpacingBox.Items.Add(numberSpacing.Label);
        numberSpacingBox.SelectedIndex = state.NumberSpacingIndex;

        var advPanel = new StackPanel { Margin = new Thickness(10) };
        fields[FontDialogFieldKind.CharacterSpacing] = spacingBox;
        fields[FontDialogFieldKind.Kerning] = kerningBox;
        fields[FontDialogFieldKind.Position] = positionBox;
        fields[FontDialogFieldKind.Ligatures] = ligatureBox;
        fields[FontDialogFieldKind.StylisticSet] = stylisticBox;
        fields[FontDialogFieldKind.NumberForm] = numberFormBox;
        fields[FontDialogFieldKind.NumberSpacing] = numberSpacingBox;
        foreach (var spec in surface.Fields)
            AutomationProperties.SetAutomationId(fields[spec.Kind], spec.AutomationId);
        foreach (var kind in surface.Tabs[1].Fields)
            FontRow(advPanel, surface.Field(kind).Label, fields[kind]);

        var tabs = new TabControl { Margin = new Thickness(0) };
        var panels = new[] { fontPanel, advPanel };
        for (var index = 0; index < surface.Tabs.Count; index++)
        {
            var tab = new TabItem { Header = surface.Tabs[index].Header, Content = new ScrollViewer { Content = panels[index], VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            AutomationProperties.SetAutomationId(tab, surface.Tabs[index].AutomationId);
            tabs.Items.Add(tab);
        }

        void Accept()
        {
            var acceptance = session.PlanAcceptance(FontDialogPlanner.CaptureControlState(
                familyBox.Text,
                sizeBox.Text,
                colorBox.SelectedIndex,
                spacingBox.Text,
                kerningBox.Text,
                positionBox.Text,
                ligatureBox.SelectedIndex,
                stylisticBox.Text,
                numberFormBox.SelectedIndex,
                numberSpacingBox.SelectedIndex,
                kind => effects[kind].IsChecked));

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

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: surface.ActionButtonWidth, rowMargin: new Thickness(0, 10, 0, 0));

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
