using FreeX.Ribbon;

namespace FreeX.App.Host;

/// <summary>
/// The Home tab authored declaratively against <see cref="RibbonDefinitionBuilder"/>. This is the
/// first slice of the source-of-truth definition that replaces the hand-authored ribbon XAML
/// (the rest of the tabs follow the same pattern in SP2). Command ids match the catalog
/// <c>CommandName</c>s used by the existing handlers, so the command registry binds 1:1.
/// </summary>
public static class HomeRibbonDefinition
{
    public static RibbonDefinition Build() => new RibbonDefinitionBuilder()
        .Tab("HomeTab", "Home", "H", tab => tab
            .Group("HomeClipboardGroup", "Clipboard", "C", priority: 100, g => g
                .SplitButton("Paste", "Paste", RibbonMenu.Empty, b => b with
                {
                    PreferredLayout = RibbonCommandLayoutKind.Large,
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Paste),
                    KeyTip = "V"
                })
                .Button("Cut", "Cut", b => Icon(b, RibbonCommandIconKind.Cut, "X"))
                .Button("Copy", "Copy", b => Icon(b, RibbonCommandIconKind.Copy, "C"))
                .Button("Format Painter", "Format Painter", b => Icon(b, RibbonCommandIconKind.FormatPainter, "FP")))

            .Group("HomeFontGroup", "Font", "F", priority: 90, g => g
                .ComboBox("Font", "Font", c => c with
                {
                    Items = new[] { "Calibri", "Arial", "Times New Roman", "Segoe UI", "Verdana" }
                })
                .ComboBox("Font Size", "Font Size", c => c with
                {
                    Items = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24" }
                })
                .Toggle("Bold", "Bold", b => Icon(b, RibbonCommandIconKind.Bold, "1"))
                .Toggle("Italic", "Italic", b => Icon(b, RibbonCommandIconKind.Italic, "2"))
                .Toggle("Underline", "Underline", b => Icon(b, RibbonCommandIconKind.Underline, "3"))
                .Toggle("Strikethrough", "Strikethrough", b => Icon(b, RibbonCommandIconKind.Strikethrough, "4"))
                .Dropdown("Borders", "Borders", RibbonMenu.Empty, b => b with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border), KeyTip = "B"
                })
                .Dropdown("Fill Color", "Fill Color", RibbonMenu.Empty, b => b with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill), KeyTip = "H"
                })
                .Dropdown("Font Color", "Font Color", RibbonMenu.Empty, b => b with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Color), KeyTip = "FC"
                }))

            .Group("HomeAlignmentGroup", "Alignment", "A", priority: 80, g => g
                .Toggle("Top Align", "Top Align", b => Icon(b, RibbonCommandIconKind.Align, "TA"))
                .Toggle("Middle Align", "Middle Align", b => Icon(b, RibbonCommandIconKind.Align, "MA"))
                .Toggle("Bottom Align", "Bottom Align", b => Icon(b, RibbonCommandIconKind.Align, "AB"))
                .Toggle("Align Left", "Align Left", b => Icon(b, RibbonCommandIconKind.Align, "AL"))
                .Toggle("Center", "Center", b => Icon(b, RibbonCommandIconKind.Align, "AC"))
                .Toggle("Align Right", "Align Right", b => Icon(b, RibbonCommandIconKind.Align, "AR"))
                .Toggle("Wrap Text", "Wrap Text", b => Icon(b, RibbonCommandIconKind.Wrap, "W"))
                .Dropdown("Merge & Center", "Merge & Center", RibbonMenu.Empty, b => b with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Merge), KeyTip = "M"
                })
                .Button("Increase Indent", "Increase Indent", b => Icon(b, RibbonCommandIconKind.Align, "A6")))

            .Group("HomeNumberGroup", "Number", "N", priority: 70, g => g
                .ComboBox("Number Format", "Number Format", c => c with
                {
                    Items = new[] { "General", "Number", "Currency", "Accounting", "Date", "Percentage", "Text" }
                })
                .Button("Accounting Number Format", "Accounting", b => Icon(b, RibbonCommandIconKind.Currency, "AN"))
                .Toggle("Percent Style", "Percent Style", b => Icon(b, RibbonCommandIconKind.Percent, "P"))
                .Toggle("Comma Style", "Comma Style", b => Icon(b, RibbonCommandIconKind.Comma, "K"))
                .Button("Increase Decimal Places", "Increase Decimal", b => Icon(b, RibbonCommandIconKind.Decimal, "0"))
                .Button("Decrease Decimal Places", "Decrease Decimal", b => Icon(b, RibbonCommandIconKind.Decimal, "9")))

            .Group("HomeStylesGroup", "Styles", "Y", priority: 60, g => g
                .Dropdown("Conditional Formatting", "Conditional Formatting", RibbonMenu.Empty, b => b with
                {
                    PreferredLayout = RibbonCommandLayoutKind.Large,
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Effects),
                    KeyTip = "L"
                })
                .Dropdown("Format as Table", "Format as Table", RibbonMenu.Empty, b => b with
                {
                    PreferredLayout = RibbonCommandLayoutKind.Large,
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table),
                    KeyTip = "T"
                })
                .Dropdown("Cell Styles", "Cell Styles", RibbonMenu.Empty, b => b with
                {
                    PreferredLayout = RibbonCommandLayoutKind.Large,
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Theme),
                    KeyTip = "J"
                }))

            .Group("HomeCellsGroup", "Cells", "E", priority: 50, g => g
                .Dropdown("Insert Cells", "Insert", RibbonMenu.Empty, b => b with
                {
                    PreferredLayout = RibbonCommandLayoutKind.Large,
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Insert),
                    KeyTip = "I"
                })
                .Dropdown("Delete Cells", "Delete", RibbonMenu.Empty, b => b with
                {
                    PreferredLayout = RibbonCommandLayoutKind.Large,
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Delete),
                    KeyTip = "D"
                })
                .Dropdown("Format Cells Size", "Format", RibbonMenu.Empty, b => b with
                {
                    PreferredLayout = RibbonCommandLayoutKind.Large,
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size),
                    KeyTip = "O"
                }))

            .Group("HomeEditingGroup", "Editing", "G", priority: 40, g => g
                .Dropdown("AutoSum", "AutoSum", RibbonMenu.Empty, b => b with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sum), KeyTip = "U"
                })
                .Dropdown("Fill", "Fill", RibbonMenu.Empty, b => b with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill), KeyTip = "FL"
                })
                .Dropdown("Clear", "Clear", RibbonMenu.Empty, b => b with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Clear), KeyTip = "EA"
                })
                .Dropdown("Sort & Filter", "Sort & Filter", RibbonMenu.Empty, b => b with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Sort), KeyTip = "S"
                })
                .Dropdown("Find & Select", "Find & Select", RibbonMenu.Empty, b => b with
                {
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Search), KeyTip = "FD"
                })))
        .Build();

    private static RibbonButton Icon(RibbonButton b, RibbonCommandIconKind kind, string keyTip) =>
        b with { Icon = new RibbonCommandIcon(kind), KeyTip = keyTip };

    private static RibbonToggleButton Icon(RibbonToggleButton b, RibbonCommandIconKind kind, string keyTip) =>
        b with { Icon = new RibbonCommandIcon(kind), KeyTip = keyTip };
}
