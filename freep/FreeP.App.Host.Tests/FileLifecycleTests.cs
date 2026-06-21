using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using FreeP.App.Host;
using FreeP.Core.IO;

namespace FreeP.App.Host.Tests;

public sealed class FileLifecycleTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "FreeP.FileLifecycleTests", Guid.NewGuid().ToString("N"));

    public FileLifecycleTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private (Window Window, FileCommands File, Func<Presentation> GetModel, Func<int> ChangeCount) CreateHarness()
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var model = Presentation.CreateEmpty();
        var changes = 0;
        var recentStorePath = Path.Combine(_tempDir, "recent.json");
        var file = new FileCommands(
            window,
            () => model,
            loaded => model = loaded,
            () => changes++,
            loadRecentFilesStore: () => RecentFilesStore.Load(recentStorePath));
        return (window, file, () => model, () => changes);
    }

    [StaFact]
    public void FreshPresentation_IsCleanWithUntitledName()
    {
        var (_, file, _, _) = CreateHarness();

        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().BeNull();
        file.DisplayName.Should().Be("Untitled");
    }

    [StaFact]
    public void MarkDirty_SetsDirtyAndNotifiesOnce()
    {
        var (_, file, _, changeCount) = CreateHarness();

        file.MarkDirty();
        file.IsDirty.Should().BeTrue();
        changeCount().Should().Be(1);

        file.MarkDirty();
        changeCount().Should().Be(1);
    }

    [StaFact]
    public void New_OnCleanPresentation_ProceedsWithoutPromptAndResetsState()
    {
        var (_, file, getModel, _) = CreateHarness();
        var path = WriteFxp("Deck.fxp", "Opened");
        file.OpenPath(path).Should().BeTrue();

        var proceeded = file.New();

        proceeded.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().BeNull();
        file.DisplayName.Should().Be("Untitled");
        getModel().Slides.Should().HaveCount(1);
    }

    [StaFact]
    public void OpenPath_LoadsFileAndMarksSavedWithPath()
    {
        var (_, file, getModel, _) = CreateHarness();
        var path = WriteFxp("Opened.fxp", "Quarterly Review");

        var opened = file.OpenPath(path);

        opened.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
        file.DisplayName.Should().Be("Opened");
        getModel().Properties.Title.Should().Be("Quarterly Review");
    }

    [StaFact]
    public void Save_AfterEdit_WritesToExistingPathAndClearsDirty()
    {
        var (_, file, getModel, _) = CreateHarness();
        var path = WriteFxp("Deck.fxp", "Initial");
        file.OpenPath(path).Should().BeTrue();

        getModel().Properties.Title = "Updated";
        file.MarkDirty();
        file.IsDirty.Should().BeTrue();

        var saved = file.Save();

        saved.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
        FxpFormat.Read(path).Properties.Title.Should().Be("Updated");
    }

    [StaFact]
    public void Save_OnCleanOpenedPresentation_StaysClean()
    {
        var (_, file, _, _) = CreateHarness();
        var path = WriteFxp("Clean.fxp", "Clean");
        file.OpenPath(path).Should().BeTrue();

        var saved = file.Save();

        saved.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
    }

    private string WriteFxp(string name, string title)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Properties.Title = title;
        var path = Path.Combine(_tempDir, name);
        FxpFormat.Write(presentation, path);
        return path;
    }
}
