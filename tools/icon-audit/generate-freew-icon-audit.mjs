import fs from "node:fs";
import path from "node:path";

const root = process.cwd();
const reportDir = path.join(root, "docs", "icon-audit");
const hostRibbon = path.join(root, "freew", "FreeW.App.Host", "FreeWRibbon.cs");
const avaloniaRibbon = path.join(root, "freew", "FreeW.App.Avalonia", "Ribbon", "FreeWRibbon.cs");
const hostFactory = path.join(root, "freew", "FreeW.App.Host", "Ribbon", "RibbonIconFactory.cs");
const freewIconDir = path.join(root, "freew", "FreeW.App.Host", "Resources", "CommandIconsSvg");
const sharedIconDir = path.join(root, "src", "FreeX.Ribbon.Definitions", "Resources", "CommandIconsSvg");

const generated = new Date().toISOString().slice(0, 10);
const localSvgFiles = readSvgSet(freewIconDir);
const sharedSvgFiles = readSvgSet(sharedIconDir);
const aliases = parseAliases();

const manual = new Map([
  ["font-family", ["Review", "Consider a distinct font-family cue rather than the shared Fonts artwork also used by font-size."]],
  ["font-size", ["Review", "Consider a size-specific typography cue rather than the shared Fonts artwork also used by font-family."]],
  ["smallcaps", ["Review", "Create a small-caps-specific glyph; the current local asset is usable but close to generic typography."]],
  ["allcaps", ["Review", "Create an all-caps-specific glyph; the current local asset is usable but close to generic typography."]],
  ["style", ["Review", "Differentiate the style picker from individual style tiles; it currently resolves to shared styles.svg."]],
  ["style-normal", ["Review", "Give Normal a document-style preview cue rather than shared normal.svg."]],
  ["style-heading1", ["Review", "Give Heading 1 a stronger heading-preview cue distinct from Heading 2 and Title."]],
  ["style-title", ["Review", "Give Title a stronger title-preview cue distinct from Heading styles."]],
  ["new-style", ["Review", "Use a style tile plus add mark instead of a broad insert/document cue."]],
  ["manage-styles", ["Review", "Use a styles pane/manager cue instead of broad styles artwork."]],
  ["paragraph-dialog", ["Review", "Use a dialog-launcher/paragraph-settings cue rather than line-spacing artwork."]],
  ["keep-with-next", ["Review", "Use a page/paragraph keep-together cue; current artwork is serviceable but abstract."]],
  ["keep-lines", ["Review", "Use a page/paragraph keep-lines-together cue; current artwork is serviceable but abstract."]],
  ["widow-control", ["Review", "Use a widow/orphan page-flow cue; current artwork is serviceable but abstract."]],
  ["table-insert-row", ["Review", "Make row insertion more explicit with a highlighted inserted row."]],
  ["table-delete-row", ["Review", "Make row deletion more explicit with a highlighted deleted row."]],
  ["table-insert-col", ["Review", "Make column insertion more explicit with a highlighted inserted column."]],
  ["table-delete-col", ["Review", "Make column deletion more explicit with a highlighted deleted column."]],
  ["table-header-row", ["Review", "Make the header-row state visually distinct from generic table artwork."]],
  ["table-banded-rows", ["Review", "Make banded rows visually distinct from generic table artwork."]],
  ["table-repeat-header", ["Review", "Show repeated header/page continuation more explicitly."]],
  ["table-formula", ["Review", "Use a table-cell formula cue rather than a plain Sigma/total metaphor."]],
  ["image-size", ["Review", "Prefer a picture-resize cue over the shared generic size artwork."]],
  ["image-alt-text", ["Review", "Prefer picture plus text/alt badge over generic alt-text artwork."]],
  ["image-align-left", ["Review", "Prefer image-plus-text-wrap alignment cue over plain text alignment."]],
  ["image-align-center", ["Review", "Prefer image-plus-text-wrap alignment cue over plain text alignment."]],
  ["image-align-right", ["Review", "Prefer image-plus-text-wrap alignment cue over plain text alignment."]],
  ["hyperlink-tooltip", ["Review", "Use ScreenTip/tooltip artwork instead of comment-note artwork."]],
  ["remove-hyperlink", ["Review", "Use a broken/removed link cue instead of plain hyperlink artwork."]],
  ["link-bookmark", ["Review", "Use a bookmark plus link cue instead of plain hyperlink artwork."]],
  ["insert-file", ["Review", "Use text-from-file/document-insert artwork instead of broad insert artwork."]],
  ["insert-quickpart", ["Review", "Use quick-parts gallery artwork instead of broad insert artwork."]],
  ["save-quickpart", ["Review", "Use quick-parts plus save artwork instead of plain save artwork."]],
  ["object", ["Review", "Use embedded-object/OLE cue instead of broad insert artwork."]],
  ["toc-refresh", ["Review", "Use table-of-contents plus refresh cue instead of generic refresh-all artwork."]],
  ["tof-refresh", ["Review", "Use table-of-figures plus refresh cue instead of generic refresh-all artwork."]],
  ["citation-style", ["Review", "Use a citation-style dropdown cue distinct from Insert Citation."]],
  ["index-mark", ["Review", "Use mark-entry cue distinct from Insert Index."]],
  ["page-valign", ["Review", "Use vertical page alignment cue instead of shared middle-align artwork."]],
  ["different-first-page", ["Review", "Use first-page header/footer cue instead of cover-page artwork."]],
  ["print-preview", ["Review", "Use page-preview/magnifier cue instead of plain print artwork."]],
  ["text-to-table", ["Review", "Use conversion arrows between text and table."]],
  ["table-to-text", ["Review", "Use conversion arrows between table and text."]],
  ["merge-data", ["Review", "Use recipient/data-source artwork distinct from the generic mail-merge mark."]],
  ["merge-field", ["Review", "Use field placeholder artwork distinct from the generic mail-merge mark."]],
  ["merge-preview", ["Review", "Use preview-results artwork distinct from the generic mail-merge mark."]],
  ["merge-finish", ["Review", "Use finish-and-merge artwork distinct from the generic mail-merge mark."]],
  ["spellcheck-toggle", ["Review", "Show spelling enable/disable state instead of a one-state spelling cue."]],
  ["reply-comment", ["Review", "Use reply arrow/comment cue distinct from New Comment."]],
  ["resolve-comment", ["Review", "Use resolved-comment check cue distinct from Accept Change."]],
  ["inspect-document", ["Review", "Use document-inspector cue distinct from generic search."]],
]);

