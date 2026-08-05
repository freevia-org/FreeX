using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Dialogs;

public sealed record MultilevelListDialogAcceptance(
    MultilevelListDefinition? Definition,
    MultilevelListDialogValidation? Validation)
{
    public bool IsAccepted => Definition is not null && Validation is null;
}

public sealed record MultilevelListCommitPlan(MultilevelListDefinition? Definition)
{
    public bool ShouldApply => Definition is not null;
}

/// <summary>
/// Owns the renderer-neutral editable state and acceptance policy for Define New Multilevel List.
/// </summary>
public sealed class MultilevelListDialogSession
{
    private readonly CultureInfo _culture;

    public MultilevelListDialogSession(
        IReadOnlyList<ListNumberFormat>? currentNumberFormats,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        _culture = culture;
        InitialState = MultilevelListDialogPlanner.BuildInitialState(currentNumberFormats, culture);
        State = new MultilevelListDialogInput(
            InitialState.LevelsIndex,
            InitialState.Level0StartAtText,
            InitialState.Level1StartAtText,
            InitialState.Level0FormatIndex,
            InitialState.Level1FormatIndex,
            InitialState.Level2FormatIndex);
        LevelChoices = Enumerable.Range(1, MultilevelListDialogPlanner.MaximumLevelCount)
            .Select(value => value.ToString(culture))
            .ToArray();
    }

    public IReadOnlyList<string> LevelChoices { get; }

    public IReadOnlyList<MultilevelListNumberFormatChoice> NumberFormatChoices =>
        MultilevelListDialogPlanner.NumberFormatChoices;

    public MultilevelListDialogInitialState InitialState { get; }

    public MultilevelListDialogInput State { get; private set; }

    public void UpdateLevels(int selectedIndex) =>
        State = State with { LevelsIndex = selectedIndex };

    public void UpdateLevel0StartAt(string? text) =>
        State = State with { Level0StartAtText = text };

    public void UpdateLevel1StartAt(string? text) =>
        State = State with { Level1StartAtText = text };

    public void UpdateLevel0Format(int selectedIndex) =>
        State = State with { Level0FormatIndex = selectedIndex };

    public void UpdateLevel1Format(int selectedIndex) =>
        State = State with { Level1FormatIndex = selectedIndex };

    public void UpdateLevel2Format(int selectedIndex) =>
        State = State with { Level2FormatIndex = selectedIndex };

    public MultilevelListDialogAcceptance PlanAcceptance() =>
        MultilevelListDialogPlanner.TryBuildResult(
            State,
            _culture,
            out var result,
            out var validation)
            ? new MultilevelListDialogAcceptance(result, Validation: null)
            : new MultilevelListDialogAcceptance(Definition: null, validation);
}
