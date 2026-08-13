using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Ribbon;
using FreeX.App.Services;
using FreeX.Ribbon.Definitions;

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
    /// Canonical ribbon command ids the WPF shell binds that the Avalonia shell intentionally does not bind
    /// through the canonical ribbon command registry. Keep this set explicit so any future platform-only gap
    /// must be documented before the no-regression gate can subtract it.
    /// </summary>
    public static readonly IReadOnlySet<string> IntentionalLinuxOmissions = new HashSet<string>(StringComparer.Ordinal)
    {
        // No intentional omissions are currently documented.
    };

    [Fact]
    public void EmitArtifacts()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);
        var classifications = FunctionalParityClassifier.Classify(rows);

        WriteJson(rows);
        WriteMarkdown(rows, classifications);
        WriteClassificationJson(rows, classifications);
        WriteClassificationMarkdown(rows, classifications);
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
    public void NoWpfRegressions_EveryAvaloniaHandledSharedCommandIsAlsoHandledByWpf()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var regressions = FunctionalParityClassifier.Classify(FunctionalParityMatrix.Compute(wpf))
            .Where(classification =>
                classification.MatrixRow.Status == FunctionalParityMatrix.ParityStatus.WpfMissing &&
                classification.Classification == FunctionalParityClassifier.ClassificationKind.RealBehaviorGap)
            .Select(classification => classification.MatrixRow.CommandId)
            .ToArray();

        regressions.Should().BeEmpty(
            "typed WPF handler ids and Avalonia bindings must compare through the same canonical identities");
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

    [Fact]
    public void AvaloniaMissing_AndIntentionalLinuxOmissions_RemainZero()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);

        var avaloniaMissing = rows
            .Where(r => r.Status == FunctionalParityMatrix.ParityStatus.AvaloniaMissing)
            .Select(r => r.CommandId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.True(avaloniaMissing.Length == 0,
            "The command/keytip parity slice should leave AVALONIA-MISSING at 0. Remaining rows:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, avaloniaMissing.Select(id => "  - " + id)));
        Assert.True(IntentionalLinuxOmissions.Count == 0,
            "No intentional Linux omissions should remain in the functional parity matrix.");
    }

    [Fact]
    public void PrioritizedCommandCleanupRows_RemainBoundInBothHosts()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf)
            .ToDictionary(row => row.CommandId, StringComparer.Ordinal);

        var prioritizedRows = new[]
        {
            FreeXRibbonCommandIds.HelpCopyDiagnostics,
            FreeXRibbonCommandIds.HelpLegalNotices,
            "Convert to Comments",
        };

        foreach (var id in prioritizedRows)
        {
            Assert.True(rows.TryGetValue(id, out var row), $"The shared ribbon definition should expose '{id}'.");
            Assert.True(row.HasWpfHandler, $"WPF should bind the prioritized cleanup command '{id}'.");
            Assert.True(row.HasAvaloniaHandler, $"Avalonia should bind the prioritized cleanup command '{id}'.");
            Assert.Equal(FunctionalParityMatrix.ParityStatus.Parity, row.Status);
        }
    }

    [Fact]
    public void Classifier_CoversEveryNonParityRow()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);
        var classifications = FunctionalParityClassifier.Classify(rows);
        var nonParityRows = rows
            .Where(r => r.Status != FunctionalParityMatrix.ParityStatus.Parity)
            .Select(r => r.CommandId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var classifiedRows = classifications
            .Select(c => c.MatrixRow.CommandId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(nonParityRows, classifiedRows);
        Assert.Empty(classifications
            .GroupBy(c => c.MatrixRow.CommandId, StringComparer.Ordinal)
            .Where(g => g.Count() != 1)
            .Select(g => g.Key));
    }

    [Fact]
    public void ConditionalFormatPopupRows_AreBoundInBothHosts()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);
        var catalogIds = ConditionalFormatPresetGalleryPlanner.PopupItems
            .Select(item => item.CommandId)
            .ToHashSet(StringComparer.Ordinal);

        var conditionalFormatRows = rows
            .Where(row => catalogIds.Contains(row.CommandId))
            .ToArray();

        Assert.Equal(catalogIds.Count, conditionalFormatRows.Length);
        Assert.All(conditionalFormatRows, row =>
        {
            Assert.True(row.HasWpfHandler);
            Assert.True(row.HasAvaloniaHandler);
            Assert.Equal(FunctionalParityMatrix.ParityStatus.Parity, row.Status);
        });

        Assert.Contains(conditionalFormatRows, row => row.CommandId == "Data Bars");
        Assert.Contains(conditionalFormatRows, row => row.CommandId == "Color Scales");
        Assert.Contains(conditionalFormatRows, row => row.CommandId == "3 Arrows");
        Assert.Contains(conditionalFormatRows, row => row.CommandId == "More Rules");
    }

    [Fact]
    public void AccountingSymbolRows_AreBoundInBothHosts()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);
        var catalogIds = HomeNumberFormatDropdownPlanner.AccountingSymbolOptions
            .Select(option => option.CommandId)
            .ToArray();

        var accountingRows = rows
            .Where(row => catalogIds.Contains(row.CommandId, StringComparer.Ordinal))
            .ToArray();

        Assert.Equal(4, accountingRows.Length);
        Assert.Equal(catalogIds.OrderBy(id => id, StringComparer.Ordinal), accountingRows
            .Select(row => row.CommandId)
            .OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(accountingRows, row =>
        {
            Assert.True(row.HasWpfHandler);
            Assert.True(row.HasAvaloniaHandler);
            Assert.Equal(FunctionalParityMatrix.ParityStatus.Parity, row.Status);
        });

        Assert.All(HomeNumberFormatDropdownPlanner.AccountingSymbolOptions, option =>
            Assert.Equal(option.NumberFormatCode, HomeNumberFormatDropdownPlanner.ResolveAccountingNumberFormatCode(option.Symbol)));
    }

    [Fact]
    public void FontBorderChoiceRows_AreBoundInBothHosts()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);
        var catalogIds = HomeFontBorderPopupCatalogPlanner.ClassifiedFontBorderRowsCovered;

        var fontBorderRows = rows
            .Where(row => catalogIds.Contains(row.CommandId))
            .ToArray();

        Assert.Equal(10, fontBorderRows.Length);
        Assert.Equal(catalogIds.OrderBy(id => id, StringComparer.Ordinal), fontBorderRows
            .Select(row => row.CommandId)
            .OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(fontBorderRows, row =>
        {
            Assert.True(row.HasWpfHandler);
            Assert.True(row.HasAvaloniaHandler);
            Assert.Equal(FunctionalParityMatrix.ParityStatus.Parity, row.Status);
        });
    }

    [Fact]
    public void Classifier_NonClickRows_AreExplicitlyBoundedInventoryNoise()
    {
        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var rows = FunctionalParityMatrix.Compute(wpf);
        var classifications = FunctionalParityClassifier.Classify(rows);

        var nonClickRows = classifications
            .Where(row => row.Classification == FunctionalParityClassifier.ClassificationKind.NonClickControlInventoryRow)
            .Select(row => row.MatrixRow.CommandId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Font",
                "Font Size",
                "Number Format",
                "Scale Height",
                "Scale Percent",
                "Scale Width",
            ],
            nonClickRows);

        var editableControls = classifications
            .Where(row => row.EvidenceKind == "shared-ribbon-combo-box-control")
            .Select(row => row.MatrixRow.CommandId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            FunctionalParityClassifier.EditableRibbonControlRows.OrderBy(id => id, StringComparer.Ordinal),
            editableControls);
        Assert.All(editableControls, id => Assert.Contains(SurfaceCatalog.RibbonCommands, entry =>
            entry.CommandId == id &&
            entry.ControlKind == "ComboBox" &&
            !entry.IsMenuItem));
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

    private static void WriteMarkdown(
        IReadOnlyList<FunctionalParityMatrix.Row> rows,
        IReadOnlyList<FunctionalParityClassifier.ClassifiedRow> classifications)
    {
        var total = rows.Count;
        int Count(FunctionalParityMatrix.ParityStatus s) => rows.Count(r => r.Status == s);

        var sb = new StringBuilder();
        sb.Append("# FreeX functional parity matrix (WPF vs Avalonia/Linux)\n\n");
        sb.Append("Generated by `FunctionalParityMatrixTests.EmitArtifacts`. Do not edit by hand.\n\n");
        sb.Append("Each row is a canonical command id the shared ribbon definition (`FreeXRibbon.Build()`) emits. ");
        sb.Append("`WPF` = the WPF host binds a Click handler for the id (`FreeXRibbonHandlerMap`). ");
        sb.Append("`Avalonia` = the Avalonia shell binds a ribbon-command-registry handler for the id ");
        sb.Append("(the shared ribbon definition + the shell's canonical endpoint dictionaries, cell-style gallery, ");
        sb.Append("and chart factory).\n\n");
        sb.Append("> Caveat: coverage is measured at the *command-binding* layer of each shell. Non-parity rows ");
        sb.Append("are classified in `functional-parity-classification.md/json` so command-binding inventory noise ");
        sb.Append("does not get mistaken for product behavior work. Gates reject real behavior gaps in either direction.\n\n");
        sb.Append("## Headline numbers\n\n");
        sb.Append("| Metric | Count |\n|---|---:|\n");
        sb.Append("| Total commands | ").Append(total).Append(" |\n");
        sb.Append("| PARITY (both) | ").Append(Count(FunctionalParityMatrix.ParityStatus.Parity)).Append(" |\n");
        sb.Append("| AVALONIA-MISSING | ").Append(Count(FunctionalParityMatrix.ParityStatus.AvaloniaMissing)).Append(" |\n");
        sb.Append("| WPF-MISSING | ").Append(Count(FunctionalParityMatrix.ParityStatus.WpfMissing)).Append(" |\n");
        sb.Append("| BOTH-MISSING | ").Append(Count(FunctionalParityMatrix.ParityStatus.BothMissing)).Append(" |\n");
        sb.Append("| Intentional Linux omissions (allowlisted) | ").Append(IntentionalLinuxOmissions.Count).Append(" |\n\n");

        sb.Append("## Non-parity classification summary\n\n");
        sb.Append("| Classification | Count |\n|---|---:|\n");
        foreach (var kind in FunctionalParityClassifier.OrderedKinds)
        {
            sb.Append("| ").Append(FunctionalParityClassifier.ClassificationLabel(kind))
              .Append(" | ").Append(classifications.Count(c => c.Classification == kind))
              .Append(" |\n");
        }
        sb.Append('\n');
        sb.Append("See `functional-parity-classification.md` for the prioritized implementation list and row-level rationale.\n\n");

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

    private static void WriteClassificationJson(
        IReadOnlyList<FunctionalParityMatrix.Row> rows,
        IReadOnlyList<FunctionalParityClassifier.ClassifiedRow> classifications)
    {
        int Count(FunctionalParityMatrix.ParityStatus s) => rows.Count(r => r.Status == s);
        var conditionalFormatPopupGalleryRows = classifications.Count(FunctionalParityClassifier.IsConditionalFormattingGalleryRow);
        var accountingSymbolPopupGalleryRows = classifications.Count(FunctionalParityClassifier.IsAccountingSymbolGalleryRow);
        var fontBorderPopupGalleryRows = classifications.Count(FunctionalParityClassifier.IsFontBorderGalleryRow);
        var handlerQualifiedHelpRows = classifications.Count(c => c.EvidenceKind == "handler-qualified-help-route");
        var sharedRibbonComboBoxRows = classifications.Count(c => c.EvidenceKind == "shared-ribbon-combo-box-control");

        var sb = new StringBuilder();
        sb.Append('{').Append('\n');
        sb.Append("  \"schema\": \"freex.parity.functional.classification.v1\",\n");
        sb.Append("  \"sourceSchema\": \"freex.parity.functional.v1\",\n");
        sb.Append("  \"summary\": {\n");
        sb.Append("    \"totalCommands\": ").Append(rows.Count).Append(",\n");
        sb.Append("    \"nonParityCommands\": ").Append(classifications.Count).Append(",\n");
        sb.Append("    \"avaloniaMissing\": ").Append(Count(FunctionalParityMatrix.ParityStatus.AvaloniaMissing)).Append(",\n");
        sb.Append("    \"wpfMissing\": ").Append(Count(FunctionalParityMatrix.ParityStatus.WpfMissing)).Append(",\n");
        sb.Append("    \"bothMissing\": ").Append(Count(FunctionalParityMatrix.ParityStatus.BothMissing)).Append(",\n");
        sb.Append("    \"intentionalLinuxOmissions\": ").Append(IntentionalLinuxOmissions.Count).Append(",\n");
        sb.Append("    \"conditional-format-popup-gallery-row\": ").Append(conditionalFormatPopupGalleryRows).Append(",\n");
        sb.Append("    \"conditional-format-popup-catalog-item\": ").Append(FunctionalParityClassifier.ConditionalFormattingGalleryRows.Count).Append(",\n");
        sb.Append("    \"accounting-symbol-popup-gallery-row\": ").Append(accountingSymbolPopupGalleryRows).Append(",\n");
        sb.Append("    \"accounting-symbol-popup-catalog-item\": ").Append(FunctionalParityClassifier.AccountingSymbolRows.Count).Append(",\n");
        sb.Append("    \"font-border-popup-gallery-row\": ").Append(fontBorderPopupGalleryRows).Append(",\n");
        sb.Append("    \"font-border-popup-catalog-item\": ").Append(FunctionalParityClassifier.FontAndBorderChoiceRows.Count).Append(",\n");
        sb.Append("    \"handler-qualified-help-route\": ").Append(handlerQualifiedHelpRows).Append(",\n");
        sb.Append("    \"shared-ribbon-combo-box-control\": ").Append(sharedRibbonComboBoxRows).Append(",\n");
        for (var i = 0; i < FunctionalParityClassifier.OrderedKinds.Count; i++)
        {
            var kind = FunctionalParityClassifier.OrderedKinds[i];
            sb.Append("    \"").Append(FunctionalParityClassifier.ClassificationName(kind)).Append("\": ")
              .Append(classifications.Count(c => c.Classification == kind))
              .Append(i == FunctionalParityClassifier.OrderedKinds.Count - 1 ? "\n" : ",\n");
        }
        sb.Append("  },\n");
        sb.Append("  \"catalogs\": {\n");
        sb.Append("    \"conditionalFormatPopupCommandIds\": ")
          .Append(JsonArray(FunctionalParityClassifier.ConditionalFormattingGalleryRows.OrderBy(id => id, StringComparer.Ordinal).ToArray()))
          .Append(",\n");
        sb.Append("    \"accountingSymbolCommandIds\": ")
          .Append(JsonArray(FunctionalParityClassifier.AccountingSymbolRows.OrderBy(id => id, StringComparer.Ordinal).ToArray()))
          .Append(",\n");
        sb.Append("    \"fontBorderChoiceCommandIds\": ")
          .Append(JsonArray(FunctionalParityClassifier.FontAndBorderChoiceRows.OrderBy(id => id, StringComparer.Ordinal).ToArray()))
          .Append('\n');
        sb.Append("  },\n");

        var topGaps = classifications
            .Where(c => c.Classification == FunctionalParityClassifier.ClassificationKind.RealBehaviorGap)
            .OrderBy(c => c.ImplementationRank)
            .ThenBy(c => c.MatrixRow.CommandId, StringComparer.Ordinal)
            .ToArray();

        sb.Append("  \"prioritizedImplementationList\": [\n");
        for (var i = 0; i < topGaps.Length; i++)
        {
            var c = topGaps[i];
            AppendClassifiedRowJson(sb, c, indent: "    ", includeClassification: false);
            sb.Append(i == topGaps.Length - 1 ? "\n" : ",\n");
        }
        sb.Append("  ],\n");

        sb.Append("  \"rows\": [\n");
        for (var i = 0; i < classifications.Count; i++)
        {
            AppendClassifiedRowJson(sb, classifications[i], indent: "    ", includeClassification: true);
            sb.Append(i == classifications.Count - 1 ? "\n" : ",\n");
        }
        sb.Append("  ]\n");
        sb.Append("}\n");

        WriteArtifact("functional-parity-classification.json", sb.ToString());
    }

    private static void AppendClassifiedRowJson(
        StringBuilder sb,
        FunctionalParityClassifier.ClassifiedRow c,
        string indent,
        bool includeClassification)
    {
        var row = c.MatrixRow;
        sb.Append(indent).Append("{ \"id\": ").Append(JsonString(row.CommandId))
          .Append(", \"tab\": ").Append(JsonString(row.TabHeader))
          .Append(", \"group\": ").Append(JsonString(row.GroupHeader))
          .Append(", \"status\": ").Append(JsonString(StatusName(row.Status)));
        if (includeClassification)
        {
            sb.Append(", \"classification\": ")
              .Append(JsonString(FunctionalParityClassifier.ClassificationName(c.Classification)));
        }
        sb.Append(", \"evidenceKind\": ").Append(JsonString(c.EvidenceKind));
        sb.Append(", \"priority\": ").Append(JsonString(c.Priority))
          .Append(", \"implementationRank\": ").Append(c.ImplementationRank)
          .Append(", \"rationale\": ").Append(JsonString(c.Rationale))
          .Append(", \"nextAction\": ").Append(JsonString(c.NextAction))
          .Append(" }");
    }

    private static void WriteClassificationMarkdown(
        IReadOnlyList<FunctionalParityMatrix.Row> rows,
        IReadOnlyList<FunctionalParityClassifier.ClassifiedRow> classifications)
    {
        int Count(FunctionalParityMatrix.ParityStatus s) => rows.Count(r => r.Status == s);
        var conditionalFormatPopupGalleryRows = classifications.Count(FunctionalParityClassifier.IsConditionalFormattingGalleryRow);
        var accountingSymbolPopupGalleryRows = classifications.Count(FunctionalParityClassifier.IsAccountingSymbolGalleryRow);
        var fontBorderPopupGalleryRows = classifications.Count(FunctionalParityClassifier.IsFontBorderGalleryRow);
        var handlerQualifiedHelpRows = classifications.Count(c => c.EvidenceKind == "handler-qualified-help-route");
        var sharedRibbonComboBoxRows = classifications.Count(c => c.EvidenceKind == "shared-ribbon-combo-box-control");

        var sb = new StringBuilder();
        sb.Append("# FreeX functional parity classification dashboard\n\n");
        sb.Append("Generated by `FunctionalParityMatrixTests.EmitArtifacts`. Do not edit by hand.\n\n");
        sb.Append("This companion classifies every `WPF-MISSING`, `AVALONIA-MISSING`, and `BOTH-MISSING` ");
        sb.Append("row from `functional-parity.json` as implementation work or command-inventory noise.\n\n");

        sb.Append("## Snapshot\n\n");
        sb.Append("| Metric | Count |\n|---|---:|\n");
        sb.Append("| Total commands | ").Append(rows.Count).Append(" |\n");
        sb.Append("| Non-parity rows classified | ").Append(classifications.Count).Append(" |\n");
        sb.Append("| AVALONIA-MISSING | ").Append(Count(FunctionalParityMatrix.ParityStatus.AvaloniaMissing)).Append(" |\n");
        sb.Append("| Intentional Linux omissions | ").Append(IntentionalLinuxOmissions.Count).Append(" |\n");
        sb.Append("| Conditional-format popup/gallery rows backed by runtime catalog | ").Append(conditionalFormatPopupGalleryRows).Append(" |\n");
        sb.Append("| Conditional-format popup runtime catalog items | ").Append(FunctionalParityClassifier.ConditionalFormattingGalleryRows.Count).Append(" |\n");
        sb.Append("| Accounting-symbol popup/gallery rows backed by shared number-format catalog | ").Append(accountingSymbolPopupGalleryRows).Append(" |\n");
        sb.Append("| Accounting-symbol shared catalog items | ").Append(FunctionalParityClassifier.AccountingSymbolRows.Count).Append(" |\n");
        sb.Append("| Font/border popup/gallery rows backed by runtime catalog | ").Append(fontBorderPopupGalleryRows).Append(" |\n");
        sb.Append("| Font/border popup runtime catalog items | ").Append(FunctionalParityClassifier.FontAndBorderChoiceRows.Count).Append(" |\n");
        sb.Append("| Handler-qualified Help routes mapped to source/adapter evidence | ").Append(handlerQualifiedHelpRows).Append(" |\n");
        sb.Append("| Shared ribbon ComboBox controls mapped to control-kind evidence | ").Append(sharedRibbonComboBoxRows).Append(" |\n");
        foreach (var kind in FunctionalParityClassifier.OrderedKinds)
        {
            sb.Append("| ").Append(FunctionalParityClassifier.ClassificationLabel(kind))
              .Append(" | ").Append(classifications.Count(c => c.Classification == kind))
              .Append(" |\n");
        }
        sb.Append('\n');

        sb.Append("## Prioritized implementation list\n\n");
        var topGaps = classifications
            .Where(c => c.Classification == FunctionalParityClassifier.ClassificationKind.RealBehaviorGap)
            .OrderBy(c => c.ImplementationRank)
            .ThenBy(c => c.MatrixRow.CommandId, StringComparer.Ordinal)
            .ToArray();

        if (topGaps.Length == 0)
        {
            sb.Append("No real behavior gaps are classified in the current functional matrix.\n\n");
        }
        else
        {
            sb.Append("| Priority | Command | Status | Why | Next action |\n");
            sb.Append("|---|---|---|---|---|\n");
            foreach (var c in topGaps)
            {
                sb.Append("| ").Append(MdCell(c.Priority))
                  .Append(" | ").Append(MdCell(c.MatrixRow.CommandId))
                  .Append(" | ").Append(StatusName(c.MatrixRow.Status))
                  .Append(" | ").Append(MdCell(c.Rationale))
                  .Append(" | ").Append(MdCell(c.NextAction))
                  .Append(" |\n");
            }
            sb.Append('\n');
        }

        sb.Append("## Classification buckets\n\n");
        sb.Append("| Classification | Count | Meaning |\n");
        sb.Append("|---|---:|---|\n");
        foreach (var kind in FunctionalParityClassifier.OrderedKinds)
        {
            sb.Append("| ").Append(FunctionalParityClassifier.ClassificationLabel(kind))
              .Append(" | ").Append(classifications.Count(c => c.Classification == kind))
              .Append(" | ").Append(MdCell(ClassificationMeaning(kind)))
              .Append(" |\n");
        }
        sb.Append('\n');

        sb.Append("## Row classifications\n\n");
        sb.Append("| Command | Group | Tab | Status | Classification | Evidence | Priority | Rationale | Next action |\n");
        sb.Append("|---|---|---|---|---|---|---|---|---|\n");
        foreach (var c in classifications
                     .OrderBy(c => c.MatrixRow.TabHeader, StringComparer.Ordinal)
                     .ThenBy(c => c.MatrixRow.GroupHeader, StringComparer.Ordinal)
                     .ThenBy(c => c.MatrixRow.CommandId, StringComparer.Ordinal))
        {
            sb.Append("| ").Append(MdCell(c.MatrixRow.CommandId))
              .Append(" | ").Append(MdCell(c.MatrixRow.GroupHeader))
              .Append(" | ").Append(MdCell(c.MatrixRow.TabHeader))
              .Append(" | ").Append(StatusName(c.MatrixRow.Status))
              .Append(" | ").Append(FunctionalParityClassifier.ClassificationLabel(c.Classification))
              .Append(" | ").Append(MdCell(c.EvidenceKind))
              .Append(" | ").Append(MdCell(c.Priority))
              .Append(" | ").Append(MdCell(c.Rationale))
              .Append(" | ").Append(MdCell(c.NextAction))
              .Append(" |\n");
        }

        WriteArtifact("functional-parity-classification.md", sb.ToString());
    }

    private static string ClassificationMeaning(FunctionalParityClassifier.ClassificationKind kind) => kind switch
    {
        FunctionalParityClassifier.ClassificationKind.RealBehaviorGap =>
            "Implement or normalize a real host/shared behavior route.",
        FunctionalParityClassifier.ClassificationKind.NonClickControlInventoryRow =>
            "Behavior is driven by non-Click controls or handler-qualified routes outside the current snapshot source.",
        FunctionalParityClassifier.ClassificationKind.PseudoCommandGalleryItem =>
            "Visible menu, swatch, or gallery entry that should be tracked through popup/gallery evidence.",
        FunctionalParityClassifier.ClassificationKind.PlatformOnly =>
            "Intentional platform-specific surface.",
        FunctionalParityClassifier.ClassificationKind.Deferred =>
            "Postponed behind a larger shared subsystem.",
        FunctionalParityClassifier.ClassificationKind.Excluded =>
            "Out of scope for the current product parity target.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown classification kind."),
    };

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
              .Append(", \"controlKind\": ").Append(JsonString(c.ControlKind))
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
