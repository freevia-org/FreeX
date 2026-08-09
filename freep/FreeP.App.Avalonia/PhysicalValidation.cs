using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Free.Shared.AppServices.Printing;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

internal sealed record PhysicalValidationOptions(string OutputDirectory)
{
    public const string Argument = "--physical-validation";

    public static bool TryParse(
        IReadOnlyList<string> args,
        out PhysicalValidationOptions? options,
        out string[] startupArguments,
        out string? error)
    {
        var filtered = new List<string>(args.Count);
        options = null;
        error = null;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument.StartsWith(Argument + "=", StringComparison.Ordinal))
            {
                if (options is not null || argument.Length == Argument.Length + 1)
                {
                    error = $"{Argument} requires one non-empty output directory and may appear once.";
                    startupArguments = filtered.ToArray();
                    return false;
                }

                options = new PhysicalValidationOptions(argument[(Argument.Length + 1)..]);
                continue;
            }

            if (!string.Equals(argument, Argument, StringComparison.Ordinal))
            {
                filtered.Add(args[index]);
                continue;
            }

            if (options is not null || index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                error = $"{Argument} requires one non-empty output directory and may appear once.";
                startupArguments = filtered.ToArray();
                return false;
            }

            options = new PhysicalValidationOptions(args[++index]);
        }

        startupArguments = filtered.ToArray();
        return true;
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Start(MainWindow window, PhysicalValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);
        window.Opened += async (_, _) => await RunAsync(window, options);
    }

    private static async Task RunAsync(MainWindow window, PhysicalValidationOptions options)
    {
        var outputDirectory = Path.GetFullPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var rows = new List<PhysicalValidationRow>();
        var screenshots = new List<string>();
        SlideShowWindow? mediaShowForCleanup = null;
        string? ffmpegPath = null;
        string? ffprobePath = null;
        string? cupsQueue = null;
        try
        {
            var capabilities = await WaitForCapabilitiesAsync(window);
            ffmpegPath = capabilities.Video.ExecutablePath;
            ffprobePath = FindExecutable("ffprobe");
            cupsQueue = capabilities.Print.PrinterName;

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

            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            var slideshow = new SlideShowWindow(window.PresentationForPhysicalValidation, 0);
            var slideshowOpened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            slideshow.Opened += (_, _) => slideshowOpened.TrySetResult(true);
            slideshow.Show(window);
            await slideshowOpened.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await Task.Delay(350);
            await CaptureAsync(outputDirectory, "slideshow-open.png");
            screenshots.Add("slideshow-open.png");
            AddRow(
                rows,
                "slideshow.open-and-render",
                slideshow.IsVisible && slideshow.Controller.CurrentSlideIndex == 0 ? "passed" : "failed",
                ["slideshow-open.png"],
                $"Visible={slideshow.IsVisible}; currentSlide={slideshow.Controller.CurrentSlideIndex}; slideCount={window.SlideCount}.");

            var initialSlide = slideshow.Controller.CurrentSlideIndex;
            var advance = slideshow.ExecuteAdvance();
            await Task.Delay(250);
            await CaptureAsync(outputDirectory, "slideshow-advanced.png");
            screenshots.Add("slideshow-advanced.png");
            AddRow(
                rows,
                "slideshow.advance",
                slideshow.Controller.CurrentSlideIndex == initialSlide + 1 ? "passed" : "failed",
                ["slideshow-advanced.png"],
                $"Advance result={advance.GetType().Name}; currentSlide={slideshow.Controller.CurrentSlideIndex}.");

            slideshow.Close();
            await Task.Delay(150);

            var videoPath = Path.Combine(outputDirectory, "exported-video.mp4");
            var videoResult = capabilities.Video.CanEncodeMp4
                ? await window.ExecuteVideoExportAsync(
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

                var mediaSlide = window.PresentationForPhysicalValidation.Slides[0];
                mediaSlide.Shapes.Add(new SlideShape
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
                        Bytes = await File.ReadAllBytesAsync(videoPath),
                    },
                });
                var mediaShow = new SlideShowWindow(window.PresentationForPhysicalValidation, 0);
                mediaShowForCleanup = mediaShow;
                var mediaOpened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                mediaShow.Opened += (_, _) => mediaOpened.TrySetResult(true);
                mediaShow.Show(window);
                await mediaOpened.Task.WaitAsync(TimeSpan.FromSeconds(8));
                await Task.Delay(900);
                await CaptureAsync(outputDirectory, "media-playback.png");
                screenshots.Add("media-playback.png");
                var mediaAvailable = mediaShow.MediaPlaybackAvailabilityForTest;
                AddRow(
                    rows,
                    "media.libvlc-playback",
                    mediaAvailable?.IsAvailable == true && mediaShow.ActiveMediaPlansForTest.Any() &&
                    mediaShow.LastMediaPlaybackFailureForTest is null ? "passed" : "not-proven",
                    ["media-playback.png"],
                    mediaAvailable?.IsAvailable == true
                        ? $"LibVLC opened the exported MP4 through the production slideshow media controller; activeMedia={mediaShow.ActiveMediaPlansForTest.Count}."
                        : $"LibVLC media playback was unavailable: {mediaAvailable?.FailureReason ?? "no availability report"}.");
            }
            else
            {
                AddRow(rows, "export.ffmpeg-dispatch", "not-proven", ["owner-before.png"], videoResult.FailureReason ?? "Linux video export was unavailable.");
                AddRow(rows, "export.ffprobe-validation", "not-proven", ["owner-before.png"], "No MP4 was produced for ffprobe validation.");
                AddRow(rows, "media.libvlc-playback", "not-proven", ["owner-before.png"], "Media playback requires a successfully exported MP4.");
            }

            var printMode = Environment.GetEnvironmentVariable("FREEX_CUPS_DRY_RUN_MODE") ?? "success";
            var printResult = capabilities.Print.CanPrint
                ? await window.ExecuteNativePrintHandoffAsync(new PresentationPrintRequest(PresentationPrintLayoutKind.FullPageSlides))
                : LinuxNativePrintResult.Failed(capabilities.Print.Reason);
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
                $"mode={printMode}; result={printResult.StatusText}; succeeded={printResult.Succeeded}; submittedPdf={submitted}; queue={cupsQueue ?? "none"}.");
            AddRow(
                rows,
                "print.pdf-package",
                window.LastPrintExecutionDescriptor?.Validation.IsValid == true ? "passed" : "failed",
                submitted ? ["cups-submitted.pdf"] : ["owner-before.png"],
                $"Print package validation: {window.LastPrintExecutionDescriptor?.Validation.FailureReason ?? "valid"}.");

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
            File.WriteAllText(
                Path.Combine(outputDirectory, "freep-physical-linux-wave13b.json"),
                JsonSerializer.Serialize(manifest, JsonOptions));
            mediaShowForCleanup?.Close();
            window.AllowCloseWithoutDirtyPromptForPhysicalValidation();
            window.Close();
        }
    }

    private static async Task<LinuxNativeOutputCapabilities> WaitForCapabilitiesAsync(MainWindow window)
    {
        window.StartNativeOutputCapabilityDetectionForTests();
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            var capabilities = window.NativeOutputCapabilitiesForPhysicalValidation;
            if (capabilities.Print.CanPrint || capabilities.Video.CanEncodeMp4)
                return capabilities;
            await Task.Delay(100);
        }

        return window.NativeOutputCapabilitiesForPhysicalValidation;
    }

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);
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
