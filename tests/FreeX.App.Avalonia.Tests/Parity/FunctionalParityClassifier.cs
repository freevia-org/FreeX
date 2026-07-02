using System;
using System.Collections.Generic;
using System.Linq;

using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Ribbon;
using FreeX.App.Services;

namespace FreeX.App.Avalonia.Tests.Parity;

/// <summary>
/// Classifies every non-parity row in the generated WPF/Avalonia functional matrix so workers can
/// separate real implementation gaps from command-inventory noise.
/// </summary>
public static class FunctionalParityClassifier
{
    public enum ClassificationKind
    {
        RealBehaviorGap,
        NonClickControlInventoryRow,
        PseudoCommandGalleryItem,
        PlatformOnly,
        Deferred,
        Excluded,
    }

    public sealed record ClassifiedRow(
        FunctionalParityMatrix.Row MatrixRow,
        ClassificationKind Classification,
        string EvidenceKind,
        string Priority,
        int ImplementationRank,
        string Rationale,
        string NextAction);

    private sealed record ClassificationRule(
        ClassificationKind Classification,
        string EvidenceKind,
        string Priority,
        int ImplementationRank,
        string Rationale,
        string NextAction);

    private static readonly IReadOnlyDictionary<string, ClassificationRule> ExactRules =
        new Dictionary<string, ClassificationRule>(StringComparer.Ordinal)
        {
            ["Help Online#HelpOnlineBtn_Click"] = InventoryRoute(
                "Handler-qualified Help command; WPF binds the #method route outside the legacy handler snapshot, and Avalonia maps it through the Help command adapter."),
            ["Feedback#FeedbackBtn_Click"] = InventoryRoute(
                "Handler-qualified Help command; WPF binds the #method route outside the legacy handler snapshot, and Avalonia maps it through the Help command adapter."),
            ["Check for Updates#CheckForUpdatesBtn_Click"] = InventoryRoute(
                "Handler-qualified Help command; WPF binds the #method route outside the legacy handler snapshot, and Avalonia maps it through the Help command adapter."),
            ["About FreeX#AboutBtn_Click"] = InventoryRoute(
                "Handler-qualified Help command; WPF binds the #method route outside the legacy handler snapshot, and Avalonia maps it through the Help command adapter."),

            ["Copy Diagnostics#CopyDiagnosticsBtn_Click"] = new(
                ClassificationKind.RealBehaviorGap,
                "missing-functional-binding",
                "P1",
                1,
                "The shared Help ribbon exposes this as a real diagnostic action, but the functional binding matrix has no counted WPF/Avalonia route for it.",
                "Introduce a shared Help diagnostic command descriptor, bind it through both host command catalogs, and refresh the WPF handler snapshot."),
            ["Legal Notices#LegalNoticesBtn_Click"] = new(
                ClassificationKind.RealBehaviorGap,
                "missing-functional-binding",
                "P2",
                2,
                "Legal notices exist in both hosts through other surfaces, but the shared Help ribbon command is not counted as a functional binding.",
                "Route the Help ribbon command through the shared Legal Notices action and include it in both binding inventories."),
            ["Convert to Comments"] = new(
                ClassificationKind.RealBehaviorGap,
                "missing-functional-binding",
                "P2",
                3,
                "WPF has a live Convert Notes to Comments handler, but the cross-host functional matrix does not count a paired Avalonia command binding.",
                "Add the Avalonia Review ribbon route through the shared comments command model, then refresh the matrix inputs."),
        };

    public static IReadOnlySet<string> HandlerQualifiedHelpRouteRows { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Help Online#HelpOnlineBtn_Click",
            "Feedback#FeedbackBtn_Click",
            "Check for Updates#CheckForUpdatesBtn_Click",
            "About FreeX#AboutBtn_Click",
        };

    public static IReadOnlySet<string> EditableRibbonControlRows { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Font",
        "Font Size",
        "Number Format",
        "Scale Width",
        "Scale Height",
        "Scale Percent",
    };

