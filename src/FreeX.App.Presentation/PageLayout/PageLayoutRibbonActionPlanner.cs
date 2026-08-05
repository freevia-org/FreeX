using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public enum PageLayoutRibbonActionKind
{
    OpenPageSetupDialog,
    ShowPageBreaksMenu,
    ShowGridlinesSheetOptions,
    ShowHeadingsSheetOptions,
    ChooseBackground,
    DeleteBackground,
    SetPrintArea,
    ClearPrintArea,
    ApplyMarginsPreset,
    ApplyOrientationPreset,
    ApplyPaperSizePreset,
    ApplyPageBreakAction
}

public sealed record PageLayoutRibbonActionDescriptor(
    string CommandId,
    PageLayoutRibbonActionKind Kind,
    PageLayoutPageSetupOpenSource PageSetupOpenSource = PageLayoutPageSetupOpenSource.DialogButton,
    PageLayoutMarginPreset? MarginPreset = null,
    PageLayoutOrientationPreset? OrientationPreset = null,
    PageLayoutPaperSizePreset? PaperSizePreset = null,
    PageBreakMenuAction? PageBreakAction = null);

public sealed record PageLayoutPresetCommandPlan<T>(
    T Value,
    string CommandLabel,
    string StatusResourceKey,
    double? HeaderMargin = null,
    double? FooterMargin = null);

/// <summary>
/// Shared Page Layout ribbon action catalog. Platform shells still own dialogs and dispatch, but the
/// PageLayout presentation layer owns the command ids, preset values, command labels, and status keys.
/// </summary>
public static class PageLayoutRibbonActionPlanner
{
    public const string PageMarginsCommandLabel = "Page Margins";
    public const string PageOrientationCommandLabel = "Orientation";
    public const string PaperSizeCommandLabel = "Paper Size";
    public const string PrintAreaCommandLabel = "Print Area";
    public const string PageBreaksCommandLabel = "Page Breaks";
    public const string ScaleToFitCommandLabel = "Scale To Fit";
    public const string BackgroundCommandLabel = "Sheet Background";
    public const string ClearBackgroundCommandLabel = "Clear Sheet Background";
    public const string HeaderFooterCommandLabel = "Header & Footer";
    public const string PrintGridlinesCommandLabel = "Print Gridlines";
    public const string PrintHeadingsCommandLabel = "Print Headings";

