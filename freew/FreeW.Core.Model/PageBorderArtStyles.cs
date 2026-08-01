namespace FreeW.Core.Model;

public sealed record PageBorderArtStyle(int ArtId, string Token, string Label);

public static class PageBorderArtStyles
{
    public static readonly IReadOnlyList<PageBorderArtStyle> Curated =
    [
        new(1, "apples", "Apples"),
        new(38, "flowersRoses", "Flowers - Roses"),
        new(84, "people", "People"),
        new(35, "birdsFlight", "Birds in Flight"),
        new(66, "eggsBlack", "Painted Eggs"),
        new(89, "decoArch", "Decorative Arch"),
        new(83, "shorebirdTracks", "Shorebird Tracks"),
        new(92, "papyrus", "Papyrus"),
        new(57, "shadowedSquares", "Shadowed Squares"),
        new(37, "bats", "Bats"),
        new(95, "weavingRibbon", "Weaving Ribbon"),
        new(47, "vine", "Vine"),
        new(160, "handmade2", "Handmade 2"),
        new(2, "mapleMuffins", "Maple Muffins"),
        new(3, "cakeSlice", "Cake Slice"),
        new(4, "candyCorn", "Candy Corn"),
        new(5, "iceCreamCones", "Ice Cream Cones"),
    ];

    private static readonly IReadOnlyDictionary<int, PageBorderArtStyle> ById =
        Curated.ToDictionary(style => style.ArtId);

    private static readonly IReadOnlyDictionary<string, PageBorderArtStyle> ByToken =
        Curated.ToDictionary(style => style.Token, StringComparer.OrdinalIgnoreCase);

    public static bool TryGetById(int artId, out PageBorderArtStyle style) =>
        ById.TryGetValue(artId, out style!);

    public static bool TryGetByToken(string? token, out PageBorderArtStyle style)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            style = null!;
            return false;
        }

        return ByToken.TryGetValue(token, out style!);
    }
}
