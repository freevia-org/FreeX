using System.Globalization;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests.Options;

public sealed class FreePOptionsPolicyTests
{
    [Fact]
    public void DescriptorsOwnDefaultsPersistenceNamesOrderingAndActivation()
    {
        FreePOptionsPolicy.Descriptors.Select(descriptor => descriptor.Kind).Should().Equal(
            FreePOptionKind.RecentFilesCap,
            FreePOptionKind.DefaultSaveFormat,
            FreePOptionKind.UiLanguage);
        FreePOptionsPolicy.Descriptors.Select(descriptor => descriptor.ApplyOrder).Should().Equal(0, 1, 2);
        FreePOptionsPolicy.Descriptors.Select(descriptor => descriptor.PersistencePropertyName).Should().Equal(
            nameof(FreePOptions.RecentFilesCap),
            nameof(FreePOptions.DefaultSaveFormat),
            nameof(FreePOptions.UiLanguage));
        FreePOptionsPolicy.Descriptors.Select(descriptor => descriptor.DefaultValue).Should().Equal(
            FreePOptions.DefaultRecentFilesCap,
            FreePOptions.FxpDefaultFormat,
            FreePOptions.SystemDefaultLanguage);
        FreePOptionsPolicy.Descriptors.Select(descriptor => descriptor.Activation).Should().Equal(
            FreePOptionActivation.Immediate,
            FreePOptionActivation.Immediate,
            FreePOptionActivation.ApplicationRestart);
    }

    [Fact]
    public void RuntimeSessionNormalizesLoadedOptionsWithoutReplacingTheLiveInstance()
    {
        var live = new FreePOptions
        {
            RecentFilesCap = 999,
            DefaultSaveFormat = " ",
            UiLanguage = "  en-us  ",
        };

        var session = new FreePOptionsRuntimeSession(live);

        session.LiveOptions.Should().BeSameAs(live);
        live.RecentFilesCap.Should().Be(FreePOptions.MaxRecentFilesCap);
        live.DefaultSaveFormat.Should().Be(FreePOptions.FxpDefaultFormat);
        live.UiLanguage.Should().Be("en-US");
    }

    [Fact]
    public void ApplyAndPersistDetectsChangesOrdersApplicationAndPersistsTheNormalizedProjection()
    {
        var live = new FreePOptions
        {
            RecentFilesCap = 15,
            DefaultSaveFormat = FreePOptions.FxpDefaultFormat,
            UiLanguage = "",
        };
        var session = new FreePOptionsRuntimeSession(live);
        FreePOptionsSnapshot? observedAtPersistence = null;

        var outcome = session.ApplyAndPersist(
            new FreePOptions
            {
                RecentFilesCap = 4,
                DefaultSaveFormat = " ",
                UiLanguage = "  fr-fr  ",
            },
            options =>
            {
                observedAtPersistence = FreePOptionsPolicy.CaptureNormalized(options);
                return true;
            });

        outcome.Plan.Changes.Should().Be(new FreePOptionsChangeSet(
            RecentFilesCapChanged: true,
            DefaultSaveFormatChanged: false,
            UiLanguageChanged: true));
        outcome.Plan.Steps.Select(step => step.Kind).Should().Equal(
            FreePOptionKind.RecentFilesCap,
            FreePOptionKind.UiLanguage);
        outcome.Plan.After.Should().Be(new FreePOptionsSnapshot(
            4,
            FreePOptions.FxpDefaultFormat,
            "fr-FR"));
        observedAtPersistence.Should().Be(outcome.Plan.After);
        outcome.Plan.After.ToOptions().Should().BeEquivalentTo(live);
        outcome.PersistenceAttempted.Should().BeTrue();
        outcome.Persisted.Should().BeTrue();
        live.RecentFilesCap.Should().Be(4);
        live.UiLanguage.Should().Be("fr-FR");
    }

    [Fact]
    public void SideEffectsRequireApplicationRestartOnlyForLanguageChanges()
    {
        var plan = FreePOptionsPolicy.PlanApply(
            new FreePOptions { UiLanguage = "en-US" },
            new FreePOptions { RecentFilesCap = 7, UiLanguage = "fr-FR" });

        plan.SideEffects.UpdateRecentFilesPolicy.Should().BeTrue();
        plan.SideEffects.UpdateDefaultSaveFormatPolicy.Should().BeFalse();
        plan.SideEffects.RefreshOptionsSummary.Should().BeTrue();
        plan.SideEffects.ApplicationRestart.Should().Be(FreePOptionsRestartDecision.Required);
        plan.SideEffects.PresentationReload.Should().Be(FreePOptionsPresentationReloadDecision.NotRequired);
    }

