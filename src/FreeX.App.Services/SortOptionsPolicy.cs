using FreeX.App.Presentation.Dialogs;
using FreeX.Core.Commands;
using CoreSortKey = FreeX.Core.Commands.SortKey;

namespace FreeX.App.Services;

public sealed record SortFirstKeyOrderSelection(
    SortOptionsFirstKeyOrderChoice? SelectedChoice,
    string EditorText);

/// <summary>Shared selection, result, and custom-list policy for native Sort Options dialogs.</summary>
public static class SortOptionsPolicy
{
    public static SortFirstKeyOrderSelection ResolveFirstKeyOrderSelection(
        string? currentValue,
        IReadOnlyList<SortOptionsFirstKeyOrderChoice> choices,
        bool preserveUnlistedEditorText)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count == 0)
            throw new ArgumentException("At least one first-key sort order is required.", nameof(choices));

        foreach (var choice in choices)
        {
            if (string.Equals(choice.Value, currentValue, StringComparison.Ordinal) ||
                string.Equals(choice.Label, currentValue, StringComparison.Ordinal))
            {
                return new SortFirstKeyOrderSelection(choice, choice.Value);
            }
        }

        var normalChoice = choices.FirstOrDefault(choice =>
                string.Equals(
                    choice.Value,
                    SortOptionsDialogCatalog.NormalFirstKeySortOrder,
                    StringComparison.Ordinal))
            ?? choices[0];

        return preserveUnlistedEditorText && !string.IsNullOrWhiteSpace(currentValue)
            ? new SortFirstKeyOrderSelection(null, currentValue)
            : new SortFirstKeyOrderSelection(normalChoice, normalChoice.Value);
    }

    public static SortDialogOptions CreateResult(
        bool caseSensitive,
        bool leftToRight,
        SortOptionsFirstKeyOrderChoice? selectedChoice,
        string? editorText)
    {
        var firstKeySortOrder = selectedChoice?.Value;
        if (string.IsNullOrWhiteSpace(firstKeySortOrder))
        {
            firstKeySortOrder = string.IsNullOrWhiteSpace(editorText)
                ? SortOptionsDialogCatalog.NormalFirstKeySortOrder
                : editorText.Trim();
        }

        return new SortDialogOptions(caseSensitive, leftToRight, firstKeySortOrder);
    }

    public static IReadOnlyList<CoreSortKey> ApplyFirstKeySortOrder(
        IReadOnlyList<CoreSortKey> sortKeys,
        string? firstKeySortOrder)
    {
        ArgumentNullException.ThrowIfNull(sortKeys);

        return CustomSortOrder.TryParse(firstKeySortOrder, out var customOrder)
            ? SortDialogPlanner.ApplyCustomOrderToFirstKey(sortKeys, customOrder)
            : sortKeys;
    }

    public static SortOptions CreateCoreOptions(SortDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SortOptions(options.CaseSensitive, options.LeftToRight);
    }
}
