using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Presentation.PageLayout;

public enum PageLayoutRibbonActionKind
{
    OpenPageSetupDialog,
    ShowPageBreaksMenu,
    ToggleViewGridlines,
    TogglePrintGridlines,
    ToggleViewHeadings,
    TogglePrintHeadings,
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
        new("View Gridlines", PageLayoutRibbonActionKind.ToggleViewGridlines),
        new("View Headings", PageLayoutRibbonActionKind.ToggleViewHeadings),

        new("Margins", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.CustomMargins),
        new("Page Orientation", PageLayoutRibbonActionKind.OpenPageSetupDialog),
        new("Paper Size", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new("Print Area", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.PrintArea),
        new("Print Titles", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.PrintTitles),
        new("Breaks", PageLayoutRibbonActionKind.ShowPageBreaksMenu),
        new("Background", PageLayoutRibbonActionKind.ChooseBackground),
        new("Scale Percent", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ScaleToFit),
        new("Scale Width", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ScaleToFit),
        new("Scale Height", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ScaleToFit),

        new(FreeXRibbonCommandIds.PageLayoutBackgroundChoose, PageLayoutRibbonActionKind.ChooseBackground),
        new(FreeXRibbonCommandIds.PageLayoutBackgroundDelete, PageLayoutRibbonActionKind.DeleteBackground),

        new(FreeXRibbonCommandIds.PageLayoutMarginsWide, PageLayoutRibbonActionKind.ApplyMarginsPreset,
            MarginPreset: PageLayoutMarginPreset.Wide),
        new(FreeXRibbonCommandIds.PageLayoutMarginsNarrow, PageLayoutRibbonActionKind.ApplyMarginsPreset,
            MarginPreset: PageLayoutMarginPreset.Narrow),
        new(FreeXRibbonCommandIds.PageLayoutMarginsNormal, PageLayoutRibbonActionKind.ApplyMarginsPreset,
            MarginPreset: PageLayoutMarginPreset.Normal),
        new(FreeXRibbonCommandIds.PageLayoutMarginsCustom, PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.CustomMargins),

        new(FreeXRibbonCommandIds.PageLayoutOrientationPortrait, PageLayoutRibbonActionKind.ApplyOrientationPreset,
            OrientationPreset: PageLayoutOrientationPreset.Portrait),
        new(FreeXRibbonCommandIds.PageLayoutOrientationLandscape, PageLayoutRibbonActionKind.ApplyOrientationPreset,
            OrientationPreset: PageLayoutOrientationPreset.Landscape),

        new(FreeXRibbonCommandIds.PageLayoutPaperSizeLetter, PageLayoutRibbonActionKind.ApplyPaperSizePreset,
            PaperSizePreset: PageLayoutPaperSizePreset.Letter),
        new(FreeXRibbonCommandIds.PageLayoutPaperSizeLegal, PageLayoutRibbonActionKind.ApplyPaperSizePreset,
            PaperSizePreset: PageLayoutPaperSizePreset.Legal),
        new(FreeXRibbonCommandIds.PageLayoutPaperSizeA4, PageLayoutRibbonActionKind.ApplyPaperSizePreset,
            PaperSizePreset: PageLayoutPaperSizePreset.A4),
        new(FreeXRibbonCommandIds.PageLayoutPaperSizeA3, PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new(FreeXRibbonCommandIds.PageLayoutPaperSizeA5, PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new(FreeXRibbonCommandIds.PageLayoutPaperSizeExecutive, PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new(FreeXRibbonCommandIds.PageLayoutPaperSizeStatement, PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new(FreeXRibbonCommandIds.PageLayoutPaperSizeTabloid, PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ExtendedPaperSize),
        new(FreeXRibbonCommandIds.PageLayoutPaperSizeB4Jis, PageLayoutRibbonActionKind.ApplyPaperSizePreset,
            PaperSizePreset: PageLayoutPaperSizePreset.B4),
        new(FreeXRibbonCommandIds.PageLayoutPaperSizeB5Jis, PageLayoutRibbonActionKind.ApplyPaperSizePreset,
            PaperSizePreset: PageLayoutPaperSizePreset.B5),

        new(FreeXRibbonCommandIds.PageLayoutPrintAreaSet, PageLayoutRibbonActionKind.SetPrintArea),
        new(FreeXRibbonCommandIds.PageLayoutPrintAreaClear, PageLayoutRibbonActionKind.ClearPrintArea),

        new("Page Setup", PageLayoutRibbonActionKind.OpenPageSetupDialog),
        new("Page Setup dialog", PageLayoutRibbonActionKind.OpenPageSetupDialog),
        new("Scale to Fit", PageLayoutRibbonActionKind.OpenPageSetupDialog,
            PageSetupOpenSource: PageLayoutPageSetupOpenSource.ScaleToFit),
        new("Print Gridlines", PageLayoutRibbonActionKind.TogglePrintGridlines),
        new("Print Headings", PageLayoutRibbonActionKind.TogglePrintHeadings),
        new(FreeXRibbonCommandIds.PageLayoutBreakInsert, PageLayoutRibbonActionKind.ApplyPageBreakAction,
            PageBreakAction: PageBreakMenuAction.Insert),
        new(FreeXRibbonCommandIds.PageLayoutBreakRemove, PageLayoutRibbonActionKind.ApplyPageBreakAction,
            PageBreakAction: PageBreakMenuAction.Remove),
        new(FreeXRibbonCommandIds.PageLayoutBreakResetAll, PageLayoutRibbonActionKind.ApplyPageBreakAction,
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
