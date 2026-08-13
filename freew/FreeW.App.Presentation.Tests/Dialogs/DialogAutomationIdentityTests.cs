using Free.Shared.IO;
using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.Dialogs;

namespace FreeW.App.Presentation.Tests.Dialogs;

public sealed class DialogAutomationIdentityTests
{
    [Fact]
    public void Dialog_planners_own_stable_cross_renderer_automation_ids()
    {
        CellShadingDialogPlanner.SwatchAutomationId(0).Should().Be("CellShadingSwatch0");
        CellShadingDialogPlanner.SwatchAutomationId(CellShadingDialogPlanner.Palette.Count - 1)
            .Should().Be($"CellShadingSwatch{CellShadingDialogPlanner.Palette.Count - 1}");
        CellShadingDialogPlanner.NoColorAutomationId.Should().Be("CellShadingNoColorButton");
        ParagraphBreaksDialogPlanner.LeftIndentAutomationId.Should().Be("paragraph-left-indent");
        PasswordPromptDialogSession.WindowAutomationId.Should().Be("PasswordPromptDialog");
        PasswordPromptDialogSession.PasswordAutomationId.Should().Be("PasswordPromptPasswordBox");
        PasswordPromptDialogSession.AcceptButtonAutomationId.Should().Be("PasswordPromptOkButton");
        PasswordPromptDialogSession.CancelButtonAutomationId.Should().Be("PasswordPromptCancelButton");
    }

    [Fact]
    public void Backstage_surface_projects_window_search_and_save_editor_ids()
    {
        var open = BackstagePaneSurfacePlanner.BuildOpenPane(
            [],
            filter: null,
            openRecent: static _ => { },
            openFolder: static _ => { },
            browse: static () => { },
            recoverUnsaved: static () => { });
        var saveAs = BackstagePaneSurfacePlanner.BuildSaveAsPane(
            [new FileFormatDescriptor(".docx", "Word Document")],
            "Document.docx",
            currentPath: null,
            saveAs: static () => { },
            saveAsExtension: static _ => { });

        BackstagePaneSurfacePlanner.WindowAutomationId.Should().Be("FreeWBackstageWindow");
        open.Search.AutomationId.Should().Be("OpenSearchBox");
        saveAs.Inline.FileNameAutomationId.Should().Be("SaveAsSuggestedFileName");
        saveAs.Inline.FileTypeAutomationId.Should().Be("SaveAsSelectedExtension");
    }
}
