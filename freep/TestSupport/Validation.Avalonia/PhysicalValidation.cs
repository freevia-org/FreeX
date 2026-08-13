using System.Text.Json;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
using Free.Shared.Drawing;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.Core.Model;

namespace FreeP.Validation.Avalonia;

internal sealed record PhysicalValidationOptions(string OutputDirectory)
{
    private const string OutputDirectoryKey = "outputDirectory";
    public const string Argument = "--physical-validation";

    private static readonly CommandLineValueOptionSpec OutputDirectoryOption = new(
        OutputDirectoryKey,
        Argument,
        $"{Argument} requires one non-empty output directory and may appear once.",
        $"{Argument} requires one non-empty output directory and may appear once.",
        $"{Argument} requires one non-empty output directory and may appear once.",
        AllowEqualsSyntax: true);

    public static bool TryParse(
        IReadOnlyList<string> args,
        out PhysicalValidationOptions? options,
        out string[] startupArguments,
        out string? error)
    {
        var parsed = CommandLineValueOptionParser.Parse(args, [OutputDirectoryOption]);
        options = parsed.Error is null && parsed.IsPresent(OutputDirectoryKey)
            ? new PhysicalValidationOptions(parsed.Value(OutputDirectoryKey)!)
            : null;
        startupArguments = parsed.RemainingArguments;
        error = parsed.Error;
        return parsed.Error is null;
    }
}

internal sealed record PhysicalValidationRow(
    string Id,
    string Status,
    string EvidenceLevel,
    IReadOnlyList<string> Evidence,
    string Note);

internal sealed record PhysicalValidationManifest(
    int SchemaVersion,
    string Suite,
    string Platform,
    string Shell,
    string App,
    string CupsMode,
    string? CupsQueue,
    string? FfmpegPath,
    string? FfprobePath,
    IReadOnlyDictionary<string, int> Summary,
    IReadOnlyList<PhysicalValidationRow> Results);

internal static class PhysicalValidationCoordinator
{
    private static readonly IProcessRunner ProcessRunner = new SystemProcessRunner();
    private static readonly JsonSerializerOptions JsonOptions =
        JsonArtifactIO.CreateSerializerOptions(ignoreNullValues: true);

    public static void Start(MainWindow.ValidationAccessAdapter access, PhysicalValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(options);
        access.StartWhenOpened(() => RunAsync(access, options));
    }

