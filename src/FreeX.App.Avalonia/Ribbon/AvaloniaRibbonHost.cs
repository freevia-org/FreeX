using Avalonia.Controls;
using FreeX.Ribbon;
using FreeX.Ribbon.Avalonia;

namespace FreeX.App.Avalonia.Ribbon;

/// <summary>
/// Builds the FreeX Avalonia ribbon: a representative multi-tab <see cref="RibbonDefinition"/>
/// (the Avalonia app cannot reference the WPF host that owns the real definition) rendered via
/// <see cref="AvaloniaRibbonRenderer"/>, with a registry of no-op commands so dropdowns and clicks
/// route without throwing.
/// </summary>
internal static class AvaloniaRibbonHost
{
    /// <summary>Builds the ribbon control to dock at the top of the main window.</summary>
    public static Control Build()
    {
        var registry = SampleRibbon.BuildRegistry();
        var definition = SampleRibbon.BuildDefinition();
        return AvaloniaRibbonRenderer.BuildRibbon(definition, registry);
    }
}

/// <summary>A do-nothing command — enough to mark a control as registered/enabled.</summary>
internal sealed class NoOpRibbonCommand : IRibbonCommand
{
    public static readonly NoOpRibbonCommand Instance = new();

    public void Execute(RibbonCommandContext context)
    {
        // Intentionally empty: the Avalonia shell wires real behavior elsewhere.
    }
}

