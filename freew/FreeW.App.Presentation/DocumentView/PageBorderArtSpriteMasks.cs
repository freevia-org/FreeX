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
}
