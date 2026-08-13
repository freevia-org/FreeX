using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Free.ToolsShared;
using FreeX.ToolsShared;

internal static partial class ChartInteropCompare
{
    private const int AverageHashSize = 16;
    private const double MinimumNonWhiteRatio = 0.01;

    private static void SaveImage(ImageSource image, string path)
    {
        if (image is not BitmapSource bitmap)
            throw new InvalidOperationException($"Unsupported FreeX renderer image type: {image.GetType().FullName}");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void EvaluateVisualParity(
        ComparisonDirectories directories,
        IReadOnlyList<ChartCompareResult> results,
        CompareOptions options)
    {
        foreach (var result in results)
        {
            var expectation = VisualExpectation.For(result, options);
            result.VisualHashThreshold = expectation.HashThreshold;
            result.KnownVisualGap = expectation.KnownGapReason is not null;
            result.KnownVisualGapReason = expectation.KnownGapReason;
            result.KnownVisualGapThreshold = expectation.KnownGapReason is null
                ? null
                : options.KnownGapVisualHashThreshold;
            result.RoundTripVisualHashThreshold = expectation.RoundTripHashThreshold;

            if (!result.OpenabilityPassed)
            {
                result.VisualStatus = VisualStatuses.SkippedOpenability;
                continue;
            }

            var native = ReadPngMetrics(result.ExcelNativePngPath);
            var freexXlsx = ReadPngMetrics(result.FreeXExcelPngPath);
            var roundTrip = ReadPngMetrics(result.ExcelRoundTripPngPath);
            var freexRenderer = ReadPngMetrics(result.FreeXRendererPngPath);

            result.ExcelNativeNonWhiteRatio = native?.NonWhiteRatio;
            result.FreeXXlsxExcelNonWhiteRatio = freexXlsx?.NonWhiteRatio;
            result.ExcelRoundTripNonWhiteRatio = roundTrip?.NonWhiteRatio;
            result.FreeXRendererNonWhiteRatio = freexRenderer?.NonWhiteRatio;
            result.ExcelNativeImageSize = native?.SizeText;
            result.FreeXXlsxExcelImageSize = freexXlsx?.SizeText;
            result.ExcelRoundTripImageSize = roundTrip?.SizeText;
            result.FreeXRendererImageSize = freexRenderer?.SizeText;

            if (native is not null && freexXlsx is not null)
                result.HashDistanceNativeVsFreeXXlsx = HashDistance(native.AverageHash, freexXlsx.AverageHash);
            if (native is not null && roundTrip is not null)
                result.HashDistanceNativeVsRoundTrip = HashDistance(native.AverageHash, roundTrip.AverageHash);
            if (native is not null && freexRenderer is not null)
                result.HashDistanceNativeVsFreeXRenderer = HashDistance(native.AverageHash, freexRenderer.AverageHash);
            result.ExcelNativeRoundTripXlsxByteIdentical = FilesByteEqual(
                result.ExcelNativeXlsxPath,
                result.ExcelRoundTripXlsxPath);

            var failures = new List<string>();
            AddImageFailure(failures, "Excel-native PNG", native);
            AddImageFailure(failures, "Excel-rendered FreeX XLSX PNG", freexXlsx);
            AddImageFailure(failures, "Excel round-trip PNG", roundTrip);

            var usedKnownGapAllowance = false;
            if (result.HashDistanceNativeVsRoundTrip is int roundTripDistance &&
                roundTripDistance > options.RoundTripVisualHashThreshold)
            {
                if (result.ExcelNativeRoundTripXlsxByteIdentical)
                {
                    result.AddNote($"Round-trip PNG hash distance {roundTripDistance} ignored because the Excel-native and FreeX round-tripped XLSX packages are byte-identical.");
                }
                else if (expectation.KnownGapReason is not null && roundTripDistance <= expectation.RoundTripHashThreshold)
                {
                    usedKnownGapAllowance = true;
                    result.AddNote($"Known visual gap tolerated: {expectation.KnownGapReason} (round-trip distance {roundTripDistance}, threshold {options.RoundTripVisualHashThreshold}, known-gap threshold {expectation.RoundTripHashThreshold}).");
                }
                else
                {
                    failures.Add($"round-trip hash distance {roundTripDistance} exceeded {expectation.RoundTripHashThreshold}");
                }
            }

            if (result.HashDistanceNativeVsFreeXXlsx is not int distance)
            {
                failures.Add("native-vs-FreeX XLSX hash distance could not be computed");
            }
            else if (distance > expectation.HashThreshold)
            {
                if (expectation.KnownGapReason is not null && distance <= options.KnownGapVisualHashThreshold)
                {
                    usedKnownGapAllowance = true;
                    result.AddNote($"Known visual gap tolerated: {expectation.KnownGapReason} (distance {distance}, threshold {expectation.HashThreshold}, known-gap threshold {options.KnownGapVisualHashThreshold}).");
                }
                else
                {
                    failures.Add($"native-vs-FreeX XLSX hash distance {distance} exceeded allowed threshold {expectation.AllowedThresholdText(options)}");
                }
            }

            if (failures.Count > 0)
            {
                result.VisualStatus = VisualStatuses.Fail;
                result.VisualFailure = string.Join("; ", failures);
            }
            else
            {
                result.VisualStatus = usedKnownGapAllowance
                    ? VisualStatuses.KnownGap
                    : VisualStatuses.Pass;
            }
        }

        WriteVisualMetrics(Path.Combine(directories.Root, "visual_metrics.csv"), results);
    }

    private static void AddImageFailure(List<string> failures, string label, PngMetrics? metrics)
    {
        if (metrics is null)
        {
            failures.Add($"{label} missing");
            return;
        }

        if (metrics.NonWhiteRatio < MinimumNonWhiteRatio)
            failures.Add($"{label} appears blank (non-white ratio {metrics.NonWhiteRatio.ToString("0.####", CultureInfo.InvariantCulture)})");
    }

    private static PngMetrics? ReadPngMetrics(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];
        BitmapSource bitmap = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var nonWhite = 0;
        var total = width * height;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (red, green, blue) = ReadCompositedPixel(pixels, stride, x, y);
                if (red < 248 || green < 248 || blue < 248)
                    nonWhite++;
            }
        }

