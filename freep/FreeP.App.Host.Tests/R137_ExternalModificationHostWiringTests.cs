using System.IO;
using System.Windows;
using Free.Shared.AppServices;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.App.Recording;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// r137-remediation2, host level: proves FreeP's WPF shell reaches the external-modification guard
/// through its own entry points. <see cref="PresentationFileCommandSessionTests"/> pins the session
/// mechanism; these pin the WIRING that the WPF session factory installs -- the overwrite prompt
/// reaching the injected <see cref="IUserMessageService"/> instead of the file being clobbered.
/// </summary>
public sealed class R137_ExternalModificationHostWiringTests : IDisposable
{
    private readonly TestTemporaryDirectory _temporaryDirectory = new("FreeP.R137ExternalModification-");

    private string TempDirectory => _temporaryDirectory.Path;

    public void Dispose() => _temporaryDirectory.Dispose();

    [StaFact]
    public void Save_AfterAnotherProgramChangedTheFile_PromptsAndDeclinedLeavesTheOtherWriterIntact()
    {
        var (file, getModel, messages) = CreateHarness();
        var path = WritePptx("Shared.pptx", "original");
        Run(file.OpenPathAsync(path)).Should().BeTrue();

        getModel().Properties.Title = "my edit";
        file.MarkDirty();
        WriteExternalChange(path, "someone else's edit");
        messages.NextResult = UserMessageResult.No;

        var saved = Run(file.SaveAsync());

        saved.Should().BeFalse();
        messages.Messages.Should().ContainSingle();
        messages.Messages[0].Buttons.Should().Be(UserMessageButtons.YesNo);
        messages.Messages[0].Icon.Should().Be(UserMessageIcon.Warning);
        messages.Messages[0].Title.Should().Be("FreeP");
        messages.Messages[0].Message.Should().Contain("Shared.pptx");
        PptxPackageReader.Read(path).Properties.Title.Should().Be(
            "someone else's edit",
            "a declined overwrite must never clobber the other writer's changes");
    }

    [StaFact]
    public void Save_AfterAnotherProgramChangedTheFile_ConfirmedOverwritesAndRebasesTheBaseline()
    {
        var (file, getModel, messages) = CreateHarness();
        var path = WritePptx("Shared.pptx", "original");
        Run(file.OpenPathAsync(path)).Should().BeTrue();

        getModel().Properties.Title = "my edit";
        file.MarkDirty();
        WriteExternalChange(path, "someone else's edit");
        messages.NextResult = UserMessageResult.Yes;

        Run(file.SaveAsync()).Should().BeTrue();

        messages.Messages.Should().ContainSingle();
        PptxPackageReader.Read(path).Properties.Title.Should().Be("my edit");

        // The successful save rebased the tracked write time, so an immediate second save must not
        // re-prompt.
        getModel().Properties.Title = "my second edit";
        file.MarkDirty();

        Run(file.SaveAsync()).Should().BeTrue();

        messages.Messages.Should().ContainSingle();
        PptxPackageReader.Read(path).Properties.Title.Should().Be("my second edit");
    }

    [StaFact]
    public void Save_WhenTheFileWasNotChanged_NeverPrompts()
    {
        var (file, getModel, messages) = CreateHarness();
        var path = WritePptx("Untouched.pptx", "original");
        Run(file.OpenPathAsync(path)).Should().BeTrue();

        getModel().Properties.Title = "my edit";
        file.MarkDirty();

        Run(file.SaveAsync()).Should().BeTrue();

        messages.Messages.Should().BeEmpty();
        PptxPackageReader.Read(path).Properties.Title.Should().Be("my edit");
    }

    // Close-with-save reaches the same guarded Save, so a second writer is caught there too rather
    // than only on an explicit File > Save.
    [StaFact]
    public void ConfirmCloseAllowed_SavingOnClose_StillAsksBeforeOverwritingAnExternalChange()
    {
        var (file, getModel, messages) = CreateHarness();
        var path = WritePptx("Closing.pptx", "original");
        Run(file.OpenPathAsync(path)).Should().BeTrue();

        getModel().Properties.Title = "my edit";
        file.MarkDirty();
        WriteExternalChange(path, "someone else's edit");
        // Yes to "save changes before closing", then Yes to "overwrite the other writer".
        messages.NextResult = UserMessageResult.Yes;

        file.ConfirmCloseAllowedAsync().GetAwaiter().GetResult().Should().BeTrue();

        messages.Messages.Should().HaveCount(2);
        messages.Messages[0].Buttons.Should().Be(UserMessageButtons.YesNoCancel);
        messages.Messages[1].Buttons.Should().Be(UserMessageButtons.YesNo);
        messages.Messages[1].Message.Should().Contain("Closing.pptx");
        PptxPackageReader.Read(path).Properties.Title.Should().Be("my edit");
    }

    private (
        PresentationFileCommandSession File,
        Func<Presentation> GetModel,
        RecordingUserMessageService Messages) CreateHarness()
    {
        var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false, Left = -10000, Top = -10000 };
        var model = Presentation.CreateEmpty();
        var messages = new RecordingUserMessageService();
        var file = WpfPresentationFileCommandSessionFactory.Create(
            window,
            () => model,
            loaded => model = loaded,
            () => { },
            loadRecentFilesStore: () => RecentFilesStore.Load(Path.Combine(TempDirectory, "recent.json")),
            messageService: messages,
            videoEncoderCapability: LinuxVideoEncoderCapability.Unavailable("Test encoder handoff deferred."),
            nativePrintCapability: PresentationNativePrintHandoffHostCapabilities.Deferred(
                "WPF print host",
                "Test printer handoff deferred."));
        return (file, () => model, messages);
    }

    private static bool Run(Task<PresentationFileCommandResult> command) =>
        command.GetAwaiter().GetResult().Succeeded;

    private string WritePptx(string name, string title)
    {
        var path = Path.Combine(TempDirectory, name);
        PptxPackageWriter.Write(CreatePresentation(title), path);
        return path;
    }

    /// <summary>
    /// Simulates a second writer (another FreeP/PowerPoint instance, a sync client, a colleague on
    /// a shared path) touching the file after it was opened, with a real mtime change.
    /// </summary>
    private static void WriteExternalChange(string path, string title)
    {
        var beforeWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        PptxPackageWriter.Write(CreatePresentation(title), path);
        File.SetLastWriteTimeUtc(path, beforeWriteTimeUtc + TimeSpan.FromMinutes(1));
    }

    private static Presentation CreatePresentation(string title)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Properties.Title = title;
        return presentation;
    }
}
