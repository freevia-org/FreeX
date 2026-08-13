import re
import xml.etree.ElementTree as ET
from collections import defaultdict

# The live MainWindow.xaml ribbon was deleted in the declarative cutover; regenerate from the
# pre-deletion ribbon preserved in git (write it with: git show <pre-cutover>:.../MainWindow.xaml).
XAML = "tools/_old_mainwindow.xaml"
OUT = "src/FreeX.App.Host/Ribbon/FreeXRibbonDefinition.cs"
RESX = "src/FreeX.App.Host/Resources/Strings.resx"

# Resolve {local:Loc Key=X} headers to their en-US display strings so dropdown items read like Excel
# ("More Functions...", "Goal Seek...", "Error Checking..."); the keytip tests look menu items up by
# their resolved header. WPF strips the leading "_" access-key mnemonic at runtime, so strip it too.
_loc = {}
for _data in ET.parse(RESX).getroot().findall("data"):
    _loc[_data.get("name")] = (_data.findtext("value") or "").replace("_", "")

def resolve(s):
    return _loc.get(s, s) if s else s

src = open(XAML, encoding="utf-8").read()
src2 = re.sub(r"\{local:Loc Key=([A-Za-z0-9_]+)\}", r"\1", src)
root = ET.fromstring(src2)
P = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
LK = "{clr-namespace:FreeX.App.Host}"
parents = {c: p for p in root.iter() for c in p}


def anc(e):
    out = []
    x = parents.get(e)
    while x is not None:
        out.append(x)
        x = parents.get(x)
    return out


def cmdname(e):
    return e.get(LK + "RibbonMetadata.CommandName")


def keytip(e):
    return e.get(LK + "RibbonTooltip.KeyTip")


def catid(e):
    return e.get(LK + "RibbonMetadata.CatalogId")


tabmeta = {}
for e in root.iter():
    if e.tag == P + "TabItem":
        cid = e.get(LK + "RibbonMetadata.CatalogId")
        if not cid:
            continue
        tabmeta[cid] = dict(
            header=e.get("Header") or cid,
            keytip=e.get(LK + "RibbonTooltip.KeyTip") or "",
            contextual=(e.get("Visibility") == "Collapsed"),
        )

data = {}
order = []
curtab = None

# A single CommandName can be reused by unrelated controls across tabs (e.g. "Normal" is a Home
# cell-style menu item AND the View workbook-view toggle, with different handlers). A flat
# CommandName -> handler map silently drops one, so a keytip fires the wrong command. We therefore
# resolve handlers per (tab, CommandName): controls win over their own menu items, and any
# CommandName that resolves to MORE THAN ONE distinct handler across tabs is "ambiguous" and gets a
# tab-qualified command id ("ViewTab/Normal") in both the definition and the handler map.
def event_handler(d):
    # Toggles/checkboxes route through Checked/Unchecked, not Click (e.g. ViewGridlinesChk_Changed).
    return d.get("Click") or d.get("Checked") or d.get("Unchecked")

# name_handlers[name] = set of distinct handlers ever seen for this CommandName (controls + menu
# items, across all tabs). A name bound to MORE THAN ONE handler is ambiguous and needs a unique id
# per handler so the keytip fires the right command.
name_handlers = defaultdict(set)

# home_handlers[name] = handler chosen for the hand-authored Home tab (controls preferred over their
# own menu items). HomeRibbonDefinition.cs uses PLAIN command ids, so these must map plainly.
home_handlers = {}

