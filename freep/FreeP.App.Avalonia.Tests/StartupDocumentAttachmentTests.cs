using Avalonia.Headless;
using Avalonia.Threading;
using FreeP.App.Avalonia;
using FreeP.Core.IO;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class StartupDocumentAttachmentTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    [Fact]
    public async Task Startup_document_stays_clean_after_window_attachment_and_settling_but_edits_are_dirty()
    {
        using var temporaryDirectory = new TestTemporaryDirectory("FreeP.Avalonia.StartupAttachmentTests-");
        var deckPath = Path.Combine(temporaryDirectory.Path, "startup.pptx");
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Notes = new TextBody
        {
            Paragraphs =
            {
                new Paragraph { Runs = { new Run { Text = "Speaker notes loaded at startup" } } },
            },
        };
        using (var stream = File.Create(deckPath))
            PptxPackageWriter.Write(presentation, stream);

        await Session.Dispatch(
                async () =>
                {
                    var window = new MainWindow([deckPath]);
                    window.Show();

                    // Exercise the same attach/measure/render/dispatcher settling window as a
                    // real desktop launch, rather than checking constructor state only.
                    Dispatcher.UIThread.RunJobs();
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    Dispatcher.UIThread.RunJobs();

                    window.IsDirty.Should().BeFalse();
                    window.DirtyGeneration.Should().Be(0);

                    // Avalonia may raise a late TextChanged while attaching the notes TextBox.
                    // Replaying the loaded value is a no-op and must not create an edit.
                    window.NotesPaneForAccessibilityTests.Text.Should().Be("Speaker notes loaded at startup");
                    window.NotesPaneForAccessibilityTests.Text = "Speaker notes loaded at startup";
                    Dispatcher.UIThread.RunJobs();
                    window.IsDirty.Should().BeFalse();
                    window.DirtyGeneration.Should().Be(0);

                    window.NotesPaneForAccessibilityTests.Text = "A genuine post-startup notes edit";
                    Dispatcher.UIThread.RunJobs();
                    window.IsDirty.Should().BeTrue();
                    window.DirtyGeneration.Should().Be(1);

                    window.Editor.InsertSlide();
                    window.IsDirty.Should().BeTrue();
                    window.StartupDirtyTraceForTests.Should().BeEmpty();
                },
            CancellationToken.None);
    }
}
