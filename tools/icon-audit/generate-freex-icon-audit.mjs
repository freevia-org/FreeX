import fs from "node:fs";
import path from "node:path";

const root = process.cwd();
const reportDir = path.join(root, "docs", "icon-audit");
const defs = [
  path.join(root, "src", "FreeX.Ribbon.Definitions", "HomeRibbonDefinition.cs"),
  path.join(root, "src", "FreeX.Ribbon.Definitions", "FreeXRibbonDefinition.cs"),
];
const iconDir = path.join(root, "src", "FreeX.Ribbon.Definitions", "Resources", "CommandIconsSvg");

const svgFiles = new Set(
  fs.readdirSync(iconDir)
    .filter((name) => name.toLowerCase().endsWith(".svg"))
    .map((name) => name.toLowerCase())
);

const aliases = {
  "increase-font-size": "grow-font",
  "decrease-font-size": "shrink-font",
  "accounting-number-format": "accounting-currency",
  "increase-decimal-places": "increase-decimal",
  "decrease-decimal-places": "decrease-decimal",
  "merge-and-center": "merge-center",
  "sort-and-filter": "sort",
  "find-and-select": "find",
  "insert-link": "hyperlink",
  "header-and-footer": "header-footer",
  "pictures": "picture",
  "advanced": "advanced-filter",
  "clear-filter": "clear-filter",
  "page-setup-dialog": "page-setup",
  "view-gridlines": "gridlines",
  "print-gridlines": "print-gridlines",
  "view-headings": "headings",
  "print-headings": "print-headings",
  "object-fill": "fill",
  "object-outline": "outline-color",
  "object-size": "size",
  "object-rotate": "rotate",
  "shape-gradient": "gradient",
  "shape-fill": "fill",
  "shape-outline": "outline-color",
  "shape-effects": "effects",
  "object-effects": "effects",
  "selection-pane": "selection-pane",
  "ink-to-shape": "shapes",
  "ink-to-math": "math-trig",
  "math": "math-trig",
  "recently-used": "recent",
  "date": "date-time",
  "lookup": "lookup-reference",
  "formula-auditing": "evaluate-formula",
  "calculation": "calculate-now",
  "workbook-stats": "statistics",
  "workbook-statistics": "statistics",
  "accessibility": "accessibility-checker",
  "refresh-pivot": "refresh-all",
  "show-details": "show-detail",
  "links-and-objects": "hyperlink",
  "help-online": "help",
  "contact-support": "contact-support",
  "what-s-new": "what-s-new",
  "whats-new": "what-s-new",
  "about-freex": "about",
  "side-by-side": "view-side-by-side",
  "sync-scrolling": "synchronous-scrolling",
  "reset-position": "reset-window-position",
  "100": "zoom-to-100",
  "save-as": "save-as",
  "export-pdf-xps": "export",
  "page-orientation": "page-orientation",
  "hide": "hide-sheet",
  "unhide": "unhide-sheet",
  "add-watch": "watch-add",
  "delete-watch": "watch-delete",
  "reapply": "reapply-filter",
  "sort-a-to-z": "sort-ascending",
  "sort-z-to-a": "sort-descending",
  "pick-from-drop-down-list": "pick-from-dropdown",
  "macro": "macros",
  "queries-connections": "queries-connections",
  "check-for-updates": "check-for-updates",
  "pin-to-list": "pin-to-list",
  "unpin-from-list": "unpin-from-list",
  "remove-from-list": "remove-from-list",
  "rename": "rename-sheet",
  "duplicate": "duplicate-sheet",
  "plus-minus-buttons": "show-detail",
  "buttons": "show-detail",
};

