using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class SortDialogPlannerTests
{
    [Fact]
    public void TypeChoices_ExposeWordSortTypesInDisplayOrder()
    {
        SortDialogPlanner.TypeChoices.Select(choice => choice.Label)
            .Should().Equal("Text", "Number", "Date");
        SortDialogPlanner.TypeChoices.Select(choice => choice.Value)
            .Should().Equal(SortKind.Text, SortKind.Number, SortKind.Date);
    }

    [Fact]
    public void PromptLabel_DistinguishesParagraphAndTableSortSurfaces()
    {
        SortDialogPlanner.PromptLabel(forTable: false)
            .Should().Be("Sort the selected paragraphs:");
        SortDialogPlanner.PromptLabel(forTable: true)
            .Should().Be("Sort the table rows by the current column:");
    }

    [Fact]
    public void BuildResult_MapsPrimaryAndOptionalKeysWithClampedTypeIndexes()
    {
        var result = SortDialogPlanner.BuildResult(
            key1TypeIndex: 1,
            key1Ascending: false,
            useKey2: true,
            key2TypeIndex: 2,
            key2Ascending: true,
            useKey3: true,
            key3TypeIndex: 99,
            key3Ascending: false,
            caseSensitive: true,
            hasHeaderRow: true);

        result.Kind.Should().Be(SortKind.Number);
        result.Ascending.Should().BeFalse();
        result.Key2.Should().Be(new SortDialogKey(SortKind.Date, Ascending: true));
        result.Key3.Should().Be(new SortDialogKey(SortKind.Date, Ascending: false));
        result.CaseSensitive.Should().BeTrue();
        result.HasHeaderRow.Should().BeTrue();
    }

    [Fact]
    public void BuildResult_OmitsDisabledSecondaryKeys()
    {
        var result = SortDialogPlanner.BuildResult(
            key1TypeIndex: -10,
            key1Ascending: true,
            useKey2: false,
            key2TypeIndex: 1,
            key2Ascending: false,
            useKey3: false,
            key3TypeIndex: 2,
            key3Ascending: false,
            caseSensitive: false,
            hasHeaderRow: false);

        result.Key1.Should().Be(new SortDialogKey(SortKind.Text, Ascending: true));
        result.Key2.Should().BeNull();
        result.Key3.Should().BeNull();
    }
}
