namespace FreeW.App.Presentation.Backstage;

public sealed record BackstagePaneText(string Title, string Description);
public sealed record BackstageRailEntryText(string PaneKey, string Label);

public static class BackstageViewTextResources
{
    public const string WindowTitle = "FreeW \u2014 File";
    public const string BackButton = "\u2190 Back";
    public const string OptionsButton = "Options";
    public const string NotImplementedSuffix = "pane not yet implemented.";
    public const string FileNameLabel = "File name";
    public const string FormatLabel = "Format";
    public const string DocumentSettingsSection = "Document Settings";
    public const string DirectPrintDeferredNote =
        "Note: Print is available in FreeW via Export to PDF (Ctrl+Shift+P). Direct printer output is planned for a future update.";
    public const string CreatePdfSection = "Create PDF/XPS Document";
    public const string CreatePdfLabel = "Create PDF";
    public const string CreatePdfDescription = "Publish a fixed-layout PDF copy for sharing or printing.";
    public const string DocumentPropertiesSection = "Document Properties";
    public const string DocumentLabel = "Document";
    public const string UntitledValue = "Untitled";
    public const string PathLabel = "Path";
    public const string NotSavedValue = "(not saved)";
    public const string SizeLabel = "Size";
    public const string ModifiedLabel = "Modified";
    public const string ProductName = "FreeW";

    public static readonly IReadOnlyList<BackstageRailEntryText> RailEntries =
    [
        new(nameof(Home), "Home"),
        new(nameof(Open), "Open"),
        new(nameof(SaveAs), "Save As"),
        new(nameof(Print), "Print"),
        new(nameof(Share), "Share"),
        new(nameof(Export), "Export"),
        new(nameof(Info), "Info"),
        new(nameof(Account), "Account"),
    ];

    public static BackstagePaneText Home { get; } = new(
        Title: "Home",
        Description: "Start with a new document or reopen a recent file.");

    public static BackstagePaneText Open { get; } = new(
        Title: "Open",
        Description: "Open a document from your recent files or browse your PC.");

    public static BackstagePaneText SaveAs { get; } = new(
        Title: "Save As",
        Description: "Save this document in a different format.");

    public static BackstagePaneText Print { get; } = new(
        Title: "Print",
        Description: "Review print settings for this document.");

    public static BackstagePaneText Share { get; } = new(
        Title: "Share",
        Description: "Share this document or send a copy.");

    public static BackstagePaneText Export { get; } = new(
        Title: "Export",
        Description: "Export this document to a different file format.");

    public static BackstagePaneText Info { get; } = new(
        Title: "Info",
        Description: "Protect, inspect, and review document information.");

    public static BackstagePaneText Account { get; } = new(
        Title: "Account",
        Description: "Account settings and product information.");
}
