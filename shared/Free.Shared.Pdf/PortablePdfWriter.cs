using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Free.Shared.Pdf;

/// <summary>
/// Dependency-free WinAnsi (Helvetica) PDF writer. Serializes an app-agnostic
/// <see cref="PdfContentDocument"/> (draw-op pages) to PDF 1.7 bytes using only built-in
/// Type1 Helvetica faces — no font files, no native dependencies — so it runs anywhere including
/// fully headless environments.
///
/// <para>
/// This is the lossless extraction of FreeX's original <c>PortablePdfDocumentExporter</c> emitter:
/// the per-op content-stream operators, the object/xref/trailer layout, and the WinAnsi text
/// encoding are byte-for-byte identical, which is what keeps FreeX's pinned PDF tests green.
/// Text outside ASCII/WinAnsi throws (callers should preflight via
/// <see cref="PdfWinAnsiTextCapability"/>); geometry is supplied by the caller via draw ops.
/// </para>
/// </summary>
public static class PortablePdfWriter
{
    private static readonly Encoding PdfEncoding = Encoding.ASCII;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private const string DeferredUnicodePdfPathRequirements =
        PdfWinAnsiTextCapability.DeferredUnicodePdfPathRequirements;

    /// <summary>Header comment written after the <c>%PDF-1.7</c> marker.</summary>
    public const string DefaultHeaderComment = "FreeX portable PDF";

