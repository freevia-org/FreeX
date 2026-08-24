using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        definition.Tabs.Select(t => t.Id).Should().Contain(new[] { "file", "home" });
    }

    [Fact]
    public void Ribbon_home_tab_has_the_expected_groups()
    {
        var home = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia).FindTab("home");
        home.Should().NotBeNull();
        home!.Groups.Select(g => g.Id)
            .Should().Contain(new[] { "clipboard", "font", "paragraph", "editing" });
    }

    [Fact]
    public void Ribbon_file_tab_exposes_explicit_pdf_text_import()
    {
        var file = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia).FindTab("file");

        file.Should().NotBeNull();
        file!.Groups
            .SelectMany(group => group.Controls)
            .Select(CommandIdOf)
            .Where(id => id is not null)
            .Select(id => id!.Value.Value)
            .Should().Contain(new[] { "freew.open", "freew.save", "freew.import-pdf-text" });
    }

    [Fact]
    public void Avalonia_file_shell_and_WPF_authority_legal_notice_commands_are_backed()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var registry = FreeWAvaloniaRibbonCommands.Build(new Editing.DocumentView(), NoopCallbacks());
        var commandIds = CommandIds(definition).Select(id => id.Value).ToArray();

        commandIds.Should().Contain(new[]
        {
            "freew.backstage",
            "freew.new",
            "freew.open",
            "freew.import-pdf-text",
            "freew.save",
        });

        foreach (var id in new[]
                 {
                     "freew.backstage",
                     "freew.new",
                     "freew.open",
                     "freew.import-pdf-text",
                     "freew.save",
                 })
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Avalonia compact File shell command '{id}' must be host-backed");

        definition.Tabs.Select(tab => tab.Id).Should().Contain("help");
        commandIds.Should().Contain(new[]
        {
            "freew.help-online",
            "freew.feedback",
            "freew.copy-diagnostics",
            "freew.check-updates",
            "freew.about",
            "freew.legal-notices",
        });

        foreach (var id in new[]
                 {
                     "freew.help-online",
                     "freew.feedback",
                     "freew.copy-diagnostics",
                     "freew.check-updates",
                     "freew.about",
                     "freew.legal-notices",
                 })
            registry.TryGet(new RibbonCommandId(id), out _)
                .Should().BeTrue($"Avalonia Help command '{id}' must be wired");

        var calls = new List<string>();
        var routedCallbacks = NoopCallbacks() with
        {
            Backstage = () => calls.Add("backstage"),
            NewDocument = () => calls.Add("new"),
            Open = () => calls.Add("open"),
            ImportPdfText = () => calls.Add("import-pdf-text"),
            Save = () => calls.Add("save"),
        };
        var routedRegistry = FreeWAvaloniaRibbonCommands.Build(new Editing.DocumentView(), routedCallbacks);
        foreach (var id in new[]
                 {
                     "freew.backstage",
                     "freew.new",
                     "freew.open",
                     "freew.import-pdf-text",
                     "freew.save",
                 })
        {
            routedRegistry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        }
        calls.Should().Equal("backstage", "new", "open", "import-pdf-text", "save");

        var mainWindow = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));
        mainWindow.Should().Contain("NewDocument: NewDocument");
        mainWindow.Should().Contain("ImportPdfText: () => _ = ImportPdfTextAsync()");
        mainWindow.Should().Contain("Backstage: () => _ = ShowBackstageAsync()");
        mainWindow.Should().Contain("Save: () => _applicationCommands.Execute(FreeWKeyboardCommand.SaveDocument)");
    }

    [Fact]
    public void Avalonia_help_commands_route_callbacks_and_are_disabled_without_host_routes()
    {
        var calls = new List<string>();
        var callbacks = NoopCallbacks() with
        {
            OpenHelpOnline = () => calls.Add("help-online"),
            OpenFeedback = () => calls.Add("feedback"),
            CopyDiagnostics = () => calls.Add("copy-diagnostics"),
            CheckForUpdates = () => calls.Add("check-updates"),
        };
        var registry = FreeWAvaloniaRibbonCommands.Build(new Editing.DocumentView(), callbacks);

        foreach (var id in new[]
                 {
                     "freew.help-online",
                     "freew.feedback",
                     "freew.copy-diagnostics",
                     "freew.check-updates",
                 })
        {
            registry.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
            command!.Execute(RibbonCommandContext.Empty);
        }

        calls.Should().Equal("help-online", "feedback", "copy-diagnostics", "check-updates");

        var unavailable = FreeWAvaloniaRibbonCommands.Build(new Editing.DocumentView(), NoopCallbacks());
        foreach (var id in new[]
                 {
                     "freew.help-online",
                     "freew.feedback",
                     "freew.copy-diagnostics",
                     "freew.check-updates",
                 })
        {
            unavailable.TryGet(new RibbonCommandId(id), out var command).Should().BeTrue();
            command.Should().BeAssignableTo<IRibbonStatefulCommand>();
            ((IRibbonStatefulCommand)command!).GetState().IsEnabled.Should().BeFalse();
        }
    }

    [Fact]
    public void Every_ribbon_command_id_is_registered()
    {
        var definition = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Avalonia);
        var callbacks = NoopCallbacks();
        var registry = FreeWAvaloniaRibbonCommands.Build(new Editing.DocumentView(), callbacks);

        foreach (var id in CommandIds(definition))
            {
                // Pure menu openers carry no direct action; see RibbonMenuOpenerIds.
                if (RibbonMenuOpenerIds.IsMenuOpener(id.Value))
                    continue;
                registry.TryGet(id, out _).Should().BeTrue($"command '{id.Value}' should be wired");
            }
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
        var sharedShellWorkflow = File.ReadAllText(FindRepoFile(
            "shared",
            "Free.Shared.Shell.Avalonia",
            "SisterAvaloniaFileCommandWorkflow.cs"));
        var documentCommandSession = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "FreeWDocumentFileCommandSession.cs"));

        mainWindow.Should().Contain("private readonly SisterAvaloniaFileCommandWorkflow _fileWorkflow;");
        mainWindow.Should().Contain("new SisterAvaloniaFileCommandWorkflow(");
        mainWindow.Should().Contain("new SisterAvaloniaFileTitleSpec(");
        mainWindow.Should().Contain("private readonly DocumentPersistenceWorkflow _documentPersistence");
        mainWindow.Should().Contain("_fileCommands.NewAsync()");
        mainWindow.Should().Contain("_fileCommands.OpenAsync()");
        mainWindow.Should().Contain("_fileCommands.SaveAsync()");
        mainWindow.Should().Contain("_fileWorkflow.ConfirmCloseAllowedAsync(");
        mainWindow.Should().Contain("new SisterAvaloniaAsyncWindowCloseCoordinator(");
        mainWindow.Should().Contain("saveAsync: SaveAsync");
        mainWindow.Should().Contain("FreeWDocumentFileWorkflow _documentFileWorkflow");
        documentCommandSession.Should().Contain("_workflow.OpenPathAsync(path, suppressRecentFiles)");
        documentCommandSession.Should().Contain("_workflow.SavePathAsync(path, filterIndex, kind)");
        mainWindow.Should().Contain("SaveCompatibilityWarningDialog.ShowAsync(this, plan)");
        mainWindow.Should().Contain("_documentPersistence.BuildSavePickerPlan(");
        mainWindow.Should().Contain("_fileWorkflow.MarkDirty();");
        mainWindow.Should().Contain("_documentFileWorkflow.ApplyOpenResultAsync(result)");
        sharedShellWorkflow.Should().Contain("new FileCommandWorkflow(");
        sharedShellWorkflow.Should().Contain("ApplicationWindowTitlePolicy.Compose(");
        sharedShellWorkflow.Should().Contain("AvaloniaSaveChangesDialog.ShowAsync(");
        sharedShellWorkflow.Should().Contain("RecentEntries => _workflow.RecentEntries");
        mainWindow.Should().NotContain("PromptSaveChangesSync");
        mainWindow.Should().NotContain("AvaloniaSaveChangesDialog.ShowAsync(");
        mainWindow.Should().NotContain("new FileCommandSession");
        mainWindow.Should().NotContain("FileLifecyclePlanner.PlanSave(");
        mainWindow.Should().NotContain("WorkbookDocumentState");
        mainWindow.Should().NotContain("_state.");
        mainWindow.Should().NotContain("CurrentFilePath");
        mainWindow.Should().NotContain("private string? _currentPath");
        mainWindow.Should().NotContain("DocumentFileFormatResolver.FindSaveAdapter(");
        mainWindow.Should().NotContain("DocumentSaveCompatibilityPlanner.Build(");
        mainWindow.Should().NotContain("new DocumentOpenExecutionRequest(");
        mainWindow.Should().NotContain("new DocumentSaveExecutionRequest(");
        mainWindow.Split("_documentPersistence.Open(path)").Should().HaveCount(2,
            "only the review-document loader bypasses the shell open coordinator");
        mainWindow.Should().Contain("return _documentPersistence.Open(path).Document;");
        mainWindow.Should().NotContain("_documentPersistence.Save(_editor.Document, target)");
        mainWindow.Should().NotContain("File.Create(path)");
    }

    [Fact]
    public void Avalonia_shell_confirms_shared_save_compatibility_plan_before_writing()
    {
        var mainWindow = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));
        var coordinator = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "DocumentFileExecutionCoordinator.cs"));
        var dialogSource = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Avalonia",
            "SaveCompatibilityWarningDialog.cs"));

        var normalizedCoordinator = coordinator.Replace("\r\n", "\n", StringComparison.Ordinal);
        var confirmationIndex = normalizedCoordinator.IndexOf(
            "await request.ConfirmCompatibilityAsync",
            StringComparison.Ordinal);
        var conflictIndex = normalizedCoordinator.IndexOf(
            "ExternalFileWriteConflictPolicy.PrepareAsync(",
            StringComparison.Ordinal);
        var saveMatch = Regex.Match(
            normalizedCoordinator,
            @"_persistence\.Save\(\s*request\.Document,\s*request\.Target,\s*conflictPreparation\.ExpectedLastWriteTimeUtc\s*\);",
            RegexOptions.Singleline);
        var completionIndex = normalizedCoordinator.IndexOf(
            "await request.CompleteSaveAsync!",
            StringComparison.Ordinal);

        confirmationIndex.Should().BeGreaterThanOrEqualTo(0);
        conflictIndex.Should().BeGreaterThan(confirmationIndex);
        saveMatch.Success.Should().BeTrue(
            "the shared coordinator must persist with the externally-checked timestamp it prepared");
        saveMatch.Index.Should().BeGreaterThan(conflictIndex);
        completionIndex.Should().BeGreaterThan(saveMatch.Index);
        mainWindow.Should().Contain("SaveCompatibilityWarningDialog.ShowAsync(this, plan)");
        dialogSource.Should().Contain("DocumentSaveCompatibilityPlan");
        dialogSource.Should().Contain("plan.Message");
        dialogSource.Should().Contain("plan.ContinueButtonText");
        dialogSource.Should().Contain("plan.CancelButtonText");
        dialogSource.Should().Contain("Close(true)");
        dialogSource.Should().Contain("Close(false)");
    }

    [Fact]
    public void Import_pdf_ribbon_command_invokes_host_route()
    {
        var invoked = 0;
        var callbacks = NoopCallbacks() with { ImportPdfText = () => invoked++ };
        var registry = FreeWAvaloniaRibbonCommands.Build(new Editing.DocumentView(), callbacks);

        registry.TryGet(new RibbonCommandId("freew.import-pdf-text"), out var command).Should().BeTrue();
        command!.Execute(RibbonCommandContext.Empty);

        invoked.Should().Be(1);
    }

    [Fact]
    public void Avalonia_pdf_import_uses_shared_dirty_gate_persistence_and_picker_plan()
    {
        var mainWindow = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));
        var importStart = mainWindow.IndexOf("private Task<bool> ImportPdfTextAsync()", StringComparison.Ordinal);
        var saveStart = mainWindow.IndexOf("private Task<bool> SaveAsync()", importStart, StringComparison.Ordinal);

        importStart.Should().BeGreaterThanOrEqualTo(0);
        saveStart.Should().BeGreaterThan(importStart);
        var importSource = mainWindow[importStart..saveStart];
        importSource.Should().Contain("_fileCommands.ImportPdfTextAsync()");
        importSource.Should().Contain("_documentPersistence.BuildPdfImportPickerPlan().FileTypes");
        mainWindow.Should().Contain("new FreeWDocumentFileCommandSession(");
        mainWindow.Should().Contain("PickPdfImportPathAsync: _pickPdfImportPathAsync");
        importSource.Should().NotContain("_documentPersistence.ImportPdfText(path)");
        importSource.Should().NotContain("DocumentFileAdapterCatalog.CreatePdfImportAdapters()");
        importSource.Should().NotContain("File.OpenRead(path)");
    }

    [Fact]
    public void Avalonia_shell_wires_review_compare_combine_to_model_backed_workflow()
    {
        var mainWindow = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "MainWindow.cs"));

        mainWindow.Should().Contain("CompareDocuments: () => _ = CompareDocumentsAsync()");
        mainWindow.Should().Contain("CombineDocuments: () => _ = CombineDocumentsAsync()");
        mainWindow.Should().Contain("ReviewCompareCombineWorkflow.ExecuteCompare(");
        mainWindow.Should().Contain("ReviewCompareCombineWorkflow.ExecuteCombine(");
        mainWindow.Should().Contain("_fileWorkflow.MarkDirtyWithPath(null);");
    }

    [Fact]
    public void Avalonia_protect_toggles_read_state_from_shared_protection_state_planner()
    {
        var editor = new Editing.DocumentView();
        var registry = FreeWAvaloniaRibbonCommands.Build(
            editor,
            NoopCallbacks() with { RestrictEditing = () => { } });

        registry.TryGet(new RibbonCommandId("freew.mark-as-final"), out var markAsFinal).Should().BeTrue();
        registry.TryGet(new RibbonCommandId("freew.restrict-editing"), out var restrictEditing).Should().BeTrue();

        markAsFinal.Should().BeAssignableTo<IRibbonStatefulCommand>();
        restrictEditing.Should().BeAssignableTo<IRibbonStatefulCommand>();
        ((IRibbonStatefulCommand)markAsFinal!).GetState().IsChecked.Should().BeFalse();
        ((IRibbonStatefulCommand)restrictEditing!).GetState().IsChecked.Should().BeFalse();

        editor.SetMarkedAsFinal(true);
        editor.SetProtection(ProtectionMode.CommentsOnly);

        ((IRibbonStatefulCommand)markAsFinal!).GetState().IsChecked.Should().BeTrue();
        ((IRibbonStatefulCommand)restrictEditing!).GetState().IsChecked.Should().BeTrue();

        var commandSource = File.ReadAllText(FindRepoFile(
            "freew",
            "FreeW.App.Avalonia",
            "Ribbon",
            "FreeWAvaloniaRibbonCommands.cs"));
        commandSource.Should().Contain("ReviewProtectionStatePlanner.Build(");
        commandSource.Should().NotContain("() => editor.IsProtected));");
    }

    [Fact]
    public void Avalonia_proofing_language_dialog_uses_shared_dialog_planner()
    {
        var source = File.ReadAllText(FindRepoFile("freew", "FreeW.App.Avalonia", "ProofingDialogs.cs"));

        source.Should().Contain("ProofingLanguageDialogPlanner.Build(currentTag, UiText.Get)");
        source.Should().Contain("choice.DisplayText");
        source.Should().NotContain("ProofingLanguageCatalog.CommonLanguages.Select");
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
        Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine(parts));


    private static FreeWRibbonHostExecutionPorts NoopCallbacks() =>
        new(() => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, () => { }, _ => { }, _ => { }, () => { }, () => { }, (_, _) => { });
}
