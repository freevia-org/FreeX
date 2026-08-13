using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

public static class PresentationClipboardPlatformMapper
{
    public const string LegacyAvaloniaApplicationFormatPrefix = "avn-app-fmt:";

    private static IReadOnlyList<PlatformClipboardFormat> RichTextFormats { get; } =
    [
        Bytes(PresentationClipboardFormats.RichText),
        Bytes(PresentationClipboardFormats.RichText, PlatformClipboardFormatScope.Application),
        Bytes(PresentationClipboardFormats.WindowsXamlPackage),
        Bytes(PresentationClipboardFormats.LinuxXamlPackage),
        Bytes(PresentationClipboardFormats.WindowsRtf),
        Bytes(PresentationClipboardFormats.LinuxRtf),
    ];

    public static PlatformClipboardReadRequest RichTextReadRequest { get; } = new(
        IncludeText: true,
        CustomFormats: RichTextFormats);

    public static PlatformClipboardReadRequest ReadRequest { get; } = new(
        IncludeText: true,
        IncludeImage: true,
        CustomFormats:
        [
            Bytes(PresentationClipboardFormats.Selection),
            Bytes(PresentationClipboardFormats.Selection, PlatformClipboardFormatScope.Application),
            Bytes(LegacyAvaloniaApplicationFormatPrefix + PresentationClipboardFormats.Selection),
            Text(PresentationClipboardFormats.OwnerToken),
            Text(PresentationClipboardFormats.OwnerToken, PlatformClipboardFormatScope.Application),
            Text(LegacyAvaloniaApplicationFormatPrefix + PresentationClipboardFormats.OwnerToken),
            .. RichTextFormats,
        ]);

    public static PlatformClipboardFormatScope ResolveNativeScope() =>
        OperatingSystem.IsWindows()
            ? PlatformClipboardFormatScope.Platform
            : PlatformClipboardFormatScope.Application;

    public static string ResolveNativeXamlPackageFormat() =>
        OperatingSystem.IsWindows()
            ? PresentationClipboardFormats.WindowsXamlPackage
            : PresentationClipboardFormats.LinuxXamlPackage;

    public static string ResolveNativeRtfFormat() =>
        OperatingSystem.IsWindows()
            ? PresentationClipboardFormats.WindowsRtf
            : PresentationClipboardFormats.LinuxRtf;

    public static PlatformClipboardContent ToPlatformContent(
        PresentationClipboardContent content,
        PlatformClipboardFormatScope nativeScope = PlatformClipboardFormatScope.Platform,
        string? xamlPackageFormat = null,
        string? rtfFormat = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var custom = new List<PlatformClipboardData>();
        if (content.SelectionBytes is { Length: > 0 })
        {
            custom.Add(PlatformClipboardData.FromBytes(
                PresentationClipboardFormats.Selection,
                content.SelectionBytes,
                nativeScope));
        }
        if (!string.IsNullOrEmpty(content.OwnerToken))
        {
            custom.Add(PlatformClipboardData.FromText(
                PresentationClipboardFormats.OwnerToken,
                content.OwnerToken,
                nativeScope));
        }
        if (content.RichTextBytes is { Length: > 0 })
        {
            custom.Add(PlatformClipboardData.FromBytes(
                PresentationClipboardFormats.RichText,
                content.RichTextBytes,
                nativeScope));
        }
        if (xamlPackageFormat is not null && content.XamlPackageBytes is { Length: > 0 })
        {
            custom.Add(PlatformClipboardData.FromBytes(
                xamlPackageFormat,
                content.XamlPackageBytes));
        }
        if (rtfFormat is not null && content.RtfBytes is { Length: > 0 })
        {
            custom.Add(PlatformClipboardData.FromBytes(
                rtfFormat,
                content.RtfBytes));
        }

        return new PlatformClipboardContent(
            Text: content.Text,
            Image: content.PngBytes is { Length: > 0 } png
                ? new PlatformClipboardImage(png)
                : null,
            CustomData: custom);
    }

    public static PresentationClipboardContent FromPlatformContent(PlatformClipboardContent? content)
    {
        if (content is null)
            return new PresentationClipboardContent();

        return new PresentationClipboardContent(
            SelectionBytes: FirstBytes(
                content,
                PresentationClipboardFormats.Selection,
                LegacyAvaloniaApplicationFormatPrefix + PresentationClipboardFormats.Selection),
            PngBytes: content.Image?.PngBytes,
            Text: content.Text,
            OwnerToken: FirstText(
                content,
                PresentationClipboardFormats.OwnerToken,
                LegacyAvaloniaApplicationFormatPrefix + PresentationClipboardFormats.OwnerToken),
            RichTextBytes: FirstBytes(content, PresentationClipboardFormats.RichText),
            XamlPackageBytes: FirstBytes(
                content,
                PresentationClipboardFormats.WindowsXamlPackage,
                PresentationClipboardFormats.LinuxXamlPackage),
            RtfBytes: FirstBytes(
                content,
                PresentationClipboardFormats.WindowsRtf,
                PresentationClipboardFormats.LinuxRtf));
    }

    private static byte[]? FirstBytes(PlatformClipboardContent content, params string[] formats)
    {
        foreach (var format in formats)
        {
            if (content.GetBytes(format) is { Length: > 0 } bytes)
                return bytes;
        }
        return null;
    }

    private static string? FirstText(PlatformClipboardContent content, params string[] formats)
    {
        foreach (var format in formats)
        {
            if (content.GetText(format) is { Length: > 0 } text)
                return text;
        }
        return null;
    }

    private static PlatformClipboardFormat Bytes(
        string name,
        PlatformClipboardFormatScope scope = PlatformClipboardFormatScope.Platform) =>
        new(name, PlatformClipboardDataKind.Bytes, scope);

    private static PlatformClipboardFormat Text(
        string name,
        PlatformClipboardFormatScope scope = PlatformClipboardFormatScope.Platform) =>
        new(name, PlatformClipboardDataKind.Text, scope);
}
