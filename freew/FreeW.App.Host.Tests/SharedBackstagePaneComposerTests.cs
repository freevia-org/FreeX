using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
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
        var planner = new SisterBackstagePaneSpecPlanner(SisterBackstagePaneTextSpec.FreeW);

        var recent = planner.BuildRecentPaneSpec(["C:/Docs/Budget.docx"], _ => { });
        var template = planner.BuildNewPaneSpec(() => { });
        var options = planner.BuildOptionsPaneSpec(
            new SummaryOptions(RecentFilesCap: 9, DefaultSaveFormat: ".docx", UiLanguage: ""),
            @"C:\Users\Ada\AppData\Local\FreeW",
            edit: () => edited = true);

        recent.EmptyText.Should().Be("No recent documents.");
        recent.Paths.Should().Equal("C:/Docs/Budget.docx");
        template.Heading.Should().Be("New");
        template.TileCaption.Should().Be("Blank document");
        template.FooterText.Should().Be("More templates are not available in this build.");
        options.Description.Should().Be("FreeW application settings. These persist between sessions and apply immediately.");
        options.EditText.Should().Be("Edit options\u2026");

        options.Edit.Should().NotBeNull();
        options.Edit!.Invoke();
        edited.Should().BeTrue();
    }

    [Fact]
    public void SisterBackstagePaneSpecPlanner_BuildsFreePPaneSpecsFromPreset()
    {
        var planner = new SisterBackstagePaneSpecPlanner(SisterBackstagePaneTextSpec.FreeP);

        var recent = planner.BuildRecentPaneSpec(Array.Empty<string>(), _ => { });
        var template = planner.BuildNewPaneSpec(() => { });
        var options = planner.BuildOptionsPaneSpec(
            new SummaryOptions(RecentFilesCap: 5, DefaultSaveFormat: ".freep", UiLanguage: "en-US"),
            @"C:\Users\Ada\AppData\Local\FreeP");

        recent.EmptyText.Should().Be("No recent presentations.");
        template.TileCaption.Should().Be("Blank presentation");
        options.Description.Should().Be("FreeP application settings. These persist between sessions.");
        options.EditText.Should().BeNull();
        options.Edit.Should().BeNull();
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
            "Info",
            "Document",
            "Report  (unsaved changes)",
            "Location",
            "Not saved yet",
            "Properties",
            "Title",
            "Budget",
            "Statistics",
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
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        invoked.Should().BeTrue();
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
