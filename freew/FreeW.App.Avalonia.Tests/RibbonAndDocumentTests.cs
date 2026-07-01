using System.IO;
using System.Linq;
using FreeW.App.Avalonia;
using FreeW.App.Avalonia.Ribbon;
using FreeW.Core.IO;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Guard tests for the FreeW Avalonia shell's portable pieces: the ribbon definition, its command
/// wiring, and the starter document. These exercise pure logic (no UI thread) so they run on the
/// freew-linux CI lane alongside the FreeW.Core suites.
/// </summary>
public class RibbonAndDocumentTests
{
    [Fact]
    public void Ribbon_definition_has_file_and_home_tabs()
    {
        var definition = FreeWRibbon.BuildDefinition();
        definition.Tabs.Select(t => t.Id).Should().Contain(new[] { "file", "home" });
    }

    [Fact]
    public void Ribbon_home_tab_has_the_expected_groups()
    {
        var home = FreeWRibbon.BuildDefinition().FindTab("home");
        home.Should().NotBeNull();
        home!.Groups.Select(g => g.Id)
            .Should().Contain(new[] { "clipboard", "font", "paragraph", "editing" });
    }

    [Fact]
    public void Ribbon_file_tab_exposes_explicit_pdf_text_import()
    {
        var file = FreeWRibbon.BuildDefinition().FindTab("file");

        file.Should().NotBeNull();
        file!.Groups
            .SelectMany(group => group.Controls)
            .Select(CommandIdOf)
            .Where(id => id is not null)
            .Select(id => id!.Value.Value)
            .Should().Contain(new[] { "freew.open", "freew.save", "freew.import-pdf-text" });
    }
    [Fact]
    public void Every_ribbon_command_id_is_registered()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var callbacks = NoopCallbacks();
        var registry = FreeWRibbon.BuildRegistry(new Editing.DocumentView(), callbacks);

        foreach (var id in CommandIds(definition))
            registry.TryGet(id, out _).Should().BeTrue($"command '{id.Value}' should be wired");
    }

    [Fact]
    public void Import_pdf_ribbon_command_invokes_host_route()
    {
        var invoked = 0;
        var callbacks = NoopCallbacks() with { ImportPdfText = () => invoked++ };
        var registry = FreeWRibbon.BuildRegistry(new Editing.DocumentView(), callbacks);

        registry.TryGet(new RibbonCommandId("freew.import-pdf-text"), out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);

        invoked.Should().Be(1);
    }

    [Fact]
    public void Sample_document_contains_title_lists_and_a_table()
    {
        var doc = SampleDocument.Create();

        doc.PlainText.Should().Contain("Welcome to FreeW");
        doc.Blocks.OfType<Paragraph>()
            .Any(p => p.Formatting.ListKind == ListKind.Bullet).Should().BeTrue();
        doc.Blocks.OfType<Paragraph>()
            .Any(p => p.Formatting.ListKind == ListKind.Number).Should().BeTrue();
        doc.Blocks.OfType<Table>().Should().ContainSingle();
    }

    [Fact]
    public void Sample_document_survives_a_docx_round_trip()
    {
        var doc = SampleDocument.Create();

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        var reopened = DocxReader.Read(stream);

        reopened.PlainText.Should().Contain("Welcome to FreeW");
        reopened.Blocks.OfType<Table>().Should().NotBeEmpty();
    }

    private static IEnumerable<RibbonCommandId> CommandIds(RibbonDefinition definition) =>
        definition.Tabs
            .SelectMany(t => t.Groups)
            .SelectMany(g => g.Controls)
            .Select(CommandIdOf)
            .Where(id => id is not null)
            .Select(id => id!.Value);

    private static RibbonCommandId? CommandIdOf(RibbonControl control) => control switch
    {
        RibbonButton b => b.CommandId,
        RibbonToggleButton t => t.CommandId,
        RibbonComboBox c => c.CommandId,
        RibbonCheckBox cb => cb.CommandId,
        RibbonSplitButton sb => sb.CommandId,
        RibbonDropdown d => d.CommandId,
        RibbonGallery g => g.CommandId,
        _ => (RibbonCommandId?)null,
    };

    private static RibbonHostCallbacks NoopCallbacks() =>
        new(() => { }, () => { }, () => { }, () => { }, () => { }, () => { });
}
