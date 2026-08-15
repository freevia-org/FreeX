using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class BackstageLocalizationTextTests
{
    [Fact]
    public void PaneTextDescriptor_ResolvesInfoOptionsAndSummaryText()
    {
        var descriptor = new SisterBackstagePaneTextDescriptor(
            Text("recent.empty", "No recent files"),
            Text("new.heading", "New"),
            Text("new.tile", "Blank"),
            Text("new.footer", "No more templates"),
            Text("options.description", "Settings"),
            new SisterBackstageExportPaneTextDescriptor(
                Text("export.heading", "Export"),
                Text("export.description", "Create a copy"),
                Text("export.group", "Fixed layout"),
                Text("export.pdf", "Export PDF"),
                Text("export.pdf.description", "Publish PDF")),
            RecentHeading: Text("recent.heading", "Recent"),
            OptionsHeading: Text("options.heading", "Options"),
            Info: new SisterBackstageInfoPaneTextDescriptor(
                Text("info.heading", "Info"),
                Text("info.location", "Location"),
                Text("info.unsaved", "Not saved"),
                Text("info.properties", "Properties"),
                Text("info.statistics", "Statistics"),
                Text("info.dirty", " (changed)"),
                new SisterBackstageCorePropertiesTextDescriptor(
                    Text("property.title", "Title"),
                    Text("property.author", "Author"),
                    Text("property.subject", "Subject"),
                    Text("property.keywords", "Keywords"),
                    Text("property.empty", "-"))),
            OptionsSummary: new ApplicationOptionsSummaryTextDescriptor(
                Text("options.recent", "Recent files"),
                Text("options.format", "Save format"),
                Text("options.language", "Language"),
                Text("options.folder", "Folder"),
                Text("options.system", "System")));
        var translations = new Dictionary<string, string>
        {
            ["recent.heading"] = "Nedavni",
            ["options.heading"] = "Postavke",
            ["info.heading"] = "Podaci",
            ["info.location"] = "Mjesto",
            ["info.unsaved"] = "Nije spremljeno",
            ["info.properties"] = "Svojstva",
            ["info.statistics"] = "Statistika",
            ["info.dirty"] = " (izmijenjeno)",
            ["property.title"] = "Naslov",
            ["property.empty"] = "n/a",
            ["options.recent"] = "Nedavne datoteke",
            ["options.system"] = "Sustav",
        };

        var text = SisterBackstagePaneTextSpec.FromDescriptor(
            descriptor,
            key => translations.GetValueOrDefault(key));
        var planner = new SisterBackstagePaneSpecPlanner(text);
        var recent = planner.BuildRecentPaneSpec([], _ => { });
        var options = planner.BuildOptionsPaneSpec(new Options(), "C:\\Data");
        var info = SisterBackstageInfoPanePlanner.Build(new SisterBackstageInfoPaneContext(
            "Document",
            "Draft",
            IsDirty: true,
            Location: null,
            new BackstageCoreProperties(null, null, null, null),
            [],
            Text: text.Info));

        recent.Heading.Should().Be("Nedavni");
        options.Heading.Should().Be("Postavke");
        options.Fields[0].Should().Be(new BackstageFieldRow("Nedavne datoteke", "5"));
        options.Fields[2].Should().Be(new BackstageFieldRow("Language", "Sustav"));
        info.EffectiveText.Heading.Should().Be("Podaci");
        info.EffectiveText.NotSavedYet.Should().Be("Nije spremljeno");
        info.Properties[0].Should().Be(new BackstageFieldRow("Naslov", "n/a"));
    }

    private static ResourceTextDescriptor Text(string key, string fallback) => new(key, fallback);

    private sealed class Options : IApplicationOptionsSummarySource
    {
        public int RecentFilesCap => 5;

        public string DefaultSaveFormat => ".docx";

        public string UiLanguage => "";
    }
}
