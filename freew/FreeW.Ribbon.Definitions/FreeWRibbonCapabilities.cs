namespace FreeW.Ribbon.Definitions;

/// <summary>Describes the canonical ribbon sections a host can realize.</summary>
public sealed record FreeWRibbonCapabilities
{
    private static readonly IReadOnlySet<string> WpfOmittedSections = new HashSet<string>(StringComparer.Ordinal)
    {
        FreeWRibbonTopologySection.File,
        FreeWRibbonTopologySection.SmartArtSize,
    };

    private static readonly IReadOnlySet<string> AvaloniaOmittedSections = new HashSet<string>(StringComparer.Ordinal)
    {
        FreeWRibbonTopologySection.HomeFormatting,
        FreeWRibbonTopologySection.DrawingInsert,
        FreeWRibbonTopologySection.DrawingText,
        FreeWRibbonTopologySection.DrawingWordArt,
    };

    private FreeWRibbonCapabilities(
        string name,
        FreeWRibbonControlPresentation controlPresentation,
        IReadOnlySet<string> omittedSections,
        IReadOnlyList<string> tabOrder)
    {
        Name = name;
        ControlPresentation = controlPresentation;
        OmittedSections = omittedSections;
        TabOrder = tabOrder;
    }

    public string Name { get; }
    public string TableContextKey { get; } = "table";
    public string PictureContextKey { get; } = "picture";
    public string DrawingContextKey { get; } = "drawing";
    public string ChartContextKey { get; } = "chart";
    public string SmartArtContextKey { get; } = "smartart";
    public bool UsesPortableControlPresentation => ControlPresentation == FreeWRibbonControlPresentation.Portable;

    internal FreeWRibbonControlPresentation ControlPresentation { get; }
    internal IReadOnlySet<string> OmittedSections { get; }
    internal IReadOnlyList<string> TabOrder { get; }

    internal bool UsesPortableControls => UsesPortableControlPresentation;

    internal bool IncludesSection(string sectionId) => !OmittedSections.Contains(sectionId);

    public static FreeWRibbonCapabilities Wpf { get; } = new(
        "WPF",
        FreeWRibbonControlPresentation.Desktop,
        WpfOmittedSections,
        [
            "home", "insert", "design", "layout", "references", "mailings", "review", "view", "help", "developer",
            "drawing-format", "picture-format", "chart-design", "chart-format", "smartart-design",
            "table-design", "table-layout", "header-footer-design",
        ]);

    public static FreeWRibbonCapabilities Avalonia { get; } = new(
        "Avalonia",
        FreeWRibbonControlPresentation.Portable,
        AvaloniaOmittedSections,
        [
            "file", "home", "insert", "layout", "design", "view", "review", "developer", "references", "mailings", "help",
            "table-design", "table-layout", "header-footer-design", "picture-format", "drawing-format",
            "chart-design", "chart-format", "smartart-design",
        ]);
}

internal enum FreeWRibbonControlPresentation
{
    Desktop,
    Portable,
}

internal static class FreeWRibbonTopologySection
{
    internal const string File = "file";
    internal const string HomeFormatting = "home.formatting";
    internal const string DrawingInsert = "drawing.insert";
    internal const string DrawingText = "drawing.text";
    internal const string DrawingWordArt = "drawing.wordart";
    internal const string SmartArtSize = "smartart.size";
}
