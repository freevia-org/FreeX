using System.Windows.Controls;
using System.Windows.Automation;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class WorkbookThemeDialog
{
    private readonly record struct ThemeColorField(
        WorkbookThemeColorSlot Slot,
        TextBox TextBox,
        Button Button,
        bool IsAccent = false);

    private IEnumerable<ThemeColorField> ThemeColorFields()
    {
        yield return new(WorkbookThemeColorSlot.Dark1, Dark1ColorBox, Dark1ColorPickerButton);
        yield return new(WorkbookThemeColorSlot.Light1, Light1ColorBox, Light1ColorPickerButton);
        yield return new(WorkbookThemeColorSlot.Dark2, Dark2ColorBox, Dark2ColorPickerButton);
        yield return new(WorkbookThemeColorSlot.Light2, Light2ColorBox, Light2ColorPickerButton);
        yield return new(WorkbookThemeColorSlot.Accent1, Accent1ColorBox, Accent1ColorPickerButton, IsAccent: true);
        yield return new(WorkbookThemeColorSlot.Accent2, Accent2ColorBox, Accent2ColorPickerButton, IsAccent: true);
        yield return new(WorkbookThemeColorSlot.Accent3, Accent3ColorBox, Accent3ColorPickerButton, IsAccent: true);
        yield return new(WorkbookThemeColorSlot.Accent4, Accent4ColorBox, Accent4ColorPickerButton, IsAccent: true);
        yield return new(WorkbookThemeColorSlot.Accent5, Accent5ColorBox, Accent5ColorPickerButton, IsAccent: true);
        yield return new(WorkbookThemeColorSlot.Accent6, Accent6ColorBox, Accent6ColorPickerButton, IsAccent: true);
        yield return new(WorkbookThemeColorSlot.Hyperlink, HyperlinkColorBox, HyperlinkColorPickerButton);
        yield return new(WorkbookThemeColorSlot.FollowedHyperlink, FollowedHyperlinkColorBox, FollowedHyperlinkColorPickerButton);
    }

    private void ApplyThemeColorAutomationMetadata()
    {
        foreach (var field in ThemeColorFields())
        {
            var label = FormatThemeColorSlotName(field.Slot);

            AutomationProperties.SetName(field.TextBox, UiText.Format("WorkbookTheme_ColorAutomationNameFormat", label));
            AutomationProperties.SetAutomationId(field.TextBox, $"WorkbookTheme{field.Slot}ColorBox");
            AutomationProperties.SetHelpText(field.TextBox, UiText.Get("WorkbookTheme_ColorHelpText"));

            AutomationProperties.SetAutomationId(field.Button, $"WorkbookTheme{field.Slot}ColorPickerButton");
            AutomationProperties.SetHelpText(field.Button, UiText.Format("WorkbookTheme_PickColorHelpTextFormat", label));
        }
    }

    private static string FormatThemeColorSlotName(WorkbookThemeColorSlot slot) => slot switch
    {
        WorkbookThemeColorSlot.Dark1 => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Dark1")),
        WorkbookThemeColorSlot.Light1 => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Light1")),
        WorkbookThemeColorSlot.Dark2 => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Dark2")),
        WorkbookThemeColorSlot.Light2 => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Light2")),
        WorkbookThemeColorSlot.Accent1 => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Accent1")),
        WorkbookThemeColorSlot.Accent2 => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Accent2")),
        WorkbookThemeColorSlot.Accent3 => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Accent3")),
        WorkbookThemeColorSlot.Accent4 => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Accent4")),
        WorkbookThemeColorSlot.Accent5 => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Accent5")),
        WorkbookThemeColorSlot.Accent6 => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Accent6")),
        WorkbookThemeColorSlot.Hyperlink => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_Hyperlink")),
        WorkbookThemeColorSlot.FollowedHyperlink => UiText.CreateAutomationName(UiText.Get("WorkbookTheme_FollowedHyperlink")),
        _ => slot.ToString()
    };
}
