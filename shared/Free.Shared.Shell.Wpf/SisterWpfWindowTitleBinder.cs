using System.Windows;
using System.Windows.Controls;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Wpf;

public sealed record SisterWpfWindowTitleSpec(
    string DisplayName,
    string ApplicationName,
    bool IsDirty,
    string DirtyMarker,
    string Separator,
    string WindowSuffix = "",
    string GroupSuffix = "",
    WindowTitleApplicationPlacement ApplicationPlacement = WindowTitleApplicationPlacement.DocumentThenApplication)
{
    public ApplicationWindowTitleSpec ToApplicationWindowTitleSpec() => new(
        ApplicationName,
        FileCommandSession.DefaultUntitledDisplayName,
        DirtyMarker,
        Separator,
        ApplicationPlacement);
}

/// <summary>
/// WPF-side sister app title binding: compose the neutral title once, then keep the OS caption and
/// custom shell title text in sync.
/// </summary>
public sealed class SisterWpfWindowTitleBinder
{
    private readonly Window _window;
    private readonly TextBlock _titleText;

    public SisterWpfWindowTitleBinder(Window window, TextBlock titleText)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(titleText);

        _window = window;
        _titleText = titleText;
    }

    public string Update(SisterWpfWindowTitleSpec spec) =>
        Update(_window, _titleText, spec);

    public static string Update(Window window, TextBlock titleText, SisterWpfWindowTitleSpec spec)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(titleText);

        var title = Compose(spec);
        window.Title = title;
        titleText.Text = title;
        return title;
    }

    public static string Compose(SisterWpfWindowTitleSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return ApplicationWindowTitlePolicy.Compose(
            spec.ToApplicationWindowTitleSpec(),
            spec.DisplayName,
            spec.IsDirty,
            spec.WindowSuffix,
            spec.GroupSuffix);
    }
}