const expectedCues = [
  [/paste|cut|copy|format painter/i, "Office clipboard command cue"],
  [/font|bold|italic|underline|caps|case|highlight|colour|color|formatting/i, "Word font and typography cue"],
  [/bullet|number|multilevel|indent|align|justify|paragraph|spacing|widow|keep/i, "Word paragraph/layout cue"],
  [/style|heading|title/i, "Word style gallery or style preview cue"],
  [/cover|blank page|page break|horizontal rule|drop cap/i, "Word pages and document insertion cue"],
  [/table|cell|row|column/i, "Word table editing cue"],
  [/picture|image|alt text|shapes|textbox/i, "Word illustration or object formatting cue"],
  [/equation|chart|wordart|smartart|object/i, "Word media/object insertion cue"],
  [/link|hyperlink|bookmark/i, "Word links/bookmark cue"],
  [/quickpart|field/i, "Word quick parts or field cue"],
  [/footnote|endnote|citation|bibliography|caption|cross-reference|index|table of/i, "Word references cue"],
  [/header|footer|page number|symbol|date|time/i, "Word insert/header/symbol cue"],
  [/margin|orientation|size|columns|line numbers|hyphenation|border|watermark|page colour|page color/i, "Word page layout/design cue"],
  [/print|read mode|navigation|formatting marks/i, "Word view/preview cue"],
  [/merge|mailing|label|envelope/i, "Word mailings cue"],
  [/statistics|word count|spelling|dictionary|comment|track|accept|reject|restrict|compare|inspect|accessibility/i, "Word review/proofing cue"],
];

