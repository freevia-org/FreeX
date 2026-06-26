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
    public void Every_ribbon_command_id_is_registered()
    {
        var definition = FreeWRibbon.BuildDefinition();
        var callbacks = new RibbonHostCallbacks(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, (_, _) => { });
        var registry = FreeWRibbon.BuildRegistry(new Editing.DocumentView(), callbacks);

        foreach (var id in CommandIds(definition))
            registry.TryGet(id, out _).Should().BeTrue($"command '{id.Value}' should be wired");
    }

    [Fact]
    public void Avalonia_shell_uses_the_shared_ribbon_renderer()
    {
        var project = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"));
        project.Should().Contain(@"..\..\shared\Free.Shared.Ribbon.Avalonia\Free.Shared.Ribbon.Avalonia.csproj");

        var mainWindow = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));
        mainWindow.Should().Contain("using Free.Shared.Ribbon.Avalonia;");
        mainWindow.Should().Contain("AvaloniaRibbonRenderer.BuildRibbon(");

        File.Exists(FindRepoFile("freew", "FreeW.App.Avalonia", "Ribbon", "AvaloniaRibbonRenderer.cs"))
            .Should()
            .BeFalse("FreeW Avalonia should not carry a private renderer now that the suite has a shared Avalonia renderer");
    }

    [Fact]
    public void Avalonia_shell_routes_file_lifecycle_through_shared_file_command_workflow()
    {
        var project = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "FreeW.App.Avalonia.csproj"));
        project.Should().Contain(@"..\..\shared\Free.Shared.AppServices\Free.Shared.AppServices.csproj");

        var mainWindow = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));
        mainWindow.Should().Contain("private readonly FileCommandWorkflow _fileWorkflow;");
        mainWindow.Should().Contain("new FileCommandWorkflow(");
        mainWindow.Should().Contain("_fileWorkflow.New(");
        mainWindow.Should().Contain("_fileWorkflow.OpenAsync(");
        mainWindow.Should().Contain("_fileWorkflow.SaveAsync(");
        mainWindow.Should().Contain("_fileWorkflow.MarkDirty();");
        // suppressRecentFiles was true (stub) and is now false so files register in the store.
        mainWindow.Should().Contain("_fileWorkflow.MarkSavedWithPath(path, suppressRecentFiles:");
        mainWindow.Should().NotContain("new FileCommandSession");
        mainWindow.Should().NotContain("FileLifecyclePlanner.PlanSave(");
        mainWindow.Should().NotContain("WorkbookDocumentState");
        mainWindow.Should().NotContain("_state.");
        mainWindow.Should().NotContain("CurrentFilePath");
        mainWindow.Should().NotContain("private string? _currentPath");
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

    private static string FindRepoFile(params string[] parts) =>
        Path.Combine(FindRepoRoot(), Path.Combine(parts));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from the test output directory.");
    }
}
