using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R107: Custom AutoFilter's second (And/Or) criterion row reused the exact same operator list as row 1
/// (<see cref="AutoFilterMenuCatalog"/>'s Between/Top 10/Bottom 10/Top 10 Percent/Bottom 10 Percent/Above
/// Average/Below Average entries included) even though row 2 only has a single plain value textbox and no
/// min/max or count widgets. Picking one of those for row 2 either silently dropped the second criterion
/// (Between/Top-N, whose value box is always left blank so <c>BuildCompositeCriteriaText</c> falls back to
/// the first criterion alone) or produced a composite "and:.../or:..." string the downstream
/// FilterCriterionInputParser rejects outright (Above/Below Average, which never appear inside a composite
/// pair). Excel's own Custom AutoFilter dialog never offers any of these as a per-row operator at all --
/// they are one-shot menu actions, never combinable with a second criterion. These tests pin row 2's
/// operator list to exclude them while leaving row 1 (which does have dedicated widgets for them) untouched.
/// </summary>
public sealed partial class AutoFilterDialogTests
{
    [Fact]
    public void R107_SecondCriteriaOperatorDropdown_ExcludesBetweenTopBottomAndAverageOptions()
    {
        var menuPlan = new AutoFilterMenuPlan(
            "Amount",
            AutoFilterMenuFilterKind.Number,
            [
                new AutoFilterMenuEntry("Sort Smallest to Largest", AutoFilterMenuEntryKind.SortAscending),
                new AutoFilterMenuEntry(new AutoFilterChecklistItem("10", "10"))
            ]);

        StaTestRunner.Run(() =>
        {
            var dialog = new AutoFilterDialog(menuPlan);
            try
            {
                var row1Prefixes = GetOperatorComboBoxPrefixes(dialog, "_criteriaOperatorBox");
                var row2Prefixes = GetOperatorComboBoxPrefixes(dialog, "_criteriaOperatorBox2");

                // No-regression: row 1 has dedicated _betweenMinBox/_betweenMaxBox/_topBottomCountBox
                // widgets, so it must keep the full Excel-typed-filter operator list.
                row1Prefixes.Should().Contain("between:");
                row1Prefixes.Should().Contain("top:");
                row1Prefixes.Should().Contain("bottom:");
                row1Prefixes.Should().Contain("toppercent:");
                row1Prefixes.Should().Contain("bottompercent:");
                row1Prefixes.Should().Contain("above average");
                row1Prefixes.Should().Contain("below average");

                // The fix under test: row 2 has no widgets to collect these, and Excel never combines
                // them with a second And/Or criterion either.
                row2Prefixes.Should().NotContain("between:");
                row2Prefixes.Should().NotContain("top:");
                row2Prefixes.Should().NotContain("bottom:");
                row2Prefixes.Should().NotContain("toppercent:");
                row2Prefixes.Should().NotContain("bottompercent:");
                row2Prefixes.Should().NotContain("above average");
                row2Prefixes.Should().NotContain("below average");

                // Row 2 must keep every operator it genuinely can combine (plain comparisons and the
                // no-value blank/nonblank options).
                row2Prefixes.Should().Contain("=");
                row2Prefixes.Should().Contain("<>");
                row2Prefixes.Should().Contain(">");
                row2Prefixes.Should().Contain(">=");
                row2Prefixes.Should().Contain("<");
                row2Prefixes.Should().Contain("<=");
                row2Prefixes.Should().Contain("blank");
                row2Prefixes.Should().Contain("nonblank");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void R107_SecondCriteriaOperatorDropdown_TextFamilyUnaffectedByExclusion()
    {
        // Sibling/no-regression path: the Text filter family has no Between/Top-N/Average entries at
        // all, so row 2 must keep its full operator list unchanged by the new exclusion.
        var menuPlan = new AutoFilterMenuPlan(
            "Region",
            AutoFilterMenuFilterKind.Text,
            [
                new AutoFilterMenuEntry("Sort A to Z", AutoFilterMenuEntryKind.SortAscending),
                new AutoFilterMenuEntry(new AutoFilterChecklistItem("West", "West"))
            ]);

        StaTestRunner.Run(() =>
        {
            var dialog = new AutoFilterDialog(menuPlan);
            try
            {
                var row1Prefixes = GetOperatorComboBoxPrefixes(dialog, "_criteriaOperatorBox");
                var row2Prefixes = GetOperatorComboBoxPrefixes(dialog, "_criteriaOperatorBox2");

                row2Prefixes.Should().BeEquivalentTo(row1Prefixes);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static List<string> GetOperatorComboBoxPrefixes(AutoFilterDialog dialog, string fieldName)
    {
        var field = typeof(AutoFilterDialog).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"AutoFilterDialog should still declare a private field named {fieldName}");
        var comboBox = (ComboBox)field!.GetValue(dialog)!;
        return comboBox.ItemsSource!
            .Cast<AutoFilterCriteriaOption>()
            .Select(option => option.CriteriaPrefix)
            .ToList();
    }
}
