using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationBackstagePanePlannerTests
{
    [Fact]
    public void FreePBackstagePaneTextCatalog_OwnsProductKeysAndFallbacks()
    {
        var descriptor = FreePBackstagePaneTextCatalog.Descriptor;

        descriptor.RecentEmptyText.ResourceKey.Should().Be(FreePBackstagePaneResourceKeys.RecentEmptyText);
        descriptor.RecentEmptyText.FallbackText.Should().Be("No recent presentations.");
        descriptor.TemplateTileCaption.FallbackText.Should().Be("Blank presentation");
        descriptor.OptionsEditText.Should().Be(new ResourceTextDescriptor(
            FreePBackstagePaneResourceKeys.OptionsEditText,
            "Edit options…"));
        descriptor.Export.PdfActionLabel.FallbackText.Should().Be("Export to PDF...");
        descriptor.Export.XpsActionLabel.Should().BeNull();
        descriptor.Info.Should().NotBeNull();
        descriptor.Info!.LocationLabel.Should().BeSameAs(CommonShellTextResources.Location);
        descriptor.OptionsSummary.Should().NotBeNull();
        descriptor.OptionsSummary!.UiLanguageLabel.Should().BeSameAs(CommonShellTextResources.UiLanguage);
        FreePBackstagePaneTextCatalog.RequiredResourceKeys
            .Should().OnlyHaveUniqueItems()
            .And.Contain([
                FreePBackstagePaneResourceKeys.OptionsEditText,
                FreePBackstagePaneResourceKeys.InfoHeading,
                CommonShellResourceKeys.SystemDefault,
            ]);
    }

    [Fact]
    public void FreePBackstagePaneTextCatalog_ResolvesLocalizedOptionsEditText()
    {
        var text = FreePBackstagePaneTextCatalog.BuildTextSpec(key =>
            key == FreePBackstagePaneResourceKeys.OptionsEditText
                ? "Modifier les options…"
                : null);

        text.OptionsEditText.Should().Be("Modifier les options…");
    }

    [Fact]
    public void FreePBackstagePaneTextCatalog_ResolvesInfoAndOptionsSummaryText()
    {
        var text = FreePBackstagePaneTextCatalog.BuildTextSpec(key => key switch
        {
            CommonShellResourceKeys.Location => "Emplacement",
            CommonShellResourceKeys.Title => "Titre",
            CommonShellResourceKeys.RecentFilesKept => "Fichiers recents conserves",
            CommonShellResourceKeys.SystemDefault => "Valeur systeme",
            _ => null,
        });

        text.Info.LocationLabel.Should().Be("Emplacement");
        text.Info.CoreProperties.TitleLabel.Should().Be("Titre");
        text.OptionsSummary.RecentFilesKeptLabel.Should().Be("Fichiers recents conserves");
        text.OptionsSummary.SystemDefaultLanguageLabel.Should().Be("Valeur systeme");
    }

    [Fact]
    public void BuildStandardPanes_OwnsMetadataRecentOptionsAndAccountProjection()
    {
        var planner = new PresentationBackstagePanePlanner();
        var presentation = Presentation.CreateEmpty();
        presentation.Properties.Title = "Roadmap";
        presentation.Properties.Author = "Ada";
        var opened = "";

        var info = planner.BuildInfoPane(
            presentation,
            "Roadmap.fxp",
            isDirty: true,
            currentPath: @"C:\Decks\Roadmap.fxp");
        var recent = planner.BuildRecentPane(
            [new RecentFileEntry { Path = @"C:\Decks\Roadmap.fxp" }],
            path => opened = path);
        var options = planner.BuildOptionsPane(
            new FreePOptions
            {
                RecentFilesCap = 9,
                DefaultSaveFormat = ".fxp",
                UiLanguage = "en-US",
            },
            @"C:\Data\FreeP");
        var account = planner.BuildAccountPane(
            "FreeP",
            "2.1.0",
            @"C:\Data\FreeP",
            openOptions: null,
            getUserName: () => "Ada",
            getMachineName: () => "DECK-PC");

        info.DocumentKindLabel.Should().Be("Presentation");
        info.DisplayName.Should().Be("Roadmap.fxp");
        info.IsDirty.Should().BeTrue();
        info.Text.Should().BeSameAs(info.EffectiveText);
        info.Properties.Should().Contain(new BackstageFieldRow("Title", "Roadmap"));
        info.Properties.Should().Contain(new BackstageFieldRow("Author", "Ada"));
        info.Statistics.Should().ContainSingle()
            .Which.Should().Be(new BackstageFieldRow("Slides", presentation.Slides.Count.ToString()));

        recent.EmptyText.Should().Be("No recent presentations.");
        recent.Paths.Should().Equal(@"C:\Decks\Roadmap.fxp");
        recent.OpenPath(recent.Paths[0]);
        opened.Should().Be(@"C:\Decks\Roadmap.fxp");

        options.Description.Should().Contain("FreeP application settings");
        options.Fields.Should().Contain(new BackstageFieldRow("Recent files kept", "9"));
        options.Fields.Should().Contain(new BackstageFieldRow("Data folder", @"C:\Data\FreeP"));

        account.Groups[0].Fields.Should().Contain(new BackstageFieldRow("Device", "DECK-PC"));
        account.Groups[1].Fields.Should().Contain(new BackstageFieldRow("Windows user", "Ada"));
        account.OptionsText.Should().Be("FreeP Options...");
    }

    [Fact]
    public void BuildStandardPanes_UsesResolvedInfoAndOptionsSummarySpecs()
    {
        var planner = new PresentationBackstagePanePlanner(key => key switch
        {
            CommonShellResourceKeys.Location => "Emplacement",
            CommonShellResourceKeys.Title => "Titre",
            CommonShellResourceKeys.DataFolder => "Dossier de donnees",
            CommonShellResourceKeys.SystemDefault => "Valeur systeme",
            _ => null,
        });
        var presentation = Presentation.CreateEmpty();
        presentation.Properties.Title = "Roadmap";

        var info = planner.BuildInfoPane(presentation, "Roadmap.fxp", false, null);
        var options = planner.BuildOptionsPane(new FreePOptions(), "data");

        info.EffectiveText.LocationLabel.Should().Be("Emplacement");
        info.Properties.Should().Contain(new BackstageFieldRow("Titre", "Roadmap"));
        options.Fields.Should().Contain(new BackstageFieldRow("Dossier de donnees", "data"));
        options.Fields.Should().Contain(new BackstageFieldRow("UI language", "Valeur systeme"));
    }

    [Fact]
    public void BuildExportPane_OwnsGroupingAutomationAndCommandRouting()
    {
        var invoked = new List<string>();
        var planner = new PresentationBackstagePanePlanner();

        var surface = planner.BuildExportPane(
            videoExportAvailable: true,
            new PresentationBackstageExportActions(
                () => invoked.Add("pdf"),
                () => invoked.Add("notes"),
                () => invoked.Add("images"),
                () => invoked.Add("video")));

        surface.Heading.Should().Be("Export");
        surface.Groups.Select(group => group.Heading).Should().Equal("Create PDF Copy", "Other File Types");
        surface.Groups.SelectMany(group => group.Actions).Select(action => action.Label).Should().Equal(
            "Export to PDF...",
            "Notes Page PDF...",
            "Images",
            "Video");
        surface.Groups.SelectMany(group => group.Actions)
            .Should().OnlyContain(action => action.IsEnabled && action.AutomationId!.StartsWith("BackstageExport_"));

        foreach (var action in surface.Groups.SelectMany(group => group.Actions))
            action.Invoke();

        invoked.Should().Equal("pdf", "notes", "images", "video");
    }

    [Fact]
    public void BuildExportPane_CanPreservePresentationPlannerTextForAvalonia()
    {
        var planner = new PresentationBackstagePanePlanner(
            usePresentationExportPlannerText: true);
        var expected = PresentationExportPlanner.BuildBackstageExportPlan();

        var surface = planner.BuildExportPane(
            videoExportAvailable: false,
            new PresentationBackstageExportActions(
                static () => { },
                static () => { },
                static () => { },
                static () => { }));

        surface.Heading.Should().Be(expected.Heading);
        surface.Description.Should().Be(expected.Description);
        surface.Groups[0].Heading.Should().Be(expected.FixedLayoutGroupHeading);
        surface.Groups[0].Actions[0].Label.Should().Be(expected.FixedLayoutActions[0].Label);
        surface.Groups[0].Actions[0].Description.Should().Be(expected.FixedLayoutActions[0].Description);
    }

    [Fact]
    public void BuildOpenPane_ExposesTheManualRecoverUnsavedPresentationsCommand()
    {
        var invoked = false;
        var planner = new PresentationBackstagePanePlanner();

        var surface = planner.BuildOpenPane(() => invoked = true);

        surface.Groups.Should().ContainSingle();
        var recovery = surface.Groups.Single().Actions.Should().ContainSingle().Subject;
        recovery.Label.Should().Be("Recover Unsaved Presentations");

        recovery.Invoke();

        invoked.Should().BeTrue();
    }

    [Fact]
    public void BuildOpenPane_ResolvesTheLocalizedRecoveryLabel()
    {
        var planner = new PresentationBackstagePanePlanner(key =>
            key == "Autosave_Recovery_Backstage_Label" ? "Recuperer les presentations non enregistrees" : null);

        var surface = planner.BuildOpenPane(static () => { });

        surface.Groups.Single().Actions.Single().Label
            .Should().Be("Recuperer les presentations non enregistrees");
    }

    [Fact]
    public void BuildOpenPane_ThrowsWhenRecoverUnsavedIsNull()
    {
        var planner = new PresentationBackstagePanePlanner();

        var act = () => planner.BuildOpenPane(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Sibling of <see cref="BuildOpenPane_ExposesTheManualRecoverUnsavedPresentationsCommand"/>:
    /// pins the pane's heading and the group/automation identity the WPF and Avalonia renderers key
    /// off of, mirroring FreeW's "Recovery" group in its own Open pane.
    /// </summary>
    [Fact]
    public void BuildOpenPane_UsesTheRecoveryGroupHeadingAndStableAutomationId()
    {
        var planner = new PresentationBackstagePanePlanner();

        var surface = planner.BuildOpenPane(static () => { });

        surface.Heading.Should().Be("Open");
        surface.Groups.Single().Heading.Should().Be("Recovery");
        surface.Groups.Single().Actions.Single().AutomationId
            .Should().Be("BackstageOpen_RecoverUnsavedPresentations");
    }

    [Fact]
    public void BuildAccountPane_UsesSharedSafeEnvironmentPolicy()
    {
        var planner = new PresentationBackstagePanePlanner();

        var account = planner.BuildAccountPane(
            "FreeP",
            "1.0",
            "data",
            openOptions: null,
            getUserName: () => throw new InvalidOperationException(),
            getMachineName: () => throw new PlatformNotSupportedException());

        account.Groups.SelectMany(group => group.Fields)
            .Where(field => field.Label is "Windows user" or "Device")
            .Select(field => field.Value)
            .Should().AllBeEquivalentTo("Not available");
    }
}
