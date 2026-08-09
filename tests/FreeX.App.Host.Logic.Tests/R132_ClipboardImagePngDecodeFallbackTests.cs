using System.Reflection;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R132-clipboard-png-decode-fallback (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs,
/// TryPasteClipboardImage/TryResolveClipboardImageBytes).
///
/// Before the fix: TryPasteClipboardImage's single try/catch wrapped BOTH the PNG-format decode
/// AND the flattened-DIB fallback together, so a sibling "PNG" clipboard entry that was PRESENT
/// but not itself decodable (a broken/unsupported PNG flavor some source apps advertise) threw out
/// of the PNG branch straight into the outer catch and failed the WHOLE paste -- even though a
/// perfectly good flattened CF_DIB/CF_BITMAP entry was sitting right there and would have pasted
/// fine on its own (the "no PNG entry at all" branch already handled that case).
///
/// Exercised at the pure decode/fallback-decision layer (TryResolveClipboardImageBytes) rather
/// than through the real OS clipboard/STA thread the R49/R57/R82/R91 integration clipboard tests
/// already rely on -- those are known STA-flaky in this repo (round-132 note), and this decision
/// logic has no OS-clipboard dependency of its own (the flattened-bitmap source is injected via a
/// delegate).
/// </summary>
public sealed class R132_ClipboardImagePngDecodeFallbackTests
{
    [Fact]
    public void TryResolveClipboardImageBytes_UndecodablePngEntryPresent_FallsBackToFlattenedBitmap()
    {
        var garbagePngBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 }; // not valid PNG data
        var flattenedBitmap = DecodeAsBitmapSource(CreateOneByOnePngBytes());

        var args = new object?[]
        {
            garbagePngBytes,
            (Func<bool>)(() => true),
            (Func<System.Windows.Media.Imaging.BitmapSource?>)(() => flattenedBitmap),
            null,
            0,
            0
        };

        var success = InvokeTryResolve(args);

        success.Should().BeTrue(
            "a sibling PNG clipboard entry that fails to decode must not fail the whole paste when " +
            "a good flattened bitmap entry is available");
        ((byte[]?)args[3]).Should().NotBeNull();
        ((int)args[4]!).Should().Be(1);
        ((int)args[5]!).Should().Be(1);
    }

    // Sibling no-regression: a VALID PNG entry must still win over the flattened bitmap -- the
    // R91-io-clipboard-image-formats-5-4 alpha-preservation behavior this same method implements
    // must not be over-corrected away by the new fallback branch.
    [Fact]
    public void TryResolveClipboardImageBytes_ValidPngEntryPresent_PrefersPngBytesOverFlattenedBitmap()
    {
        var validPngBytes = CreateOneByOnePngBytes();
        var flattenedBitmap = DecodeAsBitmapSource(CreateOneByOnePngBytes());
        var flattenedProbed = false;

        var args = new object?[]
        {
            validPngBytes,
            (Func<bool>)(() => { flattenedProbed = true; return true; }),
            (Func<System.Windows.Media.Imaging.BitmapSource?>)(() => { flattenedProbed = true; return flattenedBitmap; }),
            null,
            0,
            0
        };

        var success = InvokeTryResolve(args);

        success.Should().BeTrue();
        ((byte[]?)args[3]).Should().BeSameAs(
            validPngBytes,
            "a decodable PNG entry must be used as-is (preserving alpha), not re-derived from the flattened bitmap");
        flattenedProbed.Should().BeFalse(
            "the flattened-bitmap path must not even be probed when the PNG entry decodes fine");
    }

    // Sibling no-regression: no PNG entry at all (the overwhelmingly common case) must still fall
    // back to the flattened bitmap exactly as before.
    [Fact]
    public void TryResolveClipboardImageBytes_NoPngEntry_UsesFlattenedBitmap()
    {
        var flattenedBitmap = DecodeAsBitmapSource(CreateOneByOnePngBytes());

        var args = new object?[]
        {
            null,
            (Func<bool>)(() => true),
            (Func<System.Windows.Media.Imaging.BitmapSource?>)(() => flattenedBitmap),
            null,
            0,
            0
        };

        var success = InvokeTryResolve(args);

        success.Should().BeTrue();
        ((byte[]?)args[3]).Should().NotBeNull();
    }

    private static bool InvokeTryResolve(object?[] args)
    {
        var method = typeof(MainWindow).GetMethod(
            "TryResolveClipboardImageBytes",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(MainWindow), "TryResolveClipboardImageBytes");
        return (bool)method.Invoke(null, args)!;
    }

    private static System.Windows.Media.Imaging.BitmapSource DecodeAsBitmapSource(byte[] pngBytes)
    {
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
            new System.IO.MemoryStream(pngBytes),
            System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private static byte[] CreateOneByOnePngBytes()
    {
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(
            1, 1, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null);
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, 1, 1), new byte[] { 255, 255, 255, 255 }, 4, 0);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
