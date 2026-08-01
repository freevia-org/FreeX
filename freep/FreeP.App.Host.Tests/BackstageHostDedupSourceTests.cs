using System.IO;
using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;

namespace FreeP.App.Host.Tests;

public sealed class BackstageHostDedupSourceTests
{
    [Fact]
    public void FreeP_wpf_entry_spec_uses_the_shared_thirteen_entry_order()
    {
        static UIElement Pane() => new Border();
        var entries = SisterBackstageEntryBuilder.Build(new SisterBackstageEntrySpec(
            Pane, static () => { }, static () => { }, static () => { }, static () => { },
            Pane, Pane, Pane)
        {
            BuildPrintPane = Pane,
            BuildExportPane = Pane,
            BuildAccountPane = Pane,
        });

        entries.Select(entry => entry.Separator ? "|" : entry.Label)
            .Should().Equal(
                "Info", "New", "Open", "|", "Save", "Save As", "Print", "Export",
                "Recent", "New from template", "Account", "Options", "Close");
        entries.Should().HaveCount(13);
    }

    [Fact]
    public void BackstageView_DelegatesHostLifecycleAndActionsToSharedController()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Host",
            "Backstage",
            "BackstageView.cs"));

        source.Should().Contain("SisterBackstageHostController");
        source.Should().Contain("new SisterBackstageHostSpec(");
        source.Should().Contain("Chrome = BackstageRibbonChrome.Create()");
        source.Should().Contain("public void Show() => _backstage.Show();");
        source.Should().Contain("public void Hide() => _backstage.Hide();");
        source.Should().Contain("backstage.FrameCommand(_actions.New)");
        source.Should().Contain("PresentationExportPlanner.BuildBackstageExportPlan()");
        source.Should().Contain("_backstage.HideThen(ResolveExportAction(action.CommandId))");
        source.Should().Contain("PresentationExportPlanner.PdfExportCommandId => _actions.ExportPdf");
        source.Should().Contain("PresentationExportPlanner.NotesPagePdfExportCommandId => _actions.ExportNotesPagePdf");
        source.Should().Contain("PresentationExportPlanner.ImageExportCommandId => _actions.ExportImages");
        source.Should().Contain("PresentationExportPlanner.VideoExportCommandId => _actions.ExportVideo");
        source.Should().Contain("ExportNotesPagePdf");
        source.Should().Contain("plan.Options.DisplaySummary");
        source.Should().Contain("plan.OutputOptionChoices");
        source.Should().Contain("plan.DeferredActions.Where(action =>");
        source.Should().Contain("_backstage.ShowPane(\"Options\")");
        source.Should().NotContain("new BackstageViewShell(");
        source.Should().NotContain("SisterBackstageEntryBuilder.Build(");
        source.Should().NotContain("Hide(); _actions");
        source.Should().NotContain("_shell.Show");
    }

}
