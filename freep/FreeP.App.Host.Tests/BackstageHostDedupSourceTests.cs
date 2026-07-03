using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class BackstageHostDedupSourceTests
{
    [Fact]
    public void BackstageView_DelegatesHostLifecycleAndActionsToSharedController()
    {
        var root = FindRepositoryRoot();
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
        source.Should().Contain("plan.DeferredActions.Where(action => action.IsEnabled)");
        source.Should().Contain("_backstage.ShowPane(\"Options\")");
        source.Should().NotContain("new BackstageViewShell(");
        source.Should().NotContain("SisterBackstageEntryBuilder.Build(");
        source.Should().NotContain("Hide(); _actions");
        source.Should().NotContain("_shell.Show");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