    private static async Task RunAsync(MainWindow.ValidationAccessAdapter access, PhysicalValidationOptions options)
    {
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var rows = new List<PhysicalValidationRow>();
        var screenshots = new List<string>();
        SlideShowWindow.ValidationAccessAdapter? mediaShowForCleanup = null;
        string? ffmpegPath = null;
        string? ffprobePath = null;
        string? cupsQueue = null;
        try
        {
            var capabilities = await WaitForCapabilitiesAsync(access);
            var printerDiscovery = await access.DiscoverPrintersAsync();
            ffmpegPath = capabilities.Video.ExecutablePath;
            ffprobePath = FindExecutable("ffprobe");
            cupsQueue = printerDiscovery.DefaultPrinter;

            await CaptureAsync(outputDirectory, "owner-before.png");
            screenshots.Add("owner-before.png");
            AddRow(
                rows,
                "capability.ffmpeg-ffprobe",
                capabilities.Video.CanEncodeMp4 && ffprobePath is not null ? "passed" : "not-proven",
                ["owner-before.png"],
                capabilities.Video.CanEncodeMp4 && ffprobePath is not null
                    ? $"Linux capability detector found ffmpeg encoder '{capabilities.Video.EncoderName}' and ffprobe."
                    : $"Video capability: {capabilities.Video.Reason}; ffprobe={(ffprobePath is null ? "missing" : "found")}." );

            access.InsertSlide();
            access.InsertSlide();
            var slideshow = await access.ShowSlideShowAsync();
            await Task.Delay(350);
            await CaptureAsync(outputDirectory, "slideshow-open.png");
            screenshots.Add("slideshow-open.png");
            AddRow(
                rows,
                "slideshow.open-and-render",
                slideshow.IsVisible && slideshow.CurrentSlideIndex == 0 ? "passed" : "failed",
                ["slideshow-open.png"],
                $"Visible={slideshow.IsVisible}; currentSlide={slideshow.CurrentSlideIndex}; slideCount={access.SlideCount}.");

            var initialSlide = slideshow.CurrentSlideIndex;
            var advance = slideshow.Advance();
            await Task.Delay(250);
            await CaptureAsync(outputDirectory, "slideshow-advanced.png");
            screenshots.Add("slideshow-advanced.png");
            AddRow(
                rows,
                "slideshow.advance",
                slideshow.CurrentSlideIndex == initialSlide + 1 ? "passed" : "failed",
                ["slideshow-advanced.png"],
                $"Advance result={advance}; currentSlide={slideshow.CurrentSlideIndex}.");

            slideshow.Close();
            await Task.Delay(150);

            var videoPath = Path.Combine(outputDirectory, "exported-video.mp4");
            var videoResult = capabilities.Video.CanEncodeMp4
                ? await access.ExecuteVideoExportAsync(
                    videoPath,
                    new PresentationVideoExportRequest(
                        Quality: PresentationVideoQualityKind.Standard,
                        SecondsPerSlide: 0.25,
                        IncludeNarration: false))
                : LinuxVideoExportResult.Failed(capabilities.Video.Reason, videoPath);
            if (videoResult.Succeeded)
            {
                AddRow(
                    rows,
                    "export.ffmpeg-dispatch",
                    "passed",
                    ["exported-video.mp4"],
                    $"MainWindow.ExecuteVideoExportAsync dispatched the LinuxVideoExportAdapter with encoder '{videoResult.EncoderName}'. bytes={videoResult.ByteCount}.");
                if (ffprobePath is null)
                {
                    AddRow(
                        rows,
                        "export.ffprobe-validation",
                        "not-proven",
                        ["exported-video.mp4"],
                        "ffprobe was not available in the Linux image.");
                }
                else
                {
                    var ffprobe = await ProcessRunner.RunAsync(new ProcessInvocation(
                        ffprobePath,
                        ["-v", "error", "-show_entries", "format=format_name,duration:stream=codec_name,codec_type", "-of", "json", videoPath]));
                    File.WriteAllText(Path.Combine(outputDirectory, "ffprobe.json"), ffprobe.StandardOutput);
                    var ffprobeValid = ffprobe.ExitCode == 0 &&
                        ffprobe.StandardOutput.Contains("mp4", StringComparison.OrdinalIgnoreCase) &&
                        ffprobe.StandardOutput.Contains("codec_type", StringComparison.OrdinalIgnoreCase);
                    AddRow(
                        rows,
                        "export.ffprobe-validation",
                        ffprobeValid ? "passed" : "failed",
                        ["exported-video.mp4", "ffprobe.json"],
                        $"ffprobe exit={ffprobe.ExitCode}; output={ffprobe.StandardError.Trim()}.");
                }

                AddValidationVideo(access.Presentation, await File.ReadAllBytesAsync(videoPath));
                var mediaShow = await access.ShowSlideShowAsync();
                mediaShowForCleanup = mediaShow;
                await Task.Delay(900);
                await CaptureAsync(outputDirectory, "media-playback.png");
                screenshots.Add("media-playback.png");
                var media = mediaShow.CaptureMediaPlayback();
                AddRow(
                    rows,
                    "media.libvlc-playback",
                    media.IsAvailable == true && media.ActiveMediaCount > 0 && !media.HasFailure
                        ? "passed"
                        : "not-proven",
                    ["media-playback.png"],
                    media.IsAvailable == true
                        ? $"LibVLC opened the exported MP4 through the production slideshow media controller; activeMedia={media.ActiveMediaCount}."
                        : $"LibVLC media playback was unavailable: {media.FailureReason ?? "no availability report"}.");
            }
            else
            {
                AddRow(rows, "export.ffmpeg-dispatch", "not-proven", ["owner-before.png"], videoResult.FailureReason ?? "Linux video export was unavailable.");
                AddRow(rows, "export.ffprobe-validation", "not-proven", ["owner-before.png"], "No MP4 was produced for ffprobe validation.");
                AddRow(rows, "media.libvlc-playback", "not-proven", ["owner-before.png"], "Media playback requires a successfully exported MP4.");
            }

            var printMode = Environment.GetEnvironmentVariable("FREEX_CUPS_DRY_RUN_MODE") ?? "success";
            var printResult = await access.ExecutePrintAsync(
                new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides));
            var submittedPath = "/work/cups-dry-run/last-submitted.pdf";
            var invocationPath = "/work/cups-dry-run/last-invocation.txt";
            var submitted = File.Exists(submittedPath) && new FileInfo(submittedPath).Length > 0;
            if (submitted)
            {
                File.Copy(submittedPath, Path.Combine(outputDirectory, "cups-submitted.pdf"), overwrite: true);
                File.Copy(invocationPath, Path.Combine(outputDirectory, "cups-invocation.txt"), overwrite: true);
            }
            var expectedPrintFailure = string.Equals(printMode, "failure", StringComparison.OrdinalIgnoreCase);
            var printPassed = expectedPrintFailure
                ? !printResult.Succeeded && !submitted
                : printResult.Succeeded && submitted;
            AddRow(
                rows,
                expectedPrintFailure ? "print.cups-dry-run-rejection" : "print.cups-dry-run-submission",
                printPassed ? "passed" : "failed",
                submitted
                    ? ["cups-submitted.pdf", "cups-invocation.txt"]
                    : ["owner-before.png"],
                $"mode={printMode}; result={PresentationNativeCommandOutcomePlanner.BuildPrintStatusText(printResult)}; succeeded={printResult.Succeeded}; submittedPdf={submitted}; queue={cupsQueue ?? "none"}.");
            AddRow(
                rows,
                "print.pdf-package",
                access.LastPrintPackageIsValid ? "passed" : "failed",
                submitted ? ["cups-submitted.pdf"] : ["owner-before.png"],
                $"Print package validation: {access.LastPrintPackageFailureReason ?? "valid"}.");

            await CaptureAsync(outputDirectory, "owner-after.png");
            screenshots.Add("owner-after.png");
        }
        catch (Exception ex)
        {
            AddRow(rows, "validator.exception", "failed", screenshots.Count > 0 ? screenshots : ["owner-before.png"], $"{ex.GetType().Name}: {ex.Message}");
            File.WriteAllText(Path.Combine(outputDirectory, "validator-error.txt"), ex.ToString());
        }
        finally
        {
            var summary = rows
                .GroupBy(row => row.Status, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            summary.TryAdd("passed", 0);
            summary.TryAdd("failed", 0);
            summary.TryAdd("not-proven", 0);
            summary["total"] = rows.Count;
            var manifest = new PhysicalValidationManifest(
                1,
                "freep-physical-linux-wave13b",
                "linux",
                "avalonia",
                "FreeP",
                Environment.GetEnvironmentVariable("FREEX_CUPS_DRY_RUN_MODE") ?? "unavailable",
                cupsQueue,
                ffmpegPath,
                ffprobePath,
                summary,
                rows);
            JsonArtifactIO.Write(
                Path.Combine(outputDirectory, "freep-physical-linux-wave13b.json"),
                manifest,
                JsonOptions);
            mediaShowForCleanup?.Close();
            access.CloseWithoutDirtyPrompt();
        }
    }

