using System.IO;
using System.Linq;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.Model;
using FreeW.Ribbon.Definitions;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

public sealed class ParagraphShadingParityTests
{
    [Fact]
    public void Avalonia_shading_route_exposes_Wpf_palette_and_applies_explicit_choices()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);
        var shading = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.para-shading");

        shading.Menu.Items.Select(item => item.CommandId?.Value)
            .Should().Contain(new[] { "freew.para-shading.light-yellow", "freew.para-shading.none" });

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Shaded"));
        var editor = new DocumentView();
        editor.LoadDocument(document);
        var registry = FreeWAvaloniaRibbonCommands.Build(editor, CreateCallbacks());

        Execute(registry, "freew.para-shading.light-blue");
        ((Paragraph)document.Blocks[0]).Formatting.ShadingColorHex.Should().Be("#DEEBF7");
        Execute(registry, "freew.para-shading.none");
        ((Paragraph)document.Blocks[0]).Formatting.ShadingColorHex.Should().BeNull();
    }

    [Fact]
    public void Wpf_paragraph_shading_remains_the_authority_for_palette_behavior()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("private sealed class ParagraphShadingCommand");
        source.Should().Contain("editor.ToggleParagraphShading(hex)");
        source.Should().Contain("Content = \"No Color\"");
        source.Should().Contain("#FFF2CC");
    }

    [Fact]
    public void Avalonia_character_shading_route_exposes_Wpf_palette_and_applies_explicit_choices()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);
        var shading = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.char-shading");

        shading.Menu.Items.Select(item => item.CommandId?.Value)
            .Should().Contain(new[] { "freew.char-shading.light-blue", "freew.char-shading.none" });

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Shaded"));
        var editor = new DocumentView();
        editor.LoadDocument(document);
        editor.SelectAll();
        var registry = FreeWAvaloniaRibbonCommands.Build(editor, CreateCallbacks());

        Execute(registry, "freew.char-shading.light-blue");
        ((Paragraph)document.Blocks[0]).Runs.All(run => run.Formatting.CharacterShadingHex == "#DEEBF7")
            .Should().BeTrue();
        Execute(registry, "freew.char-shading.none");
        ((Paragraph)document.Blocks[0]).Runs.All(run => run.Formatting.CharacterShadingHex is null)
            .Should().BeTrue();
    }

    [Fact]
    public void Wpf_character_shading_remains_the_authority_for_palette_behavior()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("private sealed class CharacterShadingCommand");
        source.Should().Contain("editor.SetCharacterShading(hex)");
        source.Should().Contain("Content = CharacterFormattingPickerPlanner.NoColorLabel");
        source.Should().Contain("#FFF2CC");
    }

    [Fact]
    public void Avalonia_character_border_route_exposes_Wpf_palette_and_applies_explicit_choices()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);
        var border = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.char-border");

        border.Menu.Items.Select(item => item.CommandId?.Value)
            .Should().Contain(new[] { "freew.char-border.blue", "freew.char-border.none" });

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Bordered"));
        var editor = new DocumentView();
        editor.LoadDocument(document);
        editor.SelectAll();
        var registry = FreeWAvaloniaRibbonCommands.Build(editor, CreateCallbacks());

        Execute(registry, "freew.char-border.blue");
        ((Paragraph)document.Blocks[0]).Runs.All(run => run.Formatting.CharacterBorder is { ColorHex: "#0070C0", WidthPt: 0.5, LineStyle: BorderLineStyle.Single })
            .Should().BeTrue();
        Execute(registry, "freew.char-border.none");
        ((Paragraph)document.Blocks[0]).Runs.All(run => run.Formatting.CharacterBorder is null)
            .Should().BeTrue();
    }

    [Fact]
    public void Wpf_character_border_remains_the_authority_for_palette_behavior()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("private sealed class CharacterBorderCommand");
        source.Should().Contain("editor.SetCharacterBorder(border)");
        source.Should().Contain("Content = CharacterFormattingPickerPlanner.NoBorderLabel");
        source.Should().Contain("#0070C0");
    }

    [Fact]
    public void Avalonia_highlight_route_exposes_Wpf_palette_and_applies_explicit_choices()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeWRibbonCapabilities.Avalonia);
        var highlight = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "freew.highlight");

        highlight.Menu.Items.Select(item => item.CommandId?.Value)
            .Should().Contain(new[] { "freew.highlight.yellow", "freew.highlight.none" });

        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Highlighted"));
        var editor = new DocumentView();
        editor.LoadDocument(document);
        editor.SelectAll();
        var registry = FreeWAvaloniaRibbonCommands.Build(editor, CreateCallbacks());

        Execute(registry, "freew.highlight.yellow");
        ((Paragraph)document.Blocks[0]).Runs.All(run => run.Formatting.HighlightColorHex == "#FFFF00")
            .Should().BeTrue();
        Execute(registry, "freew.highlight.none");
        ((Paragraph)document.Blocks[0]).Runs.All(run => run.Formatting.HighlightColorHex is null)
            .Should().BeTrue();
    }

    [Fact]
    public void Wpf_highlight_remains_the_authority_for_palette_behavior()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Ribbon", "FreeWRibbonCommands.cs"));

        source.Should().Contain("new ColorPickCommand(editor, isHighlight: true)");
        source.Should().Contain("Content = isHighlight ? \"No Color\" : \"Automatic\"");
        source.Should().Contain("#2F5496");
    }

    private static void Execute(RibbonCommandRegistry registry, string id)
    {
        registry.TryGet(new RibbonCommandId(id), out var command)
            .Should().BeTrue($"command '{id}' must be registered");
        command!.Execute(RibbonCommandContext.Empty);
    }

    private static FreeW.App.Presentation.Ribbon.FreeWRibbonHostExecutionPorts CreateCallbacks() =>
        new(
            Open: () => { }, Save: () => { }, Cut: () => { }, Copy: () => { }, Paste: () => { },
            Backstage: () => { }, NewDocument: () => { }, ToggleNavigationPane: () => { },
            ToggleReviewingPane: () => { }, ToggleRevealFormatting: () => { },
            OpenFindReplaceDialog: () => { }, SetPrintLayout: () => { }, SetWebLayout: () => { },
            SetDraftView: () => { }, OpenFontDialog: () => { }, OpenParagraphDialog: () => { },
            OpenPageSetupDialog: () => { }, ToggleOrientation: () => { }, ApplyMarginPreset: _ => { },
            ApplyPaperSize: _ => { }, InsertPicture: () => { }, OpenWordCountDialog: () => { },
            ApplyZoom: (_, _) => { });
}
