using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class WorksheetAutoFilterClonerTests
{
    [Fact]
    public void Clone_DeepClonesEveryMutableCollection()
    {
        var source = CreateCompleteAutoFilter();
        var sourceColumn = source.FilterColumns.Single();

        var clone = WorksheetAutoFilterCloner.Clone(source)!;
        var clonedColumn = clone.FilterColumns.Single();

        clone.Should().NotBeSameAs(source);
        clone.Reference.Should().Be(source.Reference);
        clone.NativeXml.Should().Be(source.NativeXml);
        clone.NativeAttributes.Should().NotBeSameAs(source.NativeAttributes);
        clone.NativeChildXmls.Should().NotBeSameAs(source.NativeChildXmls);
        clone.FilterColumns.Should().NotBeSameAs(source.FilterColumns);
        AssertColumnDeepClone(sourceColumn, clonedColumn, expectedColumnId: 3);

        ((Dictionary<string, string>)source.NativeAttributes!)["root"] = "changed";
        ((List<string>)source.NativeChildXmls!).Add("<later/>");
        ((List<string>)sourceColumn.Values).Add("later");
        ((List<WorksheetAutoFilterCustomFilterModel>)sourceColumn.CustomFilters).Clear();
        ((Dictionary<string, string>)sourceColumn.NativeCustomFiltersAttributes!)["customFilters"] = "changed";
        ((Dictionary<string, string>)sourceColumn.Top10!.NativeAttributes!)["top10"] = "changed";
        ((Dictionary<string, string>)sourceColumn.DynamicFilter!.NativeAttributes!)["dynamic"] = "changed";
        ((Dictionary<string, string>)sourceColumn.ColorFilter!.NativeAttributes!)["color"] = "changed";
        ((Dictionary<string, string>)sourceColumn.IconFilter!.NativeAttributes!)["icon"] = "changed";
        ((List<WorksheetAutoFilterDateGroupItemModel>)sourceColumn.DateGroups).Clear();
        ((Dictionary<string, string>)sourceColumn.NativeFiltersAttributes!)["filters"] = "changed";
        ((List<string>)sourceColumn.NativeFilterXmls).Add("<laterFilter/>");
        ((Dictionary<string, string>)sourceColumn.NativeAttributes!)["column"] = "changed";

        clone.NativeAttributes!["root"].Should().Be("root");
        clone.NativeChildXmls.Should().Equal("<child/>");
        clonedColumn.Values.Should().Equal("value");
        clonedColumn.CustomFilters.Should().ContainSingle();
        clonedColumn.NativeCustomFiltersAttributes!["customFilters"].Should().Be("customFilters");
        clonedColumn.Top10!.NativeAttributes!["top10"].Should().Be("top10");
        clonedColumn.DynamicFilter!.NativeAttributes!["dynamic"].Should().Be("dynamic");
        clonedColumn.ColorFilter!.NativeAttributes!["color"].Should().Be("color");
        clonedColumn.IconFilter!.NativeAttributes!["icon"].Should().Be("icon");
        clonedColumn.DateGroups.Should().ContainSingle();
        clonedColumn.NativeFiltersAttributes!["filters"].Should().Be("filters");
        clonedColumn.NativeFilterXmls.Should().Equal("<filter/>");
        clonedColumn.NativeAttributes!["column"].Should().Be("column");

        source.NativeAttributes.ContainsKey("ROOT").Should().BeTrue();
        clone.NativeAttributes.ContainsKey("ROOT").Should().BeFalse(
            "cloned native metadata must retain the canonical ordinal comparer");
    }

    [Fact]
    public void CloneColumn_ColumnIdOverrideChangesOnlyIdAndStillDeepClones()
    {
        var source = CreateCompleteAutoFilter().FilterColumns.Single();

        var clone = WorksheetAutoFilterCloner.CloneColumn(source, columnId: 9);
        var cloneWithoutOverride = WorksheetAutoFilterCloner.CloneColumn(source);

        source.ColumnId.Should().Be(3);
        AssertColumnDeepClone(source, clone, expectedColumnId: 9);
        cloneWithoutOverride.ColumnId.Should().Be(3);
    }

    [Fact]
    public void Clone_NullAndNonNullEmptyMetadataPreserveTheirShape()
    {
        WorksheetAutoFilterCloner.Clone(null).Should().BeNull();

        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var source = new WorksheetAutoFilterModel(null, null)
        {
            NativeAttributes = attributes,
            NativeChildXmls = new List<string>()
        };

        var clone = WorksheetAutoFilterCloner.Clone(source)!;

        clone.NativeAttributes.Should().NotBeNull().And.BeEmpty();
        clone.NativeAttributes.Should().NotBeSameAs(attributes);
        clone.NativeChildXmls.Should().NotBeNull().And.BeEmpty();
        clone.NativeChildXmls.Should().NotBeSameAs(source.NativeChildXmls);
        clone.FilterColumns.Should().BeEmpty();
        ((Dictionary<string, string>)clone.NativeAttributes!).Comparer.Should().BeSameAs(StringComparer.Ordinal);
    }

    private static void AssertColumnDeepClone(
        WorksheetAutoFilterColumnModel source,
        WorksheetAutoFilterColumnModel clone,
        int expectedColumnId)
    {
        clone.Should().NotBeSameAs(source);
        clone.ColumnId.Should().Be(expectedColumnId);
        clone.Values.Should().Equal(source.Values);
        clone.Values.Should().NotBeSameAs(source.Values);
        clone.CustomFilters.Should().BeEquivalentTo(source.CustomFilters);
        clone.CustomFilters.Should().NotBeSameAs(source.CustomFilters);
        clone.CustomFilters.Single().Should().NotBeSameAs(source.CustomFilters.Single());
        AssertDictionaryClone(source.CustomFilters.Single().NativeAttributes, clone.CustomFilters.Single().NativeAttributes);
        AssertDictionaryClone(source.NativeCustomFiltersAttributes, clone.NativeCustomFiltersAttributes);

        clone.Top10.Should().BeEquivalentTo(source.Top10);
        clone.Top10.Should().NotBeSameAs(source.Top10);
        AssertDictionaryClone(source.Top10!.NativeAttributes, clone.Top10!.NativeAttributes);
        clone.DynamicFilter.Should().BeEquivalentTo(source.DynamicFilter);
        clone.DynamicFilter.Should().NotBeSameAs(source.DynamicFilter);
        AssertDictionaryClone(source.DynamicFilter!.NativeAttributes, clone.DynamicFilter!.NativeAttributes);
        clone.ColorFilter.Should().BeEquivalentTo(source.ColorFilter);
        clone.ColorFilter.Should().NotBeSameAs(source.ColorFilter);
        AssertDictionaryClone(source.ColorFilter!.NativeAttributes, clone.ColorFilter!.NativeAttributes);
        clone.IconFilter.Should().BeEquivalentTo(source.IconFilter);
        clone.IconFilter.Should().NotBeSameAs(source.IconFilter);
        AssertDictionaryClone(source.IconFilter!.NativeAttributes, clone.IconFilter!.NativeAttributes);

        clone.DateGroups.Should().BeEquivalentTo(source.DateGroups);
        clone.DateGroups.Should().NotBeSameAs(source.DateGroups);
        clone.DateGroups.Single().Should().NotBeSameAs(source.DateGroups.Single());
        AssertDictionaryClone(source.DateGroups.Single().NativeAttributes, clone.DateGroups.Single().NativeAttributes);
        AssertDictionaryClone(source.NativeFiltersAttributes, clone.NativeFiltersAttributes);
        clone.NativeFilterXmls.Should().Equal(source.NativeFilterXmls);
        clone.NativeFilterXmls.Should().NotBeSameAs(source.NativeFilterXmls);
        AssertDictionaryClone(source.NativeAttributes, clone.NativeAttributes);
    }

    private static void AssertDictionaryClone(
        IReadOnlyDictionary<string, string>? source,
        IReadOnlyDictionary<string, string>? clone)
    {
        clone.Should().BeEquivalentTo(source);
        clone.Should().NotBeSameAs(source);
    }

    private static WorksheetAutoFilterModel CreateCompleteAutoFilter()
    {
        static Dictionary<string, string> Attributes(string name) =>
            new(StringComparer.OrdinalIgnoreCase) { [name] = name };

        var column = new WorksheetAutoFilterColumnModel(
            ColumnId: 3,
            Values: new List<string> { "value" },
            IncludeBlank: true,
            CustomFilters: new List<WorksheetAutoFilterCustomFilterModel>
            {
                new("greaterThan", "4", Attributes("custom"))
            },
            CustomFiltersAnd: true,
            CustomFiltersAndRaw: "1",
            NativeCustomFiltersAttributes: Attributes("customFilters"),
            Top10: new WorksheetAutoFilterTop10Model(Value: 10, NativeAttributes: Attributes("top10")),
            DynamicFilter: new WorksheetAutoFilterDynamicFilterModel(
                Type: "aboveAverage",
                NativeAttributes: Attributes("dynamic")),
            ColorFilter: new WorksheetAutoFilterColorFilterModel(
                DifferentialFormatId: 2,
                NativeAttributes: Attributes("color"),
                Color: new CellColor(1, 2, 3)),
            IconFilter: new WorksheetAutoFilterIconFilterModel(
                IconSet: "3Arrows",
                IconId: 1,
                NativeAttributes: Attributes("icon")),
            DateGroups: new List<WorksheetAutoFilterDateGroupItemModel>
            {
                new(Year: 2026, Month: 8, DateTimeGrouping: "month", NativeAttributes: Attributes("date"))
            },
            NativeFiltersAttributes: Attributes("filters"),
            NativeFilterXmls: new List<string> { "<filter/>" },
            NativeAttributes: Attributes("column"));

        var autoFilter = new WorksheetAutoFilterModel("A1:B9", "<autoFilter/>")
        {
            NativeAttributes = Attributes("root"),
            NativeChildXmls = new List<string> { "<child/>" }
        };
        autoFilter.FilterColumns.Add(column);
        return autoFilter;
    }
}
