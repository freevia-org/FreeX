using System.Speech.Synthesis;
using System.Windows.Threading;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// The Windows host's <see cref="ISpeechEngine"/> for Review &gt; Read Aloud, backed by the in-box
/// <see cref="SpeechSynthesizer"/> from <c>System.Speech</c> (no cloud, no extra NuGet). Speech is local and
/// asynchronous (<see cref="SpeechSynthesizer.SpeakAsync(string)"/>); the controller's per-segment completion
/// callback is fired from <see cref="SpeechSynthesizer.SpeakCompleted"/>, but only for utterances that finish
/// naturally — a <see cref="Stop"/> cancels the in-flight prompt and suppresses that segment's callback so the
/// <see cref="ReadAloudController"/> does not advance.
///
/// <para><b>Robust with no voice installed.</b> Construction never throws even on a machine with no TTS voice:
/// the synthesizer is created lazily and guarded, and <see cref="HasVoice"/> reports whether any installed
/// voice is available. When none is, the engine degrades to a no-op (speak immediately "completes") rather
/// than crashing, so the ribbon command stays safe to invoke.</para>
///
/// Completion is marshalled back onto the UI dispatcher so the controller (and any ribbon-state / highlight
/// updates it triggers) runs on the UI thread.
/// </summary>
public sealed class SystemSpeechEngine : ISpeechEngine, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly SpeechSynthesizer? _synthesizer;

    // The completion callback for the utterance currently in flight. Cleared on Stop so a cancelled prompt's
    // SpeakCompleted (which fires with Cancelled == true) never advances the controller.
    private Action? _pendingCompleted;
    private bool _disposed;

    public SystemSpeechEngine()
        : this(Dispatcher.CurrentDispatcher)
    {
    }

    public SystemSpeechEngine(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;

        // Creating the synthesizer (or enumerating voices) can throw on a misconfigured/headless machine.
        // Never let that bubble out of construction — degrade to the no-voice no-op path instead.
        try
        {
            var synthesizer = new SpeechSynthesizer();
            synthesizer.SetOutputToDefaultAudioDevice();
            synthesizer.SpeakCompleted += OnSpeakCompleted;
            _synthesizer = synthesizer;
        }
        catch (Exception)
        {
            _synthesizer = null;
        }
    }

    public bool SupportsPause => true;

    /// <summary>True when a usable TTS voice is installed and the synthesizer initialised successfully.</summary>
    public bool HasVoice
    {
        get
        {
            if (_synthesizer is null)
                return false;
            try
            {
                return _synthesizer.GetInstalledVoices().Any(v => v.Enabled);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public void SpeakAsync(string text, Action onCompleted)
    {
        ArgumentNullException.ThrowIfNull(onCompleted);

        // No voice (or no synthesizer): treat the utterance as instantly complete so the controller still
        // advances/finishes deterministically instead of stalling. Marshalled to keep ordering on the UI thread.
        if (_synthesizer is null || !HasVoice || string.IsNullOrEmpty(text))
        {
            _pendingCompleted = null;
            _dispatcher.BeginInvoke(onCompleted);
            return;
        }

        _pendingCompleted = onCompleted;
        try
        {
            _synthesizer.SpeakAsyncCancelAll();
            _synthesizer.SpeakAsync(text);
        }
        catch (Exception)
        {
            // If the engine refuses the prompt, fail soft: complete the segment so the read-through proceeds.
            _pendingCompleted = null;
            _dispatcher.BeginInvoke(onCompleted);
        }
    }

    public void Pause()
    {
        try
        {
            _synthesizer?.Pause();
        }
        catch (Exception)
        {
            // Best-effort; pausing is non-essential and must never crash the app.
        }
    }

    public void Resume()
    {
        try
        {
            _synthesizer?.Resume();
        }
        catch (Exception)
        {
            // Best-effort.
        }
    }

    public void Stop()
    {
        // Drop the pending callback FIRST so the cancellation-driven SpeakCompleted does not advance the
        // controller, then cancel the in-flight prompt.
        _pendingCompleted = null;
        try
        {
            _synthesizer?.SpeakAsyncCancelAll();
        }
        catch (Exception)
        {
            // Best-effort.
        }
    }

    private void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e)
    {
        // Only natural completion advances the controller. Cancelled prompts (from Stop / a new SpeakAsync)
        // leave _pendingCompleted null, so we ignore them.
        if (e.Cancelled)
            return;

        var callback = _pendingCompleted;
        _pendingCompleted = null;
        if (callback is null)
            return;

        if (_dispatcher.CheckAccess())
            callback();
        else
            _dispatcher.BeginInvoke(callback);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_synthesizer is null)
            return;

        try
        {
            _synthesizer.SpeakCompleted -= OnSpeakCompleted;
            _synthesizer.SpeakAsyncCancelAll();
            _synthesizer.Dispose();
        }
        catch (Exception)
        {
            // Best-effort cleanup.
        }
    }
}
