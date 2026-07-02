using Free.Shared.Shell;

namespace FreeW.App.Presentation.Backstage;

public static class BackstageViewTextResources
{
    public const string WindowTitle = "FreeW \u2014 File";
    public const string BackButton = "\u2190 Back";
    public const string OptionsButton = "Options";
    public const string NotImplementedSuffix = "pane not yet implemented.";
    public const string FileNameLabel = "File name";
    public const string FormatLabel = "Format";
    public const string DocumentSettingsSection = "Document Settings";
    public const string EvidenceSection = "Evidence";
    public const string EvidenceScenariosLabel = "Scenarios";
    public const string NoEvidenceFixtureScenario = "No fixture scenario";
    public const string PrintPreviewEvidenceLabel = "Print preview fidelity";
    public const string PdfExportEvidenceLabel = "PDF export fidelity";
    public const string NativePrintEvidenceLabel = "Native print";
    public const string FixtureReadyEvidenceStatus = "Fixture ready";
    public const string HostBackedEvidenceStatus = "Host backed";
    public const string DeferredEvidenceStatus = "Deferred";
    public const string DirectPrintDeferredNote =
        "Note: Direct printer output is not available in the current host. Use Print Preview to review layout or Create PDF (Ctrl+Shift+P) to print through the operating system.";
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
    public const string PinnedRecentSuffix = "  (pinned)";

    public static readonly IReadOnlyList<BackstageRailEntryDescriptor> RailEntries =
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

    public static BackstagePaneDescriptor Home { get; } = new(
        Title: "Home",
        Description: "Start with a new document or reopen a recent file.");

    public static BackstagePaneDescriptor Open { get; } = new(
        Title: "Open",
        Description: "Open a recent document, search recent local files, or browse for one stored on this PC.");

    public static BackstagePaneDescriptor SaveAs { get; } = new(
        Title: "Save As",
        Description: "Choose where to save this document and select an editable file type.");

    public static BackstagePaneDescriptor Print { get; } = new(
        Title: "Print",
        Description: "Review print settings for this document.");

    public static BackstagePaneDescriptor Share { get; } = new(
        Title: "Share",
        Description: "Share a saved local document or create a copy that can be sent elsewhere.");

    public static BackstagePaneDescriptor Export { get; } = new(
        Title: "Export",
        Description: "Create a fixed-layout copy or choose an editable document format.");

    public static BackstagePaneDescriptor Info { get; } = new(
        Title: "Info",
        Description: "Protect, inspect, and review document information.");

    public static BackstagePaneDescriptor Account { get; } = new(
        Title: "Account",
        Description: "Account settings and product information.");
}
