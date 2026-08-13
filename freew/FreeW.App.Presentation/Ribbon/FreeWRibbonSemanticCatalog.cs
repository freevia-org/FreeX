namespace FreeW.App.Presentation.Ribbon;

public readonly record struct FreeWRibbonQuickStyleBinding(
    FreeWRibbonCommandAction Action,
    string StyleId);

public readonly record struct FreeWRibbonHeaderFooterSlotBinding(
    FreeWRibbonCommandAction Action,
    HeaderFooterSlotKind Slot);

/// <summary>Canonical product mappings consumed by both native ribbon command hosts.</summary>
public static class FreeWRibbonSemanticCatalog
{
    public static IReadOnlyList<FreeWRibbonQuickStyleBinding> QuickStyles { get; } =
    [
        new(FreeWRibbonCommandAction.StyleNormal, "Normal"),
        new(FreeWRibbonCommandAction.StyleHeading1, "Heading1"),
        new(FreeWRibbonCommandAction.StyleHeading2, "Heading2"),
        new(FreeWRibbonCommandAction.StyleHeading3, "Heading3"),
        new(FreeWRibbonCommandAction.StyleTitle, "Title"),
    ];

    public static IReadOnlyList<FreeWRibbonHeaderFooterSlotBinding> HeaderFooterEditSlots { get; } =
    [
        new(FreeWRibbonCommandAction.HfEditHeader, HeaderFooterSlotKind.Header),
        new(FreeWRibbonCommandAction.HfEditFooter, HeaderFooterSlotKind.Footer),
        new(FreeWRibbonCommandAction.HfEditEvenHeader, HeaderFooterSlotKind.EvenHeader),
        new(FreeWRibbonCommandAction.HfEditEvenFooter, HeaderFooterSlotKind.EvenFooter),
        new(FreeWRibbonCommandAction.HfEditFirstHeader, HeaderFooterSlotKind.FirstHeader),
        new(FreeWRibbonCommandAction.HfEditFirstFooter, HeaderFooterSlotKind.FirstFooter),
    ];

    public static IReadOnlyList<FreeWRibbonHeaderFooterSlotBinding> HeaderFooterNavigationSlots { get; } =
    [
        new(FreeWRibbonCommandAction.HfGoToHeader, HeaderFooterSlotKind.Header),
        new(FreeWRibbonCommandAction.HfGoToFooter, HeaderFooterSlotKind.Footer),
    ];
}