const expectedCues = [
  [/pivottable/i, "Excel pivot table/table grid with green accent"],
  [/recommended charts/i, "Excel recommendation card with chart columns"],
  [/column chart|bar chart|line chart|area chart|pie|doughnut|scatter|bubble|radar|stock/i, "Excel chart-family pictogram, not a reused generic chart"],
  [/sparkline/i, "Excel sparkline type glyph matching line, column, or win/loss"],
  [/timeline/i, "Excel timeline/filter control with calendar cue"],
  [/link|hyperlink/i, "Excel chain link"],
  [/comment/i, "Excel comment bubble/card cue"],
  [/text box/i, "Text box with insertion/text cue"],
  [/header|footer/i, "Page header/footer sheet cue"],
  [/symbol/i, "Omega/symbol cue"],
  [/pictures?|format picture|crop/i, "Picture thumbnail/crop handles"],
  [/shapes?|shape/i, "Office shape/fill/outline/effects cue"],
  [/bring forward|send backward/i, "Layered objects with direction cue"],
  [/selection pane/i, "Pane/list with object visibility cue"],
  [/rotate/i, "Object with curved rotate arrow"],
  [/size|resize|scale/i, "Object/page with sizing arrows"],
  [/theme/i, "Office theme palette/font/effects cue"],
  [/margin/i, "Page with margin guides"],
  [/orientation/i, "Portrait/landscape page cue"],
  [/paper size|page setup|page layout/i, "Page sheet cue"],
  [/print/i, "Printer/page print cue"],
  [/break/i, "Page break dashed line cue"],
  [/background/i, "Sheet with picture/background cue"],
  [/gridlines/i, "Grid with gridline visibility/print cue"],
  [/headings/i, "Row/column heading cue"],
  [/autosum|sum|subtotal|grand total/i, "Sigma total cue"],
  [/financial/i, "Financial function/category cue"],
  [/logical/i, "Logical function/category cue"],
  [/text functions/i, "Text function/category cue"],
  [/date|time/i, "Calendar/clock cue"],
  [/lookup|reference|select data/i, "Range selector or lookup cue"],
  [/math|trig/i, "Math function/category cue"],
  [/name manager|define name|use in formula|create from selection/i, "Named range tag/formula cue"],
  [/trace precedents|trace dependents/i, "Cell dependency arrows"],
  [/remove arrows/i, "Dependency arrows with remove mark"],
  [/show formulas|evaluate formula|calculation|calculate/i, "Formula/calculation cue"],
  [/watch window/i, "Watch window/formula monitor cue"],
  [/get data|queries|connections|data source/i, "Database/table import cue"],
  [/refresh|reapply/i, "Refresh arrows"],
  [/sort/i, "A/Z sort direction cue"],
  [/filter|advanced/i, "Filter funnel cue, advanced filter when applicable"],
  [/text to columns/i, "Split columns cue"],
  [/flash fill/i, "Flash/fill cue"],
  [/remove duplicates/i, "Duplicate table rows with remove mark"],
  [/validation/i, "Data validation check/error cue"],
  [/consolidate/i, "Combined ranges cue"],
  [/what-if|goal seek|scenario|data table|forecast/i, "Analysis/forecast cue"],
  [/group|ungroup|show detail|hide detail/i, "Outline bracket plus/minus cue"],
  [/spelling/i, "ABC checkmark cue"],
  [/statistics/i, "Workbook stats/table summary cue"],
  [/accessibility/i, "Accessibility checker cue"],
  [/alt text/i, "Image/text accessibility cue"],
  [/note/i, "Excel note/comment cue"],
  [/protect|allow users/i, "Lock/protection cue"],
  [/share/i, "Share/person/link cue"],
  [/normal|custom views|view/i, "Workbook view cue"],
  [/zoom|100%/i, "Magnifier/zoom cue"],
  [/window|arrange|freeze|split|side by side|synchronous|switch|reset window|hide|unhide/i, "Window pane arrangement cue"],
  [/help|feedback|diagnostics|updates|about|legal/i, "Help/info/support cue"],
  [/chart title|data label|trendline|error bars|axis|legend|marker|combo chart|move chart/i, "Excel contextual chart element cue"],
  [/table name|table styles|banded|first column|last column|total row|filter button|convert to range/i, "Excel table-design cue specific to the table option"],
  [/field|pivotchart|calculated|pivot/i, "Excel PivotTable/PivotChart contextual cue"],
];

