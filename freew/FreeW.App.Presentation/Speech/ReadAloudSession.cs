using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Speech;

public sealed record ReadAloudSettings(
    string? VoiceName = null,
    int Rate = 0,
    int Volume = 100)
{
    public const int MinimumRate = -10;
    public const int MaximumRate = 10;
    public const int MinimumVolume = 0;
    public const int MaximumVolume = 100;

    public static ReadAloudSettings Default { get; } = new();
}

public static class ReadAloudSettingsNormalizer
{
    public static ReadAloudSettings Normalize(ReadAloudSettings? settings)
    {
        settings ??= ReadAloudSettings.Default;
        return new ReadAloudSettings(
            VoiceName: string.IsNullOrWhiteSpace(settings.VoiceName)
                ? null
                : settings.VoiceName.Trim(),
            Rate: Math.Clamp(
                settings.Rate,
                ReadAloudSettings.MinimumRate,
                ReadAloudSettings.MaximumRate),
            Volume: Math.Clamp(
                settings.Volume,
                ReadAloudSettings.MinimumVolume,
                ReadAloudSettings.MaximumVolume));
    }
}

public sealed record ReadAloudStartPlan(
    IReadOnlyList<ReadAloudSegment> Segments,
    int StartSegmentIndex,
    ReadAloudSettings Settings)
{
    public bool HasSpeakableContent => Segments.Count > 0;
}

public static class ReadAloudStartPlanner
{
    public static ReadAloudStartPlan Plan(
        TextDocument document,
        int requestedStartSegmentIndex,
        ReadAloudSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var segments = ReadAloudController.ExtractSegments(document);
        var startSegmentIndex = segments.Count == 0
            ? 0
            : Math.Clamp(requestedStartSegmentIndex, 0, segments.Count - 1);
        return new ReadAloudStartPlan(
            Segments: segments,
            StartSegmentIndex: startSegmentIndex,
            Settings: ReadAloudSettingsNormalizer.Normalize(settings));
    }
}

public readonly record struct ReadAloudCommandAvailability(
    bool IsEnabled,
    bool IsChecked,
    ReadAloudState State,
    bool CanStart,
    bool CanPause,
    bool CanResume,
    bool CanStop,
    bool CanMovePrevious,
    bool CanMoveNext)
{
    internal static ReadAloudCommandAvailability From(ReadAloudController? controller)
    {
        var state = controller?.State ?? ReadAloudState.Stopped;
        return new ReadAloudCommandAvailability(
            IsEnabled: true,
            IsChecked: state != ReadAloudState.Stopped,
            State: state,
            CanStart: state == ReadAloudState.Stopped,
            CanPause: controller?.CanPause == true,
            CanResume: controller?.CanResume == true,
            CanStop: controller?.CanStop == true,
            CanMovePrevious: controller?.CanMovePrevious == true,
            CanMoveNext: controller?.CanMoveNext == true);
    }
}

public sealed record ReadAloudSessionPorts(
    Func<TextDocument> GetDocument,
    Func<int> GetStartSegmentIndex,
    Func<ReadAloudSettings, ISpeechEngine> CreateEngine,
    Action? PrepareStart = null);

/// <summary>
/// Owns renderer-independent Read Aloud command and lifecycle policy. Renderers provide the current
/// document/caret and a native speech engine, then project state changes through their native dispatcher.
/// </summary>
public sealed class ReadAloudSession : IDisposable
{
    private readonly ReadAloudSessionPorts _ports;
    private ReadAloudSettings _settings;
    private ISpeechEngine? _engine;
    private ReadAloudController? _controller;
    private bool _disposed;

    public ReadAloudSession(
        ReadAloudSessionPorts ports,
        ReadAloudSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.GetDocument);
        ArgumentNullException.ThrowIfNull(ports.GetStartSegmentIndex);
        ArgumentNullException.ThrowIfNull(ports.CreateEngine);

        _ports = ports;
        _settings = ReadAloudSettingsNormalizer.Normalize(settings);
    }

    public event Action? StateChanged;

    public bool IsActive => _controller?.IsActive == true;

    public ReadAloudSettings Settings => _settings;

    public ReadAloudCommandAvailability CommandAvailability =>
        ReadAloudCommandAvailability.From(_controller);

    public void ToggleStartStop()
    {
        if (IsActive)
        {
            Stop();
            return;
        }

        Start();
    }

    public bool Start()
    {
        var controller = EnsureController();
        _ports.PrepareStart?.Invoke();
        var plan = ReadAloudStartPlanner.Plan(
            _ports.GetDocument(),
            _ports.GetStartSegmentIndex(),
            _settings);
        controller.Start(plan.Segments, plan.StartSegmentIndex);
        return controller.IsActive;
    }

    public bool Pause() => _controller?.Pause() == true;

    public bool Resume() => _controller?.Resume() == true;

    public void TogglePause() => _controller?.TogglePause();

    public bool MovePrevious() => _controller?.MovePrevious() == true;

    public bool MoveNext() => _controller?.MoveNext() == true;

    public void Stop() => _controller?.Stop();

    public bool HandleDocumentChanged()
    {
        if (!IsActive)
            return false;

        Stop();
        return true;
    }

    public bool ApplySettings(ReadAloudSettings? settings)
    {
        var normalized = ReadAloudSettingsNormalizer.Normalize(settings);
        if (normalized == _settings)
            return false;

        if (IsActive)
            Stop();
        ReleaseController();
        _settings = normalized;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseController();
    }

    private ReadAloudController EnsureController()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_controller is not null)
            return _controller;

        _engine = _ports.CreateEngine(_settings)
            ?? throw new InvalidOperationException("The Read Aloud engine factory returned null.");
        _controller = new ReadAloudController(_engine);
        _controller.StateChanged += OnControllerStateChanged;
        return _controller;
    }

    private void ReleaseController()
    {
        var controller = _controller;
        _controller = null;
        if (controller is not null)
        {
            controller.StateChanged -= OnControllerStateChanged;
            controller.Stop();
        }

        if (_engine is IDisposable disposable)
            disposable.Dispose();
        _engine = null;
    }

    private void OnControllerStateChanged() => StateChanged?.Invoke();
}

/// <summary>Shared ribbon projection over the portable Read Aloud session.</summary>
public sealed class FreeWReadAloudRibbonCommand : IRibbonStatefulCommand, IDisposable
{
    private readonly ReadAloudSession _session;

    public FreeWReadAloudRibbonCommand(ReadAloudSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _session.StateChanged += OnSessionStateChanged;
    }

    public event Action? StateChanged;

    public void Execute(RibbonCommandContext context) => _session.ToggleStartStop();

    public RibbonCommandState GetState()
    {
        var state = _session.CommandAvailability;
        return new RibbonCommandState(IsEnabled: state.IsEnabled, IsChecked: state.IsChecked);
    }

    public void Dispose()
    {
        _session.StateChanged -= OnSessionStateChanged;
        _session.Dispose();
    }

    private void OnSessionStateChanged() => StateChanged?.Invoke();
}