    public static IReadOnlySet<string> AccountingSymbolRows { get; } =
        HomeNumberFormatDropdownPlanner.AccountingSymbolOptions
            .Select(option => option.CommandId)
            .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> ConditionalFormattingGalleryRows { get; } =
        ConditionalFormatPresetGalleryPlanner.PopupItems
            .Select(item => item.CommandId)
            .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> FontAndBorderChoiceRows { get; } =
        HomeFontBorderPopupCatalogPlanner.ClassifiedFontBorderRowsCovered;

    public static IReadOnlyList<ClassifiedRow> Classify(IReadOnlyList<FunctionalParityMatrix.Row> rows)
        => rows.Where(row => row.Status != FunctionalParityMatrix.ParityStatus.Parity)
            .Select(Classify)
            .OrderBy(row => row.ImplementationRank)
            .ThenBy(row => row.MatrixRow.TabHeader, StringComparer.Ordinal)
            .ThenBy(row => row.MatrixRow.GroupHeader, StringComparer.Ordinal)
            .ThenBy(row => row.MatrixRow.CommandId, StringComparer.Ordinal)
            .ToArray();

    public static ClassifiedRow Classify(FunctionalParityMatrix.Row row)
    {
        if (row.Status == FunctionalParityMatrix.ParityStatus.Parity)
            throw new ArgumentException("PARITY rows do not need a gap classification.", nameof(row));

        if (ExactRules.TryGetValue(row.CommandId, out var exact))
            return ToClassifiedRow(row, exact);

        if (EditableRibbonControlRows.Contains(row.CommandId))
        {
            return ToClassifiedRow(row, new ClassificationRule(
                ClassificationKind.NonClickControlInventoryRow,
                "shared-ribbon-combo-box-control",
                "P3",
                400,
                "Shared ribbon definition exposes this as a ComboBox control; WPF commits it through selection/text-change paths instead of a Click handler, so the binding matrix overstates this as missing.",
                "Keep this classified against the shared ribbon ComboBox surface unless the matrix grows a first-class control-binding signal."));
        }

        if (AccountingSymbolRows.Contains(row.CommandId))
        {
            return ToClassifiedRow(row, new ClassificationRule(
                ClassificationKind.PseudoCommandGalleryItem,
                "shared-number-format-symbol-catalog",
                "P3",
                500,
                "Accounting currency child entry is published by the shared number-format symbol catalog under the accounting split button, not as a standalone WPF Click-handler command id.",
                "Use the HomeNumberFormatDropdownPlanner accounting symbol catalog for parity evidence; do not treat this row as a missing WPF command."));
        }

        if (ConditionalFormattingGalleryRows.Contains(row.CommandId))
        {
            return ToClassifiedRow(row, new ClassificationRule(
                ClassificationKind.PseudoCommandGalleryItem,
                "conditional-format-popup-runtime-catalog",
                "P3",
                510,
                "Conditional-format menu/gallery entry is populated or routed through gallery planners and shared preset handlers, not one stable WPF handler id per visible choice.",
                "Use the conditional-format popup/gallery parity lane for richer evidence instead of adding placeholder handlers."));
        }

        if (FontAndBorderChoiceRows.Contains(row.CommandId))
        {
            return ToClassifiedRow(row, new ClassificationRule(
                ClassificationKind.PseudoCommandGalleryItem,
                "font-border-popup-runtime-catalog",
                "P3",
                520,
                "Font color or border-style choice row is a runtime swatch/menu selection inside a split-button gallery, not an independent command on either host.",
                "Use the HomeFontBorderPopupCatalogPlanner evidence; keep it classified as a pseudo-gallery row unless the binding matrix grows per-choice popup evidence."));
        }

        return ToClassifiedRow(row, new ClassificationRule(
            ClassificationKind.RealBehaviorGap,
            "unexplained-binding-row",
            row.Status == FunctionalParityMatrix.ParityStatus.AvaloniaMissing ? "P0" : "P1",
            row.Status == FunctionalParityMatrix.ParityStatus.AvaloniaMissing ? 0 : 100,
            "No classifier rule explains this non-parity binding row.",
            "Audit the shared command definition and both host bindings; either implement the missing route or add an explicit classifier rule."));
    }

