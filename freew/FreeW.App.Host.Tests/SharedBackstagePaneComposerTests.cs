using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;
using FreeW.App.Host.Backstage;
using FreeW.App.Presentation.Backstage;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class SharedBackstagePaneComposerTests
{
    private static readonly BackstageVisualKit Kit =
        new(Color.FromRgb(0x0F, 0x6D, 0x8C), tileWidth: 150, tileHeight: 190);

    private readonly BackstagePaneComposer _composer = new(Kit);

    [StaFact]
    public void BuildRecentPane_EmptyList_RendersConfiguredEmptyText()
    {
        var pane = _composer.BuildRecentPane(new BackstageRecentPaneSpec(
            Array.Empty<string>(),
            "No recent documents.",
            _ => throw new InvalidOperationException("empty recent list should not open a path")));

        var panel = Assert.IsType<StackPanel>(pane);

        Texts(panel).Should().Contain(["Recent", "No recent documents."]);
    }

    [StaFact]
    public void BuildRecentPane_RendersFileRowsAndInvokesOpenPath()
    {
        var path = Path.Combine("C:", "Docs", "Quarterly Review.docx");
        string? opened = null;

        var pane = _composer.BuildRecentPane(new BackstageRecentPaneSpec(
            [path],
            "No recent documents.",
            openedPath => opened = openedPath));

        var scroller = Assert.IsType<ScrollViewer>(pane);
        var panel = Assert.IsType<StackPanel>(scroller.Content);
        var item = Assert.IsType<StackPanel>(panel.Children[1]);
        var title = Assert.IsType<TextBlock>(item.Children[0]);
        var subtitle = Assert.IsType<TextBlock>(item.Children[1]);

        title.Text.Should().Be("Quarterly Review.docx");
        subtitle.Text.Should().Be(path);
        subtitle.TextTrimming.Should().Be(TextTrimming.CharacterEllipsis);

        item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent
        });

        opened.Should().Be(path);
    }

    [StaFact]
    public void BuildTemplatePane_RendersCaptionAndInvokesCreate()
    {
        var created = false;

        var pane = _composer.BuildTemplatePane(new BackstageTemplatePaneSpec(
            "New",
            "Blank document",
            "More templates are not available in this build.",
            () => created = true));

        Texts(pane).Should().Contain(["New", "Blank document", "More templates are not available in this build."]);

        var gallery = Descendants<WrapPanel>(pane).Single();
        var tile = Assert.IsType<StackPanel>(gallery.Children[0]);
        tile.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent
        });

        created.Should().BeTrue();
    }

    [StaFact]
    public void BuildOptionsPane_RendersFieldsAndOptionalEditButton()
    {
        var edited = false;

        var pane = _composer.BuildOptionsPane(new BackstageOptionsPaneSpec(
            "FreeW application settings.",
            [
                new("Recent files kept", "10"),
                new("Default save format", "docx"),
            ],
            EditText: "Edit options...",
            Edit: () => edited = true));

        Texts(pane).Should().Contain(["Options", "FreeW application settings.", "Recent files kept", "10"]);

        var edit = Descendants<Button>(pane).Single();
        edit.Content.Should().Be("Edit options...");
        edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        edited.Should().BeTrue();
    }

    [StaFact]
    public void BuildActionPane_RendersGroupedRowsAndInvokesActions()
    {
        var invoked = new List<string>();

        var pane = _composer.BuildActionPane(new BackstageActionPaneSpec(
            Heading: "Save As",
            Description: "Choose where to save this document.",
            Groups:
            [
                new("Places",
                [
                    new("This PC", "Save to local folders.", () => invoked.Add("pc")),
                    new("Browse", "Open the Windows save dialog.", () => invoked.Add("browse")),
                ]),
                new("File Types",
                [
                    new("Word Document (*.docx)", "Save an editable Word document.", () => invoked.Add("docx")),
                ]),
            ]));

        var scroller = Assert.IsType<ScrollViewer>(pane);
        var panel = Assert.IsType<StackPanel>(scroller.Content);

        Texts(panel).Should().Contain([
            "Save As",
            "Choose where to save this document.",
            "Places",
            "This PC",
            "Save to local folders.",
            "Browse",
            "File Types",
            "Word Document (*.docx)",
        ]);

        var buttons = Descendants<Button>(panel).ToList();
        buttons.Should().HaveCount(3);
        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        invoked.Should().Equal("browse");
    }

    [StaFact]
    public void BuildExportActionPane_RendersDirectLabelsAndSiblingDescriptions()
    {
        var invoked = false;

        var pane = _composer.BuildExportActionPane(new BackstageActionPaneSpec(
            Heading: "Export",
            Description: "Create a fixed-layout copy.",
            Groups:
            [
                new("Create PDF/XPS Document",
                [
                    new("Create PDF or XPS", "Export-only PDF copy.", () => invoked = true),
                ]),
            ]));

        var scroller = Assert.IsType<ScrollViewer>(pane);
        var panel = Assert.IsType<StackPanel>(scroller.Content);
        var button = Descendants<Button>(panel).Single();

        button.Content.Should().Be("Create PDF or XPS");
        button.FontSize.Should().Be(14);
        var row = Assert.IsType<StackPanel>(button.Parent);
        row.Children.OfType<TextBlock>().Single().Text.Should().Be("Export-only PDF copy.");

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        invoked.Should().BeTrue();
    }

    [StaFact]
    public void FreeWRenderer_UsesPlannerMetricsAndPreservesActionOrder()
    {
        var invoked = new List<string>();
        var surface = BackstagePaneSurfacePlanner.BuildExportPane(
            [],
            exportPdf: () => invoked.Add("pdf"),
            exportXps: () => invoked.Add("xps"),
            saveAsFormat: (_, _) => invoked.Add("format"));

        var pane = new BackstagePaneComposer(Kit, BackstagePaneSurfacePlanner.ComposerProfile)
            .BuildExportActionPane(surface.ToPaneSpec());
        var buttons = Descendants<Button>(pane).ToArray();

        buttons.Select(button => button.Content).Should().Equal(
            "Create PDF or XPS",
            "Export to XPS");
        buttons[0].FontSize.Should().Be(BackstagePaneSurfacePlanner.ComposerProfile.Metrics.ActionFontSize);
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        invoked.Should().Equal("pdf", "xps");

        var row = Assert.IsType<StackPanel>(buttons[0].Parent);
        row.Children.OfType<TextBlock>().Single().Text.Should().Contain("Export-only fixed-layout PDF copy");
    }

    [StaFact]
    public void FreeWAccountRenderer_UsesPlannerMetricsAndRoutesOptions()
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

        var pane = new BackstagePaneComposer(Kit, BackstagePaneSurfacePlanner.ComposerProfile)
            .BuildAccountPane(surface.ToPaneSpec());
        var heading = Descendants<TextBlock>(pane).Single(block => block.Text == "Account");
        heading.FontSize.Should().Be(surface.VisualMetrics.HeadingFontSize);

        var options = Descendants<Button>(pane)
            .Single(button => button.Content as string == "FreeW Options...");
        options.FontSize.Should().Be(surface.VisualMetrics.OptionsFontSize);
        options.Margin.Should().Be(new Thickness(0, 18, 0, 0));
        AutomationProperties.GetAutomationId(options).Should().Be(surface.OptionsAction.AutomationId);
        options.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        openedOptions.Should().BeTrue();
    }

    [Fact]
    public void BackstageApplicationOptionsPanePlanner_AdaptsSharedSummaryRows()
    {
        var edited = false;

        var spec = BackstageApplicationOptionsPanePlanner.Build(
            "FreeW application settings.",
            new SummaryOptions(RecentFilesCap: 6, DefaultSaveFormat: ".docx", UiLanguage: ""),
            @"C:\Users\Ada\AppData\Local\FreeW",
            editText: "Edit options...",
            edit: () => edited = true);

        spec.Description.Should().Be("FreeW application settings.");
        spec.Fields.Should().Equal(
            new BackstageFieldRow(ApplicationOptionsSummaryPlanner.RecentFilesKeptLabel, "6"),
            new BackstageFieldRow(ApplicationOptionsSummaryPlanner.DefaultSaveFormatLabel, ".docx"),
            new BackstageFieldRow(ApplicationOptionsSummaryPlanner.UiLanguageLabel, ApplicationOptionsSummaryPlanner.SystemDefaultLanguageLabel),
            new BackstageFieldRow(ApplicationOptionsSummaryPlanner.DataFolderLabel, @"C:\Users\Ada\AppData\Local\FreeW"));
        spec.EditText.Should().Be("Edit options...");

        spec.Edit.Should().NotBeNull();
        spec.Edit!.Invoke();
        edited.Should().BeTrue();
    }

    [Fact]
    public void SisterBackstagePaneSpecPlanner_BuildsFreeWPaneSpecsFromPreset()
    {
        var edited = false;
        var planner = new SisterBackstagePaneSpecPlanner(FreeWBackstagePaneTextCatalog.BuildTextSpec());

        var recent = planner.BuildRecentPaneSpec(["C:/Docs/Budget.docx"], _ => { });
        var template = planner.BuildNewPaneSpec(() => { });
        var options = planner.BuildOptionsPaneSpec(
            new SummaryOptions(RecentFilesCap: 9, DefaultSaveFormat: ".docx", UiLanguage: ""),
            @"C:\Users\Ada\AppData\Local\FreeW",
            edit: () => edited = true);
        var account = planner.BuildAccountPaneSpec(
            new SisterBackstageAccountPaneContext(
                "FreeW",
                "1.2.3",
                "Ada",
                "WORD-BOX",
                @"C:\Users\Ada\AppData\Local\FreeW"),
            openOptions: () => edited = true);
        var export = planner.BuildExportPaneSpec(
            exportPdf: () => { },
            exportXps: () => { },
            additionalGroups:
            [
                new("Change File Type", []),
            ]);

        recent.EmptyText.Should().Be("No recent documents.");
        recent.Paths.Should().Equal("C:/Docs/Budget.docx");
        template.Heading.Should().Be("New");
        template.TileCaption.Should().Be("Blank document");
        template.FooterText.Should().Be("More templates are not available in this build.");
        options.Description.Should().Be("FreeW application settings. These persist between sessions and apply immediately.");
        options.EditText.Should().Be("Edit options\u2026");
        account.Heading.Should().Be("Account");
        account.OptionsText.Should().Be("FreeW Options...");
        account.Groups.SelectMany(group => group.Fields)
            .Should().Contain(new BackstageFieldRow("Connected services", "Local desktop app"));
        export.Heading.Should().Be("Export");
        export.Description.Should().Be("Create a fixed-layout copy or choose an editable document format.");
        export.Groups.Should().HaveCount(2);
        export.Groups[0].Heading.Should().Be("Create PDF/XPS Document");
        export.Groups[0].Actions.Select(action => action.Label)
            .Should().Equal("Create PDF or XPS", "Export to XPS");
        export.Groups[1].Heading.Should().Be("Change File Type");

        options.Edit.Should().NotBeNull();
        options.Edit!.Invoke();
        edited.Should().BeTrue();
    }

    [Fact]
    public void FreeWBackstagePaneTextCatalog_ExposesResourceKeysAndFallbackText()
    {
        var descriptor = FreeWBackstagePaneTextCatalog.Descriptor;

        descriptor.RecentEmptyText.Should().Be(new ResourceTextDescriptor(
            FreeWBackstagePaneResourceKeys.RecentEmptyText,
            "No recent documents."));
        descriptor.Export.XpsActionLabel.Should().Be(new ResourceTextDescriptor(
            FreeWBackstagePaneResourceKeys.ExportXpsActionLabel,
            "Export to XPS"));
        descriptor.Info!.LocationLabel.Should().BeSameAs(CommonShellTextResources.Location);
        descriptor.Info.CoreProperties.TitleLabel.Should().BeSameAs(CommonShellTextResources.Title);
        descriptor.OptionsSummary!.UiLanguageLabel.Should().BeSameAs(CommonShellTextResources.UiLanguage);
        FreeWBackstagePaneTextCatalog.RequiredResourceKeys
            .Should().OnlyHaveUniqueItems()
            .And.Contain(FreeWBackstagePaneResourceKeys.OptionsEditText);
    }

    [Fact]
    public void SisterBackstagePaneTextSpec_ResolvesDescriptorKeysWithFallbacks()
    {
        static string? Resolve(string key) =>
            key == FreeWBackstagePaneResourceKeys.TemplateTileCaption
                ? "Localized blank document"
                : key == FreeWBackstagePaneResourceKeys.ExportPdfActionLabel
                    ? "[[" + key + "]]"
                : null;

        var text = SisterBackstagePaneTextSpec.FromDescriptor(
            FreeWBackstagePaneTextCatalog.Descriptor,
            Resolve);

        text.RecentEmptyText.Should().Be("No recent documents.");
        text.TemplateTileCaption.Should().Be("Localized blank document");
        text.Export.PdfActionLabel.Should().Be("Create PDF or XPS");
    }

    [Fact]
    public void FreeWBackstagePaneTextSpec_TreatsEchoedResourceKeysAsMissing()
    {
        var text = SisterBackstagePaneTextSpec.FromDescriptor(
            FreeWBackstagePaneTextCatalog.Descriptor,
            key => key);

        text.RecentEmptyText.Should().Be("No recent documents.");
        text.Export.PdfActionLabel.Should().Be("Create PDF or XPS");
    }

    [Fact]
    public void SisterBackstagePaneSpecPlanner_BuildsSpecsFromInjectedAlternateText()
    {
        var planner = new SisterBackstagePaneSpecPlanner(new SisterBackstagePaneTextSpec(
            "No recent presentations.",
            "New",
            "Blank presentation",
            "More templates are not available in this build.",
            "Presentation application settings. These persist between sessions.")
        {
            Export = new SisterBackstageExportPaneTextSpec(
                "Export",
                "Create a PDF copy of this presentation - one page per slide, with selectable text.",
                "Create PDF Copy",
                "Export to PDF...",
                "Publish a fixed-layout copy.")
        });

        var recent = planner.BuildRecentPaneSpec(Array.Empty<string>(), _ => { });
        var template = planner.BuildNewPaneSpec(() => { });
        var options = planner.BuildOptionsPaneSpec(
            new SummaryOptions(RecentFilesCap: 5, DefaultSaveFormat: ".freep", UiLanguage: "en-US"),
            @"C:\Users\Ada\AppData\Local\FreeP");
        var export = planner.BuildExportPaneSpec(exportPdf: () => { });

        recent.EmptyText.Should().Be("No recent presentations.");
        template.TileCaption.Should().Be("Blank presentation");
        options.Description.Should().Be("Presentation application settings. These persist between sessions.");
        options.EditText.Should().BeNull();
        options.Edit.Should().BeNull();
        export.Heading.Should().Be("Export");
        export.Description.Should().Be("Create a PDF copy of this presentation - one page per slide, with selectable text.");
        export.Groups.Should().ContainSingle();
        export.Groups[0].Heading.Should().Be("Create PDF Copy");
        export.Groups[0].Actions.Should().ContainSingle();
        export.Groups[0].Actions[0].Label.Should().Be("Export to PDF...");
    }

    [Fact]
    public void SisterBackstageAccountPanePlanner_UsesSuppliedTextSpecForLabelsAndFallbacks()
    {
        var text = SisterBackstageAccountPaneTextSpec.NeutralEnglish with
        {
            Heading = "Account Test",
            DescriptionFormat = "Inspect {0}.",
            ProductInformationHeading = "Product Block",
            ProductLabel = "Product Name",
            VersionLabel = "Build",
            UserInformationHeading = "User Block",
            ConnectedServicesValue = "Offline mode",
            OptionsTextFormat = "Configure {0}",
            MissingValueText = "(missing)"
        };

        var plan = SisterBackstageAccountPanePlanner.Build(
            new SisterBackstageAccountPaneContext(
                " FreeP ",
                "",
                "Ada",
                "PRES-BOX",
                " "),
            text);

        plan.Heading.Should().Be("Account Test");
        plan.Description.Should().Be("Inspect FreeP.");
        plan.OptionsText.Should().Be("Configure FreeP");
        plan.Groups[0].Heading.Should().Be("Product Block");
        plan.Groups[0].Fields.Should().Contain([
            new BackstageFieldRow("Product Name", "FreeP"),
            new BackstageFieldRow("Build", "(missing)"),
        ]);
        plan.Groups[1].Heading.Should().Be("User Block");
        plan.Groups[1].Fields.Should().Contain([
            new BackstageFieldRow("Connected services", "Offline mode"),
            new BackstageFieldRow("Data folder", "(missing)"),
        ]);
    }

    [Fact]
    public void SisterBackstagePaneResources_ComposesKitComposerAndSpecPlanner()
    {
        var resources = new SisterBackstagePaneResources(
            Color.FromRgb(0x0F, 0x6D, 0x8C),
            tileWidth: 150,
            tileHeight: 190,
            text: FreeWBackstagePaneTextCatalog.BuildTextSpec(),
            profile: BackstagePaneSurfacePlanner.ComposerProfile);

        resources.Kit.Should().NotBeNull();
        resources.Panes.Should().NotBeNull();

        var template = resources.PaneSpecs.BuildNewPaneSpec(() => { });
        template.TileCaption.Should().Be("Blank document");
    }

    [Fact]
    public void SisterBackstageInfoPanePlanner_BuildsCommonInfoSpec()
    {
        var spec = SisterBackstageInfoPanePlanner.Build(new SisterBackstageInfoPaneContext(
            DocumentKindLabel: "Presentation",
            DisplayName: "Quarterly Review",
            IsDirty: true,
            Location: @"C:\Decks\Review.pptx",
            CoreProperties: new BackstageCoreProperties(
                Title: "Review",
                Author: "Ada",
                Subject: "",
                Keywords: null),
            Statistics:
            [
                new("Slides", "12"),
            ],
            EditPropertiesText: "Edit properties...",
            EditProperties: () => { }));

        spec.DocumentKindLabel.Should().Be("Presentation");
        spec.DisplayName.Should().Be("Quarterly Review");
        spec.IsDirty.Should().BeTrue();
        spec.Properties.Should().Equal(
            new BackstageFieldRow(BackstageCorePropertiesPlanner.TitleLabel, "Review"),
            new BackstageFieldRow(BackstageCorePropertiesPlanner.AuthorLabel, "Ada"),
            new BackstageFieldRow(BackstageCorePropertiesPlanner.SubjectLabel, BackstageVisualKit.Or(null)),
            new BackstageFieldRow(BackstageCorePropertiesPlanner.KeywordsLabel, BackstageVisualKit.Or(null)));
        spec.Statistics.Should().Equal(new BackstageFieldRow("Slides", "12"));
        spec.EditPropertiesText.Should().Be("Edit properties...");
        spec.EditProperties.Should().NotBeNull();
    }

    [Fact]
    public void BackstageCorePropertiesPlanner_BuildsCommonPropertyRows()
    {
        var rows = BackstageCorePropertiesPlanner.Build(new BackstageCoreProperties(
            Title: "Budget",
            Author: "",
            Subject: "Planning",
            Keywords: null));

        rows.Should().Equal(
            new BackstageFieldRow(BackstageCorePropertiesPlanner.TitleLabel, "Budget"),
            new BackstageFieldRow(BackstageCorePropertiesPlanner.AuthorLabel, "—"),
            new BackstageFieldRow(BackstageCorePropertiesPlanner.SubjectLabel, "Planning"),
            new BackstageFieldRow(BackstageCorePropertiesPlanner.KeywordsLabel, "—"));
    }

    [StaFact]
    public void BuildInfoPane_RendersDirtyLocationPropertiesStatsAndOptionalEditButton()
    {
        var edited = false;

        var pane = _composer.BuildInfoPane(new BackstageInfoPaneSpec(
            DocumentKindLabel: "Document",
            DisplayName: "Report",
            IsDirty: true,
            Location: null,
            Properties:
            [
                new("Title", "Budget"),
                new("Author", BackstageVisualKit.Or(null)),
            ],
            Statistics:
            [
                new("Words", "123"),
            ],
            EditPropertiesText: "Edit document properties...",
            EditProperties: () => edited = true));

        Texts(pane).Should().Contain([
            BackstageInfoPaneText.Title,
            "Document",
            "Report  (unsaved changes)",
            BackstageInfoPaneText.LocationLabel,
            BackstageInfoPaneText.NotSavedYet,
            BackstageInfoPaneText.PropertiesHeading,
            "Title",
            "Budget",
            BackstageInfoPaneText.StatisticsHeading,
            "Words",
            "123",
        ]);

        var edit = Descendants<Button>(pane).Single();
        edit.Content.Should().Be("Edit document properties...");
        edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        edited.Should().BeTrue();
    }

    [StaFact]
    public void BuildInfoPane_RendersOptionalActionGroups()
    {
        var invoked = false;

        var pane = _composer.BuildInfoPane(new BackstageInfoPaneSpec(
            DocumentKindLabel: "Document",
            DisplayName: "Report",
            IsDirty: false,
            Location: @"C:\Docs\Report.docx",
            Properties: [],
            Statistics: [],
            ActionGroups:
            [
                new("Protect Document",
                [
                    new("Mark as Final", "Make the document read-only.", () => invoked = true),
                ]),
            ]));

        Texts(pane).Should().Contain([
            "Info",
            "Protect Document",
            "Mark as Final",
            "Make the document read-only.",
        ]);

        var action = Descendants<Button>(pane).Single();
        action.Content.Should().NotBeNull();
        AutomationProperties.GetName(action).Should().Be("Mark as Final");
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        invoked.Should().BeTrue();
    }

    [StaFact]
    public void BuildAccountPane_RendersSharedAccountPlanAndRoutesOptions()
    {
        var openedOptions = false;
        var plan = SisterBackstageAccountPanePlanner.Build(
            new SisterBackstageAccountPaneContext(
                "FreeW",
                "1.2.3",
                "Ada",
                "WORD-BOX",
                @"C:\Users\Ada\AppData\Local\FreeW"));

        var pane = _composer.BuildAccountPane(new BackstageAccountPaneSpec(
            plan.Heading,
            plan.Description,
            plan.Groups,
            plan.OptionsText,
            () => openedOptions = true));

        Texts(pane).Should().Contain([
            "Account",
            "Product Information",
            "Product",
            "FreeW",
            "User Information",
            "Windows user",
            "Ada",
            "FreeW Options...",
        ]);

        var options = Descendants<Button>(pane)
            .Single(button => button.Content as string == "FreeW Options...");
        options.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        openedOptions.Should().BeTrue();
    }

    private static IReadOnlyList<string> Texts(DependencyObject root)
    {
        var values = new List<string>();

        foreach (var text in Descendants<TextBlock>(root))
            values.Add(text.Text);
        foreach (var button in Descendants<Button>(root))
        {
            if (button.Content is string text)
                values.Add(text);
        }

        return values;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
            yield return match;

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject)
            {
                foreach (var descendant in Descendants<T>(dependencyObject))
                    yield return descendant;
            }
        }
    }

    private sealed record SummaryOptions(
        int RecentFilesCap,
        string DefaultSaveFormat,
        string UiLanguage) : IApplicationOptionsSummarySource;
}