    private static async Task<LinuxNativeOutputCapabilities> WaitForCapabilitiesAsync(
        MainWindow.ValidationAccessAdapter access)
    {
        access.StartNativeOutputCapabilityDetection();
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var capabilities = access.NativeOutputCapabilities;
            if (access.NativeOutputCapabilityDetectionCompleted)
                return capabilities;
            await Task.Delay(100);
        }

        return access.NativeOutputCapabilities;
    }

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);
    }

    private static void AddValidationVideo(Presentation presentation, byte[] bytes)
    {
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 8801,
            Name = "Physical validation video",
            Kind = SlideShapeKind.Media,
            ExtentCxEmu = 6096000,
            ExtentCyEmu = 3429000,
            Media = new MediaInfo
            {
                IsVideo = true,
                ContentType = "video/mp4",
                Bytes = bytes,
            },
        });
    }

    private static async Task CaptureAsync(string directory, string name)
    {
        var result = await ProcessRunner.RunAsync(new ProcessInvocation(
            "scrot",
            ["-o", Path.Combine(directory, name)]));
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"scrot failed: {result.StandardError}");
    }

    private static void AddRow(
        List<PhysicalValidationRow> rows,
        string id,
        string status,
        IReadOnlyList<string> evidence,
        string note) =>
        rows.Add(new PhysicalValidationRow(id, status, "physical-linux-model-and-x11", evidence, note));
}
