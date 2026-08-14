namespace Free.Shared.AppServices.Tests;

/// <summary>
/// Guards the Options planning core against re-duplication: FreeW and FreeP must keep routing their
/// General options surface through <c>Free.Shared.AppServices.BasicApplicationOptionsSurfacePlanner</c>
/// instead of re-declaring the format-choice record, the field schema, or the language-hint rule, and the
/// shared file must stay free of product text. FreeX deliberately stays out of this core — see the
/// recorded verdict in <c>src/FreeX.App.Services/OptionsDialogPlanner.cs</c>.
/// </summary>
public sealed class BasicApplicationOptionsSurfaceDedupSourceTests
{
    [Fact]
    public void SisterPlannersComposeTheSharedGeneralSurfaceInsteadOfRedeclaringIt()
    {
        var freeWPlanner = Read("freew", "FreeW.App.Presentation", "Options", "OptionsDialogPlanner.cs");
        var freePPlanner = Read("freep", "FreeP.App.Presentation", "Options", "OptionsDialogPlanner.cs");

        foreach (var planner in new[] { freeWPlanner, freePPlanner })
        {
            planner.Should().Contain("BasicApplicationOptionsSurfacePlanner.BuildGeneral(");
            planner.Should().Contain("BasicApplicationOptionsDialogSession<");
            planner.Should().NotContain("record OptionsDialogFormatChoice");
            planner.Should().NotContain("enum OptionsDialogGeneralFieldKind");
            planner.Should().NotContain("record OptionsDialogGeneralFieldSpec");
            planner.Should().NotContain("record OptionsDialogGeneralSurfaceSpec");
            planner.Should().NotContain("Empty = follow the system culture (currently {systemLanguageLabel})");
            planner.Should().NotContain("ApplicationOptionsNormalizer.NormalizeUiLanguage");
        }

        // FreeW composes the shared General spec directly into its own multi-tab surface.
        freeWPlanner.Should().Contain("BasicApplicationOptionsGeneralSpec General");
        // FreeP keeps a flat surface whose per-field members forward to the shared spec.
        freePPlanner.Should().Contain("General.RecentFilesLabel");
        freePPlanner.Should().Contain("General.FormatChoices");
    }

    [Fact]
    public void SisterSessionsAndRenderersCaptureTheSharedInputAndFormatChoice()
    {
        var freePSession = Read("freep", "FreeP.App.Presentation", "Options", "OptionsDialogSession.cs");
        freePSession.Should().Contain("BasicApplicationOptionsDialogInput input)");
        freePSession.Should().NotContain("record OptionsDialogInput");

        var freeWWpf = Read("freew", "FreeW.App.Host", "OptionsDialog.cs");
        var freeWAvalonia = Read("freew", "FreeW.App.Avalonia", "OptionsDialog.cs");
        var freePWpf = Read("freep", "FreeP.App.Host", "OptionsDialog.cs");
        var freePAvalonia = Read("freep", "FreeP.App.Avalonia", "OptionsDialog.cs");

        foreach (var renderer in new[] { freeWWpf, freeWAvalonia, freePWpf, freePAvalonia })
        {
            renderer.Should().Contain("_session.PlanAcceptance(");
            renderer.Should().NotContain("OptionsDialogFormatChoice");
            renderer.Should().NotContain("OptionsDialogGeneralFieldKind");
            renderer.Should().NotContain("TryParseRecentFilesCap");
            renderer.Should().NotContain("Empty = follow the system culture");
        }

        // FreeW projects the shared General spec (field kinds + choices) into its tabbed layout.
        foreach (var renderer in new[] { freeWWpf, freeWAvalonia })
        {
            renderer.Should().Contain("_surface.General.Fields");
            renderer.Should().Contain("_surface.General.FormatChoices");
            renderer.Should().Contain("BasicApplicationOptionsFieldKind.");
        }

        // The three shells that bind the choice list straight into a picker read back the shared record.
        foreach (var renderer in new[] { freeWAvalonia, freePWpf, freePAvalonia })
            renderer.Should().Contain("as ApplicationOptionsFormatChoice");

        // FreeP captures the shared basic input record rather than a FreeP copy of it.
        foreach (var renderer in new[] { freePWpf, freePAvalonia })
            renderer.Should().Contain("new BasicApplicationOptionsDialogInput(");
    }

    [Fact]
    public void SharedSurfaceCoreCarriesNoProductTextOrAppTypes()
    {
        var shared = Read("shared", "Free.Shared.AppServices", "BasicApplicationOptionsSurfacePlanner.cs");

        shared.Should().NotContain("Word Document");
        shared.Should().NotContain("Presentation (*.fxp)");
        shared.Should().NotContain(".docx");
        shared.Should().NotContain(".fxp");
        shared.Should().NotContain("AutoCorrect");
        shared.Should().NotContain("FreeWOptions");
        shared.Should().NotContain("FreePOptions");
        shared.Should().NotContain("using FreeW");
        shared.Should().NotContain("using FreeP");
        shared.Should().NotContain("using System.Windows");
        shared.Should().NotContain("using Avalonia");
    }

    [Fact]
    public void FreeXOptionsPlannerRecordsItsKeepLocalVerdictAndStaysOffTheBasicCore()
    {
        var freeX = Read("src", "FreeX.App.Services", "OptionsDialogPlanner.cs");

        freeX.Should().Contain("NOT duplication of the sister apps");
        freeX.Should().Contain("record OptionsDialogInput(");
        freeX.Should().NotContain("BasicApplicationOptionsSurfacePlanner.");
        freeX.Should().NotContain("BasicApplicationOptionsDialogInput");
        freeX.Should().NotContain("ApplicationOptionsFormatChoice");
        freeX.Should().NotContain("ApplicationOptionsNormalizer.TryParseRecentFilesCap");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts));
}