const manual = new Map([
  ["advanced", ["Inconsistent", "Point this command at advanced-filter.svg or add an alias from advanced to advanced-filter."]],
  ["page-setup-dialog", ["Inconsistent", "Alias page-setup-dialog to page-setup.svg or create a dialog-specific sheet icon."]],
  ["scale-width", ["Review", "Use a width-specific scale icon or remove the icon if Excel keeps this as a compact combo."]],
  ["scale-height", ["Review", "Use a height-specific scale icon or remove the icon if Excel keeps this as a compact combo."]],
  ["scale-percent", ["Review", "Use a percent/scale combo cue distinct from Percent Style."]],
  ["line-sparkline", ["Inconsistent", "Create a line-sparkline.svg that shows the line sparkline shape."]],
  ["column-sparkline", ["Inconsistent", "Create a column-sparkline.svg that shows vertical spark bars."]],
  ["win-loss-sparkline", ["Inconsistent", "Create a win-loss-sparkline.svg with positive/negative bars."]],
  ["select-data-source", ["Inconsistent", "Create a range-selector/data-source icon instead of falling back to Search."]],
  ["insert-timeline", ["Inconsistent", "Create insert-timeline.svg with a timeline/filter control, not just a date glyph."]],
  ["comment", ["Inconsistent", "Create comment.svg or map to new-comment/comment-note with an Excel comment bubble cue."]],
  ["show-comments", ["Inconsistent", "Create show-comments.svg with stacked comment/sidebar cue."]],
  ["delete-comment", ["Inconsistent", "Create delete-comment.svg or reuse a delete-comment alias."]],
  ["previous-comment", ["Inconsistent", "Create previous-comment.svg with comment bubble plus previous arrow."]],
  ["next-comment", ["Inconsistent", "Create next-comment.svg with comment bubble plus next arrow."]],
  ["view-gridlines", ["Inconsistent", "Alias to gridlines.svg or add view-gridlines.svg."]],
  ["view-headings", ["Inconsistent", "Alias to headings.svg or add view-headings.svg."]],
  ["scale-to-fit", ["Review", "Redraw with Excel's page scaling arrows; current cue is serviceable but generic."]],
]);

function stripHandler(value) {
  if (value.toLowerCase() === "clear#clearfilterbutton_click") return "Clear Filter";
  return value.split("#", 1)[0];
}

function toSlug(text) {
  let lower = text.trim().toLowerCase().replaceAll("&amp;", "and").replaceAll("&", "and");
  let out = "";
  let pendingDash = false;
  for (const ch of lower) {
    if ((ch >= "a" && ch <= "z") || (ch >= "0" && ch <= "9")) {
      if (pendingDash && out.length > 0) out += "-";
      out += ch;
      pendingDash = false;
    } else {
      pendingDash = out.length > 0;
    }
  }
  return out.replace(/^-+|-+$/g, "");
}

function slugCandidates(slug) {
  const all = [slug];
  const alias = aliases[slug];
  if (alias && alias !== slug) all.push(alias);
  return all;
}

function resolveIcon(commandId, size) {
  const slug = toSlug(stripHandler(commandId));
  for (const candidate of slugCandidates(slug)) {
    const sized = size <= 22 ? [`${candidate}-small`, candidate] : [`${candidate}-large`, candidate];
    for (const fileSlug of sized) {
      const file = `${fileSlug}.svg`;
      if (svgFiles.has(file.toLowerCase())) return file;
    }
  }
  return null;
}

function findAvailableAsset(commandId, label) {
  const slugs = [
    toSlug(commandId),
    toSlug(stripHandler(commandId)),
    toSlug(label),
  ].filter(Boolean);
  for (const slug of slugs) {
    for (const candidate of slugCandidates(slug)) {
      const file = `${candidate}.svg`;
      if (svgFiles.has(file.toLowerCase())) return file;
      const small = `${candidate}-small.svg`;
      if (svgFiles.has(small.toLowerCase())) return small;
    }
  }
  return null;
}

