using System.Globalization;

namespace Free.Shared.AppServices;

public sealed record BasicApplicationOptionsDialogInput(
    string? RecentFilesCapText,
    string? DefaultSaveFormat,
    string? UiLanguage);

public enum BasicApplicationOptionsValidationTarget
{
    RecentFilesCap,
}

public sealed record BasicApplicationOptionsDialogValidation(
    BasicApplicationOptionsValidationTarget Target,
    string Message);

public sealed record BasicApplicationOptionsDialogInitialState(
    string RecentFilesCapText,
    string? SelectedFormat,
    string UiLanguage);

public sealed record BasicApplicationOptionsDialogCommitPlan<TOptions>(
    bool ShouldApply,
    bool ShouldPersist,
    TOptions? Result,
    BasicApplicationOptionsDialogValidation? Validation)
    where TOptions : class, IBasicApplicationOptions, new();

/// <summary>
/// Owns the common lifetime of the sister apps' basic Options fields. Product sessions supply their
/// default format and validation text, then optionally decorate the normalized accepted result.
/// </summary>
public sealed class BasicApplicationOptionsDialogSession<TOptions>
    where TOptions : class, IBasicApplicationOptions, new()
{
    private readonly string _defaultSaveFormat;
    private readonly string _recentFilesCapValidationMessage;

    public BasicApplicationOptionsDialogSession(
        TOptions? options,
        CultureInfo culture,
        string defaultSaveFormat,
        string recentFilesCapValidationMessage)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultSaveFormat);
        ArgumentException.ThrowIfNullOrWhiteSpace(recentFilesCapValidationMessage);

        _defaultSaveFormat = defaultSaveFormat;
        _recentFilesCapValidationMessage = recentFilesCapValidationMessage;
        InitialResult = options ?? new TOptions();

        var normalized = BuildResult(
            InitialResult.RecentFilesCap,
            InitialResult.DefaultSaveFormat,
            InitialResult.UiLanguage,
            defaultSaveFormat);
        InitialState = new BasicApplicationOptionsDialogInitialState(
            normalized.RecentFilesCap.ToString(culture),
            defaultSaveFormat,
            normalized.UiLanguage);
        SystemLanguageLabel = string.IsNullOrEmpty(culture.Name) ? "invariant" : culture.Name;
    }

    public TOptions InitialResult { get; }

    public BasicApplicationOptionsDialogInitialState InitialState { get; }

    public string SystemLanguageLabel { get; }

    public BasicApplicationOptionsDialogCommitPlan<TOptions> PlanAcceptance(
        BasicApplicationOptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!ApplicationOptionsNormalizer.TryParseRecentFilesCap(input.RecentFilesCapText, out var cap))
        {
            return new BasicApplicationOptionsDialogCommitPlan<TOptions>(
                ShouldApply: false,
                ShouldPersist: false,
                Result: null,
                Validation: new BasicApplicationOptionsDialogValidation(
                    BasicApplicationOptionsValidationTarget.RecentFilesCap,
                    _recentFilesCapValidationMessage));
        }

        return new BasicApplicationOptionsDialogCommitPlan<TOptions>(
            ShouldApply: true,
            ShouldPersist: true,
            Result: BuildResult(cap, input.DefaultSaveFormat, input.UiLanguage, _defaultSaveFormat),
            Validation: null);
    }

    public static TOptions BuildResult(
        int recentFilesCap,
        string? defaultSaveFormat,
        string? uiLanguage,
        string fallbackSaveFormat)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackSaveFormat);

        var result = new TOptions
        {
            RecentFilesCap = recentFilesCap,
            DefaultSaveFormat = string.IsNullOrWhiteSpace(defaultSaveFormat)
                ? fallbackSaveFormat
                : defaultSaveFormat,
            UiLanguage = uiLanguage ?? ApplicationOptionsNormalizer.SystemDefaultLanguage,
        };
        result.Normalize();
        return result;
    }
}
