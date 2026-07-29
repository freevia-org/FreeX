using System.Windows.Controls;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R91-commands-sort-customlist-5-3 (src/FreeX.App.Host/SortOptionsDialog.cs).
///
/// Before the fix: SortOptionsDialog's "First key sort order" combo box only ever offered the 4
/// hardcoded built-in day/month lists (plus "Normal") -- there was no way to author or select any
/// other Custom List (e.g. "Low, Medium, High, Critical"), even though the underlying
/// CustomSortOrder.TryParse/Compare mechanism already fully supports an arbitrary comma-separated
/// list. Re-opening the dialog after a custom list had somehow been used also silently reverted the
/// displayed choice back to "Normal" (NormalizeFirstKeySortOrder only recognized the 4 built-ins).
///
/// After the fix, the combo is directly editable: typing any text flows straight through to
/// SortDialogOptions.FirstKeySortOrder, and a previously-set custom (non-built-in) value is
/// preserved and shown verbatim instead of being silently discarded.
/// </summary>
public sealed partial class SortDialogTests
{
    [Fact]
    public void SortOptionsDialog_TypingACustomList_FlowsThroughAsTheFirstKeySortOrder()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SortOptionsDialog();
            dialog.Loaded += (_, _) =>
            {
                var combo = GetControl<ComboBox>(dialog, "_firstKeySortOrderBox");
                combo.IsEditable.Should().BeTrue("the user must be able to author their own custom list directly");
                combo.Text = "Low, Medium, High, Critical";
                dialog.Dispatcher.BeginInvoke(() => ClickDefaultButton(dialog));
            };

            dialog.ShowDialog().Should().BeTrue();

            dialog.Result.FirstKeySortOrder.Should().Be("Low, Medium, High, Critical");
        });
    }

    [Fact]
    public void SortOptionsDialog_ReopenedWithAPreviouslyTypedCustomList_ShowsItInsteadOfRevertingToNormal()
    {
        StaTestRunner.Run(() =>
        {
            var current = new SortDialogOptions(
                CaseSensitive: false,
                LeftToRight: false,
                FirstKeySortOrder: "Small, Medium, Large, X-Large");
            var dialog = new SortOptionsDialog(current);
            dialog.Loaded += (_, _) =>
            {
                var combo = GetControl<ComboBox>(dialog, "_firstKeySortOrderBox");
                combo.Text.Should().Be("Small, Medium, Large, X-Large",
                    "a previously-authored custom list must be shown verbatim, not silently reverted to Normal");
                dialog.Dispatcher.BeginInvoke(() => ClickDefaultButton(dialog));
            };

            dialog.ShowDialog().Should().BeTrue();

            dialog.Result.FirstKeySortOrder.Should().Be("Small, Medium, Large, X-Large");
        });
    }

    // Sibling no-regression: choosing one of the 4 hardcoded built-in lists must still work exactly
    // as before -- the fix only adds free-text authoring, it must not disturb picking a built-in.
    [Fact]
    public void SortOptionsDialog_SelectingABuiltInList_StillWorksUnchanged()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new SortOptionsDialog();
            dialog.Loaded += (_, _) =>
            {
                var combo = GetControl<ComboBox>(dialog, "_firstKeySortOrderBox");
                combo.SelectedValue = "January, February, March, April, May, June, July, August, September, October, November, December";
                dialog.Dispatcher.BeginInvoke(() => ClickDefaultButton(dialog));
            };

            dialog.ShowDialog().Should().BeTrue();

            dialog.Result.FirstKeySortOrder.Should().Be(
                "January, February, March, April, May, June, July, August, September, October, November, December");
        });
    }
}
