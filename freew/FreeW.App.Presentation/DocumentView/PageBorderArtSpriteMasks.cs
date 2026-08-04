namespace FreeW.App.Presentation.DocumentView;

internal static class PageBorderArtSpriteMasks
{
    internal const int MaskSize = 32;
    internal const int WeavingRibbonTopRail = 0;
    internal const int WeavingRibbonBottomRail = 1;
    internal const int WeavingRibbonLeftRail = 2;
    internal const int WeavingRibbonRightRail = 3;
    internal const int WeavingRibbonTopLeftCorner = 4;
    internal const int WeavingRibbonTopRightCorner = 5;
    internal const int WeavingRibbonBottomLeftCorner = 6;
    internal const int WeavingRibbonBottomRightCorner = 7;

    internal static IReadOnlyList<byte[]> WeavingRibbonMasks { get; } = DecodeWeavingRibbonMasks();
    internal static IReadOnlyList<byte> PaintedEggMask { get; } = DecodeMask(PaintedEggPacked);

    private const string PaintedEggPacked =
        "////+Qb9/////19Ab/T/////UgD54P///79CAdDD////p0VHAH7///+5/woAuP////7/rQbg//+////9D4L//69/9P8pAP//" +
        "3z/4/3YA///Xff3/twX///d2/5frL////av/Qv93///u/1/S/6v//4H/f/3/m///La+//v+f///Q/v7/D5///4GW/f8L3///" +
        "AUD8/0vr//8FCPn/9vf//wAIv/Lm/f//vwCV59/+///6AgDnv7/+/0EfAOp/AAD+C+BgXaYAAOAfgAH5GAAA4H8AA0AHAAD0/wAL" +
        "QAEAAPz/v1kGAACQ/////xYAQP7//////////////////////w==";