# The shared declarative Home menu has evolved beyond the archived XAML this generator reads.
# Keep its executable leaf ids here so regenerating the native WPF map cannot disable those items.
# Data Bars / Color Scales are leaf commands in the shared definition (the archived XAML used
# SubmenuOpened), and the accounting entries use unique ids rather than the old repeated command name.
declarative_home_handler_overrides = {
    "3 Arrows": "CfIconSetPresetMenuItem_Click",
    "3 Arrows (Gray)": "CfIconSetPresetMenuItem_Click",
    "3 Flags": "CfIconSetPresetMenuItem_Click",
    "3 Signs": "CfIconSetPresetMenuItem_Click",
    "3 Symbols": "CfIconSetPresetMenuItem_Click",
    "3 Symbols (Uncircled)": "CfIconSetPresetMenuItem_Click",
    "3 Traffic Lights": "CfIconSetPresetMenuItem_Click",
    "3 Traffic Lights (Rimmed)": "CfIconSetPresetMenuItem_Click",
    "4 Arrows": "CfIconSetPresetMenuItem_Click",
    "4 Arrows (Gray)": "CfIconSetPresetMenuItem_Click",
    "4 Ratings": "CfIconSetPresetMenuItem_Click",
    "4 Red To Black": "CfIconSetPresetMenuItem_Click",
    "4 Traffic Lights": "CfIconSetPresetMenuItem_Click",
    "5 Arrows": "CfIconSetPresetMenuItem_Click",
    "5 Arrows (Gray)": "CfIconSetPresetMenuItem_Click",
    "5 Boxes": "CfIconSetPresetMenuItem_Click",
    "5 Quarters": "CfIconSetPresetMenuItem_Click",
    "5 Ratings": "CfIconSetPresetMenuItem_Click",
    "A Date Occurring": "CfDateMenuItem_Click",
    "About FreeX#AboutBtn_Click": "AboutBtn_Click",
    "Above Average": "CfAboveAvgMenuItem_Click",
    "Accent 1": "BorderLineColorAccent1MenuItem_Click",
    "Accent 2": "BorderLineColorAccent2MenuItem_Click",
    "Accounting Number Format British Pound": "AccountingSymbolMenuItem_Click",
    "Accounting Number Format Euro": "AccountingSymbolMenuItem_Click",
    "Accounting Number Format Japanese Yen": "AccountingSymbolMenuItem_Click",
    "Accounting Number Format US Dollar": "AccountingSymbolMenuItem_Click",
    "Below Average": "CfBelowAvgMenuItem_Click",
    "Between": "CfBetweenMenuItem_Click",
    "Black": "BorderLineColorBlackMenuItem_Click",
    "Bottom 10 Items": "CfBottom10MenuItem_Click",
    "Bottom 10%": "CfBottom10PercentMenuItem_Click",
    "Check for Updates#CheckForUpdatesBtn_Click": "CheckForUpdatesBtn_Click",
    "Color Scales": "CfColorScaleMenuItem_Click",
    "Dashed": "BorderLineStyleDashedMenuItem_Click",
    "Data Bars": "CfDataBarMenuItem_Click",
    "Dotted": "BorderLineStyleDottedMenuItem_Click",
    "Double": "BorderLineStyleDoubleMenuItem_Click",
    "Duplicate Values": "CfDuplicateMenuItem_Click",
    "Equal To": "CfEqMenuItem_Click",
    "Feedback#FeedbackBtn_Click": "FeedbackBtn_Click",
    "Gray": "BorderLineColorGrayMenuItem_Click",
    "Greater Than": "CfGtMenuItem_Click",
    "Help Online#HelpOnlineBtn_Click": "HelpOnlineBtn_Click",
    "Less Than": "CfLtMenuItem_Click",
    "Medium": "BorderLineStyleMediumMenuItem_Click",
    "More Rules": "CfIconSetMenuItem_Click",
    "Text that Contains": "CfTextMenuItem_Click",
    "Thick": "BorderLineStyleThickMenuItem_Click",
    "Thin": "BorderLineStyleThinMenuItem_Click",
    "Top 10 Items": "CfTop10MenuItem_Click",
    "Top 10%": "CfTop10PercentMenuItem_Click",
}

