using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "FreeP.Avalonia.StartupAttachmentTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var deckPath = Path.Combine(tempDir, "startup.pptx");
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

        try
        {
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

                    window.Editor.InsertSlide();
                    window.IsDirty.Should().BeTrue();
                },
                CancellationToken.None);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

}
