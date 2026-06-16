"""Remove test methods that assert on the deleted ribbon XAML (they call ReadMainWindowXaml /
Extract*Element helpers). Their command-wiring coverage is now in FreeXRibbonHandlerMap + its
hygiene test. Keeps the non-XAML (planner/source) tests in each file."""
import re
import glob

FILES = sorted(set(
    glob.glob("tests/FreeX.App.Host.Tests/*CommandSourceTests.cs")
    + glob.glob("tests/FreeX.App.Host.Tests/RibbonTabParityTests.cs")
    + glob.glob("tests/FreeX.App.Host.Tests/MainWindowFontFormattingTests.cs")
))
XAML_MARKERS = ("ReadMainWindowXaml", "ExtractButtonElement", "ExtractMenuItemElement",
                "ExtractToggleButtonElement", "ExtractComboBoxElement", "ExtractCheckBoxElement",
                "ExtractAutomationInvokeButtonElement")


def method_blocks(text):
    """Yield (start, end) spans of each test method including its leading attributes."""
    for m in re.finditer(r"\n(?P<indent>[ \t]*)\[(?:Fact|Theory)\]", text):
        attr_start = m.start() + 1
        # Walk back over preceding attribute lines (e.g. [InlineData], [Trait]) — none above [Fact]/[Theory]
        # Find the opening brace of the method body after the attributes.
        brace = text.find("{", m.end())
        if brace < 0:
            continue
        depth = 0
        i = brace
        in_str = False
        in_char = False
        while i < len(text):
            ch = text[i]
            if in_str:
                if ch == "\\":
                    i += 2
                    continue
                if ch == '"':
                    in_str = False
            elif in_char:
                if ch == "\\":
                    i += 2
                    continue
                if ch == "'":
                    in_char = False
            elif ch == '"':
                in_str = True
            elif ch == "'":
                in_char = True
            elif ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    yield attr_start, i + 1
                    break
            i += 1


for path in FILES:
    text = open(path, encoding="utf-8").read()
    blocks = list(method_blocks(text))
    remove = []
    for s, e in blocks:
        body = text[s:e]
        if any(mk in body for mk in XAML_MARKERS):
            remove.append((s, e))
    # Remove from the end to keep indices valid.
    for s, e in reversed(remove):
        text = text[:s] + text[e:]
    # Collapse leftover blank-line runs.
    text = re.sub(r"\n[ \t]*\n[ \t]*\n+", "\n\n", text)
    open(path, "w", encoding="utf-8").write(text)
    print(f"{path}: removed {len(remove)} XAML-dependent test methods")
