namespace FreeP.App.Recording.Tests;

public sealed class PresentationVideoMediaMuxPlannerTests
{
    private const string ConcatPath = "concat.txt";
    private const string OutputPath = "output.mp4";
    private const string Encoder = "libx264";

    private static PresentationVideoMediaMuxPlan PlanWithNarration(
        TimeSpan videoDuration,
        TimeSpan narrationDuration) =>
        new(
            NarrationTracks: [new PresentationVideoMediaMuxTrack(
                PresentationRecordingMediaArtifactKind.NarrationAudio,
                "narration-0000.audio",
                TimeSpan.Zero,
                narrationDuration)],
            CameraTracks: [],
            CaptionTracks: [],
            VideoDuration: videoDuration);

    private static string ValueAfter(IReadOnlyList<string> arguments, string flag)
    {
        var list = arguments.ToList();
        var index = list.IndexOf(flag);
        index.Should().BeGreaterThanOrEqualTo(0, $"expected '{flag}' to be present in the ffmpeg arguments");
        return list[index + 1];
    }

    [Fact]
    public void Narration_shorter_than_video_does_not_truncate_the_export_to_narration_length()
    {
        // 60s of slides, 20s of narration: the whole 60s deck must still be exported.
        var mediaPlan = PlanWithNarration(
            videoDuration: TimeSpan.FromSeconds(60),
            narrationDuration: TimeSpan.FromSeconds(20));

        var arguments = PresentationVideoMediaMuxPlanner.BuildFfmpegArguments(
            ConcatPath, OutputPath, Encoder, mediaPlan);

        arguments.Should().NotContain("-shortest",
            "-shortest would truncate the output to the shorter narration track, silently dropping video content");
        ValueAfter(arguments, "-t").Should().Be("60");
    }

    [Fact]
    public void Narration_longer_than_video_trims_the_export_to_the_video_timeline()
    {
        // 20s of slides, 60s of narration: output should be pinned to the 20s video, not extend to 60s.
        var mediaPlan = PlanWithNarration(
            videoDuration: TimeSpan.FromSeconds(20),
            narrationDuration: TimeSpan.FromSeconds(60));

        var arguments = PresentationVideoMediaMuxPlanner.BuildFfmpegArguments(
            ConcatPath, OutputPath, Encoder, mediaPlan);

        arguments.Should().NotContain("-shortest");
        ValueAfter(arguments, "-t").Should().Be("20");
    }

    [Fact]
    public void Narration_equal_to_video_pins_duration_to_the_shared_length()
    {
        var mediaPlan = PlanWithNarration(
            videoDuration: TimeSpan.FromSeconds(30),
            narrationDuration: TimeSpan.FromSeconds(30));

        var arguments = PresentationVideoMediaMuxPlanner.BuildFfmpegArguments(
            ConcatPath, OutputPath, Encoder, mediaPlan);

        arguments.Should().NotContain("-shortest");
        ValueAfter(arguments, "-t").Should().Be("30");
    }

    [Fact]
    public void Narration_absent_adds_no_shortest_or_duration_clamp_and_disables_audio()
    {
        var mediaPlan = new PresentationVideoMediaMuxPlan(
            NarrationTracks: [],
            CameraTracks: [],
            CaptionTracks: [],
            VideoDuration: TimeSpan.FromSeconds(45));

        var arguments = PresentationVideoMediaMuxPlanner.BuildFfmpegArguments(
            ConcatPath, OutputPath, Encoder, mediaPlan);

        arguments.Should().NotContain("-shortest");
        arguments.Should().NotContain("-t");
        arguments.Should().Contain("-an");
    }
}
