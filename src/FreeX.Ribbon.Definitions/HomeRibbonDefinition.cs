using Free.Shared.Ribbon;
using Ico = Free.Shared.Ribbon.RibbonCommandIconKind;

namespace FreeX.Ribbon.Definitions;

/// <summary>
/// The Home tab authored declaratively to match the original FreeX/Excel layout: per-command preferred
/// sizes (Paste large, Cut/Copy/Format Painter medium, Bold/Italic/… icon-only), explicit two-row groups
/// (Font, Alignment, Number), inline separators, and a narrow font-size combo. Stable command ids bind
/// directly to the host's typed command registry.
/// </summary>
public static class HomeRibbonDefinition
{
    public static RibbonDefinition Build() => new(new[] { HomeTab() });

    public static RibbonTab HomeTab() => new RibbonDefinitionBuilder()
        .Tab("HomeTab", "Home", "H", tab => tab
            .Group("HomeClipboardGroup", "Clipboard", "C", priority: 100, g => g
                .Large("Paste", "Paste", Ico.Paste, "V", menu: HomeRibbonMenus.Paste)
                .Medium("Cut", "Cut", Ico.Cut, "X")
                .Medium("Copy", "Copy", Ico.Copy, "C")
                .Medium("Format Painter", "Format Painter", Ico.FormatPainter, "FP"))

            .Group("HomeFontGroup", "Font", "F", priority: 90, g => g
                .DialogLauncher("Format Cells Font", "Font dialog", "Open Format Cells on the Font tab.")
                .ComboBox("Font", "Font", c => c with
                {
                    Width = 120,
                    Items = new[] { "Calibri", "Arial", "Times New Roman", "Segoe UI", "Verdana" }
                })
                .ComboBox("Font Size", "Font Size", c => c with
                {
                    Width = 44,
                    Items = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24" }
                })
                .Icon("Increase Font Size", "Increase Font Size", Ico.Font, "FG")
                .Icon("Decrease Font Size", "Decrease Font Size", Ico.Font, "FK")
                .RowBreak()
                .IconToggle("Bold", "Bold", Ico.Bold, "1")
                .IconToggle("Italic", "Italic", Ico.Italic, "2")
                .IconToggle("Underline", "Underline", Ico.Underline, "3")
                .IconToggle("Strikethrough", "Strikethrough", Ico.Strikethrough, "4")
                .Separator()
                .Icon("Borders", "Borders", Ico.Border, "B", menu: HomeRibbonMenus.Borders)
                .Icon("Fill Color", "Fill Color", Ico.Fill, "H", dropdown: true)
                .Icon("Font Color", "Font Color", Ico.Color, "FC", dropdown: true))

            .Group("HomeAlignmentGroup", "Alignment", "A", priority: 80, g => g
                .DialogLauncher("Format Cells Alignment", "Alignment dialog", "Open Format Cells on the Alignment tab.")
                .IconToggle("Top Align", "Top Align", Ico.Align, "AT")
                .IconToggle("Middle Align", "Middle Align", Ico.Align, "AM")
                .IconToggle("Bottom Align", "Bottom Align", Ico.Align, "AB")
                .Separator()
                .Icon("Orientation", "Orientation", Ico.Orientation, "RO", menu: HomeRibbonMenus.Orientation)
                .Medium("Wrap Text", "Wrap Text", Ico.Wrap, "W")
                .RowBreak()
                .IconToggle("Align Left", "Align Left", Ico.Align, "AL")
                .IconToggle("Center", "Center", Ico.Align, "AC")
                .IconToggle("Align Right", "Align Right", Ico.Align, "AR")
                .Separator()
                .Icon("Decrease Indent", "Decrease Indent", Ico.Align, "AO")
                .Icon("Increase Indent", "Increase Indent", Ico.Align, "AI")
                .Medium("Merge & Center", "Merge & Center", Ico.Merge, "M", menu: HomeRibbonMenus.MergeCenter))

            .Group("HomeNumberGroup", "Number", "N", priority: 70, g => g
                .DialogLauncher("Format Cells Number", "Number dialog", "Open Format Cells on the Number tab.")
                .ComboBox("Number Format", "Number Format", c => c with
                {
                    Width = 124,
                    KeyTip = "N",
                    PresentationKind = RibbonComboBoxPresentationKind.Gallery,
                    Items = new[] { "General", "Number", "Currency", "Accounting", "Date", "Percentage", "Text" }
                })
                .RowBreak()
                .Icon("Accounting Number Format", "Accounting", Ico.Currency, "AN", menu: HomeRibbonMenus.AccountingNumberFormat)
                .Icon("Percent Style", "Percent Style", Ico.Percent, "P")
                .Icon("Comma Style", "Comma Style", Ico.Comma, "K")
                .Separator()
                .Icon("Increase Decimal Places", "Increase Decimal", Ico.Decimal, "QI")
                .Icon("Decrease Decimal Places", "Decrease Decimal", Ico.Decimal, "QD"))

            .Group("HomeStylesGroup", "Styles", "Y", priority: 60, g => g
                .Large("Conditional Formatting", "Conditional Formatting", Ico.Effects, "L", menu: HomeRibbonMenus.ConditionalFormatting)
                .Large("Format as Table", "Format as Table", Ico.Table, "T", dropdown: true)
                .Large("Cell Styles", "Cell Styles", Ico.Theme, "J", menu: HomeRibbonMenus.CellStyles))

            .Group("HomeCellsGroup", "Cells", "E", priority: 65, g => g
                .Medium("Insert", "Insert", Ico.Insert, "I", menu: HomeRibbonMenus.Insert)
                .Medium("Delete", "Delete", Ico.Delete, "D", menu: HomeRibbonMenus.Delete)
                .Medium("Format", "Format", Ico.Size, "O", menu: HomeRibbonMenus.Format))

            .Group("HomeEditingGroup", "Editing", "G", priority: 40, g => g
                .Medium("AutoSum", "AutoSum", Ico.Sum, "U", menu: HomeRibbonMenus.AutoSum)
                .Medium("Fill", "Fill", Ico.Fill, "FI", menu: HomeRibbonMenus.Fill)
                .Medium("Clear", "Clear", Ico.Clear, "E", menu: HomeRibbonMenus.Clear)
                .Medium("Sort & Filter", "Sort & Filter", Ico.Sort, "S", menu: HomeRibbonMenus.SortFilter)
                .Medium("Find & Select", "Find & Select", Ico.Search, "FD", menu: HomeRibbonMenus.FindSelect)))
        .Build()
        .FindTab("HomeTab")!;
}
