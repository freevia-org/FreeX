using Avalonia.Headless;
using Free.Shared.AppServices;
using FreeW.App.Avalonia.Editing;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class AutosaveAdapterLifecycleTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Dispose_RemovesTheAdapterFromEmergencyFanOut()
    {
        AutosaveAdapter? adapter = null;
        var before = AutosaveAdapter.ActiveAdapterCountForTests;

        try
        {
            await Session.Dispatch(() =>
            {
                var editor = new DocumentView();
                editor.LoadDocument(TextDocument.CreateEmpty());
                var workflow = new FileCommandWorkflow(
                    maxRecentEntries: () => 10,
                    onChanged: () => { },
                    promptSaveChanges: _ => SaveChangesPrompt.DontSave,
                    save: () => true,
                    loadRecentFilesStore: () => RecentFilesStore.Load(
                        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")));
                adapter = new AutosaveAdapter(
                    editor,
                    workflow,
                    ports => new FreeWAutosaveSession(ports));
            }, CancellationToken.None);

            AutosaveAdapter.ActiveAdapterCountForTests.Should().Be(before + 1);
            adapter!.Dispose();
            adapter = null;

            AutosaveAdapter.ActiveAdapterCountForTests.Should().Be(before);
        }
        finally
        {
            adapter?.Dispose();
        }
    }

    [Fact]
    public void MainWindow_DisposesAutosaveWhenTheWindowLifecycleEnds()
    {
        var source = TestWorkspaceFileLocator.ReadAllText(
            "freew",
            "FreeW.App.Avalonia",
            "MainWindow.cs");

        source.Should().Contain("Closed += (_, _) => _autosave.Dispose();")
            .And.Contain("await _autosave.StopAsync();");
    }
}
