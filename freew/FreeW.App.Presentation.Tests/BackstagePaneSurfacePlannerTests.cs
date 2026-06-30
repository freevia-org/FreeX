using System.Linq;
using Free.Shared.AppServices;
using Free.Shared.IO;
using FreeW.App.Presentation.Backstage;
using FreeW.Core.IO;

namespace FreeW.App.Presentation.Tests;

public sealed class BackstagePaneSurfacePlannerTests
{
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
            .Should().Equal("Places", "Word Documents", "Web Pages", "Other Formats");
        surface.Groups[0].Actions.Select(action => action.Label).Should().Equal("This PC", "Browse");

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
    }

    private static IEnumerable<FileFormatDescriptor> Formats() =>
        DocumentFileAdapterCatalog.CreateDefaultAdapters().SelectMany(adapter => adapter.Formats);
}
