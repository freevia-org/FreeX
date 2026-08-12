using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services;

/// <summary>
/// Portable predicates for the two in-grid DV affordances that appear on the SELECTED cell:
/// (1) the dropdown-arrow button (List validation with ShowDropdown = true, matching Excel where
///     showDropDown absent/false = show, present/true = hide — the field is INVERTED in OOXML).
///     In FreeX the model already normalises this: <see cref="DataValidation.ShowDropdown"/> ==
///     true means show the dropdown, matching the logical/intended meaning.
/// (2) the input-message tooltip (any DV rule whose ShowInputMessage is true and has a non-empty
///     PromptTitle or PromptMessage).
/// </summary>
public static class DataValidationAffordancePlanner
{
    /// <summary>Arrow button width in logical (unzoomed) pixels, matching Excel's ~16 px.</summary>
    public const double ArrowButtonWidth = 16.0;

    /// <summary>
    /// Returns true when the selected cell should display a dropdown-arrow button.
    /// Predicate: the cell has at least one DV rule of type List with ShowDropdown == true.
    /// </summary>
    public static bool ShouldShowDropdownArrow(Sheet sheet, CellAddress activeCell) =>
        HasListDropdownRule(DataValidationService.GetApplicable(sheet, activeCell));

    /// <summary>
    /// Returns the pixel rect of the arrow button given the cell's bounding box.
    /// The button is 16 px wide and flush with the cell's right edge.
    /// </summary>
    public static DvArrowButtonRect GetArrowButtonRect(
        double cellLeft, double cellTop, double cellWidth, double cellHeight)
    {
        var btnWidth = Math.Min(ArrowButtonWidth, cellWidth);
        var left = cellLeft + cellWidth - btnWidth;
        return new DvArrowButtonRect(left, cellTop, btnWidth, cellHeight);
    }

    /// <summary>
    /// Returns the <see cref="DataValidationService.InputPrompt"/> for the active cell if it should
    /// be shown (ShowInputMessage == true, and at least one of title/message is non-empty).
    /// Returns null if no prompt should be displayed.
    /// </summary>
    public static DataValidationService.InputPrompt? GetInputMessagePrompt(
        Sheet sheet, CellAddress activeCell) =>
        DataValidationService.GetInputPrompt(sheet, activeCell);

    // ── Private helpers ──────────────────────────────────────────────────────

    private static bool HasListDropdownRule(IEnumerable<DataValidation> rules)
    {
        foreach (var rule in rules)
        {
            // ShowDropdown == true means show the in-cell dropdown.
            // (Excel's OOXML showDropDown attribute is INVERTED — absent = show, "1"/"true" = hide —
            //  but FreeX normalises this at load: DataValidation.ShowDropdown = true means SHOW.)
            if (rule.Type == DvType.List && rule.ShowDropdown)
                return true;
        }

        return false;
    }
}

/// <summary>Pixel bounds of the DV dropdown-arrow button (logical, unzoomed coordinates).</summary>
public readonly record struct DvArrowButtonRect(double Left, double Top, double Width, double Height);