# Static menu leaves are route identities, not display text. Keep this mapping beside the archived
# XAML importer so regeneration cannot turn localized labels back into command ids. Dynamic
# galleries (cell styles, conditional-format icon sets, colors, fonts, and similar preset payloads)
# intentionally retain their payload ids and are not listed here.
semantic_static_menu_command_ids = {
    "Crop": "drawing.crop",
    "Reset Crop": "drawing.crop.reset",
    "No Effect": "drawing.shapeEffect.none",
    "Shadow": "drawing.shapeEffect.shadow",
    "Inner Shadow": "drawing.shapeEffect.innerShadow",
    "Reflection": "drawing.shapeEffect.reflection",
    "Glow": "drawing.shapeEffect.glow",
    "Soft Edges": "drawing.shapeEffect.softEdges",
    "Bevel": "drawing.shapeEffect.bevel",
    "3-D Rotation": "drawing.shapeEffect.threeDRotation",
    "Customize": "pageLayout.theme.customize",
    "Customize Colors": "pageLayout.themeColors.customize",
    "Arial": "pageLayout.themeFonts.arial",
    "Times New Roman": "pageLayout.themeFonts.timesNewRoman",
    "Customize Fonts": "pageLayout.themeFonts.customize",
    "Subtle": "pageLayout.themeEffects.subtle",
    "Refined": "pageLayout.themeEffects.refined",
    "Customize Effects": "pageLayout.themeEffects.customize",
    "Wide": "pageLayout.margins.wide",
    "Narrow": "pageLayout.margins.narrow",
    "Custom Margins": "pageLayout.margins.custom",
    "Portrait": "pageLayout.orientation.portrait",
    "Landscape": "pageLayout.orientation.landscape",
    "Letter": "pageLayout.paperSize.letter",
    "Legal": "pageLayout.paperSize.legal",
    "Executive": "pageLayout.paperSize.executive",
    "Statement": "pageLayout.paperSize.statement",
    "Tabloid": "pageLayout.paperSize.tabloid",
    "A4": "pageLayout.paperSize.a4",
    "A3": "pageLayout.paperSize.a3",
    "A5": "pageLayout.paperSize.a5",
    "B4 (JIS)": "pageLayout.paperSize.b4Jis",
    "B5 (JIS)": "pageLayout.paperSize.b5Jis",
    "Set Print Area": "pageLayout.printArea.set",
    "Clear Print Area": "pageLayout.printArea.clear",
    "Insert Page Break": "pageLayout.break.insert",
    "Remove Page Break": "pageLayout.break.remove",
    "Reset All Page Breaks": "pageLayout.break.resetAll",
    "Choose Background": "pageLayout.background.choose",
    "Delete Background": "pageLayout.background.delete",
    "Sum": "formulas.autoSum.sum",
    "Average": "formulas.autoSum.average",
    "Count Numbers": "formulas.autoSum.countNumbers",
    "Count All": "formulas.autoSum.countAll",
    "Max": "formulas.autoSum.max",
    "Min": "formulas.autoSum.min",
    "Remove Precedent Arrows": "formulas.removeArrows.precedent",
    "Remove Dependent Arrows": "formulas.removeArrows.dependent",
    "Error Checking": "formulas.errorChecking.run",
    "Error Checking Options": "formulas.errorChecking.options",
    "Automatic": "formulas.calculation.automatic",
    "Automatic Except Data Tables": "formulas.calculation.automaticExceptDataTables",
    "Manual": "formulas.calculation.manual",
    "Circle Invalid Data": "data.validation.circleInvalid",
    "Clear Validation Circles": "data.validation.clearCircles",
    "Goal Seek": "data.whatIf.goalSeek",
    "Scenario Manager": "data.whatIf.scenarioManager",
    "Data Table": "data.whatIf.dataTable",
    "Clear Outline": "data.outline.clear",
    "200%": "view.zoom.preset.200",
    "75%": "view.zoom.preset.75",
    "50%": "view.zoom.preset.50",
    "25%": "view.zoom.preset.25",
    "More": "view.zoom.custom",
    "Tiled": "view.arrange.tiled",
    "Vertical": "view.arrange.vertical",
    "Cascade": "view.arrange.cascade",
    "Freeze Top Row": "view.freezePanes.topRow",
    "Freeze First Column": "view.freezePanes.firstColumn",
    "Unfreeze Panes": "view.freezePanes.unfreeze",
}

def record(tab, name, handler, is_control):
    if not handler:
        return
    name_handlers[name].add(handler)
    if tab == "HomeTab" and (name not in home_handlers or is_control):
        home_handlers[name] = handler

for e in root.iter():
    cid = catid(e)
    if cid and cid.endswith("Tab"):
        curtab = cid
    if cid and cid.endswith("Group"):
        items = []
        for d in e.iter():
            tag = d.tag.split("}")[-1]
            ancestors = anc(d)
            if any(a.tag == P + "ContextMenu" for a in ancestors):
                continue
            inside_ctrl = any(
                a.tag.split("}")[-1] in ("Button", "ToggleButton", "ComboBox", "CheckBox")
                for a in ancestors)
            if tag == "Rectangle" and d.get("Width") == "1" and not inside_ctrl:
                items.append(("sep",))
            elif tag in ("Button", "ToggleButton", "ComboBox", "CheckBox") and not inside_ctrl:
                cn = cmdname(d)
                if not cn:
                    continue
                style = re.sub(r"\{StaticResource (\w+)\}", r"\1", d.get("Style") or "")
                ch_handler = event_handler(d)
                record(curtab, cn, ch_handler, is_control=True)
                menu = []
                for ch in list(d):
                    if not ch.tag.split("}")[-1].endswith(".ContextMenu"):
                        continue
                    cm = ch.find(P + "ContextMenu")
                    if cm is None:
                        continue
                    for mi in list(cm):
                        mt = mi.tag.split("}")[-1]
                        if mt == "Separator":
                            menu.append(("sep",))
                        elif mt == "MenuItem":
                            mcn = mi.get(LK + "RibbonMetadata.CommandName") or resolve(mi.get("Header")) or ""
                            if mcn:
                                mi_handler = event_handler(mi)
                                # id binds to a handler (CommandName); label is the resolved header so
                                # it reads like Excel. Fall back to the id when there is no header.
                                mlabel = resolve(mi.get("Header")) or mcn
                                record(curtab, mcn, mi_handler, is_control=False)
                                menu.append(("item", mcn, mi.get(LK + "RibbonTooltip.KeyTip") or "",
                                             mi.get("InputGestureText") or "", mi_handler, mlabel))
                has_drop = d.get(LK + "RibbonMetadata.DropdownMenuButton") == "true" or len(menu) > 0
                items.append(("ctrl", tag, cn, keytip(d) or "", style, has_drop, menu, ch_handler))
        if curtab not in data:
            data[curtab] = []
            order.append(curtab)
        data[curtab].append((cid, items))

