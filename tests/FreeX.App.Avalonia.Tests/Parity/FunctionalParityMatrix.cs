using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests.Parity;

/// <summary>
/// The functional parity matrix: for every canonical command id the shared ribbon definition emits, whether
/// the WPF shell and the Avalonia shell each bind a handler, and the resulting parity classification.
///
/// WPF coverage combines the committed legacy handler-id snapshot (<c>docs/parity/wpf-handler-ids.txt</c>)
/// with typed semantic ids projected from <c>FreeXRibbonHandlerMap</c>. Avalonia coverage is
/// <see cref="SurfaceCatalog.AvaloniaBoundCanonicalIds"/>.
/// </summary>
public static class FunctionalParityMatrix
{
    public enum ParityStatus
    {
        /// <summary>Both shells handle the command.</summary>
        Parity,
        /// <summary>WPF handles it; Avalonia does not (a Linux regression unless allowlisted).</summary>
        AvaloniaMissing,
        /// <summary>Avalonia handles it; WPF does not.</summary>
        WpfMissing,
        /// <summary>Neither shell handles it (a declarative control with no handler in either shell).</summary>
        BothMissing,
    }

    public sealed record Row(
        string CommandId,
        string TabHeader,
        string GroupHeader,
        bool InSharedDefinition,
        bool HasWpfHandler,
        bool HasAvaloniaHandler,
        ParityStatus Status);

    /// <summary>Computes the full matrix over every distinct canonical id in the shared ribbon definition.</summary>
    public static IReadOnlyList<Row> Compute(IReadOnlySet<string> wpfHandlerIds)
    {
        var avalonia = SurfaceCatalog.AvaloniaBoundCanonicalIds;

        // First control/menu entry per canonical id supplies the tab/group label (stable, ordered).
        var firstEntry = new Dictionary<string, SurfaceCatalog.RibbonCommandEntry>(StringComparer.Ordinal);
        foreach (var entry in SurfaceCatalog.RibbonCommands)
            firstEntry.TryAdd(entry.CommandId, entry);

        var rows = new List<Row>();
        foreach (var id in SurfaceCatalog.CanonicalCommandIds)
        {
            var hasWpf = wpfHandlerIds.Contains(id);
            var hasAv = avalonia.Contains(id);
            var status = (hasWpf, hasAv) switch
            {
                (true, true) => ParityStatus.Parity,
                (true, false) => ParityStatus.AvaloniaMissing,
                (false, true) => ParityStatus.WpfMissing,
                _ => ParityStatus.BothMissing,
            };
            var meta = firstEntry[id];
            rows.Add(new Row(id, meta.TabHeader, meta.GroupHeader, InSharedDefinition: true, hasWpf, hasAv, status));
        }

        return rows;
    }

    /// <summary>
    /// Loads the committed legacy WPF inventory and augments it with typed semantic ids from the generated
    /// WPF handler map. The source projection is required while the preserved snapshot remains display-oriented.
    /// </summary>
    public static IReadOnlySet<string> LoadWpfHandlerIds()
    {
        var ids = File.ReadAllLines(WpfHandlerIdsPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(NormalizeLegacyWpfHandlerId)
            .ToHashSet(StringComparer.Ordinal);

        FreeXRibbonCommandSourceScanner.AddTypedCommandIds(
            File.ReadAllText(Path.Combine(
                RepoRoot(),
                "src",
                "FreeX.App.Host",
                "Ribbon",
                "FreeXRibbonHandlerMap.g.cs")),
            ids);
        return ids;
    }

    private static string NormalizeLegacyWpfHandlerId(string commandId) => commandId switch
    {
        "B4 (JIS)" => FreeXRibbonCommandIds.PageLayoutPaperSizeB4Jis,
        "B5 (JIS)" => FreeXRibbonCommandIds.PageLayoutPaperSizeB5Jis,
        "Copy Diagnostics#CopyDiagnosticsBtn_Click" => FreeXRibbonCommandIds.HelpCopyDiagnostics,
        "Legal Notices#LegalNoticesBtn_Click" => FreeXRibbonCommandIds.HelpLegalNotices,
        _ => commandId,
    };

    public static string WpfHandlerIdsPath => Path.Combine(RepoRoot(), "docs", "parity", "wpf-handler-ids.txt");

    /// <summary>Walks up from the test assembly location to the repo root (the dir holding <c>FreeX.slnx</c>).</summary>
    public static string RepoRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
}