    /// <summary>
    /// Serializes <paramref name="document"/> to <paramref name="stream"/>. Each page is rendered to
    /// a content stream from its draw ops; pages may differ in size. The writer overwrites a seekable
    /// stream from position 0.
    /// </summary>
    public static void Write(PdfContentDocument document, Stream stream, string headerComment = DefaultHeaderComment)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("Portable PDF export requires a writable stream.", nameof(stream));
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("Portable PDF export requires at least one rendered page.");

        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(0);
        }

        var fontResources = BuildFontResources(document);
        var imageResources = BuildImageResources(document);
        var opacityResources = BuildOpacityResources(document);
        var pages = document.Pages
            .Select(page => (Content: RenderContentStream(page.Ops, imageResources.ByOp, opacityResources.ByOpacity), page.WidthPoints, page.HeightPoints))
            .ToArray();
        WritePdf(stream, pages, fontResources, imageResources.Resources, opacityResources.Resources, headerComment);
    }

    /// <summary>Serializes <paramref name="document"/> to an in-memory byte array.</summary>
    public static byte[] WriteToBytes(PdfContentDocument document, string headerComment = DefaultHeaderComment)
    {
        using var stream = new MemoryStream();
        Write(document, stream, headerComment);
        return stream.ToArray();
    }

    private static string RenderContentStream(
        IReadOnlyList<PdfDrawOp> ops,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources)
    {
        var content = new StringBuilder();
        foreach (var op in ops)
            AppendDrawOp(content, op, imageResources, opacityResources);

        return content.ToString();
    }

    private static void AppendDrawOp(
        StringBuilder content,
        PdfDrawOp op,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources)
    {
        switch (op)
        {
            case PdfFillRect fill:
                AppendFilledRectangle(content, fill.X, fill.Y, fill.Width, fill.Height, fill.Color);
                break;
            case PdfStrokeRect stroke:
                AppendStrokedRectangle(content, stroke.X, stroke.Y, stroke.Width, stroke.Height, stroke.Color, stroke.LineWidth);
                break;
            case PdfFillEllipse fillEllipse:
                AppendFilledEllipse(content, fillEllipse.X, fillEllipse.Y, fillEllipse.Width, fillEllipse.Height, fillEllipse.Color);
                break;
            case PdfStrokeEllipse strokeEllipse:
                AppendStrokedEllipse(
                    content,
                    strokeEllipse.X,
                    strokeEllipse.Y,
                    strokeEllipse.Width,
                    strokeEllipse.Height,
                    strokeEllipse.Color,
                    strokeEllipse.LineWidth);
                break;
            case PdfText text:
                AppendText(content, text.X, text.Y, text.FontSize, FontResource(text.Face), text.Color, text.Text);
                break;
            case PdfLine line:
                AppendLine(content, line.X1, line.Y1, line.X2, line.Y2, line.Color, line.LineWidth);
                break;
            case PdfFilledTriangle triangle:
                AppendFilledTriangle(
                    content,
                    triangle.X1,
                    triangle.Y1,
                    triangle.X2,
                    triangle.Y2,
                    triangle.X3,
                    triangle.Y3,
                    triangle.Color);
                break;
            case PdfPath path:
                AppendPath(content, path);
                break;
            case PdfRotationGroup group:
                AppendRotationGroup(content, group, imageResources, opacityResources);
                break;
            case PdfOpacityGroup group:
                opacityResources.TryGetValue(NormalizeOpacity(group.Opacity), out var groupOpacityResource);
                AppendOpacityGroup(content, group, groupOpacityResource?.ResourceName, imageResources, opacityResources);
                break;
            case PdfImage image when imageResources.TryGetValue(image, out var resource):
                opacityResources.TryGetValue(NormalizeOpacity(image.Opacity), out var imageOpacityResource);
                AppendImage(content, image, resource, imageOpacityResource?.ResourceName);
                break;
        }
    }

    private static string FontResource(PdfFontFace face) => face switch
    {
        PdfFontFace.Bold => "F2",
        PdfFontFace.Italic => "F3",
        PdfFontFace.BoldItalic => "F4",
        _ => "F1",
    };

    private static void WritePdf(
        Stream stream,
        IReadOnlyList<(string Content, double Width, double Height)> pages,
        IReadOnlyList<(string ResourceName, string BaseFont)> fontResources,
        IReadOnlyList<PdfImageResource> imageResources,
        IReadOnlyList<PdfOpacityResource> opacityResources,
        string headerComment)
    {
        var objects = new List<PdfObject>();
        var firstPageObjectId = 3 + fontResources.Count + imageResources.Count + opacityResources.Count;
        var pageObjectIds = Enumerable.Range(0, pages.Count)
            .Select(index => firstPageObjectId + (index * 2))
            .ToArray();

        objects.Add(PdfObject.Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(PdfObject.Ascii($"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"))}] /Count {pages.Count} >>"));
        foreach (var font in fontResources)
            objects.Add(PdfObject.Ascii($"<< /Type /Font /Subtype /Type1 /BaseFont /{font.BaseFont} /Encoding /WinAnsiEncoding >>"));

        foreach (var image in imageResources)
            objects.Add(CreateImageObject(image));
        foreach (var opacity in opacityResources)
            objects.Add(CreateOpacityObject(opacity));

        var fontResourceDictionary = string.Join(
            " ",
            fontResources.Select((font, index) => $"/{font.ResourceName} {index + 3} 0 R"));
        var imageResourceDictionary = string.Join(
            " ",
            imageResources.Select((image, index) => $"/{image.ResourceName} {index + 3 + fontResources.Count} 0 R"));
        var opacityResourceDictionary = string.Join(
            " ",
            opacityResources.Select((opacity, index) => $"/{opacity.ResourceName} {index + 3 + fontResources.Count + imageResources.Count} 0 R"));
        var xObjectResources = imageResources.Count == 0
            ? string.Empty
            : $" /XObject << {imageResourceDictionary} >>";
        var extGStateResources = opacityResources.Count == 0
            ? string.Empty
            : $" /ExtGState << {opacityResourceDictionary} >>";

        for (var index = 0; index < pages.Count; index++)
        {
            var pageObjectId = pageObjectIds[index];
            var contentObjectId = pageObjectId + 1;
            objects.Add(PdfObject.Ascii(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {FormatNumber(pages[index].Width)} {FormatNumber(pages[index].Height)}] /Resources << /Font << {fontResourceDictionary} >>{xObjectResources}{extGStateResources} >> /Contents {contentObjectId} 0 R >>"));

            var pageStream = pages[index].Content.EndsWith("\n", StringComparison.Ordinal)
                ? pages[index].Content
                : pages[index].Content + "\n";
            objects.Add(PdfObject.Ascii($"<< /Length {PdfEncoding.GetByteCount(pageStream)} >>\nstream\n{pageStream}endstream"));
        }

        WriteAscii(stream, $"%PDF-1.7\n% {headerComment}\n");
        var offsets = new List<long> { 0 };
        for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            offsets.Add(stream.Position);
            WriteAscii(stream, $"{objectIndex + 1} 0 obj\n");
            stream.Write(objects[objectIndex].Bytes);
            WriteAscii(stream, "\nendobj\n");
        }

        var xrefOffset = stream.Position;
        WriteAscii(stream, $"xref\n0 {objects.Count + 1}\n");
        WriteAscii(stream, "0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            WriteAscii(stream, $"{offset.ToString("0000000000", CultureInfo.InvariantCulture)} 00000 n \n");

        WriteAscii(
            stream,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
    }

    private static IReadOnlyList<(string ResourceName, string BaseFont)> BuildFontResources(PdfContentDocument document)
    {
        var hasItalicText = document.Pages
            .SelectMany(page => page.Ops)
            .SelectMany(EnumerateOps)
            .OfType<PdfText>()
            .Any(text => text.Face is PdfFontFace.Italic or PdfFontFace.BoldItalic);

        return hasItalicText
            ? [
                ("F1", "Helvetica"),
                ("F2", "Helvetica-Bold"),
                ("F3", "Helvetica-Oblique"),
                ("F4", "Helvetica-BoldOblique"),
            ]
            : [
                ("F1", "Helvetica"),
                ("F2", "Helvetica-Bold"),
            ];
    }

    private static ImageResourceSet BuildImageResources(PdfContentDocument document)
    {
        var byOp = new Dictionary<PdfImage, PdfImageResource>(ReferenceEqualityComparer.Instance);
        var resources = new List<PdfImageResource>();

        foreach (var image in document.Pages.SelectMany(page => page.Ops).SelectMany(EnumerateOps).OfType<PdfImage>())
        {
            if (byOp.ContainsKey(image))
                continue;
            if (!TryCreateImageResource($"Im{resources.Count + 1}", image, out var resource))
                continue;

            resources.Add(resource);
            byOp.Add(image, resource);
        }

        return new ImageResourceSet(resources, byOp);
    }

    private static OpacityResourceSet BuildOpacityResources(PdfContentDocument document)
    {
        var byOpacity = new Dictionary<double, PdfOpacityResource>();
        var resources = new List<PdfOpacityResource>();

        foreach (var opacity in document.Pages
            .SelectMany(page => page.Ops)
            .SelectMany(EnumerateOps)
            .SelectMany(EnumerateOpacities)
            .Where(opacity => opacity < 1.0))
        {
            if (byOpacity.ContainsKey(opacity))
                continue;

            var resource = new PdfOpacityResource($"GS{resources.Count + 1}", opacity);
            resources.Add(resource);
            byOpacity.Add(opacity, resource);
        }

        return new OpacityResourceSet(resources, byOpacity);
    }

    private static bool TryCreateImageResource(string resourceName, PdfImage image, out PdfImageResource resource)
    {
        resource = default!;
        if (image.Width <= 0 || image.Height <= 0 || image.ImageBytes.Length == 0)
            return false;

        try
        {
            var contentType = NormalizeContentType(image.ContentType);
            resource = contentType switch
            {
                "image/png" => DecodePng(resourceName, image),
                "image/jpeg" or "image/jpg" => DecodeJpeg(resourceName, image.ImageBytes),
                _ => null!,
            };
        }
        catch (Exception ex) when (IsRecoverableImageDecodeException(ex))
        {
            return false;
        }

        return resource is not null;
    }

    private static IEnumerable<PdfDrawOp> EnumerateOps(PdfDrawOp op)
    {
        yield return op;

        if (op is PdfRotationGroup group)
        {
            foreach (var child in group.Ops.SelectMany(EnumerateOps))
                yield return child;
        }

        if (op is PdfOpacityGroup opacityGroup)
        {
            foreach (var child in opacityGroup.Ops.SelectMany(EnumerateOps))
                yield return child;
        }
    }

    private static IEnumerable<double> EnumerateOpacities(PdfDrawOp op)
    {
        switch (op)
        {
            case PdfImage image:
                yield return NormalizeOpacity(image.Opacity);
                break;
            case PdfOpacityGroup group:
                yield return NormalizeOpacity(group.Opacity);
                break;
        }
    }

    private static PdfObject CreateImageObject(PdfImageResource image)
    {
        var header =
            $"<< /Type /XObject /Subtype /Image /Width {image.PixelWidth} /Height {image.PixelHeight} " +
            $"/ColorSpace /{image.ColorSpace} /BitsPerComponent 8 /Filter /{image.Filter} /Length {image.Data.Length} >>\nstream\n";
        const string footer = "\nendstream";

        using var stream = new MemoryStream(PdfEncoding.GetByteCount(header) + image.Data.Length + PdfEncoding.GetByteCount(footer));
        stream.Write(PdfEncoding.GetBytes(header));
        stream.Write(image.Data);
        stream.Write(PdfEncoding.GetBytes(footer));
        return new PdfObject(stream.ToArray());
    }

    private static PdfObject CreateOpacityObject(PdfOpacityResource opacity) =>
        PdfObject.Ascii(
            $"<< /Type /ExtGState /ca {FormatNumber(opacity.Opacity)} /CA {FormatNumber(opacity.Opacity)} >>");

    private static PdfImageResource DecodeJpeg(string resourceName, byte[] bytes)
    {
        var (width, height, components) = ReadJpegSize(bytes);
        var colorSpace = components switch
        {
            1 => "DeviceGray",
            3 => "DeviceRGB",
            _ => throw new NotSupportedException("Portable PDF image export supports grayscale and RGB JPEG images."),
        };

        return new PdfImageResource(resourceName, width, height, colorSpace, "DCTDecode", bytes);
    }

    private static PdfImageResource DecodePng(string resourceName, PdfImage image)
    {
        var decoded = DecodePngToPdfPixels(image.ImageBytes);
        if (image.ColorEffects.HasPixelEffects)
            decoded = ApplyColorEffects(decoded, image.ColorEffects);

        return new PdfImageResource(
            resourceName,
            decoded.Width,
            decoded.Height,
            decoded.ColorSpace,
            "FlateDecode",
            Deflate(decoded.Pixels));
    }

    private static PngPdfPixels DecodePngToPdfPixels(byte[] data)
    {
        if (data.Length < PngSignature.Length || !data.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            throw new FormatException("Not a PNG image.");

        var position = PngSignature.Length;
        var width = 0;
        var height = 0;
        var bitDepth = 0;
        var colorType = 0;
        var interlace = 0;
        byte[]? palette = null;
        using var idat = new MemoryStream();

        while (position + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position, 4));
            position += 4;
            var chunkType = PdfEncoding.GetString(data, position, 4);
            position += 4;
            if (length < 0 || position + length > data.Length)
                throw new FormatException($"PNG chunk '{chunkType}' overruns the file.");

            switch (chunkType)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position + 4, 4));
                    bitDepth = data[position + 8];
                    colorType = data[position + 9];
                    interlace = data[position + 12];
                    break;
                case "PLTE":
                    palette = data.AsSpan(position, length).ToArray();
                    break;
                case "IDAT":
                    idat.Write(data, position, length);
                    break;
            }

            position += length + 4;
            if (chunkType == "IEND")
                break;
        }

        if (width <= 0 || height <= 0)
            throw new FormatException("PNG image is missing valid dimensions.");
        if (bitDepth != 8)
            throw new NotSupportedException("Portable PDF image export supports only 8-bit PNG images.");
        if (interlace != 0)
            throw new NotSupportedException("Portable PDF image export does not support interlaced PNG images.");

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new NotSupportedException($"Portable PDF image export does not support PNG color type {colorType}."),
        };

        idat.Position = 0;
        var raw = Inflate(idat);
        var pixels = UnfilterPng(raw, width, height, channels);
        return ConvertPngPixelsToPdfPixels(pixels, width, height, colorType, channels, palette);
    }

    private static PngPdfPixels ConvertPngPixelsToPdfPixels(
        byte[] pixels,
        int width,
        int height,
        int colorType,
        int channels,
        byte[]? palette)
    {
        var pixelCount = width * height;
        if (colorType is 0 or 4)
        {
            var gray = new byte[pixelCount];
            for (var i = 0; i < pixelCount; i++)
                gray[i] = pixels[i * channels];
            return new PngPdfPixels(width, height, "DeviceGray", gray);
        }

        var rgb = new byte[pixelCount * 3];
        for (var i = 0; i < pixelCount; i++)
        {
            var source = i * channels;
            var target = i * 3;
            switch (colorType)
            {
                case 2:
                case 6:
                    rgb[target] = pixels[source];
                    rgb[target + 1] = pixels[source + 1];
                    rgb[target + 2] = pixels[source + 2];
                    break;
                case 3:
                    var paletteIndex = pixels[source] * 3;
                    if (palette is null || paletteIndex + 2 >= palette.Length)
                        throw new FormatException("PNG palette index is out of range.");
                    rgb[target] = palette[paletteIndex];
                    rgb[target + 1] = palette[paletteIndex + 1];
                    rgb[target + 2] = palette[paletteIndex + 2];
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        return new PngPdfPixels(width, height, "DeviceRGB", rgb);
    }

    private static PngPdfPixels ApplyColorEffects(PngPdfPixels pixels, PdfImageColorEffects effects)
    {
        var transformed = pixels.Pixels.ToArray();
        switch (pixels.ColorSpace)
        {
            case "DeviceGray":
                PdfImageColorEffectPixels.ApplyToGray8(transformed, effects);
                break;
            case "DeviceRGB":
                PdfImageColorEffectPixels.ApplyToRgb24(transformed, effects);
                break;
            default:
                throw new NotSupportedException($"Portable PDF image color effects do not support {pixels.ColorSpace} PNG pixels.");
        }

        return pixels with { Pixels = transformed };
    }

    private static byte[] Inflate(Stream zlib)
    {
        using var output = new MemoryStream();
        using (var stream = new ZLibStream(zlib, CompressionMode.Decompress, leaveOpen: true))
            stream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var stream = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            stream.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static byte[] UnfilterPng(byte[] raw, int width, int height, int channels)
    {
        var stride = width * channels;
        var expected = height * (stride + 1);
        if (raw.Length < expected)
            throw new FormatException("PNG image data is truncated.");

        var output = new byte[height * stride];
        var input = 0;
        for (var row = 0; row < height; row++)
        {
            var filter = raw[input++];
            var rowStart = row * stride;
            for (var column = 0; column < stride; column++)
            {
                var rawValue = raw[input++];
                var left = column >= channels ? output[rowStart + column - channels] : 0;
                var up = row > 0 ? output[rowStart - stride + column] : 0;
                var upLeft = column >= channels && row > 0 ? output[rowStart - stride + column - channels] : 0;
                var value = filter switch
                {
                    0 => rawValue,
                    1 => rawValue + left,
                    2 => rawValue + up,
                    3 => rawValue + ((left + up) >> 1),
                    4 => rawValue + Paeth(left, up, upLeft),
                    _ => throw new FormatException($"PNG image uses unknown filter type {filter}."),
                };
                output[rowStart + column] = (byte)(value & 0xFF);
            }
        }

        return output;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var prediction = left + up - upLeft;
        var leftDistance = Math.Abs(prediction - left);
        var upDistance = Math.Abs(prediction - up);
        var upLeftDistance = Math.Abs(prediction - upLeft);
        if (leftDistance <= upDistance && leftDistance <= upLeftDistance)
            return left;
        return upDistance <= upLeftDistance ? up : upLeft;
    }

    private static (int Width, int Height, int Components) ReadJpegSize(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            throw new FormatException("Not a JPEG image.");

        var position = 2;
        while (position + 4 <= bytes.Length)
        {
            while (position < bytes.Length && bytes[position] == 0xFF)
                position++;
            if (position >= bytes.Length)
                break;

            var marker = bytes[position++];
            if (marker is 0xD9 or 0xDA)
                break;
            if (position + 2 > bytes.Length)
                break;

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position, 2));
            if (length < 2 || position + length > bytes.Length)
                throw new FormatException("JPEG segment overruns the file.");

            if (IsJpegStartOfFrame(marker))
            {
                if (length < 8)
                    throw new FormatException("JPEG start-of-frame segment is truncated.");
                var precision = bytes[position + 2];
                if (precision != 8)
                    throw new NotSupportedException("Portable PDF image export supports only 8-bit JPEG images.");

                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position + 5, 2));
                var components = bytes[position + 7];
                return (width, height, components);
            }

            position += length;
        }

        throw new FormatException("JPEG image is missing a start-of-frame segment.");
    }

    private static bool IsJpegStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static void AppendImage(
        StringBuilder content,
        PdfImage image,
        PdfImageResource resource,
        string? opacityResourceName)
    {
        var hasSourceCrop = TryGetSourceCroppedImagePlacement(image, resource, out var placement);
        if (image.ClipKind != PdfImageClipKind.None || hasSourceCrop)
        {
            AppendClippedImage(
                content,
                image,
                resource.ResourceName,
                opacityResourceName,
                hasSourceCrop ? placement : null);
            return;
        }

        content.AppendLine("q");
        AppendOpacityState(content, opacityResourceName);
        var rotation = -image.RotationDegrees * Math.PI / 180d;
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);
        var centerX = image.X + image.Width / 2d;
        var centerY = image.Y + image.Height / 2d;
        var a = cos * image.Width;
        var b = sin * image.Width;
        var c = -sin * image.Height;
        var d = cos * image.Height;
        var e = centerX - (cos * image.Width / 2d) + (sin * image.Height / 2d);
        var f = centerY - (sin * image.Width / 2d) - (cos * image.Height / 2d);
        content.AppendLine($"{FormatNumber(a)} {FormatNumber(b)} {FormatNumber(c)} {FormatNumber(d)} {FormatNumber(e)} {FormatNumber(f)} cm");
        content.AppendLine($"/{resource.ResourceName} Do");
        content.AppendLine("Q");
    }

    private static void AppendClippedImage(
        StringBuilder content,
        PdfImage image,
        string resourceName,
        string? opacityResourceName,
        PdfImagePlacement? croppedPlacement = null)
    {
        var placement = croppedPlacement ?? new PdfImagePlacement(image.X, image.Y, image.Width, image.Height);

        content.AppendLine("q");
        AppendOpacityState(content, opacityResourceName);
        if (Math.Abs(image.RotationDegrees) > 0.001)
            AppendRotationTransform(content, image.X + image.Width / 2d, image.Y + image.Height / 2d, image.RotationDegrees);

        AppendImageClipPath(content, image, croppedPlacement is not null);
        content.AppendLine($"{FormatNumber(placement.Width)} 0 0 {FormatNumber(placement.Height)} {FormatNumber(placement.X)} {FormatNumber(placement.Y)} cm");
        content.AppendLine($"/{resourceName} Do");
        content.AppendLine("Q");
    }

    private static bool TryGetSourceCroppedImagePlacement(
        PdfImage image,
        PdfImageResource resource,
        out PdfImagePlacement placement)
    {
        placement = default;
        if (!image.SourceCrop.HasCrop ||
            image.Width <= 0 ||
            image.Height <= 0 ||
            resource.PixelWidth <= 0 ||
            resource.PixelHeight <= 0)
            return false;

        var sourceX = Clamp(
            (int)Math.Round(NormalizeCropFraction(image.SourceCrop.Left) * resource.PixelWidth),
            0,
            resource.PixelWidth - 1);
        var sourceY = Clamp(
            (int)Math.Round(NormalizeCropFraction(image.SourceCrop.Top) * resource.PixelHeight),
            0,
            resource.PixelHeight - 1);
        var sourceWidth = Clamp(
            (int)Math.Round((1.0 - NormalizeCropFraction(image.SourceCrop.Left) - NormalizeCropFraction(image.SourceCrop.Right)) * resource.PixelWidth),
            1,
            resource.PixelWidth - sourceX);
        var sourceHeight = Clamp(
            (int)Math.Round((1.0 - NormalizeCropFraction(image.SourceCrop.Top) - NormalizeCropFraction(image.SourceCrop.Bottom)) * resource.PixelHeight),
            1,
            resource.PixelHeight - sourceY);

        if (sourceX == 0 &&
            sourceY == 0 &&
            sourceWidth == resource.PixelWidth &&
            sourceHeight == resource.PixelHeight)
            return false;

        var scaleX = image.Width / sourceWidth;
        var scaleY = image.Height / sourceHeight;
        var sourceBottom = resource.PixelHeight - sourceY - sourceHeight;
        placement = new PdfImagePlacement(
            image.X - sourceX * scaleX,
            image.Y - sourceBottom * scaleY,
            resource.PixelWidth * scaleX,
            resource.PixelHeight * scaleY);
        return true;
    }

    private static void AppendRotationGroup(
        StringBuilder content,
        PdfRotationGroup group,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources)
    {
        if (group.Ops.Count == 0)
            return;

        content.AppendLine("q");
        AppendRotationTransform(content, group.CenterX, group.CenterY, group.RotationDegrees);

        foreach (var op in group.Ops)
            AppendDrawOp(content, op, imageResources, opacityResources);

        content.AppendLine("Q");
    }

    private static void AppendOpacityGroup(
        StringBuilder content,
        PdfOpacityGroup group,
        string? opacityResourceName,
        IReadOnlyDictionary<PdfImage, PdfImageResource> imageResources,
        IReadOnlyDictionary<double, PdfOpacityResource> opacityResources)
    {
        if (group.Ops.Count == 0)
            return;

        content.AppendLine("q");
        AppendOpacityState(content, opacityResourceName);

        foreach (var op in group.Ops)
            AppendDrawOp(content, op, imageResources, opacityResources);

        content.AppendLine("Q");
    }

    private static void AppendOpacityState(StringBuilder content, string? opacityResourceName)
    {
        if (!string.IsNullOrEmpty(opacityResourceName))
            content.AppendLine($"/{opacityResourceName} gs");
    }

    private static void AppendRotationTransform(
        StringBuilder content,
        double centerX,
        double centerY,
        double rotationDegrees)
    {
        var rotation = -rotationDegrees * Math.PI / 180d;
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);
        var e = centerX - (cos * centerX) + (sin * centerY);
        var f = centerY - (sin * centerX) - (cos * centerY);
        content.AppendLine(
            $"{FormatNumber(cos)} {FormatNumber(sin)} {FormatNumber(-sin)} {FormatNumber(cos)} {FormatNumber(e)} {FormatNumber(f)} cm");
    }

    private static void AppendFilledRectangle(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor color)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "rg");
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y)} {FormatNumber(width)} {FormatNumber(height)} re f");
        content.AppendLine("Q");
    }

    private static void AppendStrokedRectangle(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor color,
        double lineWidth)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "RG");
        content.AppendLine($"{FormatNumber(lineWidth)} w");
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y)} {FormatNumber(width)} {FormatNumber(height)} re S");
        content.AppendLine("Q");
    }

    private static void AppendFilledEllipse(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor color)
    {
        if (width <= 0 || height <= 0)
            return;

        content.AppendLine("q");
        AppendRgb(content, color, "rg");
        AppendEllipsePath(content, x, y, width, height);
        content.AppendLine("f");
        content.AppendLine("Q");
    }

    private static void AppendStrokedEllipse(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfColor color,
        double lineWidth)
    {
        if (width <= 0 || height <= 0)
            return;

        content.AppendLine("q");
        AppendRgb(content, color, "RG");
        content.AppendLine($"{FormatNumber(lineWidth)} w");
        AppendEllipsePath(content, x, y, width, height);
        content.AppendLine("S");
        content.AppendLine("Q");
    }

    private static void AppendEllipsePath(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height)
    {
        const double kappa = 0.5522847498307936;
        var rx = width / 2d;
        var ry = height / 2d;
        var cx = x + rx;
        var cy = y + ry;
        var ox = rx * kappa;
        var oy = ry * kappa;

        content.AppendLine($"{FormatNumber(cx + rx)} {FormatNumber(cy)} m");
        content.AppendLine($"{FormatNumber(cx + rx)} {FormatNumber(cy + oy)} {FormatNumber(cx + ox)} {FormatNumber(cy + ry)} {FormatNumber(cx)} {FormatNumber(cy + ry)} c");
        content.AppendLine($"{FormatNumber(cx - ox)} {FormatNumber(cy + ry)} {FormatNumber(cx - rx)} {FormatNumber(cy + oy)} {FormatNumber(cx - rx)} {FormatNumber(cy)} c");
        content.AppendLine($"{FormatNumber(cx - rx)} {FormatNumber(cy - oy)} {FormatNumber(cx - ox)} {FormatNumber(cy - ry)} {FormatNumber(cx)} {FormatNumber(cy - ry)} c");
        content.AppendLine($"{FormatNumber(cx + ox)} {FormatNumber(cy - ry)} {FormatNumber(cx + rx)} {FormatNumber(cy - oy)} {FormatNumber(cx + rx)} {FormatNumber(cy)} c");
    }

    private static void AppendImageClipPath(StringBuilder content, PdfImage image, bool includeRectangularClip = false)
    {
        switch (image.ClipKind)
        {
            case PdfImageClipKind.Ellipse:
                AppendEllipsePath(content, image.X, image.Y, image.Width, image.Height);
                content.AppendLine("W n");
                break;
            case PdfImageClipKind.RoundedRectangle:
                AppendRoundedRectanglePath(content, image.X, image.Y, image.Width, image.Height);
                content.AppendLine("W n");
                break;
            case PdfImageClipKind.Triangle:
            case PdfImageClipKind.Diamond:
            case PdfImageClipKind.Parallelogram:
            case PdfImageClipKind.Hexagon:
            case PdfImageClipKind.Chevron:
                AppendPresetClipPolygonPath(content, image.X, image.Y, image.Width, image.Height, image.ClipKind);
                content.AppendLine("W n");
                break;
            case PdfImageClipKind.None when includeRectangularClip:
                content.AppendLine($"{FormatNumber(image.X)} {FormatNumber(image.Y)} {FormatNumber(image.Width)} {FormatNumber(image.Height)} re W n");
                break;
        }
    }

    private static void AppendPresetClipPolygonPath(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height,
        PdfImageClipKind clipKind)
    {
        if (width <= 0 || height <= 0)
            return;

        var points = GetPresetClipPolygonPoints(x, y, width, height, clipKind);
        if (points.Length == 0)
            return;

        content.AppendLine($"{FormatNumber(points[0].X)} {FormatNumber(points[0].Y)} m");
        for (var i = 1; i < points.Length; i++)
            content.AppendLine($"{FormatNumber(points[i].X)} {FormatNumber(points[i].Y)} l");
        content.AppendLine("h");
    }

    private static PdfPathPoint[] GetPresetClipPolygonPoints(
        double x,
        double y,
        double width,
        double height,
        PdfImageClipKind clipKind)
    {
        var right = x + width;
        var top = y + height;
        var midX = x + width / 2.0;
        var midY = y + height / 2.0;
        var quarterX = x + width * 0.25;
        var threeQuarterX = x + width * 0.75;

        return clipKind switch
        {
            PdfImageClipKind.Triangle =>
            [
                new PdfPathPoint(midX, top),
                new PdfPathPoint(right, y),
                new PdfPathPoint(x, y),
            ],
            PdfImageClipKind.Diamond =>
            [
                new PdfPathPoint(midX, top),
                new PdfPathPoint(right, midY),
                new PdfPathPoint(midX, y),
                new PdfPathPoint(x, midY),
            ],
            PdfImageClipKind.Parallelogram =>
            [
                new PdfPathPoint(quarterX, top),
                new PdfPathPoint(right, top),
                new PdfPathPoint(threeQuarterX, y),
                new PdfPathPoint(x, y),
            ],
            PdfImageClipKind.Hexagon =>
            [
                new PdfPathPoint(quarterX, top),
                new PdfPathPoint(threeQuarterX, top),
                new PdfPathPoint(right, midY),
                new PdfPathPoint(threeQuarterX, y),
                new PdfPathPoint(quarterX, y),
                new PdfPathPoint(x, midY),
            ],
            PdfImageClipKind.Chevron =>
            [
                new PdfPathPoint(x, top),
                new PdfPathPoint(threeQuarterX, top),
                new PdfPathPoint(right, midY),
                new PdfPathPoint(threeQuarterX, y),
                new PdfPathPoint(x, y),
                new PdfPathPoint(quarterX, midY),
            ],
            _ => [],
        };
    }

    private static void AppendRoundedRectanglePath(
        StringBuilder content,
        double x,
        double y,
        double width,
        double height)
    {
        if (width <= 0 || height <= 0)
            return;

        const double kappa = 0.5522847498307936;
        var radius = Math.Min(width, height) * 0.18;
        radius = Math.Min(radius, Math.Min(width, height) / 2d);
        var offset = radius * kappa;
        var right = x + width;
        var top = y + height;

        content.AppendLine($"{FormatNumber(x + radius)} {FormatNumber(y)} m");
        content.AppendLine($"{FormatNumber(right - radius)} {FormatNumber(y)} l");
        content.AppendLine($"{FormatNumber(right - radius + offset)} {FormatNumber(y)} {FormatNumber(right)} {FormatNumber(y + radius - offset)} {FormatNumber(right)} {FormatNumber(y + radius)} c");
        content.AppendLine($"{FormatNumber(right)} {FormatNumber(top - radius)} l");
        content.AppendLine($"{FormatNumber(right)} {FormatNumber(top - radius + offset)} {FormatNumber(right - radius + offset)} {FormatNumber(top)} {FormatNumber(right - radius)} {FormatNumber(top)} c");
        content.AppendLine($"{FormatNumber(x + radius)} {FormatNumber(top)} l");
        content.AppendLine($"{FormatNumber(x + radius - offset)} {FormatNumber(top)} {FormatNumber(x)} {FormatNumber(top - radius + offset)} {FormatNumber(x)} {FormatNumber(top - radius)} c");
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y + radius)} l");
        content.AppendLine($"{FormatNumber(x)} {FormatNumber(y + radius - offset)} {FormatNumber(x + radius - offset)} {FormatNumber(y)} {FormatNumber(x + radius)} {FormatNumber(y)} c");
    }

    private static void AppendLine(
        StringBuilder content,
        double x1,
        double y1,
        double x2,
        double y2,
        PdfColor color,
        double lineWidth)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "RG");
        content.AppendLine($"{FormatNumber(lineWidth)} w");
        content.AppendLine($"{FormatNumber(x1)} {FormatNumber(y1)} m");
        content.AppendLine($"{FormatNumber(x2)} {FormatNumber(y2)} l S");
        content.AppendLine("Q");
    }

    private static void AppendFilledTriangle(
        StringBuilder content,
        double x1,
        double y1,
        double x2,
        double y2,
        double x3,
        double y3,
        PdfColor color)
    {
        content.AppendLine("q");
        AppendRgb(content, color, "rg");
        content.AppendLine($"{FormatNumber(x1)} {FormatNumber(y1)} m");
        content.AppendLine($"{FormatNumber(x2)} {FormatNumber(y2)} l");
        content.AppendLine($"{FormatNumber(x3)} {FormatNumber(y3)} l f");
        content.AppendLine("Q");
    }

    private static void AppendPath(StringBuilder content, PdfPath path)
    {
        if (path.Contours.Count == 0 || (path.FillColor is null && path.StrokeColor is null))
            return;

        content.AppendLine("q");
        if (path.FillColor is { } fill)
            AppendRgb(content, fill, "rg");
        if (path.StrokeColor is { } stroke)
        {
            AppendRgb(content, stroke, "RG");
            content.AppendLine($"{FormatNumber(Math.Max(0.1, path.StrokeWidth))} w");
        }

        foreach (var contour in path.Contours)
        {
            content.AppendLine($"{FormatNumber(contour.Start.X)} {FormatNumber(contour.Start.Y)} m");
            foreach (var segment in contour.Segments)
            {
                switch (segment.Kind)
                {
                    case PdfPathSegmentKind.Line:
                        content.AppendLine($"{FormatNumber(segment.End.X)} {FormatNumber(segment.End.Y)} l");
                        break;
                    case PdfPathSegmentKind.CubicBezier:
                        content.AppendLine(
                            $"{FormatNumber(segment.Control1.X)} {FormatNumber(segment.Control1.Y)} " +
                            $"{FormatNumber(segment.Control2.X)} {FormatNumber(segment.Control2.Y)} " +
                            $"{FormatNumber(segment.End.X)} {FormatNumber(segment.End.Y)} c");
                        break;
                }
            }

            if (contour.Closed)
                content.AppendLine("h");
        }

        content.AppendLine(path.FillColor is not null && path.StrokeColor is not null
            ? "B"
            : path.FillColor is not null ? "f" : "S");
        content.AppendLine("Q");
    }

    private static void AppendText(
        StringBuilder content,
        double x,
        double y,
        double fontSize,
        string fontResource,
        PdfColor color,
        string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var textOperand = EncodeTextOperand(text);
        AppendRgb(content, color, "rg");
        content.AppendLine("BT");
        content.AppendLine($"/{fontResource} {FormatNumber(fontSize)} Tf");
        content.AppendLine($"1 0 0 1 {FormatNumber(x)} {FormatNumber(y)} Tm");
        content.AppendLine($"{textOperand} Tj");
        content.AppendLine("ET");
    }

    private static void AppendRgb(StringBuilder content, PdfColor color, string operatorName) =>
        content.AppendLine(
            $"{FormatNumber(color.R / 255d)} {FormatNumber(color.G / 255d)} {FormatNumber(color.B / 255d)} {operatorName}");

    private static string EncodeTextOperand(string text)
    {
        var normalized = NormalizePdfText(text);
        if (!RequiresWinAnsiHexText(normalized))
            return $"({EscapePdfLiteralText(normalized)})";

        return $"<{EncodeWinAnsiHexText(normalized)}>";
    }

    private static string NormalizePdfText(string text) =>
        PdfWinAnsiTextCapability.NormalizePdfText(text);

    private static bool RequiresWinAnsiHexText(string text) => text.Any(ch => ch is < ' ' or > '~');

    private static string EscapePdfLiteralText(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                    escaped.Append(@"\\");
                    break;
                case '(':
                    escaped.Append(@"\(");
                    break;
                case ')':
                    escaped.Append(@"\)");
                    break;
                case >= ' ' and <= '~':
                    escaped.Append(ch);
                    break;
                default:
                    throw new InvalidOperationException("Portable PDF ASCII text path received unsupported text.");
            }
        }

        return escaped.ToString();
    }

    private static string EncodeWinAnsiHexText(string text)
    {
        var hex = new StringBuilder(text.Length * 2);
        foreach (var ch in text)
            hex.Append(EncodeWinAnsiByte(ch).ToString("X2", CultureInfo.InvariantCulture));

        return hex.ToString();
    }

    private static byte EncodeWinAnsiByte(char ch)
    {
        if (PdfWinAnsiTextCapability.TryEncodeWinAnsiByte(ch, out var value))
            return value;

        throw new InvalidOperationException(
            "Portable PDF export currently supports ASCII and WinAnsi text only; " +
            $"characters outside the built-in Helvetica/WinAnsi set require the deferred embedded-font Unicode PDF path. {DeferredUnicodePdfPathRequirements}");
    }

    private static string FormatNumber(double value) =>
        (Math.Abs(value) < 0.0005 ? 0d : value).ToString("0.###", CultureInfo.InvariantCulture);

    private static double NormalizeOpacity(double opacity) =>
        Math.Round(Math.Clamp(double.IsFinite(opacity) ? opacity : 1.0, 0.0, 1.0), 3);

    private static double NormalizeCropFraction(double value) =>
        double.IsFinite(value) ? value : 0.0;

    private static int Clamp(int value, int min, int max) =>
        Math.Max(min, Math.Min(value, max));

    private static void WriteAscii(Stream stream, string text) =>
        stream.Write(PdfEncoding.GetBytes(text));

    private static string NormalizeContentType(string? contentType) =>
        contentType?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? string.Empty;

    private static bool IsRecoverableImageDecodeException(Exception ex) =>
        ex is FormatException
            or InvalidDataException
            or NotSupportedException
            or ArgumentException
            or IOException;

    private sealed record ImageResourceSet(
        IReadOnlyList<PdfImageResource> Resources,
        IReadOnlyDictionary<PdfImage, PdfImageResource> ByOp);

    private sealed record OpacityResourceSet(
        IReadOnlyList<PdfOpacityResource> Resources,
        IReadOnlyDictionary<double, PdfOpacityResource> ByOpacity);

    private sealed record PdfImageResource(
        string ResourceName,
        int PixelWidth,
        int PixelHeight,
        string ColorSpace,
        string Filter,
        byte[] Data);

    private sealed record PdfOpacityResource(string ResourceName, double Opacity);

    private readonly record struct PdfImagePlacement(double X, double Y, double Width, double Height);

    private sealed record PngPdfPixels(
        int Width,
        int Height,
        string ColorSpace,
        byte[] Pixels);

    private sealed record PdfObject(byte[] Bytes)
    {
        public static PdfObject Ascii(string text) => new(PdfEncoding.GetBytes(text));
    }
}