# A CommandName is ambiguous when distinct controls bind it to more than one handler (e.g. the View
# "Freeze Panes" picker button -> FreezePanesPickerBtn_Click vs its "Freeze Panes" menu item ->
# FreezeAtSelectionMenuItem_Click; or the Home cell-style "Normal" vs the View "Normal" toggle). A
# flat name->handler map silently drops all but one, firing the wrong command. For ambiguous names we
# mint a unique id per handler ("name#Handler") so each rendered control/menu item binds to exactly
# its own handler. Unambiguous names keep their plain id (so the hand-authored Home tab, which uses
# plain ids, still resolves through the same handler map).
ambiguous_names = {name for name, hs in name_handlers.items() if len(hs) > 1}

def command_id(name, handler):
    if handler and name in ambiguous_names:
        return f"{name}#{handler}"
    return name

# Build the final id -> handler map (one entry per distinct id; ambiguous ids are 1:1 with handlers).
handler_map = {}
for name, handlers in name_handlers.items():
    for handler in handlers:
        handler_map[command_id(name, handler)] = handler
# The hand-authored Home tab uses plain ids; ensure each resolves to Home's own handler even when the
# name is ambiguous (and thus only minted as "name#handler" by the generated tabs above).
for name, handler in home_handlers.items():
    handler_map.setdefault(name, handler)
handler_map.update(declarative_home_handler_overrides)
for name, semantic_id in semantic_static_menu_command_ids.items():
    for handler in name_handlers.get(name, ()):
        handler_map[semantic_id] = handler

ctxkey = {
    "ShapeFormatTab": "shape.selected",
    "PictureFormatTab": "picture.selected",
    "ChartDesignTab": "chart.selected",
    "ChartFormatTab": "chart.selected",
    "TableDesignTab": "table.active",
    "PivotTableAnalyzeTab": "pivot.active",
    "PivotTableDesignTab": "pivot.active",
}
ctxcolor = {
    "shape.selected": "Purple",
    "picture.selected": "Teal",
    "chart.selected": "Green",
    "table.active": "Blue",
    "pivot.active": "Orange",
}
ctxlabel = {
    "ShapeFormatTab": "Shape Format",
    "PictureFormatTab": "Picture Format",
    "ChartDesignTab": "Chart Design",
    "ChartFormatTab": "Chart Format",
    "TableDesignTab": "Table Design",
    "PivotTableAnalyzeTab": "PivotTable Analyze",
    "PivotTableDesignTab": "PivotTable Design",
}