/// <summary>
/// A representative Home-like ribbon plus a few more tabs, exercising large/medium/small controls,
/// a dropdown with menu items, a split button, separators, and a combo box.
/// </summary>
internal static class SampleRibbon
{
    public static RibbonDefinition BuildDefinition()
    {
        return new RibbonDefinitionBuilder()
            .Tab("home", "Home", "H", home =>
            {
                home.Group("clipboard", "Clipboard", "C", 100, g =>
                {
                    g.SplitButton("home.paste", "Paste", PasteMenu(), c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Paste),
                    });
                    g.Button("home.cut", "Cut", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Cut),
                    });
                    g.Button("home.copy", "Copy", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Copy),
                    });
                    g.Button("home.formatPainter", "Format Painter", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.FormatPainter),
                    });
                });

                home.Group("font", "Font", "F", 90, g =>
                {
                    g.ComboBox("home.fontName", "Font", c => c with
                    {
                        Items = new[] { "Calibri", "Arial", "Times New Roman", "Consolas" },
                    });
                    g.ComboBox("home.fontSize", "Size", c => c with
                    {
                        Items = new[] { "8", "9", "10", "11", "12", "14", "16", "18", "24" },
                    });
                    g.Toggle("home.bold", "Bold", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Bold),
                    });
                    g.Toggle("home.italic", "Italic", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Italic),
                    });
                    g.Toggle("home.underline", "Underline", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Underline),
                    });
                    g.Separator();
                    g.Dropdown("home.fillColor", "Fill Color", FillColorMenu(), c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill),
                    });
                    g.Dropdown("home.borders", "Borders", BordersMenu(), c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border),
                    });
                });

                home.Group("alignment", "Alignment", "A", 80, g =>
                {
                    g.Button("home.alignLeft", "Align Left", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align),
                    });
                    g.Button("home.alignCenter", "Center", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align),
                    });
                    g.Button("home.alignRight", "Align Right", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align),
                    });
                    g.Button("home.wrapText", "Wrap Text", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Wrap),
                    });
                    g.SplitButton("home.merge", "Merge & Center", MergeMenu(), c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Merge),
                    });
                });

                home.Group("number", "Number", "N", 70, g =>
                {
                    g.Dropdown("home.numberFormat", "Number Format", NumberFormatMenu(), c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Number),
                    });
                    g.Button("home.currency", "Currency", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Currency),
                    });
                    g.Button("home.percent", "Percent", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Percent),
                    });
                    g.Button("home.comma", "Comma", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Comma),
                    });
                });

                home.Group("styles", "Styles", "S", 60, g =>
                {
                    g.Button("home.cellStyles", "Cell Styles", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Theme),
                    });
                    g.Button("home.formatAsTable", "Format as Table", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table),
                    });
                    g.Button("home.conditional", "Conditional", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Color),
                    });
                });
            })
            .Tab("insert", "Insert", "I", insert =>
            {
                insert.Group("tables", "Tables", "T", 100, g =>
                {
                    g.Button("insert.pivotTable", "PivotTable", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.PivotTable),
                    });
                    g.Button("insert.table", "Table", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Table),
                    });
                });

                insert.Group("charts", "Charts", "C", 90, g =>
                {
                    g.Dropdown("insert.column", "Column", ChartTypeMenu(), c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn),
                    });
                    g.Button("insert.line", "Line", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartLine),
                    });
                    g.Button("insert.pie", "Pie", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartPie),
                    });
                    g.Button("insert.scatter", "Scatter", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartScatter),
                    });
                });

                insert.Group("illustrations", "Illustrations", "L", 80, g =>
                {
                    g.Button("insert.picture", "Picture", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Picture),
                    });
                    g.Button("insert.shapes", "Shapes", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Rectangle),
                    });
                    g.Button("insert.textBox", "Text Box", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextBox),
                    });
                });
            })
            .Tab("data", "Data", "D", data =>
            {
                data.Group("getData", "Get Data", "G", 100, g =>
                {
                    g.Button("data.getData", "Get Data", c => c with
                    {
                        PreferredLayout = RibbonCommandLayoutKind.Large,
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.GetData),
                    });
                    g.Button("data.refresh", "Refresh All", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Refresh),
                    });
                });

                data.Group("sortFilter", "Sort & Filter", "S", 90, g =>
                {
                    g.Button("data.sortAsc", "Sort A-Z", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.SortAscending),
                    });
                    g.Button("data.sortDesc", "Sort Z-A", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.SortDescending),
                    });
                    g.Toggle("data.filter", "Filter", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Filter),
                    });
                });

                data.Group("tools", "Data Tools", "O", 80, g =>
                {
                    g.Dropdown("data.validation", "Data Validation", ValidationMenu(), c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Logical),
                    });
                    g.Button("data.consolidate", "Consolidate", c => c with
                    {
                        Icon = new RibbonCommandIcon(RibbonCommandIconKind.Consolidate),
                    });
                });
            })
            .Build();
    }

    public static IRibbonCommandRegistry BuildRegistry()
    {
        var registry = new RibbonCommandRegistry();
        foreach (var id in EnumerateCommandIds(BuildDefinition()))
            registry.Register(id, NoOpRibbonCommand.Instance);
        return registry;
    }

    private static IEnumerable<RibbonCommandId> EnumerateCommandIds(RibbonDefinition definition)
    {
        foreach (var tab in definition.Tabs)
        foreach (var group in tab.Groups)
        foreach (var control in group.Controls)
        {
            if (!string.IsNullOrEmpty(control.CommandId.Value))
                yield return control.CommandId;

            foreach (var menuId in EnumerateMenuIds(control))
                yield return menuId;
        }
    }

    private static IEnumerable<RibbonCommandId> EnumerateMenuIds(RibbonControl control)
    {
        var menu = control switch
        {
            RibbonSplitButton split => split.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };
        if (menu is null)
            yield break;

        foreach (var id in EnumerateMenuIds(menu.Items))
            yield return id;
    }

    private static IEnumerable<RibbonCommandId> EnumerateMenuIds(IReadOnlyList<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } id && !string.IsNullOrEmpty(id.Value))
                yield return id;
            foreach (var childId in EnumerateMenuIds(item.Children))
                yield return childId;
        }
    }

    private static RibbonMenu PasteMenu() => new(new[]
    {
        new RibbonMenuItem("Paste", "home.paste", InputGesture: "Ctrl+V"),
        new RibbonMenuItem("Paste Values", "home.pasteValues"),
        new RibbonMenuItem("Paste Formatting", "home.pasteFormat"),
        RibbonMenuItem.Separator(),
        new RibbonMenuItem("Paste Special...", "home.pasteSpecial"),
    });

    private static RibbonMenu FillColorMenu() => new(new[]
    {
        new RibbonMenuItem("No Fill", "home.fillNone"),
        new RibbonMenuItem("Yellow", "home.fillYellow"),
        new RibbonMenuItem("Green", "home.fillGreen"),
        new RibbonMenuItem("More Colors...", "home.fillMore"),
    });

    private static RibbonMenu BordersMenu() => new(new[]
    {
        new RibbonMenuItem("All Borders", "home.bordersAll"),
        new RibbonMenuItem("Outside Borders", "home.bordersOutside"),
        new RibbonMenuItem("No Border", "home.bordersNone"),
    });

    private static RibbonMenu MergeMenu() => new(new[]
    {
        new RibbonMenuItem("Merge & Center", "home.mergeCenter"),
        new RibbonMenuItem("Merge Across", "home.mergeAcross"),
        new RibbonMenuItem("Merge Cells", "home.mergeCells"),
        new RibbonMenuItem("Unmerge Cells", "home.unmerge"),
    });

    private static RibbonMenu NumberFormatMenu() => new(new[]
    {
        new RibbonMenuItem("General", "home.fmtGeneral"),
        new RibbonMenuItem("Number", "home.fmtNumber"),
        new RibbonMenuItem("Currency", "home.fmtCurrency"),
        new RibbonMenuItem("Date", "home.fmtDate"),
        new RibbonMenuItem("Percentage", "home.fmtPercent"),
    });

    private static RibbonMenu ChartTypeMenu() => new(new[]
    {
        new RibbonMenuItem("Clustered Column", "insert.colClustered"),
        new RibbonMenuItem("Stacked Column", "insert.colStacked"),
        new RibbonMenuItem("100% Stacked Column", "insert.col100"),
    });

    private static RibbonMenu ValidationMenu() => new(new[]
    {
        new RibbonMenuItem("Data Validation...", "data.validationDialog"),
        new RibbonMenuItem("Circle Invalid Data", "data.circleInvalid"),
        new RibbonMenuItem("Clear Validation Circles", "data.clearCircles"),
    });
}
