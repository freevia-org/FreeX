using FreeX.Core.Commands;
using FreeX.Core.Model;
using SharedTableStyleGalleryPlanner = FreeX.App.Presentation.TableUI.TableStyleGalleryPlanner;

namespace FreeX.App.Host;

public sealed record TableStyleGalleryOption(
    string Label,
    string StyleName,
    StructuredTableStyleBanding Banding);

/// <summary>
/// WPF-host facade over the portable <see cref="SharedTableStyleGalleryPlanner"/>: the built-in table-style
/// catalog (Light / Medium / Dark families with theme-resolved banding) is single-sourced in
/// <c>FreeX.App.Presentation</c> so the WPF gallery, the Avalonia/macOS gallery, and the load-time materializer
/// all agree on every style name + color. This facade only re-projects the shared option record onto the host's
/// own <see cref="TableStyleGalleryOption"/> so existing call sites and tests are undisturbed.
/// </summary>
public static class TableStyleGalleryPlanner
{
    public static IReadOnlyList<TableStyleGalleryOption> GetOptions() =>
        Project(SharedTableStyleGalleryPlanner.GetOptions());

    public static IReadOnlyList<TableStyleGalleryOption> GetOptions(WorkbookTheme theme) =>
        Project(SharedTableStyleGalleryPlanner.GetOptions(theme));

    public static TableStyleGalleryOption GetOption(int index) =>
        Project(SharedTableStyleGalleryPlanner.GetOption(index));

    public static TableStyleGalleryOption GetOption(int index, WorkbookTheme theme) =>
        Project(SharedTableStyleGalleryPlanner.GetOption(index, theme));

    public static bool TryGetOption(string? styleName, out TableStyleGalleryOption option)
        => TryGetOption(styleName, WorkbookTheme.Office, out option);

    public static bool TryGetOption(string? styleName, WorkbookTheme theme, out TableStyleGalleryOption option)
    {
        if (SharedTableStyleGalleryPlanner.TryGetOption(styleName, theme, out var shared))
        {
            option = Project(shared);
            return true;
        }

        option = null!;
        return false;
    }

    private static IReadOnlyList<TableStyleGalleryOption> Project(
        IReadOnlyList<Presentation.TableUI.TableStyleGalleryOption> options)
    {
        var projected = new TableStyleGalleryOption[options.Count];
        for (var index = 0; index < options.Count; index++)
            projected[index] = Project(options[index]);
        return projected;
    }

    private static TableStyleGalleryOption Project(Presentation.TableUI.TableStyleGalleryOption option) =>
        new(option.Label, option.StyleName, option.Banding);
}
