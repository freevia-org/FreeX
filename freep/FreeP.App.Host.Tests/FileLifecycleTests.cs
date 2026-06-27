using System.IO;
using System.Reflection;
using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.IO;
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
        var path = WritePptx("Deck.pptx", "Opened");
        file.OpenPath(path).Should().BeTrue();

        var proceeded = file.New();

        proceeded.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().BeNull();
        file.DisplayName.Should().Be("Untitled");
        getModel().Slides.Should().HaveCount(1);
    }

    [StaFact]
    public void DialogPlans_DefaultToPptxAndKeepLegacyFxpFilters()
    {
        var openPlan = GetOpenDialogPlan();
        openPlan.Filter.Should().Be(
            "PowerPoint presentations (*.pptx)|*.pptx|FreeP legacy presentations (*.fxp)|*.fxp|All files (*.*)|*.*");
        openPlan.DefaultExtensionWithDot.Should().Be(".pptx");

        var savePlan = BuildSaveAsDialogPlan(null);
        savePlan.SuggestedFileName.Should().Be("Presentation.pptx");
        savePlan.DefaultExtensionWithDot.Should().Be(".pptx");
        savePlan.DefaultExtensionWithoutDot.Should().Be("pptx");
        savePlan.FilterIndex.Should().Be(1);
        savePlan.Filter.Should().Be(openPlan.Filter);

        var legacySourcePlan = BuildSaveAsDialogPlan("Legacy.fxp");
        legacySourcePlan.SuggestedFileName.Should().Be("Legacy.pptx");
        legacySourcePlan.FilterIndex.Should().Be(1);
    }

    [StaFact]
    public void OpenPath_LoadsPptxFileAndMarksSavedWithPath()
    {
        var (_, file, getModel, _) = CreateHarness();
        var path = WritePptx("Opened.pptx", "Quarterly Review");

        var opened = file.OpenPath(path);

        opened.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
        file.DisplayName.Should().Be("Opened");
        getModel().Properties.Title.Should().Be("Quarterly Review");
    }

    [StaFact]
    public void OpenPath_StillLoadsLegacyFxpFileAndMarksSavedWithPath()
    {
        var (_, file, getModel, _) = CreateHarness();
        var path = WriteFxp("Legacy.fxp", "Legacy Review");

        var opened = file.OpenPath(path);

        opened.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
        file.DisplayName.Should().Be("Legacy");
        getModel().Properties.Title.Should().Be("Legacy Review");
    }

    [StaFact]
    public void Save_AfterEdit_WritesPptxToExistingPathAndClearsDirty()
    {
        var (_, file, getModel, _) = CreateHarness();
        var path = WritePptx("Deck.pptx", "Initial");
        file.OpenPath(path).Should().BeTrue();

        getModel().Properties.Title = "Updated";
        file.MarkDirty();
        file.IsDirty.Should().BeTrue();

        var saved = file.Save();

        saved.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
        PptxPackageReader.Read(path).Properties.Title.Should().Be("Updated");
    }

    [StaFact]
    public void Save_AfterEdit_StillWritesLegacyFxpToExistingPathAndClearsDirty()
    {
        var (_, file, getModel, _) = CreateHarness();
        var path = WriteFxp("Legacy.fxp", "Initial");
        file.OpenPath(path).Should().BeTrue();

        getModel().Properties.Title = "Updated Legacy";
        file.MarkDirty();
        file.IsDirty.Should().BeTrue();

        var saved = file.Save();

        saved.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
        FxpFormat.Read(path).Properties.Title.Should().Be("Updated Legacy");
    }

    [StaFact]
    public void Save_OnCleanOpenedPresentation_StaysClean()
    {
        var (_, file, _, _) = CreateHarness();
        var path = WritePptx("Clean.pptx", "Clean");
        file.OpenPath(path).Should().BeTrue();

        var saved = file.Save();

        saved.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
    }

    private string WritePptx(string name, string title)
    {
        var path = Path.Combine(_tempDir, name);
        PptxPackageWriter.Write(CreatePresentation(title), path);
        return path;
    }

    private string WriteFxp(string name, string title)
    {
        var path = Path.Combine(_tempDir, name);
        FxpFormat.Write(CreatePresentation(title), path);
        return path;
    }

    private static Presentation CreatePresentation(string title)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Properties.Title = title;
        return presentation;
    }

    private static FileOpenDialogPlan GetOpenDialogPlan()
    {
        var field = typeof(FileCommands).GetField(
            "OpenDialogPlan",
            BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        return (FileOpenDialogPlan)field!.GetValue(null)!;
    }

    private static FileSaveDialogPlan BuildSaveAsDialogPlan(string? currentPath)
    {
        var method = typeof(FileCommands).GetMethod(
            "BuildSaveAsDialogPlan",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (FileSaveDialogPlan)method!.Invoke(null, [currentPath])!;
    }
}
