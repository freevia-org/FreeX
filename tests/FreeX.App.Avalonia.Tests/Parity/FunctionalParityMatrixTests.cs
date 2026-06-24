using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Xunit;

namespace FreeX.App.Avalonia.Tests.Parity;

/// <summary>
/// Emits the functional parity artifacts and gates against Avalonia (Linux) regressions.
///
/// The gate (<see cref="NoAvaloniaRegressions_EveryWpfHandledCommandIsAlsoHandledByAvalonia"/>) asserts that
/// every canonical command id the WPF shell handles is also handled by the Avalonia shell, except for the
/// small, explicitly-documented <see cref="IntentionalLinuxOmissions"/> allowlist.
/// </summary>
public sealed class FunctionalParityMatrixTests
{
    /// <summary>
    /// Canonical ribbon command ids the WPF shell binds that the Avalonia (Linux) shell does NOT bind through
    /// the canonical ribbon command registry — the documented, intentional Linux omissions. Each entry is one
    /// of three honest classes (annotated below): genuinely Windows-only features, ids the Avalonia shell
    /// serves through a NATIVE MENU / parent-button path instead of a per-item ribbon command, and a couple of
    /// label-alias mismatches. The gate subtracts exactly this set; a guard test asserts every entry is a real
    /// WPF-handled shared-definition id, so the allowlist can never mask a regression with a stale id.
    ///
    /// NOTE on scope: this is a measurement of canonical-ribbon-command-registry binding. An id listed here as
    /// "served via native menu" IS reachable by the Linux user (e.g. Next/Previous Note, theme submenus, shape
    /// effects) — it is simply not wired under this canonical id in the ribbon registry. Closing these is a
    /// matter of re-keying the existing handlers, tracked as follow-up; the allowlist documents the current
    /// honest state so the gate stays a true no-regression tripwire.
    /// </summary>
    public static readonly IReadOnlySet<string> IntentionalLinuxOmissions = new HashSet<string>(StringComparer.Ordinal)
    {
        // ─────────────────────────────────────────────────────────────────────────────────────────────
        // (1) GENUINELY WINDOWS-ONLY
        // ─────────────────────────────────────────────────────────────────────────────────────────────
        // View ▸ Window multi-window features that depend on the Windows window manager / MDI behavior.
        "View Side by Side", "Synchronous Scrolling",
        // JIS B-series paper sizes: the Linux Page Setup wires "B4"/"B5"; the WPF "(JIS)"-suffixed ids are
        // Win32 PaperKind names with no Linux equivalent.
        "B4 (JIS)", "B5 (JIS)",

    };