icomap = [
    ("paste", "Paste"), ("cut", "Cut"), ("copy", "Copy"), ("format painter", "FormatPainter"),
    ("bold", "Bold"), ("italic", "Italic"), ("underline", "Underline"), ("strikethrough", "Strikethrough"),
    ("border", "Border"), ("fill color", "Fill"), ("font color", "Color"), ("font size", "Font"), ("font", "Font"),
    ("align", "Align"), ("wrap", "Wrap"), ("merge", "Merge"), ("orientation", "Orientation"), ("indent", "Align"),
    ("number format", "Number"), ("accounting", "Currency"), ("currency", "Currency"), ("percent", "Percent"),
    ("comma", "Comma"), ("decimal", "Decimal"),
    ("conditional", "Effects"), ("format as table", "Table"), ("cell styles", "Theme"),
    ("pivottable", "PivotTable"), ("pivotchart", "ChartColumn"), ("pivot", "PivotTable"),
    ("recommended chart", "ChartColumn"), ("column chart", "ChartColumn"), ("bar chart", "ChartColumn"),
    ("line chart", "ChartLine"), ("pie chart", "ChartPie"), ("doughnut", "ChartPie"), ("scatter", "ChartScatter"),
    ("bubble", "ChartScatter"), ("area chart", "ChartArea"), ("radar", "ChartArea"), ("stock", "ChartLine"),
    ("chart", "ChartColumn"),
    ("sparkline", "Sparkline"), ("slicer", "Filter"), ("timeline", "Date"), ("link", "Link"), ("comment", "Comment"),
    ("note", "Comment"), ("text box", "TextBox"), ("header", "HeaderFooter"), ("footer", "HeaderFooter"), ("symbol", "Symbol"),
    ("picture", "Picture"), ("shape", "RibbonShape"), ("bring forward", "BringForward"), ("send backward", "SendBackward"),
    ("selection pane", "List"), ("rotate", "Rotate"), ("object size", "Size"), ("crop", "Picture"), ("effects", "Effects"),
    ("gradient", "Fill"), ("outline", "Border"),
    ("theme", "Theme"), ("margin", "Margins"), ("paper size", "Page"), ("print area", "Print"), ("break", "PageBreak"),
    ("background", "Picture"), ("print titles", "Print"), ("page setup", "Page"), ("scale", "Scale"), ("gridlines", "Grid"),
    ("headings", "Grid"),
    ("autosum", "Sum"), ("sum", "Sum"), ("financial", "Financial"), ("logical", "Logical"), ("text function", "TextFunction"),
    ("date", "Date"), ("lookup", "Search"), ("math", "Math"), ("more function", "Function"), ("function", "Function"),
    ("name manager", "Label"), ("define name", "Label"), ("use in formula", "Function"), ("create from selection", "Label"),
    ("trace", "Link"), ("remove arrow", "Clear"), ("show formula", "Function"), ("error checking", "Warning"),
    ("evaluate", "Function"), ("watch", "Watch"),
    ("calculate", "Refresh"), ("calculation", "Refresh"),
    ("get data", "GetData"), ("refresh", "Refresh"), ("sort a to z", "SortAscending"), ("sort z to a", "SortDescending"),
    ("sort", "Sort"), ("filter", "Filter"), ("clear", "Clear"), ("advanced", "Filter"), ("reapply", "Refresh"),
    ("text to columns", "TextColumns"), ("flash fill", "Flash"), ("remove duplicate", "Delete"), ("data validation", "List"),
    ("consolidate", "Consolidate"),
    ("what-if", "Function"), ("forecast", "ChartLine"), ("group", "Group"), ("ungroup", "Ungroup"), ("subtotal", "Sum"),
    ("detail", "List"),
    ("spelling", "Spelling"), ("statistics", "Info"), ("accessibility", "Accessibility"), ("alt text", "Label"),
    ("protect", "Protect"), ("share", "Share"), ("normal", "View"), ("page break preview", "PageBreak"),
    ("page layout", "Page"), ("custom view", "View"),
    ("ruler", "Ruler"), ("formula bar", "Function"), ("zoom", "Zoom"), ("window", "Window"), ("freeze", "Freeze"),
    ("split", "Window"), ("side by side", "Window"), ("synchronous", "Window"), ("switch", "Window"), ("hide", "View"),
    ("unhide", "View"), ("arrange", "Grid"),
    ("insert", "Insert"), ("delete", "Delete"), ("format", "Size"), ("find", "Search"), ("select", "Search"),
    ("field", "Label"), ("summarize", "Sum"), ("resize", "Scale"), ("convert", "Refresh"), ("total row", "Sum"),
    ("first column", "Table"), ("last column", "Table"), ("banded", "Table"), ("grand total", "Sum"),
    ("report layout", "List"), ("blank row", "List"),
    ("axis", "ChartLine"), ("legend", "Label"), ("trendline", "ChartLine"), ("error bar", "ChartLine"),
    ("series", "ChartColumn"), ("marker", "ChartScatter"), ("plot area", "ChartArea"), ("data label", "Label"),
    ("title", "Label"), ("move chart", "ChartColumn"), ("change chart", "ChartColumn"), ("combo", "ChartColumn"),
    ("select data", "ChartColumn"), ("styles", "Theme"), ("layout", "List"), ("properties", "Info"), ("options", "Info"),
    ("tools", "Function"), ("show", "View"),
]


def icon(cn):
    low = cn.lower()
    for kw, ic in icomap:
        if kw in low:
            return ic
    return "Generic"


