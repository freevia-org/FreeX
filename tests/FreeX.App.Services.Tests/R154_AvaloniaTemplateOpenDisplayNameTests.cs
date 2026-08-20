using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R154 shared-templates F2: the Avalonia shell's File > Open call overrode the shared open
/// workflow's completion display name with the extension-INCLUDING file name
/// (CompletionDisplayName: Path.GetFileName(target.Path)), unlike the WPF host's identical Open
/// call, which passes no override at all. For a template open (OpensAsTemplate == true),
/// CurrentFilePath is forced null (the document has no real backing file yet -- Save forces Save
/// As), so WorkbookSession.DisplayName falls back to exactly this completion name. Avalonia
/// therefore titled a brand-new, nowhere-saved, template-derived workbook "Invoice.xltx - FreeX"
/// -- literally showing the template's own extension as if that were the currently-open,
/// currently-saved file -- while WPF correctly showed "Invoice - FreeX", matching Excel's own
/// convention of never showing a real file extension on an unsaved document.
/// </summary>
public sealed class R154_AvaloniaTemplateOpenDisplayNameTests : IDisposable
{
    private readonly TestTemporaryDirectory _tempDirectory =
        new(nameof(R154_AvaloniaTemplateOpenDisplayNameTests) + "-");

    public void Dispose() => _tempDirectory.Dispose();

    [Fact]
    public void AvaloniaOpenCallSite_DoesNotOverrideCompletionDisplayNameWithFileExtension()
    {
        var avaloniaSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var wpfSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Host", "MainWindow.Backstage.cs"));

        // WPF's identical File > Open call never overrides the completion display name at all,
        // so a template's fallback name always comes from WorkbookOpenResult.DisplayName
        // (Path.GetFileNameWithoutExtension(path), see WorkbookOpenService.cs) -- no extension.
        wpfSource.Should().NotContain("CompletionDisplayName");

        // The Avalonia Open call site must not resurrect the extension-including override --
        // that is exactly what leaked "Invoice.xltx" into the title of a brand-new,
        // template-derived, nowhere-saved workbook (this is the fail-before/pass-after guard:
        // this assertion fails against the pre-fix source, which contains that exact literal).
        avaloniaSource.Should().NotContain("CompletionDisplayName: Path.GetFileName(target.Path)");
    }

    [Fact]
    public async Task TemplateOpen_WithoutDisplayNameOverride_LeavesNoExtensionInPlanOrSession()
    {
        // Exercises the exact shared choke point every FreeX Open gesture funnels through
        // (WorkbookFileWorkflow.OpenAsync -> WorkbookFileCompletionPlanner.PlanOpen ->
        // WorkbookSessionFactory.CreateOpened -> WorkbookSession.DisplayName), driven the way the
        // fixed Avalonia call site (and the WPF host, which never overrode it) now drives it: no
        // CompletionDisplayName override.
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var format = new FileFormatDescriptor(
            ".fxtpl", "Fake Template", CanOpen: true, CanSave: false, OpensAsTemplate: true);
        var adapter = new TestFileAdapter(load: _ => workbook, formats: [format]);
        var path = Path.Combine(_tempDirectory.Path, "Invoice.fxtpl");
        await File.WriteAllTextAsync(path, "template-payload");

        var fileWorkflow = new WorkbookFileWorkflow([adapter]);
        fileWorkflow.TryResolveOpenTarget(path, out var target, out var message).Should().BeTrue(message);

        var openResult = await fileWorkflow.OpenAsync(new WorkbookOpenWorkflowRequest(
            target!,
            (_, _) => Task.CompletedTask));

        openResult.Succeeded.Should().BeTrue();
        var plan = openResult.Context!.CompletionPlan;
        plan.OpenedAsTemplate.Should().BeTrue();
        plan.CurrentFilePath.Should().BeNull("a template open never gets a real backing file");
        plan.DisplayName.Should().Be(
            "Invoice",
            "Excel never shows a real file extension on an unsaved, template-derived document");

        var session = new WorkbookSessionFactory().CreateOpened(
            target!,
            openResult.Context.Result,
            viewportHeight: 240,
            viewportWidth: 320,
            adapters: [adapter],
            completionPlan: plan);

        session.CurrentFilePath.Should().BeNull();
        session.DisplayName.Should().Be("Invoice");
    }

    [Fact]
    public async Task TemplateOpen_RegressesToExtension_WhenDisplayNameIsOverriddenWithFileName()
    {
        // Sibling/mechanism proof: reproduces exactly what the OLD Avalonia call site produced
        // (CompletionDisplayName: Path.GetFileName(target.Path)) through the same real shared
        // workflow, so the source-contract fix above is provably load-bearing rather than
        // incidental -- this is the actual bug the fix removes.
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var format = new FileFormatDescriptor(
            ".fxtpl", "Fake Template", CanOpen: true, CanSave: false, OpensAsTemplate: true);
        var adapter = new TestFileAdapter(load: _ => workbook, formats: [format]);
        var path = Path.Combine(_tempDirectory.Path, "Invoice.fxtpl");
        await File.WriteAllTextAsync(path, "template-payload");

        var fileWorkflow = new WorkbookFileWorkflow([adapter]);
        fileWorkflow.TryResolveOpenTarget(path, out var target, out var message).Should().BeTrue(message);

        var openResult = await fileWorkflow.OpenAsync(new WorkbookOpenWorkflowRequest(
            target!,
            (_, _) => Task.CompletedTask,
            CompletionDisplayName: Path.GetFileName(target!.Path)));

        openResult.Context!.CompletionPlan.DisplayName.Should().Be("Invoice.fxtpl");
    }

    [Fact]
    public async Task NormalOpen_WithoutDisplayNameOverride_StillShowsExtensionViaCurrentFilePath()
    {
        // Sibling no-regression: a normal (non-template) open must keep showing its extension in
        // the title, exactly as before this fix, because WorkbookSession.DisplayName reads
        // Path.GetFileName(CurrentFilePath) once a real backing file is set -- the completion
        // plan's own (now extension-less) DisplayName field never reaches the title for this case.
        var workbook = new Workbook("Book1");
        workbook.AddSheet("Sheet1");
        var format = new FileFormatDescriptor(".fxl", "FreeX Workbook", CanOpen: true, CanSave: true);
        var adapter = new TestFileAdapter(load: _ => workbook, formats: [format]);
        var path = Path.Combine(_tempDirectory.Path, "Budget.fxl");
        await File.WriteAllTextAsync(path, "workbook-payload");

        var fileWorkflow = new WorkbookFileWorkflow([adapter]);
        fileWorkflow.TryResolveOpenTarget(path, out var target, out var message).Should().BeTrue(message);

        var openResult = await fileWorkflow.OpenAsync(new WorkbookOpenWorkflowRequest(
            target!,
            (_, _) => Task.CompletedTask));

        openResult.Succeeded.Should().BeTrue();
        var plan = openResult.Context!.CompletionPlan;
        plan.OpenedAsTemplate.Should().BeFalse();
        plan.CurrentFilePath.Should().Be(path);

        var session = new WorkbookSessionFactory().CreateOpened(
            target!,
            openResult.Context.Result,
            viewportHeight: 240,
            viewportWidth: 320,
            adapters: [adapter],
            completionPlan: plan);

        session.CurrentFilePath.Should().Be(path);
        session.DisplayName.Should().Be("Budget.fxl");
    }
}
