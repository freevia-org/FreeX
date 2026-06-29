using System.Collections.Generic;
using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
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

    private (
        Window Window,
        FileCommands File,
        Func<Presentation> GetModel,
        Func<int> ChangeCount,
        RecordingUserMessageService Messages) CreateHarness()
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var model = Presentation.CreateEmpty();
        var changes = 0;
        var recentStorePath = Path.Combine(_tempDir, "recent.json");
        var messages = new RecordingUserMessageService();
        var file = new FileCommands(
            window,
            () => model,
            loaded => model = loaded,
            () => changes++,
            loadRecentFilesStore: () => RecentFilesStore.Load(recentStorePath),
            messageService: messages);
        return (window, file, () => model, () => changes, messages);
    }

    [StaFact]
    public void FreshPresentation_IsCleanWithUntitledName()
    {
        var (_, file, _, _, _) = CreateHarness();

        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().BeNull();
        file.DisplayName.Should().Be("Untitled");
    }

    [StaFact]
    public void MarkDirty_SetsDirtyAndNotifiesOnce()
    {
        var (_, file, _, changeCount, _) = CreateHarness();

        file.MarkDirty();
        file.IsDirty.Should().BeTrue();
        changeCount().Should().Be(1);

        file.MarkDirty();
        changeCount().Should().Be(1);
    }

    [StaFact]
    public void New_OnCleanPresentation_ProceedsWithoutPromptAndResetsState()
    {
        var (_, file, getModel, _, _) = CreateHarness();
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
    public void New_OnDirtyPresentation_UsesInjectedMessageServiceForSavePrompt()
    {
        var (_, file, _, _, messages) = CreateHarness();
        messages.NextResult = UserMessageResult.No;

        file.MarkDirty();
        var proceeded = file.New();

        proceeded.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        messages.Messages.Should().ContainSingle();
        var prompt = messages.Messages[0];
        prompt.Message.Should().Be(
            "Do you want to save changes to Untitled before creating a new presentation?");
        prompt.Title.Should().Be("FreeP");
        prompt.Buttons.Should().Be(UserMessageButtons.YesNoCancel);
        prompt.Icon.Should().Be(UserMessageIcon.Warning);
    }

    [StaFact]
    public void DialogPlans_DefaultToPptxAndKeepLegacyFxpFilters()
    {
        var openPlan = PresentationFileDialogPlanner.BuildOpenDialogPlan();
        openPlan.Filter.Should().Be(
            "PowerPoint presentations (*.pptx)|*.pptx|FreeP legacy presentations (*.fxp)|*.fxp|All files (*.*)|*.*");
        openPlan.DefaultExtensionWithDot.Should().Be(".pptx");

        var savePlan = PresentationFileDialogPlanner.BuildSaveAsDialogPlan(null);
        savePlan.SuggestedFileName.Should().Be("Presentation.pptx");
        savePlan.DefaultExtensionWithDot.Should().Be(".pptx");
        savePlan.DefaultExtensionWithoutDot.Should().Be("pptx");
        savePlan.FilterIndex.Should().Be(1);
        savePlan.Filter.Should().Be(openPlan.Filter);

        var legacySourcePlan = PresentationFileDialogPlanner.BuildSaveAsDialogPlan("Legacy.fxp");
        legacySourcePlan.SuggestedFileName.Should().Be("Legacy.pptx");
        legacySourcePlan.FilterIndex.Should().Be(1);
    }

    [StaFact]
    public void OpenPath_LoadsPptxFileAndMarksSavedWithPath()
    {
        var (_, file, getModel, _, _) = CreateHarness();
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
        var (_, file, getModel, _, _) = CreateHarness();
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
        var (_, file, getModel, _, _) = CreateHarness();
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
        var (_, file, getModel, _, _) = CreateHarness();
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
        var (_, file, _, _, _) = CreateHarness();
        var path = WritePptx("Clean.pptx", "Clean");
        file.OpenPath(path).Should().BeTrue();

        var saved = file.Save();

        saved.Should().BeTrue();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().Be(path);
    }

    [StaFact]
    public void OpenPath_CorruptPresentation_UsesInjectedMessageServiceForError()
    {
        var (_, file, _, _, messages) = CreateHarness();
        var path = Path.Combine(_tempDir, "corrupt.pptx");
        File.WriteAllText(path, "this is not a valid pptx");

        var opened = file.OpenPath(path);

        opened.Should().BeFalse();
        file.IsDirty.Should().BeFalse();
        file.CurrentPath.Should().BeNull();
        messages.Messages.Should().ContainSingle();
        var error = messages.Messages[0];
        error.Message.Should().StartWith("Could not open the presentation:\n");
        error.Title.Should().Be("FreeP");
        error.Buttons.Should().Be(UserMessageButtons.Ok);
        error.Icon.Should().Be(UserMessageIcon.Error);
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

    private sealed class RecordingUserMessageService : IUserMessageService
    {
        public UserMessageResult NextResult { get; set; } = UserMessageResult.Ok;

        public List<MessageCall> Messages { get; } = new();

        public void ShowError(string message, string title = "Error") =>
            ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Error);

        public void ShowWarning(string message, string title = "Warning") =>
            ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Warning);

        public void ShowInfo(string message, string title = "Information") =>
            ShowMessage(message, title, UserMessageButtons.Ok, UserMessageIcon.Information);

        public bool AskYesNo(string message, string title = "Confirm") =>
            ShowMessage(message, title, UserMessageButtons.YesNo, UserMessageIcon.Question) == UserMessageResult.Yes;

        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon)
        {
            Messages.Add(new MessageCall(message, title, buttons, icon));
            return NextResult;
        }
    }

    private sealed class MessageCall
    {
        public MessageCall(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon)
        {
            Message = message;
            Title = title;
            Buttons = buttons;
            Icon = icon;
        }

        public string Message { get; }

        public string Title { get; }

        public UserMessageButtons Buttons { get; }

        public UserMessageIcon Icon { get; }
    }
}
