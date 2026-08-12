using System.Windows;
using Free.Shared.AppServices;
using Free.Shared.Shell.Wpf;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

internal static class WpfOsClipboard
{
    internal const string SelectionFormat = PresentationClipboardFormats.Selection;
    internal const string OwnerTokenFormat = PresentationClipboardFormats.OwnerToken;
    internal const string RichTextFormat = PresentationClipboardFormats.RichText;
    internal const string WindowsXamlPackageFormat = PresentationClipboardFormats.WindowsXamlPackage;
    internal const string AvaloniaApplicationFormatPrefix =
        PresentationClipboardPlatformMapper.LegacyAvaloniaApplicationFormatPrefix;
    internal const string LegacyAvaloniaSelectionFormat =
        AvaloniaApplicationFormatPrefix + PresentationClipboardFormats.Selection;
    internal const string LegacyAvaloniaOwnerTokenFormat =
        AvaloniaApplicationFormatPrefix + PresentationClipboardFormats.OwnerToken;

    internal static DataObject BuildDataObject(PresentationClipboardContent content) =>
        WpfPlatformClipboard.BuildDataObject(
            PresentationClipboardPlatformMapper.ToPlatformContent(content));

    internal static PresentationClipboardContent ReadDataObject(IDataObject? data)
    {
        var read = WpfPlatformClipboard.ReadDataObject(
            data,
            PresentationClipboardPlatformMapper.ReadRequest);
        return read.Status == PlatformClipboardReadStatus.Success
            ? PresentationClipboardPlatformMapper.FromPlatformContent(read.Value)
            : new PresentationClipboardContent();
    }
}
