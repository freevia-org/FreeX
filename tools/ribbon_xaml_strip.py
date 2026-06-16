"""Replace the hand-authored RibbonTabs TabControl content with a minimal tab-header-only TabControl.
The visible ribbon content is now produced at runtime by RibbonWpfRenderer from the declarative
definition; the named content controls live in the generated MainWindow.RibbonBackplane.g.cs."""
import re

PATH = "src/FreeX.App.Host/MainWindow.xaml"
text = open(PATH, encoding="utf-8").read()

start = text.index('<TabControl x:Name="RibbonTabs"')

# Find the matching </TabControl> by depth from start.
depth = 0
i = start
while i < len(text):
    m = re.compile(r"<TabControl[\s>]|</TabControl>").search(text, i)
    if not m:
        raise SystemExit("no close")
    if m.group() == "</TabControl>":
        depth -= 1
        if depth == 0:
            end = m.end()
            break
    else:
        depth += 1
    i = m.end()

block = text[start:end]

# Keep the TabControl opening tag and the ItemsPanel; drop everything else.
open_tag = block[:block.index(">") + 1]
items_panel_match = re.search(r"<TabControl\.ItemsPanel>.*?</TabControl\.ItemsPanel>", block, re.S)
items_panel = items_panel_match.group(0) if items_panel_match else ""

# Extract each TabItem opening tag, normalized to self-closing.
tab_items = []
for m in re.finditer(r"<TabItem\b[^>]*?/?>", block):
    tag = m.group(0).rstrip()
    tag = re.sub(r"/?>$", "", tag).rstrip()
    tab_items.append("            " + tag + " />")

new_block = (
    open_tag + "\n"
    + "            " + items_panel + "\n"
    + "\n".join(tab_items) + "\n"
    + "        </TabControl>"
)

text = text[:start] + new_block + text[end:]
open(PATH, "w", encoding="utf-8").write(text)
print(f"Replaced RibbonTabs block ({end - start} chars) with minimal {len(tab_items)}-tab header.")