# Mirrors RibbonCommandPresentationPlanner.IsLargeRibbonCommand (the authoritative hero-button list).
LARGE_EQ = {
    "paste", "table", "pivottable", "3d map", "insert picture", "pictures", "shapes",
    "insert link", "comment", "symbol", "line", "rotate object", "object size",
    "sort ascending", "sort descending", "filter", "group", "ungroup", "zoom", "100%", "macros", "feedback",
}
LARGE_CONTAINS = [
    "conditional formatting", "format as table", "cell styles", "add-ins", "recommended chart",
    "recommended pivottable", "insert symbol", "insert slicer", "insert timeline", "header", "equation",
    "text box", "rectangle", "ellipse", "bring forward", "send backward", "selection pane", "themes",
    "margins", "orientation", "paper size", "print area", "breaks", "background", "print titles",
    "theme colors", "theme fonts", "theme effects", "scale to fit", "insert function", "autosum",
    "name manager", "define name", "use in formula", "create from selection", "calculation options",
    "calculate now", "calculate sheet", "get data", "refresh all", "text to columns", "flash fill",
    "remove duplicates", "data validation", "consolidate", "data model", "analyze data", "what-if",
    "forecast sheet", "collapse group", "expand group", "subtotal", "spelling", "workbook statistics",
    "check accessibility", "show changes", "new comment", "show comments", "protect sheet",
    "protect workbook", "allow edit", "normal", "page break preview", "page layout", "custom views",
    "zoom to 100", "zoom to selection", "help online", "copy diagnostics", "check for updates",
    "about freex", "legal notices",
]
ICON_STYLES = {"RibbonIconButton", "RibbonIconToggleButton"}


def is_large(name):
    n = name.strip().lower()
    if n in LARGE_EQ:
        return True
    return any(s in n for s in LARGE_CONTAINS)


# Mirrors RibbonCommandPresentationPlanner.ShouldHideFromInsertRibbon: keep only the primary chart
# types + recommended/sparklines on the Insert tab; the rest are chart-formatting commands surfaced
# through the chart contextual tabs, not as Insert buttons.
_PRIMARY_INSERT_CHARTS = {
    "column chart", "stacked column chart", "100% stacked column chart", "line chart", "pie chart",
    "doughnut chart", "bar chart", "stacked bar chart", "100% stacked bar chart", "scatter chart",
    "bubble chart", "area chart", "radar chart", "stock chart",
}


def should_hide_from_insert(title):
    n = (title or "").strip().lower()
    if not any(k in n for k in ("chart", "axis", "legend", "trendline", "series", "plot",
                                "label", "slice", "doughnut hole", "secondary")):
        return False
    return (n not in _PRIMARY_INSERT_CHARTS
            and "sparkline" not in n
            and "recommended chart" not in n)


# Per-group collapse priority: HIGHER stays expanded longer; LOWER collapses to an overflow button
# first. Tuned to read like Excel — the primary action group of each tab is protected (high), large
# secondary galleries (Charts) collapse early, and small utility groups (Symbols, Comments, Links)
# collapse first. Keyed by the group's display header; unlisted groups fall back to left-to-right
# descending order so earlier (more important) groups outlast later ones.
GROUP_PRIORITY = {
    # Insert: Tables is the protected primary; Charts is large and collapses before the small groups.
    "Tables": 200, "Charts": 60, "Sparklines": 120, "Filters": 110, "Links": 100,
    "Comments": 90, "Text": 80, "Symbols": 70,
    # Page Layout: Page Setup is primary; Themes/Scale collapse earlier.
    "Page Setup": 200, "Scale To Fit": 90, "Sheet Options": 80, "Themes": 110, "Arrange": 70,
    # Formulas: Function Library primary; Calculation/Defined Names mid; Formula Auditing collapses.
    "Function Library": 200, "Defined Names": 130, "Formula Auditing": 90, "Calculation": 120,
    # Data: Get & Transform / Sort & Filter primary; What-If/Outline collapse.
    "Get & Transform Data": 200, "Queries & Connections": 90, "Sort & Filter": 180,
    "Data Tools": 130, "Forecast": 80, "Outline": 70, "Data Types": 100,
    # Review: Proofing primary; Notes/Protect collapse first.
    "Proofing": 200, "Accessibility": 120, "Comments ": 110, "Notes": 80, "Protect": 70, "Changes": 90,
    # View: Workbook Views / Show primary; Zoom/Window collapse.
    "Workbook Views": 200, "Show": 180, "Zoom": 110, "Window": 90,
}


def group_priority(tab, cid, header, index):
    if header in GROUP_PRIORITY:
        return GROUP_PRIORITY[header]
    # Fallback: descending left-to-right so the leftmost (primary) group outlasts the rest.
    return max(40, 180 - index * 10)