function parseDefinitions() {
  const commands = [];
  for (const file of defs) {
    const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);
    let tab = null;
    let group = null;
    let contextual = false;
    for (const rawLine of lines) {
      const line = rawLine.trim();
      const tabMatch = line.match(/\.(ContextualTab|Tab)\("([^"]+)",\s*"([^"]+)"/);
      if (tabMatch) {
        contextual = tabMatch[1] === "ContextualTab";
        tab = { id: tabMatch[2], header: tabMatch[3] };
        group = null;
        continue;
      }

      const groupMatch = line.match(/\.Group\("([^"]+)",\s*"([^"]+)"/);
      if (groupMatch) {
        group = { id: groupMatch[1], header: groupMatch[2] };
        continue;
      }

      const sized = line.match(/\.(Large|Medium|Icon|IconToggle)\("([^"]+)",\s*"([^"]+)",\s*Ico\.([A-Za-z0-9_]+)/);
      if (sized && tab && group) {
        commands.push({
          tab: tab.header,
          tabId: tab.id,
          contextual,
          group: group.header,
          layout: sized[1],
          commandId: sized[2],
          label: sized[3],
          iconKind: sized[4],
        });
        continue;
      }

      const checkOrCombo = line.match(/\.(CheckBox|ComboBox)\("([^"]+)",\s*"([^"]+)".*RibbonCommandIconKind\.([A-Za-z0-9_]+)/);
      if (checkOrCombo && tab && group) {
        commands.push({
          tab: tab.header,
          tabId: tab.id,
          contextual,
          group: group.header,
          layout: checkOrCombo[1],
          commandId: checkOrCombo[2],
          label: checkOrCombo[3],
          iconKind: checkOrCombo[4],
        });
      }
    }
  }
  return commands;
}

function expectedCue(label) {
  for (const [re, cue] of expectedCues) {
    if (re.test(label)) return cue;
  }
  return "Excel command-specific ribbon cue";
}

function classify(command, small, large, available) {
  const slug = toSlug(stripHandler(command.commandId));
  const baseSlug = toSlug(stripHandler(command.commandId));
  if (command.tab === "Data" && command.group === "Sort Filter" && command.label === "Clear") {
    if (small === "clear-filter.svg" || large === "clear-filter.svg") {
      return {
        status: "OK",
        action: "Keep clear-filter.svg for Data > Clear; generic clear.svg remains available for non-filter clearing.",
      };
    }

    return {
      status: "Inconsistent",
      action: "Use clear-filter.svg for Data > Clear and keep clear.svg for general clearing.",
    };
  }

  if (!small && !large && manual.has(slug)) {
    const [status, action] = manual.get(slug);
    return { status, action };
  }
  if (!small && !large && manual.has(baseSlug)) {
    const [status, action] = manual.get(baseSlug);
    return { status, action };
  }

  if (!small && !large) {
    return {
      status: command.contextual ? "Inconsistent" : "Review",
      action: `Add a command-specific SVG for ${toSlug(stripHandler(command.commandId)) || toSlug(command.label)}.svg or add an explicit alias; current runtime falls back to ${command.iconKind}.`,
    };
  }

  if (command.contextual) {
    return {
      status: "Review",
      action: "Redraw toward Excel contextual-tab language: denser, more pictorial, and specific to the selected object.",
    };
  }

  if (command.tab === "Home") {
    return {
      status: "OK",
      action: "Keep; only minor polish if neighboring Home icons are redrawn.",
    };
  }

  return {
    status: "OK",
    action: "Keep the command-specific asset; compare after higher-priority generic/contextual fixes.",
  };
}

function esc(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function iconCell(file, size, fallback) {
  if (file) {
    const rel = path.relative(reportDir, path.join(iconDir, file)).replaceAll(path.sep, "/");
    return `<img src="${esc(rel)}" width="${size}" height="${size}" alt="${esc(file)}">`;
  }
  return `<span class="fallback" style="width:${size}px;height:${size}px">${esc(fallback)}</span>`;
}

function statusClass(status) {
  return status.toLowerCase().replace(/\s+/g, "-");
}

function buildRows(commands) {
  return commands.map((command, index) => {
    const small = resolveIcon(command.commandId, 20);
    const large = resolveIcon(command.commandId, 32);
    const available = findAvailableAsset(command.commandId, command.label);
    const { status, action } = classify(command, small, large, available);
    return {
      index: index + 1,
      ...command,
      slug: toSlug(command.commandId),
      small,
      large,
      available,
      source: small || large ? "command SVG" : `fallback ${command.iconKind}`,
      expected: expectedCue(command.label),
      status,
      action,
    };
  });
}

function counts(rows) {
  return rows.reduce((acc, row) => {
    acc[row.status] = (acc[row.status] || 0) + 1;
    return acc;
  }, {});
}

function renderHtml(rows) {
  const byTab = Map.groupBy(rows, (row) => row.tab);
  const count = counts(rows);
  const generated = new Date().toISOString().slice(0, 10);
  const usedAssets = new Map();
  for (const row of rows) {
    for (const file of [row.small, row.large]) {
      if (!file) continue;
      usedAssets.set(file, (usedAssets.get(file) || 0) + 1);
    }
  }
  const highPriority = rows
    .filter((row) => row.status === "Inconsistent")
    .slice(0, 40)
    .map((row) => `<li><b>${esc(row.tab)} / ${esc(row.label)}</b>: ${esc(row.action)}</li>`)
    .join("\n");

  const sections = [...byTab.entries()].map(([tab, tabRows]) => {
    const tabCounts = counts(tabRows);
    const rowsHtml = tabRows.map((row) => `
      <tr class="${statusClass(row.status)}">
        <td class="num">${row.index}</td>
        <td>${esc(row.group)}</td>
        <td><b>${esc(row.label)}</b><br><code>${esc(row.commandId)}</code></td>
        <td>${esc(row.layout)}</td>
        <td class="icon">${iconCell(row.small, 20, row.iconKind)}</td>
        <td class="icon">${iconCell(row.large, 32, row.iconKind)}</td>
        <td>${esc(row.source)}${row.available && !row.small ? `<br><span class="note">asset exists: ${esc(row.available)}</span>` : ""}</td>
        <td>${esc(row.expected)}</td>
        <td><span class="badge">${esc(row.status)}</span></td>
        <td>${esc(row.action)}</td>
      </tr>`).join("\n");

    return `
      <section>
        <h2>${esc(tab)}${tabRows[0].contextual ? " (contextual)" : ""}</h2>
        <p class="tab-summary">${tabRows.length} commands: ${esc(JSON.stringify(tabCounts).replaceAll('"', ""))}</p>
        <table>
          <thead>
            <tr>
              <th>#</th>
              <th>Group</th>
              <th>Command</th>
              <th>Layout</th>
              <th>20px</th>
              <th>32px</th>
              <th>FreeX source</th>
              <th>Excel cue</th>
              <th>Status</th>
              <th>Suggested action</th>
            </tr>
          </thead>
          <tbody>${rowsHtml}</tbody>
        </table>
      </section>`;
  }).join("\n");

  const assetInventory = [...svgFiles].sort().map((file, index) => {
    const rel = path.relative(reportDir, path.join(iconDir, file)).replaceAll(path.sep, "/");
    const used = usedAssets.get(file) || 0;
    return `
      <tr class="${used ? "ok" : "review"}">
        <td class="num">${index + 1}</td>
        <td><code>${esc(file)}</code></td>
        <td class="icon"><img src="${esc(rel)}" width="20" height="20" alt="${esc(file)}"></td>
        <td class="icon"><img src="${esc(rel)}" width="32" height="32" alt="${esc(file)}"></td>
        <td>${used ? `${used} command renderings` : "not reached by current declarative ribbon"}</td>
      </tr>`;
  }).join("\n");

  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>FreeX Ribbon Icon Audit - ${generated}</title>
  <style>
    :root { color-scheme: light; --bad:#fff1f0; --review:#fff8e1; --ok:#eef8f1; --ink:#242424; --muted:#666; --line:#ddd; }
    body { margin: 24px; font: 13px/1.45 "Segoe UI", Arial, sans-serif; color: var(--ink); background: white; }
    h1 { font-size: 28px; margin: 0 0 8px; }
    h2 { font-size: 20px; margin: 32px 0 6px; border-bottom: 1px solid var(--line); padding-bottom: 4px; }
    p { max-width: 1100px; }
    code { font: 11px Consolas, monospace; color: #555; }
    table { width: 100%; border-collapse: collapse; table-layout: fixed; margin: 10px 0 28px; }
    th, td { border: 1px solid var(--line); padding: 6px 7px; vertical-align: middle; }
    th { position: sticky; top: 0; z-index: 1; background: #f5f5f5; text-align: left; }
    td.num { width: 34px; text-align: right; color: var(--muted); }
    td.icon { text-align: center; width: 58px; background: white; }
    img { image-rendering: auto; vertical-align: middle; }
    .fallback { display: inline-flex; align-items: center; justify-content: center; border: 1px dashed #aaa; border-radius: 3px; color: #777; font-size: 8px; overflow: hidden; background: #fafafa; }
    .inconsistent { background: var(--bad); }
    .review { background: var(--review); }
    .ok { background: var(--ok); }
    .badge { display: inline-block; min-width: 78px; text-align: center; border-radius: 999px; padding: 2px 8px; background: white; border: 1px solid #ccc; font-weight: 600; }
    .note { color: #8a4b00; font-size: 11px; }
    .summary { display: grid; grid-template-columns: repeat(4, max-content); gap: 10px; align-items: center; margin: 16px 0; }
    .pill { border: 1px solid #ccc; border-radius: 999px; padding: 4px 10px; background: #fafafa; }
    .tab-summary { color: var(--muted); margin: 0; }
    ol { max-width: 1180px; }
  </style>
</head>
<body>
  <h1>FreeX Ribbon Icon Audit</h1>
  <p>Generated ${generated}. This report audits the declarative FreeX ribbon commands against the current Microsoft Excel ribbon metaphor: command-specific pictorial icons, object-specific contextual tabs, and distinct small/large renderings. Microsoft art is not reproduced here; the comparison is semantic and stylistic.</p>
  <p>Reference baseline: Microsoft Support describes the full Excel ribbon as the default command surface, and Microsoft Learn describes contextual tabs as hidden ribbon tabs that appear for an object context, with Table Design as the Excel example.</p>
  <div class="summary">
    <span class="pill">${rows.length} command icons audited</span>
    <span class="pill">${svgFiles.size} SVG assets inventoried</span>
    <span class="pill">OK: ${count.OK || 0}</span>
    <span class="pill">Review: ${count.Review || 0}</span>
    <span class="pill">Inconsistent: ${count.Inconsistent || 0}</span>
  </div>
  <h2>Highest Priority Findings</h2>
  <ol>${highPriority}</ol>
  ${sections}
  <section>
    <h2>SVG Asset Inventory</h2>
    <p class="tab-summary">Every bundled SVG under <code>src/FreeX.Ribbon.Definitions/Resources/CommandIconsSvg</code>, shown at 20px and 32px. Usage counts are based on the declarative ribbon command audit above.</p>
    <table>
      <thead>
        <tr>
          <th>#</th>
          <th>SVG asset</th>
          <th>20px</th>
          <th>32px</th>
          <th>Ribbon usage</th>
        </tr>
      </thead>
      <tbody>${assetInventory}</tbody>
    </table>
  </section>
</body>
</html>`;
}

function renderMarkdown(rows) {
  const count = counts(rows);
  const generated = new Date().toISOString().slice(0, 10);
  const inconsistent = rows.filter((row) => row.status === "Inconsistent");
  const review = rows.filter((row) => row.status === "Review");
  const contextualInconsistent = inconsistent.filter((row) => row.contextual).length;
  const suffixedCommands = rows.filter((row) => row.commandId.includes("#")).length;
  const lines = [
    "# FreeX Ribbon Icon Audit",
    "",
    `Generated: ${generated}`,
    "",
    "## Summary",
    "",
    `- Commands audited: ${rows.length}`,
    `- SVG assets inventoried in HTML: ${svgFiles.size}`,
    `- OK: ${count.OK || 0}`,
    `- Review: ${count.Review || 0}`,
    `- Inconsistent: ${count.Inconsistent || 0}`,
    `- Contextual-tab inconsistent rows: ${contextualInconsistent}`,
    `- Handler-suffixed commands normalized before SVG lookup: ${suffixedCommands}`,
    "",
    "## Main Findings",
    "",
    "- The Home tab is mostly usable and has command-specific SVGs for the familiar Excel metaphors.",
    "- The non-Home tabs are mixed: many commands have good SVG names, but Data, Review, View, and Page Layout still have generic fallbacks for stateful commands.",
    "- Contextual tabs are the weakest area. Chart, table, and PivotTable commands often use fallback geometry or reuse a broad Table/PivotTable/Chart icon where Excel uses object-specific pictograms.",
    "- Command IDs containing `#..._Click` are normalized before SVG lookup, so handler-suffixed commands can reach matching assets such as `selection-pane.svg` and `remove-duplicates.svg`.",
    "",
    "## Suggested First Pass",
    "",
    "1. Keep expanding command-specific SVG coverage for contextual Chart, Table, Picture/Shape, and PivotTable tabs.",
    "2. Redraw contextual Chart Design/Format icons as a complete set: Chart Elements, Styles, Select Data, Change Chart Type, Move Chart, Fill/Border/Marker/Axes/Labels.",
    "3. Redraw Table Design and PivotTable contextual icons as complete sets, avoiding repeated generic Table/PivotTable glyphs.",
    "4. Fill Review comment icons: delete, previous, next, and show comments.",
    "5. Compare the final rendered ribbon tabs after SVG asset coverage is complete.",
    "",
    "Open `freex-icon-audit-2026-06-18.html` for the full command table and the SVG asset inventory, both with 20px and 32px renderings.",
    "",
    "## Inconsistent Rows",
    "",
    "| Tab | Group | Command | Runtime source | Suggested action |",
    "| --- | --- | --- | --- | --- |",
    ...inconsistent.map((row) => `| ${row.contextual ? `${row.tab} (contextual)` : row.tab} | ${row.group} | ${row.label} | ${row.source.replaceAll("|", "/")} | ${row.action.replaceAll("|", "/")} |`),
    "",
    "## Review Rows",
    "",
    "| Tab | Group | Command | Runtime source | Suggested action |",
    "| --- | --- | --- | --- | --- |",
    ...review.map((row) => `| ${row.contextual ? `${row.tab} (contextual)` : row.tab} | ${row.group} | ${row.label} | ${row.source.replaceAll("|", "/")} | ${row.action.replaceAll("|", "/")} |`),
  ];
  return lines.join("\n");
}

fs.mkdirSync(reportDir, { recursive: true });
const rows = buildRows(parseDefinitions());
fs.writeFileSync(path.join(reportDir, "freex-icon-audit-2026-06-18.html"), renderHtml(rows), "utf8");
fs.writeFileSync(path.join(reportDir, "freex-icon-audit-2026-06-18.md"), renderMarkdown(rows), "utf8");
fs.writeFileSync(path.join(reportDir, "freex-icon-audit-2026-06-18.json"), JSON.stringify(rows, null, 2), "utf8");
console.log(`Wrote ${rows.length} command rows to ${reportDir}`);