    public static IReadOnlyList<PageLayoutRibbonActionDescriptor> RibbonActionDescriptors { get; } =
    [
        new("pageLayout.gridlines", PageLayoutRibbonActionKind.ShowGridlinesSheetOptions),
        new("pageLayout.headings", PageLayoutRibbonActionKind.ShowHeadingsSheetOptions),

        new("pageLayout.margins", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.CustomMargins),
        new("pageLayout.orientation", PageLayoutRibbonActionKind.OpenPageSetupDialog),
        new("pageLayout.size", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new("pageLayout.printArea", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.PrintArea),
        new("pageLayout.printTitles", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.PrintTitles),
        new("pageLayout.breaks", PageLayoutRibbonActionKind.ShowPageBreaksMenu),
        new("pageLayout.background", PageLayoutRibbonActionKind.ChooseBackground),
        new("pageLayout.scale", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ScaleToFit),
        new("pageLayout.width", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ScaleToFit),
        new("pageLayout.height", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ScaleToFit),

        new("Choose Background", PageLayoutRibbonActionKind.ChooseBackground),
        new("Delete Background", PageLayoutRibbonActionKind.DeleteBackground),

        new("Normal", PageLayoutRibbonActionKind.ApplyMarginsPreset,
            MarginPreset: PageLayoutMarginPreset.Normal),
        new("Wide", PageLayoutRibbonActionKind.ApplyMarginsPreset,
            MarginPreset: PageLayoutMarginPreset.Wide),
        new("Narrow", PageLayoutRibbonActionKind.ApplyMarginsPreset,
            MarginPreset: PageLayoutMarginPreset.Narrow),
        new("Normal#MarginNormalMenuItem_Click", PageLayoutRibbonActionKind.ApplyMarginsPreset,
            MarginPreset: PageLayoutMarginPreset.Normal),
        new("Custom Margins", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.CustomMargins),

        new("Portrait", PageLayoutRibbonActionKind.ApplyOrientationPreset,
            OrientationPreset: PageLayoutOrientationPreset.Portrait),
        new("Landscape", PageLayoutRibbonActionKind.ApplyOrientationPreset,
            OrientationPreset: PageLayoutOrientationPreset.Landscape),

        new("Letter", PageLayoutRibbonActionKind.ApplyPaperSizePreset,
            PaperSizePreset: PageLayoutPaperSizePreset.Letter),
        new("Legal", PageLayoutRibbonActionKind.ApplyPaperSizePreset,
            PaperSizePreset: PageLayoutPaperSizePreset.Legal),
        new("A4", PageLayoutRibbonActionKind.ApplyPaperSizePreset,
            PaperSizePreset: PageLayoutPaperSizePreset.A4),
        new("A3", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new("A5", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new("Executive", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new("Statement", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new("Tabloid", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new("B4 (JIS)", PageLayoutRibbonActionKind.ApplyPaperSizePreset,
            PaperSizePreset: PageLayoutPaperSizePreset.B4),
        new("B5 (JIS)", PageLayoutRibbonActionKind.ApplyPaperSizePreset,
            PaperSizePreset: PageLayoutPaperSizePreset.B5),

        new("Set Print Area", PageLayoutRibbonActionKind.SetPrintArea),
        new("Clear Print Area", PageLayoutRibbonActionKind.ClearPrintArea),

        new("Page Setup", PageLayoutRibbonActionKind.OpenPageSetupDialog),
        new("Page Setup dialog", PageLayoutRibbonActionKind.OpenPageSetupDialog),
        new("Scale to Fit", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ScaleToFit),
        new("Print Gridlines", PageLayoutRibbonActionKind.ShowGridlinesSheetOptions),
        new("Print Headings", PageLayoutRibbonActionKind.ShowHeadingsSheetOptions),
        new("Insert Page Break", PageLayoutRibbonActionKind.ApplyPageBreakAction,
            PageBreakAction: PageBreakMenuAction.Insert),
        new("Remove Page Break", PageLayoutRibbonActionKind.ApplyPageBreakAction,
            PageBreakAction: PageBreakMenuAction.Remove),
        new("Reset All Page Breaks", PageLayoutRibbonActionKind.ApplyPageBreakAction,
            PageBreakAction: PageBreakMenuAction.ResetAll),
    ];

    public static PageLayoutPresetCommandPlan<WorksheetPageMargins> PlanMarginsPreset(
        PageLayoutMarginPreset preset)
    {
        var (headerMargin, footerMargin) = PageLayoutRibbonPolicyPlanner.ResolveHeaderFooterMargins(preset);
        return new(
            PageLayoutRibbonPolicyPlanner.ResolveMargins(preset),
            PageMarginsCommandLabel,
            preset switch
            {
                PageLayoutMarginPreset.Wide => "RibbonWire_MarginsWide",
                PageLayoutMarginPreset.Narrow => "RibbonWire_MarginsNarrow",
                _ => "RibbonWire_MarginsNormal",
            },
            headerMargin,
            footerMargin);
    }

    public static PageLayoutPresetCommandPlan<WorksheetPageOrientation> PlanOrientationPreset(
        PageLayoutOrientationPreset preset) =>
        new(
            PageLayoutRibbonPolicyPlanner.ResolveOrientation(preset),
            PageOrientationCommandLabel,
            preset == PageLayoutOrientationPreset.Landscape
                ? "RibbonWire_OrientationLandscape"
                : "RibbonWire_OrientationPortrait");

    public static PageLayoutPresetCommandPlan<WorksheetPaperSize> PlanPaperSizePreset(
        PageLayoutPaperSizePreset preset) =>
        new(
            PageLayoutRibbonPolicyPlanner.ResolvePaperSize(preset),
            PaperSizeCommandLabel,
            preset switch
            {
                PageLayoutPaperSizePreset.Letter => "RibbonWire_PaperLetter",
                PageLayoutPaperSizePreset.Legal => "RibbonWire_PaperLegal",
                PageLayoutPaperSizePreset.B4 => "RibbonWire_PaperB4",
                PageLayoutPaperSizePreset.B5 => "RibbonWire_PaperB5",
                _ => "RibbonWire_PaperA4",
            });
}
