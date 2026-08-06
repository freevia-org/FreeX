using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Ribbon.Icons;
using SharedRibbonIconFactory = Free.Shared.Ribbon.Wpf.RibbonIconFactory;
using SvgCommandIconLoader = Free.Shared.Ribbon.Wpf.SvgCommandIconLoader;

namespace FreeW.App.Host;

internal static class RibbonIconFactory
{
    private static readonly SvgCommandIconLoader CommandIconLoader = new(
        resourceFolder: "CommandIconsSvg",
        slugFromCommandName: ToCommandIconSlug,
        slugCandidates: GetCommandIconSlugCandidates,
        sizeKeySelector: size => size <= 22 ? "s" : "l");

    public static FrameworkElement CreateCommandIcon(
        string commandName,
        RibbonCommandIcon fallbackIcon,
        double size,
        Brush glyphBrush)
    {
        return TryCreateCommandIcon(commandName, fallbackIcon, size, glyphBrush)
            ?? SharedRibbonIconFactory.CreateIcon(fallbackIcon, size, glyphBrush);
    }

    public static FrameworkElement? TryCreateCommandIcon(
        string commandName,
        RibbonCommandIcon fallbackIcon,
        double size,
        Brush glyphBrush)
    {
        if (TryLoadCommandIcon(commandName, glyphBrush, size) is { } source)
        {
            return new Image
            {
                Source = source,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
        }

        return null;
    }

    private static ImageSource? TryLoadCommandIcon(string commandName, Brush glyphBrush, double size) =>
        CommandIconLoader.TryLoad(commandName, glyphBrush, size);

    private static IEnumerable<string> GetCommandIconSlugCandidates(string slug)
    {
        // Prefer aliases so overloaded names such as "size" resolve to the intended FreeW artwork.
        foreach (var candidate in RibbonCommandIconSlugAliases.GetCandidates(slug))
            yield return candidate;
    }

    private static string ToCommandIconSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();
        if (trimmed.StartsWith("freew.", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["freew.".Length..];

        var lower = trimmed
            .ToLowerInvariant()
            .Replace("&amp;", "and", StringComparison.Ordinal)
            .Replace("&", "and", StringComparison.Ordinal);
        var builder = new System.Text.StringBuilder(lower.Length);
        var pendingDash = false;

        foreach (var ch in lower)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');
                builder.Append(ch);
                pendingDash = false;
            }
            else
            {
                pendingDash = builder.Length > 0;
            }
        }

        return builder.ToString().Trim('-');
    }
}
