namespace FreeW.App.Presentation.Ribbon;

public enum ThesaurusPaneActionKind
{
    Replace,
    Copy
}

public sealed record ThesaurusPaneActionIntent(
    ThesaurusPaneActionKind Kind,
    string Text);

public sealed record ThesaurusPaneActionAvailability(
    ThesaurusPaneActionIntent? ReplaceIntent,
    ThesaurusPaneActionIntent? CopyIntent)
{
    public bool CanReplace => ReplaceIntent is not null;
    public bool CanCopy => CopyIntent is not null;
}

public sealed record ThesaurusPaneTransition(
    bool IsVisible,
    bool VisibilityChanged,
    bool ShouldRender,
    ThesaurusDisplayPlan DisplayPlan);

/// <summary>
/// Owns renderer-neutral visibility, lookup, and action decisions for the modeless thesaurus pane.
/// Hosts retain current-word and replacement adapters, clipboard access, focus, and native rendering.
/// </summary>
public sealed class ThesaurusPaneSession
{
    public bool IsVisible { get; private set; }

    public ThesaurusDisplayPlan CurrentPlan { get; private set; } =
        ThesaurusPresentationPlanner.Build(null, null);

    public string CurrentWord => CurrentPlan.SourceWord;

    public ThesaurusPaneTransition Toggle(string? currentWord) =>
        IsVisible ? Hide() : Show(currentWord);

    public ThesaurusPaneTransition Show(string? currentWord)
    {
        var visibilityChanged = !IsVisible;
        IsVisible = true;
        return Lookup(currentWord, visibilityChanged);
    }

    public ThesaurusPaneTransition Hide()
    {
        var visibilityChanged = IsVisible;
        IsVisible = false;
        return BuildTransition(visibilityChanged, shouldRender: false);
    }

    public ThesaurusPaneTransition Refresh(string? currentWord) =>
        IsVisible
            ? Lookup(currentWord, visibilityChanged: false)
            : BuildTransition(visibilityChanged: false, shouldRender: false);

    public ThesaurusPaneTransition CompleteReplacement(bool replaced, string? currentWord) =>
        replaced
            ? Refresh(currentWord)
            : BuildTransition(visibilityChanged: false, shouldRender: false);

    public ThesaurusPaneActionAvailability PlanAction(
        ThesaurusActionRow action,
        bool canReplace,
        bool canCopy)
    {
        ArgumentNullException.ThrowIfNull(action);

        var replaceIntent = action.CanInsert() && canReplace
            ? new ThesaurusPaneActionIntent(ThesaurusPaneActionKind.Replace, action.DisplayText)
            : null;
        var copyIntent = !string.IsNullOrWhiteSpace(action.DisplayText) && canCopy
            ? new ThesaurusPaneActionIntent(ThesaurusPaneActionKind.Copy, action.DisplayText)
            : null;
        return new ThesaurusPaneActionAvailability(replaceIntent, copyIntent);
    }

    private ThesaurusPaneTransition Lookup(string? currentWord, bool visibilityChanged)
    {
        CurrentPlan = ThesaurusPresentationPlanner.Lookup(currentWord);
        return BuildTransition(visibilityChanged, shouldRender: true);
    }

    private ThesaurusPaneTransition BuildTransition(bool visibilityChanged, bool shouldRender) =>
        new(IsVisible, visibilityChanged, shouldRender, CurrentPlan);
}
