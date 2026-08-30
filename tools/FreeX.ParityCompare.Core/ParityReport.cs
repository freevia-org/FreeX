using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeX.ParityCompare.Core;

/// <summary>
/// Renders a <see cref="ParityComparison"/> into the three report artifacts:
/// <c>parity-report.html</c> (side-by-side gallery), <c>parity-report.json</c>,
/// and <c>parity-report.md</c>. Image paths in the HTML are made relative to the
/// report directory so the report folder is self-contained / movable.
/// </summary>
public static class ParityReport
{
    public const string HtmlName = "parity-report.html";
    public const string JsonName = "parity-report.json";
    public const string MarkdownName = "parity-report.md";

    /// <summary>Relative link from the report dir to the functional-parity matrix on main.</summary>
    public const string FunctionalParityLink = "../../docs/parity/functional-parity.md";

    /// <summary>
    /// Write all three artifacts into <paramref name="reportDir"/>. If
    /// <paramref name="functionalParityMatrixPath"/> points to the existing
    /// <c>docs/parity/functional-parity.md</c>, it is copied next to the report so the
    /// HTML/MD links resolve and functional + visual parity live in one folder. Returns the HTML path.
    /// </summary>
    public static string WriteAll(ParityComparison c, string reportDir, string? functionalParityMatrixPath = null)
    {
        Directory.CreateDirectory(reportDir);
        File.WriteAllText(Path.Combine(reportDir, JsonName), BuildJson(c), Encoding.UTF8);
        File.WriteAllText(Path.Combine(reportDir, MarkdownName), BuildMarkdown(c), Encoding.UTF8);

        if (functionalParityMatrixPath != null && File.Exists(functionalParityMatrixPath))
        {
            try { File.Copy(functionalParityMatrixPath, Path.Combine(reportDir, "functional-parity.md"), overwrite: true); }
            catch { /* link is best-effort; report still valid without it */ }
        }

        var html = BuildHtml(c, reportDir);
        var htmlPath = Path.Combine(reportDir, HtmlName);
        File.WriteAllText(htmlPath, html, Encoding.UTF8);
        return htmlPath;
    }

    // -------------------------------------------------------------------
    // JSON
    // -------------------------------------------------------------------
    public static string BuildJson(ParityComparison c)
    {
        var dto = new
        {
            schema = "freex.parity.visual-report.v1",
            generated = c.GeneratedAt.ToString("o"),
            windows = new { platform = c.WindowsPlatform, shell = c.WindowsShell },
            linux = new { platform = c.LinuxPlatform, shell = c.LinuxShell },
            hardThreshold = c.HardThreshold,
            passed = c.Passed,
            summary = new
            {
                total = c.TotalSurfaces,
                both = c.BothCount,
                windowsOnly = c.WindowsOnlyCount,
                linuxOnly = c.LinuxOnlyCount,
                hardSurfaces = c.HardSurfaceCount,
                screens = c.ScreenSurfaceCount,
                staticTabs = c.StaticTabSurfaceCount,
                contextualTabs = c.ContextualTabSurfaceCount,
                overlays = c.OverlaySurfaceCount,
                dialogs = c.DialogSurfaceCount,
                hardRegressions = c.HardRegressions.Count,
            },
            surfaces = c.Surfaces.Select(s => new
            {
                id = s.Id,
                kind = s.Kind,
                presence = s.Presence.ToString(),
                severity = s.Severity.ToString(),
                diffPercent = s.DiffPercent,
                evaluation = Evaluation(s, c.HardThreshold),
                hardRegression = s.IsHardRegression(c.HardThreshold),
                windowsNote = s.WindowsNote,
                linuxNote = s.LinuxNote,
                error = s.Error,
            }),
        };
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
    }

