using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

public delegate bool RibbonAdaptiveWpfFallbackStep(
    RibbonAdaptiveGroupState[] states,
    bool preserveFirstGroup,
    IReadOnlySet<int>? protectedGroupIndexes,
    out int changedIndex,
    out RibbonAdaptiveGroupState previousState);

public delegate bool RibbonAdaptiveWpfStateApplier(
    int index,
    RibbonAdaptiveGroupState state,
    RibbonAdaptiveGroupState previousState);

/// <summary>
/// Renderer-side measured correction loops. Apps supply profile-aware transition selection and a visual
/// state adapter; this class owns the generic converge/rollback mechanics.
/// </summary>
public static class RibbonAdaptiveWpfFallback
{
    public static bool ApplyFallbackUntilFits(
        RibbonAdaptiveGroupState[] states,
        bool preserveFirstGroup,
        IReadOnlySet<int>? protectedGroupIndexes,
        Func<IReadOnlyList<RibbonAdaptiveGroupState>, bool> measureOverflows,
        RibbonAdaptiveWpfFallbackStep tryFallback,
        RibbonAdaptiveWpfStateApplier applyState)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(measureOverflows);
        ArgumentNullException.ThrowIfNull(tryFallback);
        ArgumentNullException.ThrowIfNull(applyState);

        var appliedCorrection = false;
        while (measureOverflows(states))
        {
            if (!tryFallback(
                    states,
                    preserveFirstGroup,
                    protectedGroupIndexes,
                    out var changedIndex,
                    out var previousState))
            {
                break;
            }

            appliedCorrection |= applyState(changedIndex, states[changedIndex], previousState);
        }

        return appliedCorrection;
    }

    public static bool ApplyExpansionPass(
        RibbonAdaptiveGroupState[] states,
        IReadOnlyList<int> expandableIndexes,
        Func<IReadOnlyList<RibbonAdaptiveGroupState>, bool> measureOverflows,
        RibbonAdaptiveWpfStateApplier applyState)
    {
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(expandableIndexes);
        ArgumentNullException.ThrowIfNull(measureOverflows);
        ArgumentNullException.ThrowIfNull(applyState);

        var appliedCorrection = false;
        var madeProgress = true;
        while (madeProgress)
        {
            madeProgress = false;
            for (var expandableIndex = 0; expandableIndex < expandableIndexes.Count; expandableIndex++)
            {
                var index = expandableIndexes[expandableIndex];
                if (index < 0 || index >= states.Length)
                    continue;

                var currentState = states[index];
                if (!RibbonAdaptiveStateTransitions.TryGetNextExpandedState(currentState, out var expandedState))
                    continue;

                states[index] = expandedState;
                appliedCorrection |= applyState(index, expandedState, currentState);
                if (!measureOverflows(states))
                {
                    madeProgress = true;
                    continue;
                }

                states[index] = currentState;
                appliedCorrection |= applyState(index, currentState, expandedState);
            }
        }

        return appliedCorrection;
    }
}
