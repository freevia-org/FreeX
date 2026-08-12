using System.Globalization;
using Free.Shared.AppServices;

namespace FreeP.App.Compositor;

public sealed record OptionsDialogInput(
    string? RecentFilesCapText,
    string? Format,
    string? UiLanguage);

/// <summary>
/// Renderer-neutral lifetime and acceptance policy for the paired FreeP options dialogs.
/// </summary>
public sealed class OptionsDialogSession
{
    private readonly BasicApplicationOptionsDialogSession<FreePOptions> _basicSession;

    public static string RecentFilesCapValidationMessage =>
        $"Enter a whole number between {FreePOptions.MinRecentFilesCap} and {FreePOptions.MaxRecentFilesCap}.";

    public OptionsDialogSession(FreePOptions? options, CultureInfo culture)
    {
        _basicSession = new BasicApplicationOptionsDialogSession<FreePOptions>(
            options,
            culture,
            FreePOptions.FxpDefaultFormat,
            RecentFilesCapValidationMessage);
        Surface = OptionsDialogPlanner.BuildSurface(
            _basicSession.InitialResult,
            _basicSession.SystemLanguageLabel);
    }

    public FreePOptions InitialResult => _basicSession.InitialResult;

    public OptionsDialogSurfaceSpec Surface { get; }

    public BasicApplicationOptionsDialogInitialState InitialState => _basicSession.InitialState;

    public BasicApplicationOptionsDialogCommitPlan<FreePOptions> PlanAcceptance(OptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return _basicSession.PlanAcceptance(new BasicApplicationOptionsDialogInput(
            input.RecentFilesCapText,
            input.Format,
            input.UiLanguage));
    }
}
