import re
import xml.etree.ElementTree as ET

XAML = "src/FreeX.App.Host/MainWindow.xaml"
OUT = "src/FreeX.App.Host/Ribbon/FreeXRibbonDefinition.cs"

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
                            mcn = mi.get(LK + "RibbonMetadata.CommandName") or mi.get("Header") or ""
                            if mcn:
                                menu.append(("item", mcn, mi.get(LK + "RibbonTooltip.KeyTip") or "",
                                             mi.get("InputGestureText") or ""))
                has_drop = d.get(LK + "RibbonMetadata.DropdownMenuButton") == "true" or len(menu) > 0
                items.append(("ctrl", tag, cn, keytip(d) or "", style, has_drop, menu))
        if curtab not in data:
            data[curtab] = []
            order.append(curtab)
        data[curtab].append((cid, items))

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
        mcn, mkt, mg = esc(m[1]), esc(m[2]), esc(m[3])
        args = f'"{mcn}", "{mcn}"'
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
    for cid, items in groups:
        ghdr = esc(grouphdr(cid, tab))
        # Cap control count (keep separators), and drop leading/trailing/duplicate separators.
        kept = []
        ctrl_count = 0
        for it in items:
            if it[0] == "sep":
                if kept and kept[-1][0] != "sep":
                    kept.append(it)
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
            _, kind, cn, k, style, has_drop, menu = it
            ic = icon(cn)
            cesc = esc(cn)
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
                cl.append(f'                .ComboBox("{cesc}", "{cesc}", c => c with {{ {", ".join(parts)} }})')
            elif kind == "CheckBox":
                cl.append(f'                .CheckBox("{cesc}", "{cesc}", b => b with {{ Icon = new RibbonCommandIcon(RibbonCommandIconKind.{ic}) }})')
            elif is_large(cn):
                cl.append(f'                .Large("{cesc}", "{cesc}", Ico.{ic}, "{kk}"{drop})')
            elif style in ICON_STYLES or kind == "ToggleButton":
                if kind == "ToggleButton":
                    cl.append(f'                .IconToggle("{cesc}", "{cesc}", Ico.{ic}, "{kk}")')
                else:
                    cl.append(f'                .Icon("{cesc}", "{cesc}", Ico.{ic}, "{kk}"{drop})')
            else:
                cl.append(f'                .Medium("{cesc}", "{cesc}", Ico.{ic}, "{kk}"{drop})')
        body = "\n".join(cl)
        out.append(f'            .Group("{cid}", "{ghdr}", null, priority: {gp},')
        out.append("                g => g")
        out.append(body + ")")
        gp -= 10
    out.append("        )")
out.append("        .Build();")
out.append("}")

open(OUT, "w", encoding="utf-8").write("\n".join(out) + "\n")
print("wrote", OUT)
print("tabs:", sum(1 for t in (mainorder + ctxorder) if t in data and any(g[1] for g in data[t])))
print("groups:", sum(1 for t in data for g in data[t] if g[1]))
print("controls:", sum(len(g[1][:16]) for t in data for g in data[t] if g[1]))
