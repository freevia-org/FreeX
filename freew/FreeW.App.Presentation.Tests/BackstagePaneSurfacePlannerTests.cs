using System.Linq;
using Free.Shared.AppServices;
using Free.Shared.IO;
using Free.Shared.Localization;
using Free.Shared.Shell;
using FreeW.App.Presentation.Backstage;
using FreeW.Core.IO;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class BackstagePaneSurfacePlannerTests
{
    [Fact]
    public void HomePaneVisualMetrics_match_WPF_authority_surface_registration()
    {
        var metrics = BackstagePaneSurfacePlanner.HomePaneVisualMetrics;

        metrics.PaneMaxWidth.Should().Be(720);
        metrics.HeadingFontSize.Should().Be(26);
        metrics.HeadingBottomMargin.Should().Be(new BackstageThickness(0, 0, 0, 18));
        metrics.DescriptionFontSize.Should().Be(12);
        metrics.DescriptionBottomMargin.Should().Be(new BackstageThickness(0, 0, 0, 16));
        metrics.SectionHeaderFontSize.Should().Be(15);
        metrics.SectionHeaderMargin.Should().Be(new BackstageThickness(0, 16, 0, 6));
        metrics.ActionFontSize.Should().Be(14);
        metrics.DescriptionTextFontSize.Should().Be(11);
        metrics.ActionRowMargin.Should().Be(new BackstageThickness(0, 0, 0, 10));
        metrics.ActionDescriptionMargin.Should().Be(new BackstageThickness(0, 2, 0, 0));
    }

    [Fact]
    public void BuildHomePane_Returns_shared_order_geometry_and_live_callbacks()
    {
        var opened = string.Empty;
        var surface = BackstagePaneSurfacePlanner.BuildHomePane(
            [
                new RecentFileEntry { Path = @"C:\Docs\Budget.docx" },
                new RecentFileEntry { Path = @"C:\Docs\Plan.rtf", IsPinned = true },
            ],
            newDocument: static () => { },
            openRecent: path => opened = path,
            browse: static () => { },
            openMore: static () => { });

        surface.VisualMetrics.Should().Be(BackstagePaneSurfacePlanner.HomePaneVisualMetrics);
        surface.Groups.Select(group => group.Heading).Should().Equal("New", "Recent Documents", "Open");
        surface.Groups.SelectMany(group => group.Actions).Select(action => action.Label)
            .Should().Equal("Blank document", "Budget.docx", "Plan.rtf", "Browse", "Open More Documents");

        surface.Groups[1].Actions[1].Invoke();
        opened.Should().Be(@"C:\Docs\Plan.rtf");
    }

    [Fact]
    public void OpenPaneVisualMetrics_match_WPF_authority_surface_registration()
    {
        var metrics = BackstagePaneSurfacePlanner.OpenPaneVisualMetrics;

        metrics.SearchMargin.Should().Be(new BackstageThickness(0, 0, 0, 12));
        metrics.SearchPadding.Should().Be(new BackstageThickness(8, 3, 8, 3));
        metrics.TabsWidth.Should().Be(640);
        metrics.TabsMargin.Should().Be(new BackstageThickness(0, 0, 0, 14));
        metrics.ActionRowMargin.Should().Be(new BackstageThickness(0, 0, 0, 10));
        metrics.DescriptionMargin.Should().Be(new BackstageThickness(0, 2, 0, 0));
    }

    [Fact]
    public void BuildPrintPane_ReturnsPageFieldsAndWiredActions()
    {
        var printed = false;
        var previewed = false;

        var surface = BackstagePaneSurfacePlanner.BuildPrintPane(
            "Agenda",
            new PageSettings
            {
                WidthPt = 612,
                HeightPt = 792,
                MarginTopPt = 72,
                MarginBottomPt = 72,
                MarginLeftPt = 54,
                MarginRightPt = 54,
            },
            print: () => printed = true,
            printPreview: () => previewed = true);

        surface.Title.Should().Be("Print");
        surface.Description.Should().Contain("Print this document");
        surface.DeferredNote.Should().BeNull();
        surface.Fields.Should().Contain(row => row.Label == "Document" && row.Value == "Agenda");
        surface.Fields.Should().Contain(row => row.Label == "Paper" && row.Value == "8.5\" x 11\"");
        surface.Groups.Select(group => group.Heading).Should().Equal("Print", "Settings");
        surface.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.PrintPreviewFidelity &&
            row.FixtureScenarioIds.Contains("backstage-print-preview-fidelity") &&
            row.Requirements.Count == 2);
        surface.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.PdfExportFidelity &&
            row.FixtureScenarioIds.Contains("backstage-pdf-export-fidelity") &&
            row.Requirements.Count == 2);
        surface.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.NativePrint &&
            row.Status == BackstagePrintEvidenceStatus.HostBacked &&
            row.Requirements.Count == 0);

        var print = surface.Groups.SelectMany(group => group.Actions)
            .Single(action => action.AutomationId == "PrintAction_Print");
        var preview = surface.Groups.SelectMany(group => group.Actions)
            .First(action => action.AutomationId == "PrintAction_PrintPreview");

        print.IsEnabled.Should().BeTrue();
        preview.IsEnabled.Should().BeTrue();

        print.Invoke!();
        preview.Invoke!();

        printed.Should().BeTrue();
        previewed.Should().BeTrue();
    }

    [Fact]
    public void BuildPrintPane_WhenPreviewIsAvailableButNativePrintIsMissing_ExplainsDeferredPrint()
    {
        var previewed = false;

        var surface = BackstagePaneSurfacePlanner.BuildPrintPane(
            "Draft",
            new PageSettings(),
            print: null,
            printPreview: () => previewed = true,
            directPrintCapability: BackstageDirectPrintCapability.Deferred(
                "The current Avalonia target exposes no native PrintDialog or printer service; use Print Preview or Create PDF for OS printing."));

        surface.DeferredNote.Should().Be(BackstageViewTextResources.DirectPrintDeferredNote);
        surface.Fields.Should().Contain(row =>
            row.Label == "Direct print" &&
            row.Value.Contains("current Avalonia target", StringComparison.Ordinal));
        surface.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.NativePrint &&
            row.Status == BackstagePrintEvidenceStatus.Deferred &&
            row.Description.Contains("PrintDialog", StringComparison.Ordinal));

        var print = surface.Groups.SelectMany(group => group.Actions)
            .Single(action => action.AutomationId == "PrintAction_Print");
        var preview = surface.Groups.SelectMany(group => group.Actions)
            .First(action => action.AutomationId == "PrintAction_PrintPreview");

        print.IsEnabled.Should().BeFalse("native printer selection is host-specific and can be deferred independently of preview");
        print.Description.Should().Contain("Create PDF");
        preview.IsEnabled.Should().BeTrue();

        preview.Invoke!();

        previewed.Should().BeTrue();
    }

    [Fact]
    public void BuildPrintPane_DisablesActionsWhenCallbacksAreMissing()
    {
        var surface = BackstagePaneSurfacePlanner.BuildPrintPane(
            "Draft",
            new PageSettings(),
            print: null,
            printPreview: null);

        surface.DeferredNote.Should().Be(BackstageViewTextResources.DirectPrintDeferredNote);
        surface.Groups.SelectMany(group => group.Actions)
            .Should().OnlyContain(action => !action.IsEnabled && action.Invoke == null);
    }

    [Fact]
    public void BuildInfoPane_ReturnsDocumentFieldsAndSafetyActions()
    {
        var marked = false;
        var inspected = false;

        var surface = BackstagePaneSurfacePlanner.BuildInfoPane(
            [new BackstageFieldRow("Document", "Plan.docx")],
            markAsFinal: () => marked = true,
            restrictEditing: null,
            inspectDocument: () => inspected = true,
            checkAccessibility: null);

        surface.Title.Should().Be("Info");
        surface.Description.Should().Contain("Protect");
        surface.DocumentFields.Should().Equal(new BackstageFieldRow("Document", "Plan.docx"));
        surface.SafetyGroups.Select(group => group.Heading).Should().Equal("Protect Document", "Inspect Document");

        var markAsFinal = surface.SafetyGroups.SelectMany(group => group.Actions)
            .Single(action => action.AutomationId == "InfoAction_MarkAsFinal");
        var restrictEditing = surface.SafetyGroups.SelectMany(group => group.Actions)
            .Single(action => action.AutomationId == "InfoAction_RestrictEditing");
        var inspectDocument = surface.SafetyGroups.SelectMany(group => group.Actions)
            .Single(action => action.AutomationId == "InfoAction_InspectDocument");

        markAsFinal.IsEnabled.Should().BeTrue();
        restrictEditing.IsEnabled.Should().BeFalse();
        inspectDocument.IsEnabled.Should().BeTrue();

        markAsFinal.Invoke!();
        inspectDocument.Invoke!();

        marked.Should().BeTrue();
        inspected.Should().BeTrue();
    }

    [Fact]
    public void BuildInfoPane_UsesDocumentStateForSafetyActionText()
    {
        var document = new TextDocument
        {
            MarkedAsFinal = true,
            Protection = new ProtectionSettings(ProtectionMode.TrackChangesOnly),
        };
        document.Properties.Author = "Ada";

        var surface = BackstagePaneSurfacePlanner.BuildInfoPane(
            [new BackstageFieldRow("Document", "Plan.docx")],
            markAsFinal: static () => { },
            restrictEditing: static () => { },
            inspectDocument: static () => { },
            checkAccessibility: static () => { },
            document: document);

        var actions = surface.SafetyGroups.SelectMany(group => group.Actions).ToArray();

        actions.Single(action => action.AutomationId == "InfoAction_MarkAsFinal")
            .Label.Should().Be("Edit Anyway");
        actions.Single(action => action.AutomationId == "InfoAction_RestrictEditing")
            .Description.Should().Contain("Tracked changes only");
        actions.Single(action => action.AutomationId == "InfoAction_InspectDocument")
            .Description.Should().Contain("1 metadata item");
    }

    [Fact]
    public void BuildAccountPane_ReturnsAccountRowsAndOptionsAction()
    {
        var openedOptions = false;

        var surface = BackstagePaneSurfacePlanner.BuildAccountPane(
            new SisterBackstageAccountPaneContext(
                "FreeW",
                "1.2.3",
                "Ada",
                "WORD-BOX",
                @"C:\Users\Ada\AppData\Local\FreeW"),
            openOptions: () => openedOptions = true);

        surface.Title.Should().Be("Account");
        surface.Description.Should().Contain("FreeW installation");
        surface.Groups.Select(group => group.Heading).Should().Equal("Product Information", "User Information");
        surface.Groups[0].Fields.Should().Contain(new BackstageFieldRow("Version", "1.2.3"));
        surface.OptionsAction.Label.Should().Be("FreeW Options...");
        surface.OptionsAction.AutomationId.Should().Be("AccountOptionsButton");
        surface.OptionsAction.IsEnabled.Should().BeTrue();

        surface.OptionsAction.Invoke!();

        openedOptions.Should().BeTrue();
    }

    [Fact]
    public void BuildOpenPane_ReturnsSearchTabsAndFilteredRows()
    {
        var opened = "";
        var openedFolder = "";

        var surface = BackstagePaneSurfacePlanner.BuildOpenPane(
            [
                new RecentFileEntry { Path = @"C:\Docs\Budget.docx" },
                new RecentFileEntry { Path = @"C:\Docs\Notes.rtf" },
                new RecentFileEntry { Path = @"C:\Reports\Budget Review.docx" },
            ],
            filter: "budget",
            openRecent: path => opened = path,
            openFolder: path => openedFolder = path,
            browse: static () => { },
            recoverUnsaved: static () => { });

        surface.Title.Should().Be("Open");
        surface.Description.Should().Be("Open a recent document, search recent local files, or browse for one stored on this PC.");
        surface.Search.AutomationName.Should().Be("Search recent documents");
        surface.Tabs.DocumentsTabLabel.Should().Be("Documents");
        surface.Tabs.FoldersTabLabel.Should().Be("Folders");
        surface.Tabs.EmptyDocumentsText.Should().Be("No recent documents match this search.");
        surface.Tabs.EmptyFoldersText.Should().Be("No recent folders match this search.");
        surface.Tabs.PlacesHeading.Should().Be("Places");
        surface.Tabs.RecoveryHeading.Should().Be("Recovery");

        surface.Plan.DocumentRows.Select(row => row.Label).Should().Equal("Budget.docx", "Budget Review.docx");
        surface.Plan.FolderRows.Select(row => row.Label).Should().Equal("Docs", "Reports");
        surface.Plan.PlaceRows.Select(row => row.Label).Should().Equal("This PC", "Browse");
        surface.Plan.RecoveryRows.Single().Label.Should().Be("Recover Unsaved Documents");

        surface.Plan.DocumentRows[1].Invoke();
        surface.Plan.FolderRows[0].Invoke();

        opened.Should().Be(@"C:\Reports\Budget Review.docx");
        openedFolder.Should().Be(@"C:\Docs");
    }

    [Fact]
    public void BuildOpenPane_PreservesCrossHostActionOrderAndDescriptions()
    {
        var surface = BackstagePaneSurfacePlanner.BuildOpenPane(
            [new RecentFileEntry { Path = @"C:\Docs\Budget.docx" }],
            filter: null,
            openRecent: static _ => { },
            openFolder: static _ => { },
            browse: static () => { },
            recoverUnsaved: static () => { });

        surface.Plan.DocumentRows.Select(row => row.Label)
            .Concat(surface.Plan.FolderRows.Select(row => row.Label))
            .Concat(surface.Plan.PlaceRows.Select(row => row.Label))
            .Concat(surface.Plan.RecoveryRows.Select(row => row.Label))
            .Should().Equal("Budget.docx", "Docs", "This PC", "Browse", "Recover Unsaved Documents");

        surface.Plan.PlaceRows.Select(row => row.Description)
            .Should().Equal(
                "Browse local folders and connected drives.",
                "Open the Windows file picker.");
    }

    [Fact]
    public void BuildSaveAsPane_ReturnsInlineSurfacePlacesAndFileTypes()
    {
        var saveAsCount = 0;
        var savedExtension = "";

        var surface = BackstagePaneSurfacePlanner.BuildSaveAsPane(
            Formats(),
            displayName: "Quarterly Draft",
            currentPath: @"C:\Docs\Quarterly Draft.rtf",
            saveAs: () => saveAsCount++,
            saveAsExtension: extension => savedExtension = extension);

        surface.Title.Should().Be("Save As");
        surface.Description.Should().Be("Choose where to save this document and select an editable file type.");
        surface.Inline.FileNameHeading.Should().Be("File name");
        surface.Inline.SaveAsTypeHeading.Should().Be("Save as type");
        surface.Inline.SaveButtonLabel.Should().Be("Save");
        surface.InlinePlan.SuggestedFileName.Should().Be("Quarterly Draft.rtf");
        surface.InlinePlan.SelectedExtension.Should().Be(".rtf");

        surface.Groups.Select(group => group.Heading)
            .Should().Equal("Places", "Word Documents", "Web Pages", "Other Formats", "Compatibility Formats");
        surface.Groups[0].Actions.Select(action => action.Label).Should().Equal("This PC", "Browse");
        surface.Groups.Single(group => group.Heading == "Compatibility Formats")
            .Actions.Single(action => action.Label == "Word 97-2003 Document (*.doc)")
            .Description.Should().Contain("Compatibility format");

        surface.Groups[0].Actions[1].Invoke();
        surface.Groups.Single(group => group.Heading == "Other Formats")
            .Actions.Single(action => action.Label == "Plain Text (*.txt, *.text)")
            .Invoke();

        saveAsCount.Should().Be(1);
        savedExtension.Should().Be(".txt");
    }

    [Fact]
    public void BuildSharePane_ReturnsSharedPaneTextAndRows()
    {
        var openedPath = "";
        var savedCopy = false;

        var surface = BackstagePaneSurfacePlanner.BuildSharePane(
            @"C:\Docs\Plan.docx",
            path => path == @"C:\Docs\Plan.docx",
            saveAs: static () => { },
            openContainingFolder: path => openedPath = path,
            saveCopy: () => savedCopy = true,
            exportPdf: static () => { });

        surface.Title.Should().Be("Share");
        surface.Description.Should().Be("Share a saved local document or create a copy that can be sent elsewhere.");
        surface.Groups.Select(group => group.Heading).Should().Equal("Share", "Send a Copy");
        surface.Groups[0].Actions.Single().Label.Should().Be("Open Containing Folder");

        surface.Groups[0].Actions.Single().Invoke();
        surface.Groups[1].Actions.Single(action => action.Label == "Save a Copy").Invoke();

        openedPath.Should().Be(@"C:\Docs\Plan.docx");
        savedCopy.Should().BeTrue();
    }

    [Fact]
    public void BuildExportPane_UsesExportTextCapabilitiesAndChangeFileTypeRows()
    {
        var exportedPdf = false;
        var exportedXps = false;
        var savedExtension = "";
        var text = BackstageExportPaneSurfaceText.FromDescriptor(
            SisterBackstagePaneTextDescriptorPlanner.Build(SisterBackstageAppKind.FreeW).Export,
            key => key == SisterBackstagePaneResourceKeys.FreeWExportDescription ? "Localized export surface." : null);

        var surface = BackstagePaneSurfacePlanner.BuildExportPane(
            Formats(),
            exportPdf: () => exportedPdf = true,
            exportXps: () => exportedXps = true,
            saveAsExtension: extension => savedExtension = extension,
            text);

        surface.Title.Should().Be("Export");
        surface.Description.Should().Be("Localized export surface.");
        surface.Groups.Select(group => group.Heading).Should().Equal("Create PDF/XPS Document", "Change File Type");
        surface.Groups[0].Actions.Select(action => action.Label).Should().Equal("Create PDF or XPS", "Export to XPS");
        surface.Groups[0].Actions[0].Description.Should().Contain("Export-only fixed-layout PDF copy");
        surface.Groups[0].Actions[1].Description.Should().Contain("Export-only fixed-layout XPS copy");

        surface.Groups[0].Actions[0].Invoke();
        surface.Groups[0].Actions[1].Invoke();
        surface.Groups[1].Actions.Single(action => action.Label == "Word Document (*.docx)").Invoke();

        exportedPdf.Should().BeTrue();
        exportedXps.Should().BeTrue();
        savedExtension.Should().Be(".docx");

        var pdfOnly = BackstagePaneSurfacePlanner.BuildExportPane(
            Formats(),
            exportPdf: static () => { },
            exportXps: null,
            saveAsExtension: static _ => { });

        pdfOnly.Groups[0].Actions.Select(action => action.Label).Should().Equal("Create PDF");
        pdfOnly.Groups[0].Actions[0].Description.Should().Contain("not editable round-trip support");
    }

    [Fact]
    public void BuildExportPane_WhenXpsIsUnavailable_KeepsPdfAndEditableFormatOrder()
    {
        var surface = BackstagePaneSurfacePlanner.BuildExportPane(
            Formats(),
            exportPdf: static () => { },
            exportXps: null,
            saveAsFormat: static (_, _) => { });

        surface.Groups.Select(group => group.Heading)
            .Should().Equal("Create PDF/XPS Document", "Change File Type");
        surface.Groups[0].Actions.Select(action => action.Label)
            .Should().Equal("Create PDF");
        surface.Groups[1].Actions.Select(action => action.Label)
            .Should().Equal(
                "Word Document (*.docx)",
                "Strict Open XML Document (*.docx)",
                "Word Macro-Enabled Document (*.docm)",
                "Word Template (*.dotx)",
                "Word Macro-Enabled Template (*.dotm)",
                "Word XML Document (*.xml)",
                "Word 2003 XML Document (*.xml)",
                "Web Page, Filtered (*.htm, *.html)",
                "Web Page (*.htm, *.html)",
                "Single File Web Page (*.mht, *.mhtml)",
                "OpenDocument Text (*.odt)",
                "OpenDocument Text Template (*.ott)",
                "Rich Text Format (*.rtf)",
                "Plain Text (*.txt, *.text)",
                "Log File (*.log)",
                "Word 97-2003 Document (*.doc)",
                "Word 97-2003 Template (*.dot)");
    }

    [Fact]
    public void BuildExportPane_Uses_one_shared_WPF_authority_action_order_contract()
    {
        var invoked = new List<string>();
        var surface = BackstagePaneSurfacePlanner.BuildExportPane(
            Formats(),
            exportPdf: () => invoked.Add("pdf"),
            exportXps: () => invoked.Add("xps"),
            saveAsFormat: (_, _) => invoked.Add("format"));

        surface.Groups.SelectMany(group => group.Actions).Select(action => action.Label)
            .Should().Equal(
                "Create PDF or XPS",
                "Export to XPS",
                "Word Document (*.docx)",
                "Strict Open XML Document (*.docx)",
                "Word Macro-Enabled Document (*.docm)",
                "Word Template (*.dotx)",
                "Word Macro-Enabled Template (*.dotm)",
                "Word XML Document (*.xml)",
                "Word 2003 XML Document (*.xml)",
                "Web Page, Filtered (*.htm, *.html)",
                "Web Page (*.htm, *.html)",
                "Single File Web Page (*.mht, *.mhtml)",
                "OpenDocument Text (*.odt)",
                "OpenDocument Text Template (*.ott)",
                "Rich Text Format (*.rtf)",
                "Plain Text (*.txt, *.text)",
                "Log File (*.log)",
                "Word 97-2003 Document (*.doc)",
                "Word 97-2003 Template (*.dot)");

        surface.Groups[0].Actions[0].Invoke();
        surface.Groups[0].Actions[1].Invoke();
        surface.Groups[1].Actions[0].Invoke();
        invoked.Should().Equal("pdf", "xps", "format");
    }

    [Fact]
    public void ExportPaneVisualMetrics_encode_the_measured_WPF_authority_geometry()
    {
        var metrics = BackstageExportPanePlanner.VisualMetrics;

        metrics.PaneMaxWidth.Should().Be(720);
        metrics.HeadingFontSize.Should().Be(26);
        metrics.HeadingBottomMargin.Should().Be(new BackstageThickness(0, 0, 0, 18));
        metrics.DescriptionFontSize.Should().Be(12);
        metrics.DescriptionBottomMargin.Should().Be(new BackstageThickness(0, 0, 0, 16));
        metrics.SectionHeaderFontSize.Should().Be(15);
        metrics.SectionHeaderMargin.Should().Be(new BackstageThickness(0, 16, 0, 6));
        metrics.ActionFontSize.Should().Be(14);
        metrics.DescriptionTextFontSize.Should().Be(11);
        metrics.ActionRowMargin.Should().Be(new BackstageThickness(0, 0, 0, 10));
        metrics.ActionDescriptionMargin.Should().Be(new BackstageThickness(0, 2, 0, 0));
    }

    [Fact]
    public void BackstageExportPaneSurfaceText_FallsBackForUnresolvedDescriptorResources()
    {
        var descriptor = SisterBackstagePaneTextDescriptorPlanner.Build(SisterBackstageAppKind.FreeW).Export;
        var text = BackstageExportPaneSurfaceText.FromDescriptor(descriptor, Resolve);

        text.Description.Should().Be(descriptor.Description.FallbackText);
        text.PdfActionLabel.Should().Be(descriptor.PdfActionLabel.FallbackText);
        text.PdfActionDescription.Should().Be(descriptor.PdfActionDescription.FallbackText);

        string? Resolve(string key)
        {
            if (key == descriptor.Description.ResourceKey)
                return LocalizedTextCatalog.CreateMissingText(key);
            if (key == descriptor.PdfActionLabel.ResourceKey)
                return key;
            if (key == descriptor.PdfActionDescription.ResourceKey)
                return string.Empty;

            return null;
        }
    }

    private static IEnumerable<FileFormatDescriptor> Formats() =>
        DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(adapter => adapter.Formats);
}
