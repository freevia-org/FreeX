using System.Runtime.InteropServices;
using System.Text.Json;
using FreeP.App.Media;

var factory = new LibVlcMediaPlaybackBackendFactory();
var availability = factory.Probe();
var nativeCreated = factory.TryCreate(out var backend, out var failure);
var states = new List<string>();
var playbackSignal = new ManualResetEventSlim();
var ended = false;
var sessionCreated = false;
var openSucceeded = false;
var playObserved = false;
var seekSucceeded = false;
var stopSucceeded = false;
MediaPlaybackFailure? sessionFailure = null;

try
{
    if (nativeCreated && backend is not null)
    {
        using var session = backend.CreateSession();
        sessionCreated = true;
        session.StateChanged += (_, state) =>
        {
            states.Add(state.ToString());
            if (state is MediaPlaybackState.Playing or MediaPlaybackState.Ended or MediaPlaybackState.Failed)
                playbackSignal.Set();
        };
        session.Ended += (_, _) =>
        {
            ended = true;
            playbackSignal.Set();
        };
        session.Failed += (_, playbackFailure) =>
        {
            sessionFailure = playbackFailure;
            playbackSignal.Set();
        };

        session.Open(MediaPlaybackSource.FromBytes(CreateWav(), "audio/wav", isVideo: false));
        openSucceeded = session.State is not MediaPlaybackState.Failed;
        session.Volume = 35;
        seekSucceeded = session.Capabilities.Seek && session.Seek(TimeSpan.FromMilliseconds(20));
        session.Play();
        playbackSignal.Wait(TimeSpan.FromSeconds(5));
        playObserved = states.Contains(MediaPlaybackState.Playing.ToString())
            || states.Contains(MediaPlaybackState.Ended.ToString());
        session.Stop();
        stopSucceeded = session.State == MediaPlaybackState.Stopped;
    }
}
finally
{
    backend?.Dispose();
}

var report = new
{
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.OSArchitecture.ToString(),
    backend = availability.Capabilities.BackendName,
    isAvailable = availability.IsAvailable && nativeCreated,
    availability.Capabilities.Audio,
    availability.Capabilities.Video,
    availability.Capabilities.VideoSurface,
    availability.Capabilities.Seek,
    availability.Capabilities.Volume,
    failureReason = availability.FailureReason,
    failureKind = failure?.Kind.ToString(),
    failureException = failure?.Exception?.ToString(),
    sessionCreated,
    openSucceeded,
    playObserved,
    seekSucceeded,
    stopSucceeded,
    ended,
    states,
    sessionFailure = sessionFailure?.Message,
};

Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return report.isAvailable
    && report.sessionCreated
    && report.openSucceeded
    && report.playObserved
    && report.seekSucceeded
    && report.stopSucceeded
    && report.sessionFailure is null
    ? 0
    : 2;

static byte[] CreateWav()
{
    const int sampleRate = 8000;
    const short channels = 1;
    const short bitsPerSample = 16;
    const int sampleCount = 1600;
    var dataLength = sampleCount * channels * (bitsPerSample / 8);
    using var stream = new MemoryStream(44 + dataLength);
    using var writer = new BinaryWriter(stream);
    writer.Write("RIFF"u8.ToArray());
    writer.Write(36 + dataLength);
    writer.Write("WAVE"u8.ToArray());
    writer.Write("fmt "u8.ToArray());
    writer.Write(16);
    writer.Write((short)1);
    writer.Write(channels);
    writer.Write(sampleRate);
    writer.Write(sampleRate * channels * (bitsPerSample / 8));
    writer.Write((short)(channels * (bitsPerSample / 8)));
    writer.Write(bitsPerSample);
    writer.Write("data"u8.ToArray());
    writer.Write(dataLength);
    writer.Write(new byte[dataLength]);
    return stream.ToArray();
}