    [Fact]
    public void AcceptedUnchangedOptionsStillPersistToPreserveExistingHostBehavior()
    {
        var session = new FreePOptionsRuntimeSession(new FreePOptions());
        var persistenceCalls = 0;

        var outcome = session.ApplyAndPersist(
            new FreePOptions(),
            _ =>
            {
                persistenceCalls++;
                return true;
            });

        outcome.Plan.Changes.Any.Should().BeFalse();
        outcome.Plan.Steps.Should().BeEmpty();
        outcome.Plan.ShouldPersist.Should().BeTrue();
        outcome.Plan.SideEffects.ApplicationRestart.Should().Be(FreePOptionsRestartDecision.NotRequired);
        outcome.Plan.SideEffects.PresentationReload.Should().Be(FreePOptionsPresentationReloadDecision.NotRequired);
        persistenceCalls.Should().Be(1);
    }

    [Fact]
    public void PersistenceFailureDoesNotRollBackTheAlreadyAppliedLiveOptions()
    {
        var live = new FreePOptions();
        var session = new FreePOptionsRuntimeSession(live);

        var outcome = session.ApplyAndPersist(
            new FreePOptions { RecentFilesCap = 3 },
            _ => false);

        outcome.Persisted.Should().BeFalse();
        live.RecentFilesCap.Should().Be(3);
    }

    [Fact]
    public void DialogSessionOwnsInitialStateValidationAndNormalizedAcceptance()
    {
        var session = new OptionsDialogSession(
            new FreePOptions { RecentFilesCap = 9, UiLanguage = "en-US" },
            CultureInfo.GetCultureInfo("en-US"));

        session.InitialState.RecentFilesCapText.Should().Be("9");
        session.InitialState.SelectedFormat.Should().Be(FreePOptions.FxpDefaultFormat);
        session.InitialState.UiLanguage.Should().Be("en-US");

        var invalid = session.PlanAcceptance(new OptionsDialogInput("bad", null, null));
        invalid.ShouldApply.Should().BeFalse();
        invalid.ShouldPersist.Should().BeFalse();
        invalid.Validation.Should().Be(new OptionsDialogValidation(
            OptionsDialogValidationTarget.RecentFilesCap,
            OptionsDialogSession.RecentFilesCapValidationMessage));

        var accepted = session.PlanAcceptance(new OptionsDialogInput("6", " ", "  uk-ua  "));
        accepted.ShouldApply.Should().BeTrue();
        accepted.ShouldPersist.Should().BeTrue();
        accepted.Result.Should().BeEquivalentTo(new FreePOptions
        {
            RecentFilesCap = 6,
            DefaultSaveFormat = FreePOptions.FxpDefaultFormat,
            UiLanguage = "uk-UA",
        });
    }

    [Fact]
    public void NativeHostsDelegateOptionsPolicyToPresentationSessions()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpfDialog = Read(root, "freep", "FreeP.App.Host", "OptionsDialog.cs");
        var avaloniaDialog = Read(root, "freep", "FreeP.App.Avalonia", "OptionsDialog.cs");
        var wpfWindow = Read(root, "freep", "FreeP.App.Host", "MainWindow.cs");
        var avaloniaWindow = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var wpfProgram = Read(root, "freep", "FreeP.App.Host", "Program.cs");
        var avaloniaApp = Read(root, "freep", "FreeP.App.Avalonia", "App.cs");

        foreach (var source in new[] { wpfDialog, avaloniaDialog })
        {
            source.Should().Contain("new OptionsDialogSession(");
            source.Should().Contain("_session.PlanAcceptance(");
            source.Should().NotContain("OptionsDialogPlanner.BuildSurface(");
            source.Should().NotContain("OptionsDialogPlanner.TryParseRecentFilesCap(");
            source.Should().NotContain("OptionsDialogPlanner.BuildResult(");
            source.Should().NotContain("private static string SystemLanguageLabel");
            source.Should().NotContain("Enter a whole number between");
        }

        foreach (var source in new[] { wpfWindow, avaloniaWindow })
        {
            source.Should().Contain("new FreePOptionsRuntimeSession(_options)");
            source.Should().Contain("_optionsRuntime.ApplyAndPersist(");
            source.Should().NotContain("_options.RecentFilesCap = edited.RecentFilesCap");
            source.Should().NotContain("_options.DefaultSaveFormat = edited.DefaultSaveFormat");
            source.Should().NotContain("_options.UiLanguage = edited.UiLanguage");
        }

        wpfProgram.Should().Contain("SelectUiLanguage: FreePOptionsPolicy.SelectUiLanguage");
        avaloniaApp.Should().Contain("var optionsStore = ApplicationOptionsStore<FreePOptions>.Create();");
        avaloniaApp.Should().Contain("optionsStore: optionsStore");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
