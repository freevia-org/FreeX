using Free.Shared.AppServices;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationFileLifecycleAdapterTests
{
    [Fact]
    public async Task Default_operations_delegate_to_the_shared_file_command_workflow()
    {
        var changed = 0;
        var workflow = CreateWorkflow(() => changed++);
        var adapter = new PresentationFileLifecycleAdapter(workflow);
        var loadedNew = false;

        adapter.MarkDirty();
        var created = await adapter.NewAsync("creating a new presentation", () =>
        {
            loadedNew = true;
            return Task.CompletedTask;
        });
        adapter.MarkSavedWithPath("C:/Decks/Quarterly.pptx", suppressRecentFiles: true);
        var savedPath = string.Empty;
        var saved = await adapter.SaveAsync(
            path =>
            {
                savedPath = path;
                return Task.FromResult(true);
            },
            () => Task.FromResult(false));

        created.Should().BeTrue();
        loadedNew.Should().BeTrue();
        adapter.IsDirty.Should().BeFalse();
        adapter.CurrentFileName.Should().Be("Quarterly.pptx");
        adapter.DisplayName.Should().Be("Quarterly");
        saved.Should().BeTrue();
        savedPath.Should().Be("C:/Decks/Quarterly.pptx");
        changed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Async_host_hooks_preserve_native_destructive_action_prompting()
    {
        var calls = new List<string>();
        var workflow = CreateWorkflow(() => { });
        var adapter = new PresentationFileLifecycleAdapter(
            workflow,
            async (action, load) =>
            {
                calls.Add("new:" + action);
                await load();
                return true;
            },
            async (action, pick, open) =>
            {
                calls.Add("open:" + action);
                var path = await pick();
                return path is not null && await open(path);
            },
            action =>
            {
                calls.Add("close:" + action);
                return Task.FromResult(true);
            });

        (await adapter.NewAsync("new", () =>
        {
            calls.Add("load-new");
            return Task.CompletedTask;
        })).Should().BeTrue();
        (await adapter.OpenAsync(
            "open",
            () => Task.FromResult<string?>("C:/Decks/Open.pptx"),
            path =>
            {
                calls.Add("load:" + path);
                return Task.FromResult(true);
            })).Should().BeTrue();
        (await adapter.ConfirmCloseAllowedAsync("closing")).Should().BeTrue();

        calls.Should().Equal(
            "new:new", "load-new",
            "open:open", "load:C:/Decks/Open.pptx",
            "close:closing");
    }

    private static FileCommandWorkflow CreateWorkflow(Action onChanged) => new(
        maxRecentEntries: () => 10,
        onChanged,
        promptSaveChanges: _ => SaveChangesPrompt.DontSave,
        save: () => true,
        untitledDisplayName: "Presentation",
        loadRecentFilesStore: () => new RecentFilesStore(
            Path.Combine(Path.GetTempPath(), $"freep-lifecycle-{Guid.NewGuid():N}.json")));
}