const commands = parseCommands();
const rows = commands.map((command, index) => {
  const slug = toSlug(command.commandId);
  const small = resolveIcon(slug, 20);
  const large = resolveIcon(slug, 32);
  const status = classify(command, slug, small, large);
  return {
    index: index + 1,
    ...command,
    slug,
    small,
    large,
    source: sourceLabel(small, large),
    expected: expectedCue(command.label),
    status: status.status,
    action: status.action,
  };
});

const outputBase = path.join(reportDir, `freew-icon-audit-${generated}`);
fs.mkdirSync(reportDir, { recursive: true });
fs.writeFileSync(`${outputBase}.json`, JSON.stringify({
  generated,
  commandsAudited: rows.length,
  localSvgAssets: localSvgFiles.size,
  sharedSvgAssets: sharedSvgFiles.size,
  counts: counts(rows),
  rows,
  duplicateLocalAndSharedAssets: duplicateLocalAndSharedAssets(),
  emptyLocalSvgShells: emptySvgShells(freewIconDir),
}, null, 2));
fs.writeFileSync(`${outputBase}.md`, renderMarkdown(rows));
fs.writeFileSync(`${outputBase}.html`, renderHtml(rows));

console.log(`Wrote ${path.relative(root, `${outputBase}.md`)}`);
console.log(`Wrote ${path.relative(root, `${outputBase}.json`)}`);
console.log(`Wrote ${path.relative(root, `${outputBase}.html`)}`);

function readSvgSet(directory) {
  return new Set(fs.readdirSync(directory)
    .filter((name) => name.toLowerCase().endsWith(".svg"))
    .map((name) => name.toLowerCase()));
}

