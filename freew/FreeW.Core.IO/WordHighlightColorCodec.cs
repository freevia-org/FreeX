namespace FreeW.Core.IO;

/// <summary>Converts Word's fixed highlight-gallery tokens to and from canonical sRGB values.</summary>
public static class WordHighlightColorCodec
{
    public static string? ToHex(string? token) => token switch
    {
        "yellow" => "#FFFF00",
        "green" => "#00FF00",
        "cyan" => "#00FFFF",
        "magenta" => "#FF00FF",
        "blue" => "#0000FF",
        "red" => "#FF0000",
        "darkBlue" => "#000080",
        "darkCyan" => "#008080",
        "darkGreen" => "#008000",
        "darkMagenta" => "#800080",
        "darkRed" => "#800000",
        "darkYellow" => "#808000",
        "darkGray" => "#808080",
        "lightGray" => "#C0C0C0",
        "black" => "#000000",
        "white" => "#FFFFFF",
        _ => null,
    };

    public static string? ToToken(string? hex)
    {
        if (hex is null)
            return null;

        return hex.TrimStart('#').ToUpperInvariant() switch
        {
            "FFFF00" => "yellow",
            "00FF00" => "green",
            "00FFFF" => "cyan",
            "FF00FF" => "magenta",
            "0000FF" => "blue",
            "FF0000" => "red",
            "000080" => "darkBlue",
            "008080" => "darkCyan",
            "008000" => "darkGreen",
            "800080" => "darkMagenta",
            "800000" => "darkRed",
            "808000" => "darkYellow",
            "808080" => "darkGray",
            "C0C0C0" => "lightGray",
            "000000" => "black",
            "FFFFFF" => "white",
            _ => null,
        };
    }
}
