using System.Globalization;
using FreePOptions = FreeP.App.Compositor.FreePOptions;
using FreeWOptions = FreeW.App.Presentation.Options.FreeWOptions;

namespace Free.Shared.AppServices.Tests;

public sealed class BasicApplicationOptionsDialogSessionTests
{
    private static string FreeWValidation =>
        $"Enter a whole number between {ApplicationOptionsNormalizer.MinRecentFilesCap} and {ApplicationOptionsNormalizer.MaxRecentFilesCap} for the recent-files count.";
    private static string FreePValidation =>
        $"Enter a whole number between {ApplicationOptionsNormalizer.MinRecentFilesCap} and {ApplicationOptionsNormalizer.MaxRecentFilesCap}.";

    [Fact]
    public void RealSisterModels_ShareNormalizedInitialStateWithProductFormats()
    {
        var culture = CultureInfo.GetCultureInfo("en-GB");
        var freeW = new BasicApplicationOptionsDialogSession<FreeWOptions>(
            new FreeWOptions { RecentFilesCap = 999, UiLanguage = " uk-ua " },
            culture,
            FreeWOptions.DocxDefaultFormat,
            FreeWValidation);
        var freeP = new BasicApplicationOptionsDialogSession<FreePOptions>(
            new FreePOptions { RecentFilesCap = 999, UiLanguage = " uk-ua " },
            culture,
            FreePOptions.FxpDefaultFormat,
            FreePValidation);

        freeW.InitialState.Should().Be(new BasicApplicationOptionsDialogInitialState(
            ApplicationOptionsNormalizer.MaxRecentFilesCap.ToString(culture),
            FreeWOptions.DocxDefaultFormat,
            "uk-UA"));
        freeP.InitialState.Should().Be(new BasicApplicationOptionsDialogInitialState(
            ApplicationOptionsNormalizer.MaxRecentFilesCap.ToString(culture),
            FreePOptions.FxpDefaultFormat,
            "uk-UA"));
        freeW.SystemLanguageLabel.Should().Be("en-GB");
        freeP.SystemLanguageLabel.Should().Be("en-GB");
    }

    [Fact]
    public void RealSisterModels_ShareValidationAndCommitLifecycleWithProductMessages()
    {
        var freeW = CreateFreeW();
        var freeP = CreateFreeP();

        var invalidW = freeW.PlanAcceptance(new("bad", null, null));
        var invalidP = freeP.PlanAcceptance(new("bad", null, null));
        invalidW.ShouldApply.Should().BeFalse();
        invalidW.ShouldPersist.Should().BeFalse();
        invalidW.Result.Should().BeNull();
        invalidW.Validation.Should().Be(new BasicApplicationOptionsDialogValidation(
            BasicApplicationOptionsValidationTarget.RecentFilesCap,
            FreeWValidation));
        invalidP.Validation.Should().Be(new BasicApplicationOptionsDialogValidation(
            BasicApplicationOptionsValidationTarget.RecentFilesCap,
            FreePValidation));

        var acceptedW = freeW.PlanAcceptance(new("6", " ", " de-de "));
        var acceptedP = freeP.PlanAcceptance(new("6", " ", " de-de "));
        acceptedW.ShouldApply.Should().BeTrue();
        acceptedW.ShouldPersist.Should().BeTrue();
        acceptedW.Result.Should().BeEquivalentTo(new
        {
            RecentFilesCap = 6,
            DefaultSaveFormat = FreeWOptions.DocxDefaultFormat,
            UiLanguage = "de-DE",
        });
        acceptedP.Result.Should().BeEquivalentTo(new
        {
            RecentFilesCap = 6,
            DefaultSaveFormat = FreePOptions.FxpDefaultFormat,
            UiLanguage = "de-DE",
        });
    }

    [Fact]
    public void ProductSessions_DelegateBasicOwnershipAndKeepProductExtensionsOutsideShared()
    {
        var shared = Read("shared", "Free.Shared.AppServices", "BasicApplicationOptionsDialogSession.cs");
        var freeWSession = Read("freew", "FreeW.App.Presentation", "Options", "OptionsDialogSession.cs");
        var freeWWorkflow = Read("freew", "FreeW.App.Presentation", "Options", "OptionsDialogWorkflowPlanner.cs");
        var freeWPlanner = Read("freew", "FreeW.App.Presentation", "Options", "OptionsDialogPlanner.cs");
        var freePSession = Read("freep", "FreeP.App.Presentation", "Options", "OptionsDialogSession.cs");
        var freePPlanner = Read("freep", "FreeP.App.Presentation", "Options", "OptionsDialogPlanner.cs");
        var freePResources = Read("freep", "FreeP.App.Localization", "Resources", "Strings.resx");

        foreach (var session in new[] { freeWSession, freePSession })
        {
            session.Should().Contain("BasicApplicationOptionsDialogSession<");
            session.Should().Contain("_basicSession.PlanAcceptance(");
            session.Should().NotContain("ApplicationOptionsNormalizer.TryParseRecentFilesCap");
            session.Should().NotContain("private static string SystemLanguageLabel");
        }

        shared.Should().NotContain("Word Document (*.docx)");
        shared.Should().NotContain("Presentation (*.fxp)");
        shared.Should().NotContain("AutoCorrect");
        freeWPlanner.Should().Contain("Word Document (*.docx)");
        freePPlanner.Should().Contain("Loc.Get(\"Options_PresentationFormat\")");
        freePResources.Should().Contain("<value>Presentation (*.fxp)</value>");
        freeWWorkflow.Should().Contain("result.AutoCorrectEnabled");
        freeWWorkflow.Should().Contain("result.AutoCorrect = autoCorrect");
        freeWWorkflow.Should().NotContain("TryParseRecentFilesCap");
    }

    private static BasicApplicationOptionsDialogSession<FreeWOptions> CreateFreeW() =>
        new(new FreeWOptions(), CultureInfo.InvariantCulture, FreeWOptions.DocxDefaultFormat, FreeWValidation);

    private static BasicApplicationOptionsDialogSession<FreePOptions> CreateFreeP() =>
        new(new FreePOptions(), CultureInfo.InvariantCulture, FreePOptions.FxpDefaultFormat, FreePValidation);

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts));
}