    public static string ClassificationName(ClassificationKind kind) => kind switch
    {
        ClassificationKind.RealBehaviorGap => "real-behavior-gap",
        ClassificationKind.NonClickControlInventoryRow => "non-click-control-inventory-row",
        ClassificationKind.PseudoCommandGalleryItem => "pseudo-command-gallery-item",
        ClassificationKind.PlatformOnly => "platform-only",
        ClassificationKind.Deferred => "deferred",
        ClassificationKind.Excluded => "excluded",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown classification kind."),
    };

    public static string ClassificationLabel(ClassificationKind kind) => kind switch
    {
        ClassificationKind.RealBehaviorGap => "Real behavior gap",
        ClassificationKind.NonClickControlInventoryRow => "Non-Click/control inventory row",
        ClassificationKind.PseudoCommandGalleryItem => "Pseudo-command/gallery item",
        ClassificationKind.PlatformOnly => "Platform-only",
        ClassificationKind.Deferred => "Deferred",
        ClassificationKind.Excluded => "Excluded",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown classification kind."),
    };

    public static bool IsConditionalFormattingGalleryRow(ClassifiedRow row) =>
        row.Classification == ClassificationKind.PseudoCommandGalleryItem &&
        string.Equals(row.MatrixRow.TabHeader, "Home", StringComparison.Ordinal) &&
        string.Equals(row.MatrixRow.GroupHeader, "Styles", StringComparison.Ordinal) &&
        ConditionalFormattingGalleryRows.Contains(row.MatrixRow.CommandId);

    public static bool IsAccountingSymbolGalleryRow(ClassifiedRow row) =>
        row.Classification == ClassificationKind.PseudoCommandGalleryItem &&
        string.Equals(row.MatrixRow.TabHeader, "Home", StringComparison.Ordinal) &&
        string.Equals(row.MatrixRow.GroupHeader, "Number", StringComparison.Ordinal) &&
        AccountingSymbolRows.Contains(row.MatrixRow.CommandId);

    public static bool IsFontBorderGalleryRow(ClassifiedRow row) =>
        row.Classification == ClassificationKind.PseudoCommandGalleryItem &&
        string.Equals(row.MatrixRow.TabHeader, "Home", StringComparison.Ordinal) &&
        string.Equals(row.MatrixRow.GroupHeader, "Font", StringComparison.Ordinal) &&
        FontAndBorderChoiceRows.Contains(row.MatrixRow.CommandId);

    public static IReadOnlyList<ClassificationKind> OrderedKinds { get; } =
    [
        ClassificationKind.RealBehaviorGap,
        ClassificationKind.NonClickControlInventoryRow,
        ClassificationKind.PseudoCommandGalleryItem,
        ClassificationKind.PlatformOnly,
        ClassificationKind.Deferred,
        ClassificationKind.Excluded,
    ];

    private static ClassificationRule InventoryRoute(string rationale) => new(
        ClassificationKind.NonClickControlInventoryRow,
        "handler-qualified-help-route",
        "P3",
        300,
        rationale,
        "Keep this mapped to the concrete Help handler and Avalonia Help adapter unless the binding inventory source starts reading handler-qualified WPF routes.");

    private static ClassifiedRow ToClassifiedRow(FunctionalParityMatrix.Row row, ClassificationRule rule)
        => new(row, rule.Classification, rule.EvidenceKind, rule.Priority, rule.ImplementationRank, rule.Rationale, rule.NextAction);
}
