using System.Windows.Threading;
using Free.Shared.Ribbon;
using FreeW.App.Host;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// STA tests for the host side of Review &gt; Read Aloud: the <see cref="SystemSpeechEngine"/> is robust
/// when constructed (it must never throw even with no installed voice and degrades to a deterministic
/// no-op), and <see cref="DocumentView.ReadAloudStartSegmentIndex"/> maps the caret to the matching
/// speakable segment. Speech itself isn't asserted (no audio in CI) — the engine/controller contract is
/// covered headlessly by the pure ReadAloudController tests; here we verify the WPF integration points.
/// </summary>
public sealed class ReadAloudTests
{
    private static DocumentView ViewWith(params string[] paragraphs)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        foreach (var text in paragraphs)
            doc.Blocks.Add(new Paragraph(text));

        var view = new DocumentView();
        view.LoadModel(doc);
        return view;
    }

    [StaFact]
    public void SystemSpeechEngine_ConstructsWithoutThrowing()
    {
        // Must not throw even on a machine with no TTS voice (construction guards the synthesizer).
        using var engine = new SystemSpeechEngine();
        Assert.NotNull(engine);
    }

    [StaFact]
    public void SystemSpeechEngine_DrivesControllerToCompletionRegardlessOfVoice()
    {
        // Whether or not a voice is installed, the engine must complete each utterance (a real one when a
        // voice exists, an immediate no-op otherwise) so the controller advances and finishes deterministically.
        using var engine = new SystemSpeechEngine();
        var controller = new ReadAloudController(engine);

        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Hello world."));

        controller.Start(doc);

        if (!engine.HasVoice)
        {
            // No voice: SpeakAsync posts each completion to the dispatcher. Pump the queue (a no-op
            // background invoke flushes pending BeginInvoke callbacks) until the read-through finishes.
            PumpDispatcher();
            Assert.Equal(ReadAloudState.Stopped, controller.State);
        }
        else
        {
            // A voice is present: speech is asynchronous; just confirm a read-through started cleanly and
            // can be stopped without error (we don't block the test waiting on real audio).
            Assert.True(controller.IsActive);
            controller.Stop();
            Assert.Equal(ReadAloudState.Stopped, controller.State);
        }
    }

    // Flushes pending dispatcher work (BeginInvoke completions) by draining the queue down to the lowest
    // priority a few times — enough for the controller to walk a small document to completion.
    private static void PumpDispatcher()
    {
        for (var i = 0; i < 16; i++)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
    }

    [StaFact]
    public void ReadAloudStartSegmentIndex_DefaultCaretIsFirstSegment()
    {
        var view = ViewWith("First", "Second", "Third");

        Assert.Equal(0, view.ReadAloudStartSegmentIndex());
    }

    [StaFact]
    public void ReadAloudStartSegmentIndex_EmptyBodyIsZero()
    {
        var view = ViewWith("   ");

        Assert.Equal(0, view.ReadAloudStartSegmentIndex());
    }

    [StaFact]
    public void WpfReadAloudCommand_OwnsADisposableSharedSessionAdapter()
    {
        var view = ViewWith("First");
        var commands = FreeWRibbonCommands.Build(view, new RibbonStateStore());

        Assert.True(commands.TryGet("freew.read-aloud", out var command));
        var lifetime = Assert.IsAssignableFrom<IDisposable>(command);

        lifetime.Dispose();
        lifetime.Dispose();
    }
}
