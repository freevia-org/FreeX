"""Generate the hidden control backplane: stub fields for the named ribbon controls that C# code
references, so the hand-authored ribbon XAML can be deleted while the code still compiles and holds
state. The declarative ribbon is the visible UI; these controls are invisible state/handler holders."""
import re
import xml.etree.ElementTree as ET
import glob

XAML = "src/FreeX.App.Host/MainWindow.xaml"
OUT = "src/FreeX.App.Host/MainWindow.RibbonBackplane.g.cs"

src = open(XAML, encoding="utf-8").read()
src2 = re.sub(r"\{local:Loc Key=([A-Za-z0-9_]+)\}", r"\1", src)
root = ET.fromstring(src2)
P = "{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
X = "{http://schemas.microsoft.com/winfx/2006/xaml}"
LK = "{clr-namespace:FreeX.App.Host}"

tabs = next(e for e in root.iter(P + "TabControl") if e.get(X + "Name") == "RibbonTabs")

# Map clr type for each XAML element tag.
TYPE = {
    "ToggleButton": "System.Windows.Controls.Primitives.ToggleButton",
    "Button": "System.Windows.Controls.Button",
    "ComboBox": "System.Windows.Controls.ComboBox",
    "CheckBox": "System.Windows.Controls.CheckBox",
    "Rectangle": "System.Windows.Shapes.Rectangle",
    "StackPanel": "System.Windows.Controls.StackPanel",
    "ContextMenu": "System.Windows.Controls.ContextMenu",
    "MenuItem": "System.Windows.Controls.MenuItem",
    "AutomationInvokeButton": "AutomationInvokeButton",
}

named = []
for e in tabs.iter():
    n = e.get(X + "Name")
    if not n:
        continue
    tag = e.tag.split("}")[-1]
    if tag in ("TabItem", "TabControl"):
        continue
    cmd = e.get(LK + "RibbonMetadata.CommandName")
    named.append((n, tag, cmd))

# Keep only controls referenced in C# code.
code = ""
for f in glob.glob("src/FreeX.App.Host/**/*.cs", recursive=True):
    if "obj" in f or "bin" in f or f.endswith(".g.cs"):
        continue
    try:
        code += open(f, encoding="utf-8", errors="ignore").read()
    except Exception:
        pass

referenced = [(n, t, c) for n, t, c in named
              if t in TYPE and re.search(r"(?<![A-Za-z0-9_])" + re.escape(n) + r"(?![A-Za-z0-9_])", code)]

out = []
out.append("using System.Collections.Generic;")
out.append("using System.Windows.Controls;")
out.append("")
out.append("namespace FreeX.App.Host;")
out.append("")
out.append("// Auto-generated hidden control backplane. These named controls used to live in the")
out.append("// hand-authored ribbon XAML; the visible ribbon is now declarative (RibbonWpfRenderer).")
out.append("// They remain as invisible state/handler holders so existing code compiles and runs;")
out.append("// their state is mirrored onto the rendered ribbon by WireDeclarativeStateSync, and they")
out.append("// serve as the 'sender' for handlers invoked through the native command registry.")
out.append("public partial class MainWindow")
out.append("{")
for n, t, c in referenced:
    out.append(f"    private readonly {TYPE[t]} {n} = new();")
out.append("")
out.append("    /// <summary>Backplane controls that carry a ribbon CommandName, keyed by it.</summary>")
out.append("    private readonly Dictionary<string, Control> RibbonBackplaneControls = new(System.StringComparer.Ordinal);")
out.append("")
out.append("    private void InitializeRibbonControlBackplane()")
out.append("    {")
for n, t, c in referenced:
    if c and TYPE[t].endswith(("Button", "ComboBox", "CheckBox", "MenuItem")) or (c and t in ("ToggleButton", "Button", "ComboBox", "CheckBox", "MenuItem", "AutomationInvokeButton")):
        out.append(f'        RibbonMetadata.SetCommandName({n}, "{c}");')
        out.append(f'        RibbonBackplaneControls["{c}"] = {n};')
out.append("    }")
out.append("}")

open(OUT, "w", encoding="utf-8").write("\n".join(out) + "\n")
print("wrote", OUT, "with", len(referenced), "stub controls")
