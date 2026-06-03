using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class AutoFilterDialogTests
{
    [Fact]
    public void FilterItems_ReturnsSearchMatchesWithoutChangingSelection()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", true),
            new AutoFilterDialogItem("Banana", "Banana", false),
            new AutoFilterDialogItem("(Blanks)", "", true)
        };

        var filtered = AutoFilterDialog.FilterItems(items, "app");

        filtered.Should().Equal(new AutoFilterDialogItem("Apple", "Apple", true));
        items.Should().Contain(new AutoFilterDialogItem("Banana", "Banana", false));
    }

    [Fact]
    public void SelectAllAndClearAll_UpdateChecklistSelections()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", false),
            new AutoFilterDialogItem("Banana", "Banana", true)
        };

        AutoFilterDialog.SelectAll(items).Should().OnlyContain(item => item.IsSelected);
        AutoFilterDialog.ClearAll(items).Should().OnlyContain(item => !item.IsSelected);
    }

    [Fact]
    public void SetSelectionForSearch_UpdatesVisibleMatchesAndPreservesHiddenSelections()
    {
        var items = new[]
        {
            new AutoFilterDialogItem("Apple", "Apple", false),
            new AutoFilterDialogItem("Apricot", "Apricot", false),
            new AutoFilterDialogItem("Banana", "Banana", true)
        };

        var updated = AutoFilterDialog.SetSelectionForSearch(items, "ap", isSelected: true);

        updated.Should().Equal(
            new AutoFilterDialogItem("Apple", "Apple", true),
            new AutoFilterDialogItem("Apricot", "Apricot", true),
            new AutoFilterDialogItem("Banana", "Banana", true));
    }

    [Fact]
    public void DialogItems_AreMutableForChecklistBinding()
    {
        var item = new AutoFilterDialogItem("Apple", "Apple", true);

        item.IsSelected = false;

        AutoFilterDialog.BuildResult(AutoFilterSortDirection.None, [item], "", "")
            .SelectedValues.Should().BeEmpty();
    }
}
