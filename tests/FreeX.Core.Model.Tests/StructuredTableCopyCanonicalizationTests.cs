using System.Reflection;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class StructuredTableCopyCanonicalizationTests
{
    [Fact]
    public void CopyState_CoversEveryPublicStructuredTableProperty()
    {
        var modelProperties = typeof(StructuredTableModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name);
        var stateProperties = typeof(StructuredTableCopyState)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name);

        stateProperties.Should().BeEquivalentTo(modelProperties,
            because: "adding a table field must also extend the canonical complete copy state");
    }

    [Fact]
    public void ProductionTableCopyPaths_DelegateToCanonicalModelState()
    {
        var copyPathFiles = new[]
        {
            "MoveRangeCommand.cs",
            "RowColumnShiftHelpers.AddressState.cs",
            "SortCommand.cs",
            "StructuredTableCommand.cs",
            "StructuredTableDesignCommands.cs",
            "StructuredTableTotalsCommand.cs"
        };

        foreach (var file in copyPathFiles)
        {
            var source = ModelSourceTestSupport.ReadCommandsSource(file);
            source.Should().Contain("CaptureCopyState()", because: $"{file} owns a complete table copy path");
            source.Should().Contain("StructuredTableModel.FromCopyState", because: $"{file} must materialize through the canonical primitive");
            if (!string.Equals(file, "StructuredTableCommand.cs", StringComparison.Ordinal))
                source.Should().NotContain("new StructuredTableModel", because: $"{file} only copies existing tables");
        }

        var tableCommandSource = ModelSourceTestSupport.ReadCommandsSource("StructuredTableCommand.cs");
        tableCommandSource.Split("new StructuredTableModel", StringSplitOptions.None).Should().HaveCount(2,
            because: "the create-table path is the sole intentional command-owned table initializer");

        var sheetCloneSource = ModelSourceTestSupport.ReadModelSource("Sheet.Clone.cs");
        sheetCloneSource.Should().Contain("StructuredTableModel.FromCopyState");
        sheetCloneSource.Should().Contain("StructuredTableModel.DeepCloneFilterColumn");
    }

    [Fact]
    public void DuplicateSheet_PreservesAndDeepClonesCompleteTableFilterGraph()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Source");
        var fixture = CreateFilterColumn();
        var table = CreateTable(sheet, fixture.Column);
        sheet.StructuredTables.Add(table);

        new DuplicateSheetCommand(sheet.Id).Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var copiedTable = workbook.Sheets[1].StructuredTables.Should().ContainSingle().Subject;
        var copiedFilter = copiedTable.FilterColumns.Should().ContainSingle().Subject;
        AssertCompleteIndependentFilterCopy(fixture.Column, copiedFilter);

        fixture.ColorAttributes["source-only"] = "color";
        fixture.DateGroupAttributes["source-only"] = "date";
        fixture.CustomFilterAttributes["source-only"] = "custom";

        copiedFilter.ColorFilter!.NativeAttributes.Should().NotContainKey("source-only");
        copiedFilter.DateGroups[0].NativeAttributes.Should().NotContainKey("source-only");
        copiedFilter.CustomFilters[0].NativeAttributes.Should().NotContainKey("source-only");
    }

    [Fact]
    public void InsertRows_PreservesAndDeepClonesCompleteTableFilterGraph_AndUndoRestoresOriginal()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Source");
        var fixture = CreateFilterColumn();
        var table = CreateTable(sheet, fixture.Column);
        sheet.StructuredTables.Add(table);
        var command = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 2);
        var context = new TestCommandContext(workbook);

        command.Apply(context).Success.Should().BeTrue();

        var shiftedTable = sheet.StructuredTables.Should().ContainSingle().Subject;
        shiftedTable.Should().NotBeSameAs(table);
        shiftedTable.Range.Start.Row.Should().Be(5);
        shiftedTable.Range.End.Row.Should().Be(8);
        var shiftedFilter = shiftedTable.FilterColumns.Should().ContainSingle().Subject;
        AssertCompleteIndependentFilterCopy(fixture.Column, shiftedFilter);

        fixture.ColorAttributes["source-only"] = "color";
        fixture.DateGroupAttributes["source-only"] = "date";
        shiftedFilter.ColorFilter!.NativeAttributes.Should().NotContainKey("source-only");
        shiftedFilter.DateGroups[0].NativeAttributes.Should().NotContainKey("source-only");

        command.Revert(context);

        sheet.StructuredTables.Should().ContainSingle().Which.Should().BeSameAs(table);
    }

    private static StructuredTableModel CreateTable(Sheet sheet, StructuredTableFilterColumnModel filterColumn)
    {
        var table = new StructuredTableModel
        {
            Id = 7,
            Name = "Table7",
            DisplayName = "Table7",
            Range = new GridRange(
                new CellAddress(sheet.Id, 3, 1),
                new CellAddress(sheet.Id, 6, 2)),
            HasAutoFilter = true,
            PackagePart = "xl/tables/table7.xml"
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Date"));
        table.FilterColumns.Add(filterColumn);
        return table;
    }

    private static FilterFixture CreateFilterColumn()
    {
        var colorAttributes = new Dictionary<string, string> { ["color-native"] = "kept" };
        var dateGroupAttributes = new Dictionary<string, string> { ["date-native"] = "kept" };
        var customFilterAttributes = new Dictionary<string, string> { ["custom-native"] = "kept" };
        var column = new StructuredTableFilterColumnModel(
            ColumnId: 1,
            Values: ["2026-08-30"],
            IncludeBlank: true,
            CustomFilters: [new StructuredTableCustomFilterModel("greaterThan", "5", customFilterAttributes)],
            CustomFiltersAnd: true,
            CustomFiltersAndRaw: "1",
            NativeCustomFiltersAttributes: new Dictionary<string, string> { ["customs-native"] = "kept" },
            NativeFilterXmls: ["<top10 val=\"10\" />"],
            NativeAttributes: new Dictionary<string, string> { ["column-native"] = "kept" })
        {
            ColorFilter = new WorksheetAutoFilterColorFilterModel(
                DifferentialFormatId: 4,
                CellColor: false,
                DifferentialFormatIdRaw: "4",
                CellColorRaw: "0",
                NativeAttributes: colorAttributes,
                Color: new CellColor(0x33, 0x66, 0x99)),
            DateGroups =
            [
                new WorksheetAutoFilterDateGroupItemModel(
                    Year: 2026,
                    Month: 8,
                    Day: 30,
                    DateTimeGrouping: "day",
                    YearRaw: "2026",
                    MonthRaw: "8",
                    DayRaw: "30",
                    NativeAttributes: dateGroupAttributes)
            ]
        };
        return new FilterFixture(column, colorAttributes, dateGroupAttributes, customFilterAttributes);
    }

    private static void AssertCompleteIndependentFilterCopy(
        StructuredTableFilterColumnModel source,
        StructuredTableFilterColumnModel copy)
    {
        copy.Should().BeEquivalentTo(source);
        copy.Should().NotBeSameAs(source);
        copy.Values.Should().NotBeSameAs(source.Values);
        copy.CustomFilters.Should().NotBeSameAs(source.CustomFilters);
        copy.CustomFilters[0].Should().NotBeSameAs(source.CustomFilters[0]);
        copy.CustomFilters[0].NativeAttributes.Should().NotBeSameAs(source.CustomFilters[0].NativeAttributes);
        copy.NativeCustomFiltersAttributes.Should().NotBeSameAs(source.NativeCustomFiltersAttributes);
        copy.NativeFilterXmls.Should().NotBeSameAs(source.NativeFilterXmls);
        copy.NativeAttributes.Should().NotBeSameAs(source.NativeAttributes);
        copy.ColorFilter.Should().NotBeSameAs(source.ColorFilter);
        copy.ColorFilter!.NativeAttributes.Should().NotBeSameAs(source.ColorFilter!.NativeAttributes);
        copy.DateGroups.Should().NotBeSameAs(source.DateGroups);
        copy.DateGroups[0].Should().NotBeSameAs(source.DateGroups[0]);
        copy.DateGroups[0].NativeAttributes.Should().NotBeSameAs(source.DateGroups[0].NativeAttributes);
    }

    private sealed record FilterFixture(
        StructuredTableFilterColumnModel Column,
        Dictionary<string, string> ColorAttributes,
        Dictionary<string, string> DateGroupAttributes,
        Dictionary<string, string> CustomFilterAttributes);
}
