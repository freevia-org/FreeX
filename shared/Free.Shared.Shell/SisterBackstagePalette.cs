namespace Free.Shared.Shell;

public readonly record struct BackstageRgb(byte R, byte G, byte B);

/// <summary>
/// Platform-neutral Backstage colors and tile dimensions shared by the sister-app renderers.
/// </summary>
public sealed record SisterBackstagePalette(
    BackstageRgb Sidebar,
    BackstageRgb Hover,
    BackstageRgb Selected,
    BackstageRgb Separator,
    BackstageRgb Link,
    double TileWidth,
    double TileHeight)
{
    public static SisterBackstagePalette FreeW { get; } = new(
        new(0x17, 0x32, 0x4D),
        new(0x26, 0x4B, 0x6B),
        new(0x0F, 0x24, 0x38),
        new(0x36, 0x55, 0x73),
        new(0x0F, 0x6D, 0x8C),
        TileWidth: 150,
        TileHeight: 190);

    public static SisterBackstagePalette FreeP { get; } = new(
        new(0xB7, 0x47, 0x2A),
        new(0xC9, 0x5A, 0x3D),
        new(0x8F, 0x37, 0x21),
        new(0xCE, 0x6A, 0x4F),
        new(0xB7, 0x47, 0x2A),
        TileWidth: 190,
        TileHeight: 150);
}
