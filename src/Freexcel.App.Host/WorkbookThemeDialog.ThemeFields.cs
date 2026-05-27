using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
}
