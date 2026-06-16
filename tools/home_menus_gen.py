"""Extract the Home tab's dropdown menus from the pre-deletion ribbon XAML (git history) into a
generated HomeRibbonMenus.g.cs that HomeRibbonDefinition references, so Home dropdowns + keytips work."""
import re
import xml.etree.ElementTree as ET

SRC = "tools/_old_mainwindow.xaml"
OUT = "src/FreeX.App.Host/Ribbon/HomeRibbonMenus.g.cs"
RESX = "src/FreeX.App.Host/Resources/Strings.resx"

# Resolve {local:Loc Key=X} headers to their en-US display strings so menu item labels read like
# Excel ("Values", "Paste Special...") — the keytip tests look menu items up by their resolved
# header. (WPF strips the leading "_" access-key mnemonic at runtime, so we strip it here too.)
loc = {}
for data in ET.parse(RESX).getroot().findall("data"):
    loc[data.get("name")] = (data.findtext("value") or "").replace("_", "")

def resolve(s):
    return loc.get(s, s) if s else s

text = open(SRC, encoding="utf-8").read()
text = re.sub(r"\{local:Loc Key=([A-Za-z0-9_]+)\}", r"\1", text)
root = ET.fromstring(text)
P = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
X = "{http://schemas.microsoft.com/winfx/2006/xaml}"
LK = "{clr-namespace:FreeX.App.Host}"

parents = {c: p for p in root.iter() for c in p}


def anc(e):
    out = []
    x = parents.get(e)
    while x is not None:
        out.append(x)
        x = parents.get(x)
    return out


# Find the Home TabItem.
home = None
for e in root.iter(P + "TabItem"):
    if e.get(LK + "RibbonMetadata.CatalogId") == "HomeTab":
        home = e
        break


def esc(s):
    return (s or "").replace("\\", "\\\\").replace('"', '\\"')


def safe(name):
    return re.sub(r"[^A-Za-z0-9]", "", name)


def child_menuitems(mi):
    # Direct MenuItem/Separator children (a MenuItem's submenu is its own immediate children).
    return [c for c in list(mi) if c.tag.split("}")[-1] in ("MenuItem", "Separator")]


def menu_items(cm, builder="m"):
    parts = []
    for mi in list(cm):
        t = mi.tag.split("}")[-1]
        if t == "Separator":
            if parts and not parts[-1].endswith("Separator()"):
                parts.append(".Separator()")
        elif t == "MenuItem":
            cn = mi.get(LK + "RibbonMetadata.CommandName") or resolve(mi.get("Header")) or ""
            if not cn:
                continue
            # The id binds to a handler (CommandName); the label is the resolved header so it reads
            # like Excel ("Values" not "Paste Values"). Fall back to the id when there is no header.
            label = resolve(mi.get("Header")) or cn
            kt = mi.get(LK + "RibbonTooltip.KeyTip") or ""
            ig = mi.get("InputGestureText") or ""
            kids = child_menuitems(mi)
            if kids:
                # Nested submenu: header item whose children carry their own keytips (Borders ->
                # Line Color -> color choices; Conditional Formatting -> Icon Sets -> 3 Arrows).
                inner = menu_items(mi, "sm")
                ktarg = f'"{esc(kt)}"' if kt else "null"
                parts.append(f'.Submenu("{esc(label)}", {ktarg}, sm => sm{inner})')
            else:
                args = f'"{esc(cn)}", "{esc(label)}"'
                if kt or ig:
                    args += f', "{esc(kt)}"'
                if ig:
                    args += f', "{esc(ig)}"'
                parts.append(f".Item({args})")
    while parts and parts[0].endswith("Separator()"):
        parts.pop(0)
    while parts and parts[-1].endswith("Separator()"):
        parts.pop()
    return "".join(parts)


methods = []
seen = set()
for d in home.iter():
    tag = d.tag.split("}")[-1]
    if tag not in ("Button", "ToggleButton"):
        continue
    if any(a.tag == P + "ContextMenu" for a in anc(d)):
        continue
    cn = d.get(LK + "RibbonMetadata.CommandName")
    if not cn or cn in seen:
        continue
    cmwrap = next((ch for ch in list(d) if ch.tag.split("}")[-1].endswith(".ContextMenu")), None)
    if cmwrap is None:
        continue
    cm = cmwrap.find(P + "ContextMenu")
    if cm is None:
        continue
    expr = menu_items(cm)
    if not expr:
        continue
    seen.add(cn)
    methods.append((cn, expr))

out = []
out.append("namespace FreeX.App.Host;")
out.append("")
out.append("/// <summary>Generated Home-tab dropdown menus (extracted from the original ribbon XAML).</summary>")
out.append("public static class HomeRibbonMenus")
out.append("{")
for cn, expr in methods:
    out.append(f"    public static void {safe(cn)}(RibbonMenuBuilder m) => m{expr};")
out.append("}")
open(OUT, "w", encoding="utf-8").write("\n".join(out) + "\n")
print("wrote", OUT, "with", len(methods), "Home menus:", ", ".join(cn for cn, _ in methods))