    private const string WeavingRibbonPacked =
        "QKoaAAAAAACkqgYAAAAAQKqqAAAAAACQqqoAAAAAAKSqCgEAAAAAqqpCAQAAAECqqlABAAAAkKqqVAUAAACkqgpVBQAAAKqqQlUF" +
        "AABAqqpQVQUAAJCqqlRVFQAApKqqVVUVAACqqipVVRUAQKqqClVVFQCQqqoCVVUVAKiqqgBUVVUAqqoqAFRVVYCqqgoAVFVVoKqq" +
        "AgBUVVWoqqoAAFRVBaqqGgAAVFWBqqoGAABQVaCqqgEAAFBVqKqqAAAAUAWqqhoAAABQgaqqBgAAAFCgqqoBAAAAUKiqqgAAAAAA" +
        "qqoaAAAAAICqqgYAAAAApKqqAQAAAABUVVUAAAAAAKSqqgAAAAAAkKqqAgAAAABAqqoKAAAAAACqqioAAAAAUKSqqgAAAABQkaqq" +
        "AgAAAFBFqqoaAAAAUBWqqmoAAABQVaSqqgEAAFRVkaqqBgAAVFUFqqoaAABUVRWqqmoAAFRVVaSqqgEAVFVVkKqqBgBUVVVAqqoa" +
        "AFVVVQCqqmoAVVUVAKSqqgFVVRUAkKqqBlVVFQBAqqoaVVUVAACqqmpQVRUAAKCqqkJVBQAAgKqqClUFAAAAqqoqVQUAAACqqqpQ" +
        "BQAAAKCqqkIBAAAAgKqqCgEAAAAAqqoqAQAAAACqqqoAAAAAAKCpqgIAAAAAgJCqCgAAAAAAAFUVAAAAAAAAAAAAAKCqqgEAAAAA" +
        "gKqqBgAAAAAAqqoaAAAAAACqqqoAAAAAUKCqqgIAAABQhaqqCgAAAFAVqqoqAAAAVFWpqqoAAABUVZGqqgIAAFVVRaqqCgAAVVUV" +
        "qqoqAABVVVWoqqoAAFVVVaCqqgJAVVVVgKqqCkBVVVUAqqpqQFVVVQCkqqpRVVVVAJCqqkJVVVUAQKqqClVVVQAAqqoqVVVVAACk" +
        "qqpRVRUAAJCqqkZVFQAAQKqqGlUVAAAAqqpqVRUAAACkqqpRFQAAAJCqqkYVAAAAQKqqGgUAAAAAqqpqBQAAAACgqqoCAAAAAICq" +
        "qgoAAAAAAKqqKgAAAAAAVVVVAAAAAACqqmoAAAAAQKqqGgAAAACQqqoCAAAAAKSqqgAAAAAAqqoqBQAAAICqqgoFAAAAoKqqQhUA" +
        "AACoqqpQFQAAAKqqKlUVAACAqqoKVRUAAKCqqkJVFQAAqKqqUFUVAACqqhpVVVUAgKqqBlVVVQCgqqpRVVVVAKmqqlBVVVVAqqoa" +
        "QFVVVZCqqgZAVVVVpKqqAQBVVVWpqqoAAFVVVaqqGgAAVVUVqqoGAABVVQWqqgEAAFVVgaqqAAAAVFWgqhoAAABUFaqqBgAAAFAF" +
        "qqoBAAAAUIGqqgAAAABQoKoKAAAAAACqqgIAAAAAQKqqAAAAAACQqqoAAAAAAFRVVQAAAAAAAABAAAAAAAAAAFAAAAAAAAAAZAAA" +
        "AAAAAABpAAAAAAAAQGoAAAAAAACQagAAAAAAAKRqAAAAAAAAqWoAAAAAAECqagAAAAAAkKoqAAAAAACkqioAAAAAAKmqKgAAAABA" +
        "qqoaAAAAAJCqqgYAAAAApKqqAQAAAACpqqoAAAAAQKqqGgAAAACQqqoGAAAAAKSqqgEAAAAAqaqqAAAAAECqqhoAAAAAkKqqBgAA" +
        "AACkqqoBAAAAAKmqqgAAAABAqqoaAAAAAJCqqgYAAAAApKqqAQAAAACpqqoAAAAAQKqqGgAAAACQqqoGAAAAAKSqqgEAAAAAUFVV" +
        "AAAAAAAAAAAAAAAAAAEAAAAAAAAABgAAAAAAAAAaAAAAAAAAAKoAAAAAAAAAqgEAAAAAAACqBgAAAAAAAKoaAAAAAAAAqqoAAAAA" +
        "AACqqgEAAAAAAKqqBgAAAAAAqaoaAAAAAACQqqoAAAAAAECqqgEAAAAAAKqqBgAAAAAAqqoaAAAAAACgqqoAAAAAAICqqgEAAAAA" +
        "AKqqBgAAAAAAqqoaAAAAAACgqqoAAAAAAICqqgEAAAAAQKqqBgAAAAAAqqoaAAAAAACkqqoAAAAAAJCqqgEAAAAAQKqqBgAAAAAA" +
        "qqoaAAAAAACkqqoAAAAAAJCqqgEAAAAAQKqqBgAAAAAAVVUBpKqqAAAAAACQqqoCAAAAAECqqgoAAAAAAKqqKgAAAAAApKqqAAAA" +
        "AACQqqoCAAAAAECqqgoAAAAAAKqqKgAAAAAApKqqAAAAAACQqqoCAAAAAECqqgoAAAAAAKqqKgAAAAAApKqqAAAAAACQqqoCAAAA" +
        "AECqqgoAAAAAAKqqKgAAAAAApKqqAAAAAACQqqoBAAAAAECqqgYAAAAAAKqqGgAAAAAApKoqAAAAAACQqioAAAAAAECqagAAAAAA" +
        "AKpqAAAAAAAApGoAAAAAAACQagAAAAAAAEBqAAAAAAAAAGoAAAAAAAAAZAAAAAAAAABQAAAAAAAAAEAAAAAAAAAAQAAAAABAqqoK" +
        "AAAAAJCqqgIAAAAApKqqAAAAAACpqqoAAAAAQKqqCgAAAACQqqoCAAAAAKSqqgAAAAAAqaqqAAAAAECqqgoAAAAAkKqqAgAAAACk" +
        "qqoAAAAAAKmqqgAAAABAqqoKAAAAAJCqqgIAAAAApKqqAAAAAACpqqoAAAAAQKqqCgAAAACQqqoCAAAAAKSqqgAAAAAAqaqqAAAA" +
        "AACqqgoAAAAAAKqqAgAAAAAAqqoAAAAAAACqqgAAAAAAAKoKAAAAAAAAqgIAAAAAAACqAAAAAAAAAKoAAAAAAAAACgAAAAAAAAAC" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private static IReadOnlyList<byte[]> DecodeWeavingRibbonMasks()
    {
        var packed = Convert.FromBase64String(WeavingRibbonPacked);
        const int maskCount = 8;
        const int pixelsPerMask = MaskSize * MaskSize;
        if (packed.Length * 4 != maskCount * pixelsPerMask)
            throw new InvalidOperationException("Invalid Weaving Ribbon mask payload.");

        var masks = new byte[maskCount][];
        for (var maskIndex = 0; maskIndex < maskCount; maskIndex++)
        {
            var mask = new byte[pixelsPerMask];
            var firstPixel = maskIndex * pixelsPerMask;
            for (var pixel = 0; pixel < pixelsPerMask; pixel++)
            {
                var absolutePixel = firstPixel + pixel;
                mask[pixel] = (byte)((packed[absolutePixel / 4] >> (2 * (absolutePixel % 4))) & 0x03);
            }
            masks[maskIndex] = mask;
        }
        return masks;
    }

    private static IReadOnlyList<byte> DecodeMask(string packedBase64)
    {
        var packed = Convert.FromBase64String(packedBase64);
        var pixels = new byte[MaskSize * MaskSize];
        if (packed.Length * 4 != pixels.Length)
            throw new InvalidOperationException("Invalid page-border art mask payload.");

        for (var pixel = 0; pixel < pixels.Length; pixel++)
            pixels[pixel] = (byte)((packed[pixel / 4] >> (2 * (pixel % 4))) & 0x03);
        return pixels;
    }
}