def grouphdr(cid, tab):
    s = cid[:-5] if cid.endswith("Group") else cid
    tp = tab[:-3]
    if s.startswith(tp):
        s = s[len(tp):]
    s = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", s)
    s = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", " ", s)
    return s.strip() or cid


def esc(t):
    return t.replace("\\", "\\\\").replace('"', '\\"').replace("&", "&")


def method(kind):
    return {"Button": "Button", "ToggleButton": "Toggle", "CheckBox": "CheckBox", "ComboBox": "ComboBox"}[kind]


def menu_expr(menu):
    parts = []
    n = 0
    for m in menu:
        if m[0] == "sep":
            if parts and not parts[-1].endswith("Separator()"):
                parts.append(".Separator()")
            continue
        if n >= 14:
            break
        mkt, mg, mhandler = esc(m[2]), esc(m[3]), m[4]
        mlabel = esc(m[5] if len(m) > 5 else m[1])
        mid = esc(semantic_static_menu_command_ids.get(m[1], command_id(m[1], mhandler)))
        args = f'"{mid}", "{mlabel}"'
        if mkt or mg:
            args += f', "{mkt}"'
        if mg:
            args += f', "{mg}"'
        parts.append(f".Item({args})")
        n += 1
    while parts and parts[0].endswith("Separator()"):
        parts.pop(0)
    while parts and parts[-1].endswith("Separator()"):
        parts.pop()
    return "".join(parts)


out = []
out.append("using FreeX.Ribbon;")
out.append("using Ico = FreeX.Ribbon.RibbonCommandIconKind;")
out.append("")
out.append("namespace FreeX.App.Host;")
out.append("")
out.append("/// <summary>")
out.append("/// The complete FreeX ribbon authored declaratively, generated from the catalog structure of")
out.append("/// the original MainWindow.xaml ribbon (all main tabs + contextual tabs). Command ids match")
out.append("/// the catalog CommandNames so the registry binds them to existing handlers.")
out.append("/// </summary>")
out.append("public static class FreeXRibbonDefinition")
out.append("{")
out.append("    public static RibbonDefinition Build() => new RibbonDefinitionBuilder()")

mainorder = ["HomeTab", "InsertTab", "DrawTab", "PageLayoutTab", "FormulasTab", "DataTab", "ReviewTab", "ViewTab", "HelpTab"]
ctxorder = ["ChartDesignTab", "ChartFormatTab", "PictureFormatTab", "ShapeFormatTab", "TableDesignTab", "PivotTableAnalyzeTab", "PivotTableDesignTab"]