        var samples = new double[AverageHashSize * AverageHashSize];
        var sampleIndex = 0;
        for (var row = 0; row < AverageHashSize; row++)
        {
            var y = Math.Min(height - 1, (int)((row + 0.5) * height / AverageHashSize));
            for (var column = 0; column < AverageHashSize; column++)
            {
                var x = Math.Min(width - 1, (int)((column + 0.5) * width / AverageHashSize));
                var (red, green, blue) = ReadCompositedPixel(pixels, stride, x, y);
                samples[sampleIndex++] = (red * 0.299) + (green * 0.587) + (blue * 0.114);
            }
        }

        var average = samples.Average();
        var hash = samples.Select(value => value < average).ToArray();
        return new PngMetrics(width, height, nonWhite / (double)total, hash);
    }

    private static (byte Red, byte Green, byte Blue) ReadCompositedPixel(byte[] pixels, int stride, int x, int y)
    {
        var offset = (y * stride) + (x * 4);
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        var alpha = pixels[offset + 3] / 255.0;
        return (
            (byte)Math.Round((red * alpha) + (255 * (1 - alpha))),
            (byte)Math.Round((green * alpha) + (255 * (1 - alpha))),
            (byte)Math.Round((blue * alpha) + (255 * (1 - alpha))));
    }

    private static int HashDistance(IReadOnlyList<bool> left, IReadOnlyList<bool> right)
    {
        var count = Math.Min(left.Count, right.Count);
        var distance = Math.Abs(left.Count - right.Count);
        for (var index = 0; index < count; index++)
        {
            if (left[index] != right[index])
                distance++;
        }

        return distance;
    }

    private static bool FilesByteEqual(string? leftPath, string? rightPath)
    {
        if (string.IsNullOrWhiteSpace(leftPath) ||
            string.IsNullOrWhiteSpace(rightPath) ||
            !File.Exists(leftPath) ||
            !File.Exists(rightPath))
        {
            return false;
        }

        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        if (left.Length != right.Length)
            return false;

        Span<byte> leftBuffer = stackalloc byte[8192];
        Span<byte> rightBuffer = stackalloc byte[8192];
        while (true)
        {
            var leftRead = left.Read(leftBuffer);
            var rightRead = right.Read(rightBuffer);
            if (leftRead != rightRead)
                return false;
            if (leftRead == 0)
                return true;
            if (!leftBuffer[..leftRead].SequenceEqual(rightBuffer[..rightRead]))
                return false;
        }
    }

    private static void WriteVisualMetrics(string path, IReadOnlyList<ChartCompareResult> results)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Chart,Family,VisualStatus,KnownVisualGap,VisualThreshold,KnownGapThreshold,RoundTripThreshold,NativeRoundTripXlsxByteIdentical,FreeXRendererNonWhite,ExcelNativeNonWhite,ExcelFreeXXlsxNonWhite,ExcelRoundTripNonWhite,HashDistance_Native_vs_FreeXXlsx,HashDistance_Native_vs_RoundTrip,HashDistance_Native_vs_FreeXRenderer,NativeSize,FreeXXlsxExcelSize,RoundTripSize,FreeXRendererSize,VisualFailure,KnownGapReason");
        foreach (var result in results)
        {
            csv.AppendCsvRow(
                result.Chart,
                result.Family,
                result.VisualStatus,
                result.KnownVisualGap,
                result.VisualHashThreshold,
                result.KnownVisualGapThreshold,
                result.RoundTripVisualHashThreshold,
                result.ExcelNativeRoundTripXlsxByteIdentical,
                result.FreeXRendererNonWhiteRatio,
                result.ExcelNativeNonWhiteRatio,
                result.FreeXXlsxExcelNonWhiteRatio,
                result.ExcelRoundTripNonWhiteRatio,
                result.HashDistanceNativeVsFreeXXlsx,
                result.HashDistanceNativeVsRoundTrip,
                result.HashDistanceNativeVsFreeXRenderer,
                result.ExcelNativeImageSize,
                result.FreeXXlsxExcelImageSize,
                result.ExcelRoundTripImageSize,
                result.FreeXRendererImageSize,
                result.VisualFailure,
                result.KnownVisualGapReason);
        }

        File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
    }

    private static void TryWriteVisualContactSheets(string runDirectory, IReadOnlyList<ChartCompareResult> results)
    {
        try
        {
            WriteVisualContactSheets(runDirectory, results);
        }
        catch (Exception ex)
        {
            var error = $"Visual contact sheet generation failed: {ex.GetType().Name}: {ex.Message}";
            Console.Error.WriteLine(error);
            File.WriteAllText(Path.Combine(runDirectory, "visual_contact_sheet_errors.txt"), error, Encoding.UTF8);
        }
    }

    private static void WriteVisualContactSheets(string runDirectory, IReadOnlyList<ChartCompareResult> results)
    {
        WriteVisualContactSheet(Path.Combine(runDirectory, "visual_contact_sheet_all.png"), results, "all");
        foreach (var group in results.GroupBy(result => result.Family, StringComparer.OrdinalIgnoreCase))
        {
            WriteVisualContactSheet(
                Path.Combine(runDirectory, $"visual_contact_sheet_{ToolFileNameSanitizer.ReplaceInvalidFileNameChars(group.Key, lowerInvariant: true)}.png"),
                group.ToList(),
                group.Key);
        }
    }

    private static void WriteVisualContactSheet(string path, IReadOnlyList<ChartCompareResult> results, string label)
    {
        if (results.Count == 0)
            return;

        const int rowLabelWidth = 160;
        const int columnWidth = 220;
        const int headerHeight = 46;
        const int rowHeight = 176;
        const int thumbnailHeight = 126;
        string[] headers = ["FreeX renderer", "Excel FreeX XLSX", "Excel native", "Excel round-trip"];

        var width = rowLabelWidth + (columnWidth * headers.Length);
        var height = headerHeight + (rowHeight * results.Count);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawText(CreateText($"Visual contact sheet: {label}", 15, Brushes.Black, FontWeights.SemiBold), new Point(12, 6));
            for (var column = 0; column < headers.Length; column++)
            {
                var x = rowLabelWidth + (column * columnWidth);
                context.DrawText(CreateText(headers[column], 12, Brushes.Black, FontWeights.SemiBold), new Point(x + 8, 26));
            }

            for (var index = 0; index < results.Count; index++)
            {
                var result = results[index];
                var y = headerHeight + (index * rowHeight);
                var rowBrush = index % 2 == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(248, 248, 248));
                context.DrawRectangle(rowBrush, null, new Rect(0, y, width, rowHeight));
                context.DrawLine(new Pen(Brushes.Gainsboro, 1), new Point(0, y), new Point(width, y));

                context.DrawText(CreateText(result.Chart, 13, Brushes.Black, FontWeights.SemiBold), new Point(10, y + 10));
                context.DrawText(CreateText(result.VisualStatus, 11, StatusBrush(result.VisualStatus), FontWeights.Normal), new Point(10, y + 32));
                if (result.HashDistanceNativeVsFreeXXlsx is int distance)
                    context.DrawText(CreateText($"d={distance}", 11, Brushes.DimGray, FontWeights.Normal), new Point(10, y + 50));

                DrawImageCell(context, result.FreeXRendererPngPath, rowLabelWidth, y + 8, columnWidth, thumbnailHeight);
                DrawImageCell(context, result.FreeXExcelPngPath, rowLabelWidth + columnWidth, y + 8, columnWidth, thumbnailHeight);
                DrawImageCell(context, result.ExcelNativePngPath, rowLabelWidth + (2 * columnWidth), y + 8, columnWidth, thumbnailHeight);
                DrawImageCell(context, result.ExcelRoundTripPngPath, rowLabelWidth + (3 * columnWidth), y + 8, columnWidth, thumbnailHeight);
            }
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void DrawImageCell(DrawingContext context, string? path, double x, double y, double width, double height)
    {
        var bounds = new Rect(x + 8, y + 22, width - 16, height);
        context.DrawRectangle(Brushes.White, new Pen(Brushes.Gainsboro, 1), bounds);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            context.DrawText(CreateText("missing", 12, Brushes.DimGray, FontWeights.Normal), new Point(bounds.X + 8, bounds.Y + 8));
            return;
        }

        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0];
        var scale = Math.Min(bounds.Width / image.PixelWidth, bounds.Height / image.PixelHeight);
        var drawWidth = image.PixelWidth * scale;
        var drawHeight = image.PixelHeight * scale;
        var imageBounds = new Rect(
            bounds.X + ((bounds.Width - drawWidth) / 2),
            bounds.Y + ((bounds.Height - drawHeight) / 2),
            drawWidth,
            drawHeight);
        context.DrawImage(image, imageBounds);
    }

    private static FormattedText CreateText(string text, double fontSize, Brush brush, FontWeight weight) =>
        new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            fontSize,
            brush,
            1.0);

    private static Brush StatusBrush(string status) => status switch
    {
        VisualStatuses.Pass => Brushes.ForestGreen,
        VisualStatuses.KnownGap => Brushes.DarkOrange,
        VisualStatuses.Fail => Brushes.Firebrick,
        _ => Brushes.DimGray
    };

}
