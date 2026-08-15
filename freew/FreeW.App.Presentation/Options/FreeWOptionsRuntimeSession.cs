using System.Linq;
using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Options;

public sealed record FreeWEditorTypingOptionsPlan(
    bool AutoCorrectEnabled,
    AutoFormatOptions AutoFormat,
    AutoCorrectOptions AutoCorrect);

public sealed record FreeWOptionsPersistOutcome(
    FreeWEditorTypingOptionsPlan EditorTypingOptions,
    bool Persisted);

/// <summary>
/// Owns the mutable application-options instance consumed by a running FreeW shell and projects the
/// editor settings that native hosts apply to their platform-specific work area.
/// </summary>
public sealed class FreeWOptionsRuntimeSession
{
    public FreeWOptionsRuntimeSession(FreeWOptions liveOptions)
    {
        LiveOptions = liveOptions ?? throw new ArgumentNullException(nameof(liveOptions));
        LiveOptions.Normalize();
    }

    public FreeWOptions LiveOptions { get; }

    public FreeWEditorTypingOptionsPlan EditorTypingOptions => new(
        LiveOptions.AutoCorrectEnabled,
        LiveOptions.AutoFormat,
        LiveOptions.AutoCorrect);

    public FreeWEditorTypingOptionsPlan Apply(FreeWOptions editedOptions)
    {
        ArgumentNullException.ThrowIfNull(editedOptions);

        LiveOptions.RecentFilesCap = editedOptions.RecentFilesCap;
        LiveOptions.DefaultSaveFormat = editedOptions.DefaultSaveFormat;
        LiveOptions.UiLanguage = editedOptions.UiLanguage;
        LiveOptions.AutoCorrectEnabled = editedOptions.AutoCorrectEnabled;
        LiveOptions.AutoFormat = editedOptions.AutoFormat;
        LiveOptions.AutoCorrect = editedOptions.AutoCorrect;
        LiveOptions.Normalize();

        return EditorTypingOptions;
    }

    /// <summary>
    /// Applies the edited options to <see cref="LiveOptions"/> (as <see cref="Apply"/>) and then persists
    /// them. When <paramref name="reloadFresh"/> is supplied, reloads the freshest on-disk snapshot
    /// immediately before saving and copies across only the fields that actually differ between this
    /// dialog session's open-time snapshot (<see cref="LiveOptions"/> as it stood when this method was
    /// called) and <paramref name="editedOptions"/> -- so a concurrently running FreeW window or process
    /// that already persisted a change to a field this session never touched is not silently reverted
    /// (last-writer-wins lost update). Mirrors FreeX's <c>FreeXOptionsRuntimeSession.CommitDialog</c> /
    /// <c>OptionsDialogPlanner.MergeOntoFreshLoad</c>. Omitting <paramref name="reloadFresh"/> preserves the
    /// previous whole-document-overwrite behavior for callers that only need an in-memory apply (e.g.
    /// tests).
    /// </summary>
    public FreeWOptionsPersistOutcome ApplyAndPersist(
        FreeWOptions editedOptions,
        Func<FreeWOptions, bool> persist,
        Func<FreeWOptions>? reloadFresh = null)
    {
        ArgumentNullException.ThrowIfNull(editedOptions);
        ArgumentNullException.ThrowIfNull(persist);

        var openTimeSnapshot = reloadFresh is null ? null : LiveOptions.Clone();
        var plan = Apply(editedOptions);

        if (reloadFresh is null || openTimeSnapshot is null)
            return new FreeWOptionsPersistOutcome(plan, persist(LiveOptions));

        var fresh = reloadFresh();
        fresh.Normalize();
        MergeOntoFreshLoad(fresh, openTimeSnapshot, editedOptions);

        var persisted = persist(fresh);
        if (persisted)
            CopyInto(LiveOptions, fresh);

        return new FreeWOptionsPersistOutcome(plan, persisted);
    }

    private static void MergeOntoFreshLoad(FreeWOptions freshFromDisk, FreeWOptions openTimeSnapshot, FreeWOptions edited)
    {
        BasicApplicationOptionsMerge.MergeOntoFreshLoad(freshFromDisk, openTimeSnapshot, edited);

        if (edited.AutoCorrectEnabled != openTimeSnapshot.AutoCorrectEnabled)
            freshFromDisk.AutoCorrectEnabled = edited.AutoCorrectEnabled;
        if (edited.AutoFormat != openTimeSnapshot.AutoFormat)
            freshFromDisk.AutoFormat = edited.AutoFormat;
        if (!AutoCorrectOptionsEqual(edited.AutoCorrect, openTimeSnapshot.AutoCorrect))
            freshFromDisk.AutoCorrect = edited.AutoCorrect;

        freshFromDisk.Normalize();
    }

    private static bool AutoCorrectOptionsEqual(AutoCorrectOptions? a, AutoCorrectOptions? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;

        return a.CorrectTwoInitialCapitals == b.CorrectTwoInitialCapitals
            && a.CapitalizeDayNames == b.CapitalizeDayNames
            && a.ReplaceText == b.ReplaceText
            && a.Replacements.SequenceEqual(b.Replacements);
    }

    private static void CopyInto(FreeWOptions target, FreeWOptions source)
    {
        target.RecentFilesCap = source.RecentFilesCap;
        target.DefaultSaveFormat = source.DefaultSaveFormat;
        target.UiLanguage = source.UiLanguage;
        target.AutoCorrectEnabled = source.AutoCorrectEnabled;
        target.AutoFormat = source.AutoFormat;
        target.AutoCorrect = source.AutoCorrect;
    }
}
