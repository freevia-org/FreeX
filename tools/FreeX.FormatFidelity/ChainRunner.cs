using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreeX.App.Services;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Free.ToolsShared;
using FreeX.ToolsShared;

namespace FreeX.FormatFidelity;

internal sealed class ChainOutcome
{
    public required Chain Chain { get; init; }
    public string? HopError { get; init; }
    public List<DimensionResult> Results { get; init; } = new();
}

/// <summary>
/// Executes a conversion chain: load source once (F0), snapshot the reference, then drive each hop
/// via <c>FileFormatResolver.FindSaveAdapter/FindOpenAdapter</c> over the default catalog (never
/// instantiating adapters directly), and finally compare Fn against F0 gated by the chain cap.
/// </summary>
internal sealed class ChainRunner
{
    private readonly string _scratchDir;
    private readonly IReadOnlyList<IFileAdapter> _adapters = WorkbookFileAdapterCatalog.CreateDefaultAdapters();

    public ChainRunner(string scratchDir) => _scratchDir = scratchDir;

    public ChainOutcome Run(Chain chain)
    {
        Workbook source;
        try
        {
            using var s = File.OpenRead(chain.SourcePath);
            // Source is always xlsx in Phase 0; resolve its open adapter from the catalog.
            var openAdapter = FileFormatResolver.FindOpenAdapter(_adapters, Path.GetExtension(chain.SourcePath), out _)
                ?? throw new InvalidOperationException($"no open adapter for {chain.SourcePath}");
            source = openAdapter.Load(s);
        }
        catch (Exception ex)
        {
            return new ChainOutcome { Chain = chain, HopError = $"source load failed: {Describe(ex)}" };
        }

        if (chain.PromoteDataSheet)
            Chains.PromoteLargestDataSheet(source);

        if (chain.MutateBeforeSnapshot)
            Chains.ForceRebuildMutation(source);

        var reference = WorkbookSnapshot.Capture(source);

        Workbook current = source;
        int hopIndex = 0;
        foreach (var hop in chain.Hops)
        {
            hopIndex++;
            try
            {
                // When a hop names a specific Save-As type (e.g. "CSV UTF-8" vs plain ".csv"), resolve by
                // format name so several adapters sharing one extension can each be exercised.
                var saveAdapter = (hop.FormatName is { } sn
                        ? FileFormatResolver.FindSaveAdapterByFormatName(_adapters, hop.Extension, sn, out _)
                        : FileFormatResolver.FindSaveAdapter(_adapters, hop.Extension, out _))
                    ?? throw new InvalidOperationException($"no save adapter for {hop.Extension}");
                var loadAdapter = (hop.FormatName is { } ln
                        ? FileFormatResolver.FindOpenAdapterByFormatName(_adapters, hop.Extension, ln, out _)
                        : FileFormatResolver.FindOpenAdapter(_adapters, hop.Extension, out _))
                    ?? throw new InvalidOperationException($"no open adapter for {hop.Extension}");

                var tempFile = Path.Combine(
                    _scratchDir,
                    $"{ToolFileNameSanitizer.ReplaceNonAlphaNumericWithUnderscore(chain.Name)}.hop{hopIndex}{hop.Extension}");

                if (saveAdapter is XlsxFileAdapter && string.Equals(hop.ProfileKey, "xlsx-rebuilt", StringComparison.OrdinalIgnoreCase))
                    XlsxFileAdapter.DetachSourcePackage(current);

                using (var outStream = File.Create(tempFile))
                    saveAdapter.Save(current, outStream);

                using var inStream = File.OpenRead(tempFile);
                current = loadAdapter.Load(inStream);
            }
            catch (Exception ex)
            {
                return new ChainOutcome { Chain = chain, HopError = $"hop {hopIndex} ({hop.ProfileKey}) failed: {Describe(ex)}" };
            }
        }

        var got = WorkbookSnapshot.Capture(current);
        var results = DimensionComparer.Compare(reference, got, chain.HopProfiles);
        return new ChainOutcome { Chain = chain, Results = results };
    }

    private static string Describe(Exception ex)
    {
        var top = ex.StackTrace?.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("at ", StringComparison.Ordinal))?.Trim();
        return $"{ex.GetType().Name}: {ex.Message}" + (top is null ? "" : $" @ {top}");
    }
}