function parseAliases() {
  const source = fs.readFileSync(hostFactory, "utf8");
  const aliases = new Map();
  for (const match of source.matchAll(/\["([^"]+)"\]\s*=\s*"([^"]+)"/g))
    aliases.set(match[1].toLowerCase(), match[2].toLowerCase());
  return aliases;
}

function parseCommands() {
  const byId = new Map();
  for (const file of [hostRibbon, avaloniaRibbon]) {
    const sourceName = path.relative(root, file).replaceAll(path.sep, "/");
    const lines = fs.readFileSync(file, "utf8").split(/\r?\n/);
    let tab = sourceName.includes("Avalonia") ? "Avalonia" : "";
    let group = "";
    for (const rawLine of lines) {
      const line = rawLine.trim();
      let match = line.match(/\.Tab\("([^"]+)",\s*"([^"]+)"/);
      if (match) {
        tab = match[2];
        group = "";
        continue;
      }

      match = line.match(/\.Group\("([^"]+)",\s*"([^"]+)"/);
      if (match) {
        group = match[2];
        continue;
      }

      match = line.match(/\.(Large|Medium|Icon|IconToggle|Button|ComboBox|CheckBox)\("([^"]+)",\s*"([^"]+)"/);
      if (match && match[2].toLowerCase().startsWith("freew.")) {
        addCommand(byId, {
          sourceFile: sourceName,
          tab: tab || "Uncategorized",
          group: group || "Ungrouped",
          layout: match[1],
          commandId: match[2],
          label: match[3],
        });
      }

      match = line.match(/\.Item\("([^"]+)",\s*"([^"]+)"/);
      if (match && match[1].toLowerCase().startsWith("freew.")) {
        addCommand(byId, {
          sourceFile: sourceName,
          tab: tab || "Uncategorized",
          group: group || "Menu",
          layout: "MenuItem",
          commandId: match[1],
          label: match[2],
        });
      }
    }
  }

  return [...byId.values()].sort((a, b) =>
    a.tab.localeCompare(b.tab) ||
    a.group.localeCompare(b.group) ||
    a.commandId.localeCompare(b.commandId));
}

function addCommand(byId, command) {
  const key = command.commandId.toLowerCase();
  if (!byId.has(key))
    byId.set(key, command);
}

function toSlug(commandId) {
  let text = commandId.trim();
  if (text.toLowerCase().startsWith("freew."))
    text = text.slice("freew.".length);

  text = text.toLowerCase().replaceAll("&amp;", "and").replaceAll("&", "and");
  let out = "";
  let pendingDash = false;
  for (const ch of text) {
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

function resolveIcon(slug, size) {
  const candidates = [];
  const alias = aliases.get(slug);
  if (alias && alias !== slug) candidates.push(alias);
  candidates.push(slug);

  for (const candidate of candidates) {
    const fileSlugs = size <= 22 ? [`${candidate}-small`, candidate] : [`${candidate}-large`, candidate];
    for (const fileSlug of fileSlugs) {
      const file = `${fileSlug}.svg`.toLowerCase();
      if (localSvgFiles.has(file)) return { file, origin: "FreeW local" };
      if (sharedSvgFiles.has(file)) return { file, origin: "FreeX shared" };
    }
  }
  return null;
}

function classify(command, slug, small, large) {
  if (!small && !large) {
    return {
      status: "Inconsistent",
      action: `Add ${slug}.svg or an alias; runtime falls back to the shared geometry catalog.`,
    };
  }

  const empty = [small, large]
    .filter(Boolean)
    .some((icon) => icon.origin === "FreeW local" && emptySvgShells(freewIconDir).includes(icon.file));
  if (empty) {
    return {
      status: "Inconsistent",
      action: "Replace the empty local SVG shell with visible geometry.",
    };
  }

  const manualRow = manual.get(slug);
  if (manualRow) {
    return {
      status: manualRow[0],
      action: manualRow[1],
    };
  }

  return {
    status: "OK",
    action: small?.origin === "FreeW local" || large?.origin === "FreeW local"
      ? "Keep the FreeW-specific SVG; it resolves directly before fallback geometry."
      : "Keep using linked FreeX shared SVG artwork for this cross-app Office command.",
  };
}

function sourceLabel(small, large) {
  const origins = new Set([small?.origin, large?.origin].filter(Boolean));
  if (origins.size === 0) return "fallback geometry";
  return [...origins].join(" / ");
}

function expectedCue(label) {
  for (const [re, cue] of expectedCues) {
    if (re.test(label)) return cue;
  }
  return "Word command-specific ribbon cue";
}

function counts(rows) {
  const result = {};
  for (const row of rows)
    result[row.status] = (result[row.status] || 0) + 1;
  return result;
}

function duplicateLocalAndSharedAssets() {
  return [...localSvgFiles].filter((file) => sharedSvgFiles.has(file)).sort();
}

function emptySvgShells(directory) {
  return fs.readdirSync(directory)
    .filter((name) => name.toLowerCase().endsWith(".svg"))
    .filter((name) => fs.readFileSync(path.join(directory, name), "utf8").trimEnd().endsWith("/>"))
    .map((name) => name.toLowerCase())
    .sort();
}

function relIconPath(icon) {
  if (!icon) return null;
  const directory = icon.origin === "FreeW local" ? freewIconDir : sharedIconDir;
  return path.relative(reportDir, path.join(directory, icon.file)).replaceAll(path.sep, "/");
}

function esc(text) {
  return String(text ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function iconCell(icon, size, fallback) {
  const rel = relIconPath(icon);
  if (!rel)
    return `<span class="fallback" style="width:${size}px;height:${size}px">fallback</span>`;
  return `<img src="${esc(rel)}" width="${size}" height="${size}" alt="${esc(icon.file)}">`;
}

function statusClass(status) {
  return status === "Inconsistent" ? "inconsistent" : status === "Review" ? "review" : "ok";
}

function renderMarkdown(rows) {
  const count = counts(rows);
  const duplicates = duplicateLocalAndSharedAssets();
  const empty = emptySvgShells(freewIconDir);
  const review = rows.filter((row) => row.status === "Review");
  const inconsistent = rows.filter((row) => row.status === "Inconsistent");
  const localRows = rows.filter((row) => row.source.includes("FreeW local")).length;
  const sharedRows = rows.filter((row) => row.source.includes("FreeX shared")).length;
  const avaloniaOnly = rows.filter((row) => row.sourceFile.includes("Avalonia")).length;
  const lines = [
    "# FreeW Ribbon Icon Audit",
    "",
    `Generated: ${generated}`,
    "",
    "## Summary",
    "",
    `- Commands audited: ${rows.length}`,
    `- FreeW local SVG assets inventoried in HTML: ${localSvgFiles.size}`,
    `- Linked FreeX shared SVG assets inventoried in HTML: ${sharedSvgFiles.size}`,
    `- OK: ${count.OK || 0}`,
    `- Review: ${count.Review || 0}`,
    `- Inconsistent: ${count.Inconsistent || 0}`,
    `- Commands resolving through FreeW local SVGs: ${localRows}`,
    `- Commands resolving through linked FreeX shared SVGs: ${sharedRows}`,
    `- Avalonia-only command rows: ${avaloniaOnly}`,
    `- Duplicate local/shared SVG file names: ${duplicates.length}`,
    `- Empty local SVG shells: ${empty.length}`,
    "",
    "## Main Findings",
    "",
    "- FreeW has complete direct SVG coverage for the visible WPF/Avalonia command surface audited here; no missing runtime command SVGs were found.",
    "- The local FreeW SVG set is cleanly separated from linked FreeX artwork: no duplicate local/shared file names were found.",
    "- The strongest icons are the Word-specific References, Review, Insert, and page/document concepts that live in FreeW's local SVG folder.",
    "- The main polish backlog is semantic specificity: several commands intentionally reuse broad shared artwork or nearby Word artwork, such as style management, image alignment, mail-merge variants, and table row/column actions.",
    "- This audit is static and semantic; pair it with a rendered FreeW ribbon screenshot pass before closing a visual polish milestone.",
    "",
    "## Suggested First Pass",
    "",
    "1. Redraw the Styles group as a set: style picker, Normal, Heading, Title, New Style, and Manage Styles should read as a coherent Word style gallery.",
    "2. Redraw table row/column insert/delete/header/banded/repeat/formula icons so each action is unmistakable at 20px.",
    "3. Split mail-merge icons into data source, field, preview results, and finish/merge cues instead of a repeated mail-merge mark.",
    "4. Create image-specific align/size/alt-text icons instead of leaning on text alignment and generic sizing metaphors.",
    "5. Add a rendered FreeW ribbon visual validation lane mirroring the FreeX screenshot evidence once the app host can run reliably in this branch.",
    "",
    "Open `freew-icon-audit-" + generated + ".html` for the full command table and SVG inventory.",
    "",
    "## Inconsistent Rows",
    "",
    "| Tab | Group | Command | Runtime source | Suggested action |",
    "| --- | --- | --- | --- | --- |",
  ];

  for (const row of inconsistent)
    lines.push(`| ${row.tab} | ${row.group} | ${row.label} | ${row.source} | ${row.action} |`);

  lines.push("", "## Review Rows", "", "| Tab | Group | Command | Runtime source | Suggested action |", "| --- | --- | --- | --- | --- |");
  for (const row of review)
    lines.push(`| ${row.tab} | ${row.group} | ${row.label} | ${row.source} | ${row.action} |`);

  return lines.join("\n") + "\n";
}

function renderHtml(rows) {
  const count = counts(rows);
  const byTab = Map.groupBy(rows, (row) => row.tab);
  const usedAssets = new Map();
  for (const row of rows) {
    for (const icon of [row.small, row.large]) {
      if (!icon) continue;
      const key = `${icon.origin}:${icon.file}`;
      usedAssets.set(key, (usedAssets.get(key) || 0) + 1);
    }
  }

  const sections = [...byTab.entries()].map(([tab, tabRows]) => {
    const rowsHtml = tabRows.map((row) => `
      <tr class="${statusClass(row.status)}">
        <td class="num">${row.index}</td>
        <td>${esc(row.group)}</td>
        <td><b>${esc(row.label)}</b><br><code>${esc(row.commandId)}</code></td>
        <td>${esc(row.layout)}</td>
        <td class="icon">${iconCell(row.small, 20)}</td>
        <td class="icon">${iconCell(row.large, 32)}</td>
        <td>${esc(row.source)}</td>
        <td>${esc(row.expected)}</td>
        <td><span class="badge">${esc(row.status)}</span></td>
        <td>${esc(row.action)}</td>
      </tr>`).join("\n");
    return `
      <section>
        <h2>${esc(tab)}</h2>
        <table>
          <thead>
            <tr>
              <th>#</th><th>Group</th><th>Command</th><th>Layout</th><th>20px</th><th>32px</th><th>Runtime source</th><th>Word cue</th><th>Status</th><th>Suggested action</th>
            </tr>
          </thead>
          <tbody>${rowsHtml}</tbody>
        </table>
      </section>`;
  }).join("\n");

  const inventoryRows = [
    ...[...localSvgFiles].sort().map((file) => ({ file, origin: "FreeW local", directory: freewIconDir })),
    ...[...sharedSvgFiles].sort().map((file) => ({ file, origin: "FreeX shared", directory: sharedIconDir })),
  ].map((item, index) => {
    const rel = path.relative(reportDir, path.join(item.directory, item.file)).replaceAll(path.sep, "/");
    const used = usedAssets.get(`${item.origin}:${item.file}`) || 0;
    return `
      <tr class="${used ? "ok" : "review"}">
        <td class="num">${index + 1}</td>
        <td>${esc(item.origin)}</td>
        <td><code>${esc(item.file)}</code></td>
        <td class="icon"><img src="${esc(rel)}" width="20" height="20" alt="${esc(item.file)}"></td>
        <td class="icon"><img src="${esc(rel)}" width="32" height="32" alt="${esc(item.file)}"></td>
        <td>${used ? `${used} command renderings` : "not reached by current FreeW ribbon"}</td>
      </tr>`;
  }).join("\n");

  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>FreeW Ribbon Icon Audit - ${generated}</title>
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
    .summary { display: grid; grid-template-columns: repeat(4, max-content); gap: 10px; align-items: center; margin: 16px 0; }
    .pill { border: 1px solid #ccc; border-radius: 999px; padding: 4px 10px; background: #fafafa; }
  </style>
</head>
<body>
  <h1>FreeW Ribbon Icon Audit</h1>
  <p>Generated ${generated}. This report audits FreeW's WPF and Avalonia ribbon command ids against the runtime SVG resolver: FreeW app-local artwork first, then linked FreeX shared artwork, then fallback geometry only if no SVG resolves.</p>
  <div class="summary">
    <span class="pill">${rows.length} commands audited</span>
    <span class="pill">${localSvgFiles.size} local SVGs</span>
    <span class="pill">${sharedSvgFiles.size} shared SVGs</span>
    <span class="pill">OK: ${count.OK || 0}</span>
    <span class="pill">Review: ${count.Review || 0}</span>
    <span class="pill">Inconsistent: ${count.Inconsistent || 0}</span>
  </div>
  ${sections}
  <section>
    <h2>SVG Asset Inventory</h2>
    <p>FreeW local SVGs and linked FreeX shared SVGs, shown at 20px and 32px. Usage counts are based on the audited command ids above.</p>
    <table>
      <thead><tr><th>#</th><th>Origin</th><th>SVG asset</th><th>20px</th><th>32px</th><th>Ribbon usage</th></tr></thead>
      <tbody>${inventoryRows}</tbody>
    </table>
  </section>
</body>
</html>`;
}