    [Fact]
    public void EmitArtifacts()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);

        WriteJson(rows);
        WriteMarkdown(rows);
        WriteSurfaceCatalogJson();
    }

    [Fact]
    public void NoAvaloniaRegressions_EveryWpfHandledCommandIsAlsoHandledByAvalonia()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);

        var regressions = rows
            .Where(r => r.Status == FunctionalParityMatrix.ParityStatus.AvaloniaMissing)
            .Select(r => r.CommandId)
            .Where(id => !IntentionalLinuxOmissions.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.True(regressions.Length == 0,
            "Avalonia (Linux) shell is missing handlers the WPF shell has (not on the documented "
            + "IntentionalLinuxOmissions allowlist):" + Environment.NewLine
            + string.Join(Environment.NewLine, regressions.Select(id => "  - " + id)));
    }

    [Fact]
    public void Allowlist_OnlyContainsRealWpfHandledSharedCommands()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var sharedIds = SurfaceCatalog.CanonicalCommandIds.ToHashSet(StringComparer.Ordinal);

        var stale = IntentionalLinuxOmissions
            .Where(id => !sharedIds.Contains(id) || !wpf.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.True(stale.Length == 0,
            "IntentionalLinuxOmissions entries that are not real WPF-handled shared-definition commands "
            + "(remove them so the allowlist cannot mask a genuine gap): " + string.Join(", ", stale));
    }

    private static void WriteJson(IReadOnlyList<FunctionalParityMatrix.Row> rows)
    {
        var total = rows.Count;
        int Count(FunctionalParityMatrix.ParityStatus s) => rows.Count(r => r.Status == s);

        var sb = new StringBuilder();
        sb.Append('{').Append('\n');
        sb.Append("  \"schema\": \"freex.parity.functional.v1\",\n");
        sb.Append("  \"summary\": {\n");
        sb.Append("    \"totalCommands\": ").Append(total).Append(",\n");
        sb.Append("    \"parity\": ").Append(Count(FunctionalParityMatrix.ParityStatus.Parity)).Append(",\n");
        sb.Append("    \"avaloniaMissing\": ").Append(Count(FunctionalParityMatrix.ParityStatus.AvaloniaMissing)).Append(",\n");
        sb.Append("    \"wpfMissing\": ").Append(Count(FunctionalParityMatrix.ParityStatus.WpfMissing)).Append(",\n");
        sb.Append("    \"bothMissing\": ").Append(Count(FunctionalParityMatrix.ParityStatus.BothMissing)).Append(",\n");
        sb.Append("    \"intentionalLinuxOmissions\": ").Append(IntentionalLinuxOmissions.Count).Append('\n');
        sb.Append("  },\n");
        sb.Append("  \"commands\": [\n");
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            sb.Append("    { \"id\": ").Append(JsonString(r.CommandId))
              .Append(", \"tab\": ").Append(JsonString(r.TabHeader))
              .Append(", \"group\": ").Append(JsonString(r.GroupHeader))
              .Append(", \"wpf\": ").Append(r.HasWpfHandler ? "true" : "false")
              .Append(", \"avalonia\": ").Append(r.HasAvaloniaHandler ? "true" : "false")
              .Append(", \"status\": ").Append(JsonString(StatusName(r.Status)))
              .Append(" }").Append(i == rows.Count - 1 ? "\n" : ",\n");
        }
        sb.Append("  ]\n");
        sb.Append("}\n");

        WriteArtifact("functional-parity.json", sb.ToString());
    }

    private static void WriteMarkdown(IReadOnlyList<FunctionalParityMatrix.Row> rows)
    {
        var total = rows.Count;
        int Count(FunctionalParityMatrix.ParityStatus s) => rows.Count(r => r.Status == s);

        var sb = new StringBuilder();
        sb.Append("# FreeX functional parity matrix (WPF vs Avalonia/Linux)\n\n");
        sb.Append("Generated by `FunctionalParityMatrixTests.EmitArtifacts`. Do not edit by hand.\n\n");
        sb.Append("Each row is a canonical command id the shared ribbon definition (`FreeXRibbon.Build()`) emits. ");
        sb.Append("`WPF` = the WPF host binds a Click handler for the id (`FreeXRibbonHandlerMap`). ");
        sb.Append("`Avalonia` = the Avalonia shell binds a ribbon-command-registry handler for the id ");
        sb.Append("(`AvaloniaCommandIdAdapter` + the shell's raw-canonical `ExtraCommands`, cell-style gallery, ");
        sb.Append("and chart factory).\n\n");
        sb.Append("> Caveat: coverage is measured at the *command-binding* layer of each shell. `WPF-MISSING` ");
        sb.Append("rows are dominated by controls the WPF host drives through a non-Click path (combo-boxes like ");
        sb.Append("Font / Number Format / Scale*, the Help-tab buttons, and conditional-format icon-set gallery ");
        sb.Append("items) rather than a genuine WPF feature gap. The gate only fires on `AVALONIA-MISSING`.\n\n");
        sb.Append("## Headline numbers\n\n");
        sb.Append("| Metric | Count |\n|---|---:|\n");
        sb.Append("| Total commands | ").Append(total).Append(" |\n");
        sb.Append("| PARITY (both) | ").Append(Count(FunctionalParityMatrix.ParityStatus.Parity)).Append(" |\n");
        sb.Append("| AVALONIA-MISSING | ").Append(Count(FunctionalParityMatrix.ParityStatus.AvaloniaMissing)).Append(" |\n");
        sb.Append("| WPF-MISSING | ").Append(Count(FunctionalParityMatrix.ParityStatus.WpfMissing)).Append(" |\n");
        sb.Append("| BOTH-MISSING | ").Append(Count(FunctionalParityMatrix.ParityStatus.BothMissing)).Append(" |\n");
        sb.Append("| Intentional Linux omissions (allowlisted) | ").Append(IntentionalLinuxOmissions.Count).Append(" |\n\n");

        sb.Append("## Matrix\n\n");
        sb.Append("| Command | Group | Tab | WPF | Avalonia | Status |\n");
        sb.Append("|---|---|---|:---:|:---:|---|\n");
        foreach (var r in rows.OrderBy(r => r.TabHeader, StringComparer.Ordinal)
                               .ThenBy(r => r.GroupHeader, StringComparer.Ordinal)
                               .ThenBy(r => r.CommandId, StringComparer.Ordinal))
        {
            sb.Append("| ").Append(MdCell(r.CommandId))
              .Append(" | ").Append(MdCell(r.GroupHeader))
              .Append(" | ").Append(MdCell(r.TabHeader))
              .Append(" | ").Append(r.HasWpfHandler ? "yes" : "—")
              .Append(" | ").Append(r.HasAvaloniaHandler ? "yes" : "—")
              .Append(" | ").Append(StatusName(r.Status))
              .Append(" |\n");
        }

        WriteArtifact("functional-parity.md", sb.ToString());
    }

    private static void WriteSurfaceCatalogJson()
    {
        var sb = new StringBuilder();
        sb.Append('{').Append('\n');
        sb.Append("  \"schema\": \"freex.parity.surface-catalog.v1\",\n");
        sb.Append("  \"ribbonCommands\": [\n");
        var cmds = SurfaceCatalog.RibbonCommands;
        for (var i = 0; i < cmds.Count; i++)
        {
            var c = cmds[i];
            sb.Append("    { \"id\": ").Append(JsonString(c.CommandId))
              .Append(", \"tab\": ").Append(JsonString(c.TabHeader))
              .Append(", \"group\": ").Append(JsonString(c.GroupHeader))
              .Append(", \"display\": ").Append(JsonString(c.Display))
              .Append(", \"keyTip\": ").Append(c.KeyTip is null ? "null" : JsonString(c.KeyTip))
              .Append(", \"contextual\": ").Append(c.IsContextual ? "true" : "false")
              .Append(", \"menuItem\": ").Append(c.IsMenuItem ? "true" : "false")
              .Append(" }").Append(i == cmds.Count - 1 ? "\n" : ",\n");
        }
        sb.Append("  ],\n");
        sb.Append("  \"dialogs\": ").Append(JsonArray(SurfaceCatalog.Dialogs)).Append(",\n");
        sb.Append("  \"backstagePanes\": ").Append(JsonArray(SurfaceCatalog.BackstagePanes)).Append(",\n");
        sb.Append("  \"contextMenus\": ").Append(JsonArray(SurfaceCatalog.ContextMenus)).Append('\n');
        sb.Append("}\n");

        WriteArtifact("surface-catalog.json", sb.ToString());
    }

    private static string StatusName(FunctionalParityMatrix.ParityStatus s) => s switch
    {
        FunctionalParityMatrix.ParityStatus.Parity => "PARITY",
        FunctionalParityMatrix.ParityStatus.AvaloniaMissing => "AVALONIA-MISSING",
        FunctionalParityMatrix.ParityStatus.WpfMissing => "WPF-MISSING",
        _ => "BOTH-MISSING",
    };

    private static void WriteArtifact(string fileName, string content)
    {
        var dir = Path.Combine(FunctionalParityMatrix.RepoRoot(), "docs", "parity");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    private static string JsonArray(IReadOnlyList<string> values)
        => "[" + string.Join(", ", values.Select(JsonString)) + "]";

    private static string JsonString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string MdCell(string value) => value.Replace("|", "\\|");
}
