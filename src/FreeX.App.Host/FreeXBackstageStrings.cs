using Free.Shared.Shell;

namespace FreeX.App.Host;

/// <summary>
/// FreeX implementation of the shared shell's <see cref="IBackstageStrings"/>, delegating to
/// the host's localized <see cref="UiText"/> catalog. Installed into
/// <see cref="BackstageStrings.Current"/> at startup so the neutral backstage planners
/// (greeting, recent-file list) render FreeX's localized strings unchanged.
/// </summary>
internal sealed class FreeXBackstageStrings : IBackstageStrings
{
    public string Get(string key) => UiText.Get(key);

    public string Format(string key, params object?[] args) => UiText.Format(key, args);
}