    // -------------------------------------------------------------------
    // Markdown
    // -------------------------------------------------------------------
    public static string BuildMarkdown(ParityComparison c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# FreeX cross-platform visual parity report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {c.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}  ");
        sb.AppendLine($"Windows: `{c.WindowsShell}` ({c.WindowsPlatform}) — Linux: `{c.LinuxShell}` ({c.LinuxPlatform})  ");
        sb.AppendLine($"Grid fidelity threshold: **{c.HardThreshold:0.##}%** mean-pixel-diff  ");
        sb.AppendLine($"Result: **{(c.Passed ? "PASS" : "FAIL")}** ({c.HardRegressions.Count} hard regression(s))");
        sb.AppendLine();
        sb.AppendLine("See also: [functional parity matrix](functional-parity.md) (command-binding parity).");
        sb.AppendLine();
        sb.AppendLine("## Headline numbers");
        sb.AppendLine();
        sb.AppendLine("| Metric | Count |");
        sb.AppendLine("|---|---:|");
        sb.AppendLine($"| Total surfaces | {c.TotalSurfaces} |");
        sb.AppendLine($"| Present in both | {c.BothCount} |");
        sb.AppendLine($"| Windows-only (missing on Linux) | {c.WindowsOnlyCount} |");
        sb.AppendLine($"| Linux-only (missing on Windows) | {c.LinuxOnlyCount} |");
        sb.AppendLine($"| Grid (hard) surfaces | {c.HardSurfaceCount} |");
        sb.AppendLine($"| Demo screens | {c.ScreenSurfaceCount} |");
        sb.AppendLine($"| Static tab screens | {c.StaticTabSurfaceCount} |");
        sb.AppendLine($"| Contextual tab screens | {c.ContextualTabSurfaceCount} |");
        sb.AppendLine($"| Overlay / backstage screens | {c.OverlaySurfaceCount} |");
        sb.AppendLine($"| Dialog screens | {c.DialogSurfaceCount} |");
        sb.AppendLine($"| Hard regressions (> threshold) | {c.HardRegressions.Count} |");
        sb.AppendLine();
        sb.AppendLine("## Grid / content surfaces");
        sb.AppendLine();
        sb.AppendLine("These are the fidelity gate: both shells render the same document model.");
        sb.AppendLine("Diffs above the threshold indicate a genuine rendering defect.");
        sb.AppendLine();
        sb.AppendLine("| Surface | Diff% | Flag |");
        sb.AppendLine("|---|---:|---|");
        foreach (var s in c.Surfaces.Where(s => s.Severity == DiffSeverity.Hard))
        {
            string diff = s.DiffPercent is { } d ? d.ToString("0.00", CultureInfo.InvariantCulture) : "—";
            string flag = s.IsHardRegression(c.HardThreshold) ? "**REGRESSION**"
                : s.Presence == SurfacePresence.WindowsOnly ? "missing-on-linux"
                : s.Presence == SurfacePresence.LinuxOnly ? "missing-on-windows"
                : s.Error != null ? "error" : "ok";
            sb.AppendLine($"| `{s.Id}` | {diff} | {flag} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Demo Screens");
        sb.AppendLine();
        sb.AppendLine("These are same-size whole-window screenshots of the seeded demo workbook.");
        sb.AppendLine();
        AppendSurfaceTable(sb, c, s => s.Kind.Equals("screen", StringComparison.OrdinalIgnoreCase));

        sb.AppendLine("## Static Ribbon Tabs");
        sb.AppendLine();
        sb.AppendLine("These are same-size whole-window screenshots with the static ribbon tab selected.");
        sb.AppendLine();
        AppendSurfaceTable(sb, c, s => s.Kind.Equals("static-tab", StringComparison.OrdinalIgnoreCase));

        sb.AppendLine("## Contextual Ribbon Tabs");
        sb.AppendLine();
        sb.AppendLine("These are same-size whole-window screenshots after activating the tab's selection context.");
        sb.AppendLine();
        AppendSurfaceTable(sb, c, s => s.Kind.Equals("contextual-tab", StringComparison.OrdinalIgnoreCase));

        sb.AppendLine("## Overlays / Backstage");
        sb.AppendLine();
        sb.AppendLine("These rows include File/Backstage overlay surfaces. Missing rows are still useful: they mark work the Linux shell has not ported yet.");
        sb.AppendLine();
        AppendSurfaceTable(sb, c, s =>
            s.Kind.Equals("backstage", StringComparison.OrdinalIgnoreCase)
            || s.Kind.Equals("overlay", StringComparison.OrdinalIgnoreCase));

        sb.AppendLine("## Chrome surfaces (ribbon tabs + backstage)");
        sb.AppendLine();
        sb.AppendLine("> **Expected differences.** The Avalonia shell adds a compact toolbar row (Open/Save/Undo/Redo/…)");
        sb.AppendLine("> between the ribbon and the grid, and uses its own native title bar — so whole-window captures");
        sb.AppendLine("> of ribbon tabs and the backstage will always show structural chrome differences. These diffs");
        sb.AppendLine("> are informational and never gate-failing. Large values (> 20%) are annotated for review.");
        sb.AppendLine();
        sb.AppendLine("| Surface | Diff% | Flag |");
        sb.AppendLine("|---|---:|---|");
        foreach (var s in c.Surfaces.Where(s => s.Severity == DiffSeverity.Chrome))
        {
            string diff = s.DiffPercent is { } d ? d.ToString("0.00", CultureInfo.InvariantCulture) : "—";
            string flag = s.Presence == SurfacePresence.WindowsOnly ? "missing-on-linux"
                : s.Presence == SurfacePresence.LinuxOnly ? "missing-on-windows"
                : s.IsLargeChromeDiff() ? "large-chrome-diff"
                : s.Error != null ? "error" : "ok";
            sb.AppendLine($"| `{s.Id}` | {diff} | {flag} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Dialog / other surfaces");
        sb.AppendLine();
        sb.AppendLine("| Surface | Kind | Diff% | Flag |");
        sb.AppendLine("|---|---|---:|---|");
        foreach (var s in c.Surfaces.Where(s => s.Severity == DiffSeverity.Informational))
        {
            string diff = s.DiffPercent is { } d ? d.ToString("0.00", CultureInfo.InvariantCulture) : "—";
            string flag = s.Presence == SurfacePresence.WindowsOnly ? "missing-on-linux"
                : s.Presence == SurfacePresence.LinuxOnly ? "missing-on-windows"
                : s.Error != null ? "error" : "ok";
            sb.AppendLine($"| `{s.Id}` | {s.Kind} | {diff} | {flag} |");
        }
        return sb.ToString();
    }

    private static string Presence(SurfacePresence p) => p switch
    {
        SurfacePresence.Both => "both",
        SurfacePresence.WindowsOnly => "win-only",
        SurfacePresence.LinuxOnly => "linux-only",
        _ => "?",
    };

    // -------------------------------------------------------------------
    // HTML
    // -------------------------------------------------------------------
    public static string BuildHtml(ParityComparison c, string reportDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>FreeX cross-platform parity report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1b1b1b;background:#fafafa}");
        sb.AppendLine("h1{font-size:22px} .meta{color:#555;margin-bottom:18px}");
        sb.AppendLine(".summary{display:flex;gap:18px;flex-wrap:wrap;margin:12px 0 24px}");
        sb.AppendLine(".card{background:#fff;border:1px solid #ddd;border-radius:8px;padding:10px 16px;min-width:120px}");
        sb.AppendLine(".card .n{font-size:22px;font-weight:600} .card .l{font-size:12px;color:#666}");
        sb.AppendLine(".pass{color:#1a7f37;font-weight:600} .fail{color:#cf222e;font-weight:600}");
        sb.AppendLine(".row{background:#fff;border:1px solid #e0e0e0;border-radius:8px;margin:12px 0;padding:12px}");
        sb.AppendLine(".row.hard{border-left:5px solid #8250df} .row.reg{border-left:5px solid #cf222e}");
        sb.AppendLine(".row.miss{border-left:5px solid #bf8700} .row.chrome{border-left:5px solid #e6c84a}");
        sb.AppendLine(".hdr{display:flex;align-items:baseline;gap:12px;margin-bottom:8px}");
        sb.AppendLine(".id{font-weight:600;font-size:15px} .badge{font-size:11px;padding:2px 8px;border-radius:10px;background:#eee;color:#333}");
        sb.AppendLine(".badge.hard{background:#efe6ff;color:#6639ba} .badge.chrome{background:#fffbef;color:#5a4500} .badge.info{background:#eef;color:#3355bb}");
        sb.AppendLine(".diff{margin-left:auto;font-variant-numeric:tabular-nums;font-weight:600}");
        sb.AppendLine(".gallery{display:grid;grid-template-columns:1fr 1fr;gap:12px}");
        sb.AppendLine(".cell{text-align:center} .cell img{max-width:100%;border:1px solid #ccc;background:#fff}");
        sb.AppendLine(".cell .cap{font-size:12px;color:#666;margin-top:4px} .missing{color:#bf8700;padding:30px;border:1px dashed #ccc}");
        sb.AppendLine(".note{font-size:12px;color:#777;margin-top:6px} a{color:#0969da}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<h1>FreeX cross-platform visual parity</h1>");
        sb.AppendLine("<div class=\"meta\">");
        sb.Append($"Generated {Esc(c.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"))} &nbsp;|&nbsp; ");
        sb.Append($"Windows <code>{Esc(c.WindowsShell)}</code> ({Esc(c.WindowsPlatform)}) vs ");
        sb.Append($"Linux <code>{Esc(c.LinuxShell)}</code> ({Esc(c.LinuxPlatform)}) &nbsp;|&nbsp; ");
        sb.Append($"hard grid threshold {c.HardThreshold:0.##}% &nbsp;|&nbsp; ");
        sb.Append(c.Passed ? "<span class=\"pass\">PASS</span>" : "<span class=\"fail\">FAIL</span>");
        sb.AppendLine("<br><a href=\"functional-parity.md\">Functional parity matrix</a> (command-binding parity)</div>");

        sb.AppendLine("<div class=\"summary\">");
        Card(sb, c.TotalSurfaces, "surfaces");
        Card(sb, c.BothCount, "in both");
        Card(sb, c.WindowsOnlyCount, "win only");
        Card(sb, c.LinuxOnlyCount, "linux only");
        Card(sb, c.HardSurfaceCount, "grid (hard)");
        Card(sb, c.ScreenSurfaceCount, "screens");
        Card(sb, c.StaticTabSurfaceCount, "static tabs");
        Card(sb, c.ContextualTabSurfaceCount, "contextual tabs");
        Card(sb, c.OverlaySurfaceCount, "overlays");
        Card(sb, c.DialogSurfaceCount, "dialogs");
        Card(sb, c.HardRegressions.Count, "regressions");
        sb.AppendLine("</div>");

        sb.AppendLine("<h2>Grid / content surfaces</h2>");
        sb.AppendLine("<p style=\"color:#555;font-size:14px\">Fidelity gate — both shells render the same document model. Diffs above threshold indicate a genuine rendering defect.</p>");
        foreach (var s in c.Surfaces.Where(s => s.Severity == DiffSeverity.Hard))
            RenderSurfaceRow(sb, s, c.HardThreshold, reportDir);

        sb.AppendLine("<h2>Chrome surfaces — ribbon tabs &amp; backstage</h2>");
        sb.AppendLine("<div style=\"background:#fffbef;border:1px solid #e6c84a;border-radius:8px;padding:10px 14px;margin:8px 0 16px;font-size:13px;color:#5a4500\">");
        sb.AppendLine("<strong>Expected differences.</strong> The Avalonia shell adds a compact toolbar row (Open / Save / Undo / Redo / …) between the ribbon and the grid, and uses its own native title bar. Whole-window captures of ribbon tabs and the backstage therefore always show structural chrome differences. These diffs are <em>informational and never gate-failing</em>. Values above 20% are flagged for review.");
        sb.AppendLine("</div>");
        foreach (var s in c.Surfaces.Where(s => s.Severity == DiffSeverity.Chrome))
            RenderSurfaceRow(sb, s, c.HardThreshold, reportDir);

        sb.AppendLine("<h2>Demo screens</h2>");
        sb.AppendLine("<p style=\"color:#555;font-size:14px\">Same-size whole-window screenshots of the seeded demo workbook.</p>");
        foreach (var s in c.Surfaces.Where(s => s.Kind.Equals("screen", StringComparison.OrdinalIgnoreCase)))
            RenderSurfaceRow(sb, s, c.HardThreshold, reportDir);

        sb.AppendLine("<h2>Static ribbon tabs</h2>");
        sb.AppendLine("<p style=\"color:#555;font-size:14px\">Same-size whole-window screenshots with one static ribbon tab selected.</p>");
        foreach (var s in c.Surfaces.Where(s => s.Kind.Equals("static-tab", StringComparison.OrdinalIgnoreCase)))
            RenderSurfaceRow(sb, s, c.HardThreshold, reportDir);

        sb.AppendLine("<h2>Contextual ribbon tabs</h2>");
        sb.AppendLine("<p style=\"color:#555;font-size:14px\">Same-size whole-window screenshots with each contextual tab's selection context active.</p>");
        foreach (var s in c.Surfaces.Where(s => s.Kind.Equals("contextual-tab", StringComparison.OrdinalIgnoreCase)))
            RenderSurfaceRow(sb, s, c.HardThreshold, reportDir);

        sb.AppendLine("<h2>Overlays / Backstage</h2>");
        sb.AppendLine("<div style=\"background:#fffbef;border:1px solid #e6c84a;border-radius:8px;padding:10px 14px;margin:8px 0 16px;font-size:13px;color:#5a4500\">");
        sb.AppendLine("<strong>Evaluation note.</strong> Overlay rows are informational. Missing Linux rows usually mean the corresponding File/Backstage overlay has not been ported yet, not that the capture failed.");
        sb.AppendLine("</div>");
        foreach (var s in c.Surfaces.Where(s =>
                     s.Kind.Equals("backstage", StringComparison.OrdinalIgnoreCase)
                     || s.Kind.Equals("overlay", StringComparison.OrdinalIgnoreCase)))
            RenderSurfaceRow(sb, s, c.HardThreshold, reportDir);

        sb.AppendLine("<h2>Dialog / other surfaces</h2>");
        foreach (var s in c.Surfaces.Where(s => s.Severity == DiffSeverity.Informational))
            RenderSurfaceRow(sb, s, c.HardThreshold, reportDir);

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void RenderSurfaceRow(StringBuilder sb, SurfaceComparison s, double threshold, string reportDir)
    {
        bool reg = s.IsHardRegression(threshold);
        bool miss = s.Presence != SurfacePresence.Both;
        bool largeChrome = s.IsLargeChromeDiff();
        string rowCls = reg ? "row reg"
            : miss ? "row miss"
            : s.Severity == DiffSeverity.Hard ? "row hard"
            : s.Severity == DiffSeverity.Chrome ? "row chrome"
            : "row";
        sb.AppendLine($"<div class=\"{rowCls}\">");
        sb.AppendLine("<div class=\"hdr\">");
        sb.Append($"<span class=\"id\">{Esc(s.Id)}</span>");
        string badgeCls = s.Severity == DiffSeverity.Hard ? "hard"
            : s.Severity == DiffSeverity.Chrome ? "chrome"
            : "info";
        sb.Append($"<span class=\"badge {badgeCls}\">{s.Severity}</span>");
        sb.Append($"<span class=\"badge\">{Presence(s.Presence)}</span>");
        if (reg) sb.Append("<span class=\"badge\" style=\"background:#ffebe9;color:#cf222e\">REGRESSION</span>");
        if (largeChrome) sb.Append("<span class=\"badge\" style=\"background:#fffbef;color:#5a4500\">large chrome diff</span>");
        sb.Append($"<span class=\"badge\">{Esc(Evaluation(s, threshold))}</span>");
        string diffTxt = s.DiffPercent is { } d ? $"diff {d:0.00}%" : (miss ? "n/a" : "—");
        sb.Append($"<span class=\"diff\">{diffTxt}</span>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"gallery\">");
        ImageCell(sb, "Windows", s.WindowsImage, reportDir, s.WindowsNote);
        ImageCell(sb, "Linux", s.LinuxImage, reportDir, s.LinuxNote);
        sb.AppendLine("</div>");
        if (s.Error != null) sb.AppendLine($"<div class=\"note\">⚠ {Esc(s.Error)}</div>");
        sb.AppendLine("</div>");
    }

    private static void AppendSurfaceTable(StringBuilder sb, ParityComparison c, Func<SurfaceComparison, bool> predicate)
    {
        sb.AppendLine("| Surface | Kind | Diff% | Evaluation |");
        sb.AppendLine("|---|---|---:|---|");
        foreach (var s in c.Surfaces.Where(predicate))
        {
            string diff = s.DiffPercent is { } d ? d.ToString("0.00", CultureInfo.InvariantCulture) : "—";
            sb.AppendLine($"| `{s.Id}` | {s.Kind} | {diff} | {Evaluation(s, c.HardThreshold)} |");
        }
        sb.AppendLine();
    }

    private static string Evaluation(SurfaceComparison s, double hardThreshold)
    {
        if (s.Error is not null)
            return "capture/diff error";
        if (s.Presence == SurfacePresence.WindowsOnly)
            return "missing on Linux";
        if (s.Presence == SurfacePresence.LinuxOnly)
            return "missing on Windows";
        if (s.IsHardRegression(hardThreshold))
            return "hard regression";
        if (s.IsLargeChromeDiff())
            return "large visual difference";
        if (s.DiffPercent is { } d && d > 0)
            return "visual difference";
        return "matched";
    }

    private static void Card(StringBuilder sb, int n, string label) =>
        sb.AppendLine($"<div class=\"card\"><div class=\"n\">{n}</div><div class=\"l\">{Esc(label)}</div></div>");

    private static void ImageCell(StringBuilder sb, string label, string? imgPath, string reportDir, string? note)
    {
        sb.AppendLine("<div class=\"cell\">");
        if (imgPath != null)
        {
            string rel = MakeRelative(reportDir, imgPath);
            sb.AppendLine($"<img src=\"{Esc(rel)}\" alt=\"{Esc(label)}\">");
        }
        else
        {
            sb.AppendLine($"<div class=\"missing\">no {Esc(label)} capture</div>");
        }
        sb.Append($"<div class=\"cap\">{Esc(label)}");
        if (!string.IsNullOrEmpty(note)) sb.Append($" — {Esc(note)}");
        sb.AppendLine("</div></div>");
    }

    private static string MakeRelative(string fromDir, string toPath)
    {
        try
        {
            var rel = Path.GetRelativePath(fromDir, toPath);
            return rel.Replace('\\', '/');
        }
        catch { return toPath.Replace('\\', '/'); }
    }

    private static string Esc(string? s) => (s ?? "")
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