for tab in mainorder + ctxorder:
    if tab not in data:
        continue
    if tab == "HomeTab":
        continue  # Home is hand-authored in HomeRibbonDefinition for full fidelity.
    meta = tabmeta.get(tab, dict(header=tab, keytip="", contextual=tab in ctxkey))
    raw_hdr = meta["header"]
    if raw_hdr.startswith("MainWindow_Header_"):
        raw_hdr = raw_hdr[len("MainWindow_Header_"):]
    raw_hdr = re.sub(r"(?<=[a-z])(?=[A-Z])", " ", raw_hdr)
    if tab in ctxlabel:
        raw_hdr = ctxlabel[tab]
    hdr = esc(raw_hdr)
    kt = esc(meta["keytip"])
    groups = [g for g in data[tab] if any(it[0] == "ctrl" for it in g[1])]
    if not groups:
        continue
    if tab in ctxkey:
        key = ctxkey[tab]
        col = ctxcolor.get(key, "Green")
        lab = esc(ctxlabel.get(tab, hdr))
        out.append(f'        .ContextualTab("{tab}", "{hdr}", new RibbonTabContext("{key}", "{lab}", RibbonContextColor.{col}), tab => tab')
    else:
        out.append(f'        .Tab("{tab}", "{hdr}", "{kt}", tab => tab')
    gp = 180
    for gi, (cid, items) in enumerate(groups):
        ghdr = esc(grouphdr(cid, tab))
        gp = group_priority(tab, cid, grouphdr(cid, tab), gi)
        # Cap control count (keep separators), and drop leading/trailing/duplicate separators.
        kept = []
        ctrl_count = 0
        for it in items:
            if it[0] == "sep":
                if kept and kept[-1][0] != "sep":
                    kept.append(it)
                continue
            if tab == "InsertTab" and should_hide_from_insert(it[2]):
                continue
            if ctrl_count >= 16:
                continue
            kept.append(it)
            ctrl_count += 1
        while kept and kept[-1][0] == "sep":
            kept.pop()

        cl = []
        for it in kept:
            if it[0] == "sep":
                cl.append("                .Separator()")
                continue
            _, kind, cn, k, style, has_drop, menu, ch_handler = it
            ic = icon(cn)
            cesc = esc(cn)               # label (display text)
            idesc = esc(command_id(cn, ch_handler))  # command id (handler-qualified when ambiguous)
            kk = esc(k)
            mx = menu_expr(menu) if menu else ""
            if mx:
                drop = f", menu: m => m{mx}"
            elif has_drop:
                drop = ", dropdown: true"
            else:
                drop = ""
            if kind == "ComboBox":
                low = cn.lower()
                if "font size" in low:
                    citems = '"8", "9", "10", "11", "12", "14", "16", "18", "20", "24"'
                    width = "44"
                elif "font" in low:
                    citems = '"Calibri", "Arial", "Times New Roman", "Segoe UI", "Verdana"'
                    width = "120"
                elif "number" in low:
                    citems = '"General", "Number", "Currency", "Accounting", "Date", "Percentage", "Text"'
                    width = "120"
                elif "width" in low or "height" in low:
                    citems = '"Automatic", "1 page", "2 pages"'
                    width = "96"
                elif "percent" in low or "scale" in low:
                    citems = '"100%", "90%", "80%", "75%", "50%"'
                    width = "70"
                else:
                    citems = ""
                    width = ""
                parts = [f"Icon = new RibbonCommandIcon(RibbonCommandIconKind.{ic})"]
                if width:
                    parts.append(f"Width = {width}")
                if citems:
                    parts.append("Items = new[] { " + citems + " }")
                if kk:
                    parts.append(f'KeyTip = "{kk}"')
                cl.append(f'                .ComboBox("{idesc}", "{cesc}", c => c with {{ {", ".join(parts)} }})')
            elif kind == "CheckBox":
                cb_parts = [f"Icon = new RibbonCommandIcon(RibbonCommandIconKind.{ic})"]
                if kk:
                    cb_parts.append(f'KeyTip = "{kk}"')
                cl.append(f'                .CheckBox("{idesc}", "{cesc}", b => b with {{ {", ".join(cb_parts)} }})')
            elif is_large(cn):
                cl.append(f'                .Large("{idesc}", "{cesc}", Ico.{ic}, "{kk}"{drop})')
            elif style in ICON_STYLES or kind == "ToggleButton":
                if kind == "ToggleButton":
                    cl.append(f'                .IconToggle("{idesc}", "{cesc}", Ico.{ic}, "{kk}")')
                else:
                    cl.append(f'                .Icon("{idesc}", "{cesc}", Ico.{ic}, "{kk}"{drop})')
            else:
                cl.append(f'                .Medium("{idesc}", "{cesc}", Ico.{ic}, "{kk}"{drop})')
        body = "\n".join(cl)
        out.append(f'            .Group("{cid}", "{ghdr}", null, priority: {int(gp)},')
        out.append("                g => g")
        out.append(body + ")")
    out.append("        )")
out.append("        .Build();")
out.append("}")

open(OUT, "w", encoding="utf-8").write("\n".join(out) + "\n")
print("wrote", OUT)

# Emit the CommandName -> Click-handler-method map for the native command registry.
HMAP = "src/FreeX.App.Host/Ribbon/FreeXRibbonHandlerMap.g.cs"
hm = []
hm.append("using System.Collections.Generic;")
hm.append("")
hm.append("namespace FreeX.App.Host;")
hm.append("")
hm.append("/// <summary>Generated map: ribbon CommandName -> the MainWindow Click-handler method name.</summary>")
hm.append("public static class FreeXRibbonHandlerMap")
hm.append("{")
hm.append("    public static readonly IReadOnlyDictionary<string, string> Handlers =")
hm.append("        new Dictionary<string, string>(System.StringComparer.Ordinal)")
hm.append("        {")
for cmd in sorted(handler_map):
    hm.append(f'            ["{esc(cmd)}"] = "{esc(handler_map[cmd])}",')
hm.append("        };")
hm.append("}")
open(HMAP, "w", encoding="utf-8").write("\n".join(hm) + "\n")
print("wrote", HMAP, "with", len(handler_map), "handlers")
print("tabs:", sum(1 for t in (mainorder + ctxorder) if t in data and any(g[1] for g in data[t])))
print("groups:", sum(1 for t in data for g in data[t] if g[1]))
print("controls:", sum(len(g[1][:16]) for t in data for g in data[t] if g[1]))
