using System.Reflection;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class PivotTableCopyCanonicalizationTests
{
    [Fact]
    public void CopyState_CoversEveryPublicPivotTableProperty()
    {
        var modelProperties = typeof(PivotTableModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name);
        var stateProperties = typeof(PivotTableCopyState)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name);

        stateProperties.Should().BeEquivalentTo(modelProperties,
            because: "adding a pivot-table field must also extend the canonical complete copy state");

        var sheetCloneSource = ModelSourceTestSupport.ReadModelSource("Sheet.Clone.cs");
        sheetCloneSource.Should().Contain("pt.CaptureCopyState() with");
        sheetCloneSource.Should().Contain("PivotTableModel.FromCopyState(state)");
        sheetCloneSource.Should().NotContain("var clonedPt = new PivotTableModel");
    }

    [Fact]
    public void SheetClone_CanonicalCopyPreservesEveryFieldAndIndependentLists()
    {
        var workbook = new Workbook("test");
        var sourceSheet = workbook.AddSheet("Source");
        var pivot = CreateFullyPopulatedPivot(sourceSheet);
        sourceSheet.PivotTables.Add(pivot);
        var copyId = SheetId.New();

        var copySheet = sourceSheet.Clone(copyId, "Copy");

        var copy = copySheet.PivotTables.Should().ContainSingle().Subject;
        var expectedState = pivot.CaptureCopyState() with
        {
            SourceRange = Range(copyId, 1, 1, 10, 3),
            TargetRange = Range(copyId, 12, 2, 18, 6),
            LastRenderedRange = Range(copyId, 12, 2, 20, 7),
            PackagePart = string.Empty
        };
        copy.CaptureCopyState().Should().BeEquivalentTo(expectedState);
        copy.Should().NotBeSameAs(pivot);
        copy.FieldListSortAscending.Should().BeTrue();
        copy.ShowRowGrandTotals.Should().BeFalse();
        copy.ShowColumnGrandTotals.Should().BeTrue();
        AssertIndependentLists(pivot, copy);

        pivot.PackagePart.Should().Be("xl/pivotTables/pivotTable3.xml");
        pivot.SourceRange.Start.Sheet.Should().Be(sourceSheet.Id);
    }

    [Fact]
    public void DuplicateSheet_PreservesCopySemanticsWhileReidentifyingPivotAndCache()
    {
        var workbook = new Workbook("test");
        var sourceSheet = workbook.AddSheet("Source");
        var pivot = CreateFullyPopulatedPivot(sourceSheet);
        sourceSheet.PivotTables.Add(pivot);
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = pivot.CacheId,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = sourceSheet.Name,
            SourceReference = pivot.SourceRange.ToString(),
            PackagePart = "xl/pivotCache/pivotCacheDefinition3.xml"
        });

        new DuplicateSheetCommand(sourceSheet.Id).Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();

        var copySheet = workbook.Sheets[1];
        var copy = copySheet.PivotTables.Should().ContainSingle().Subject;
        copy.Name.Should().NotBe(pivot.Name);
        copy.CacheId.Should().NotBe(pivot.CacheId);
        copy.PackagePart.Should().BeEmpty();
        copy.SourceRange.Should().Be(Range(copySheet.Id, 1, 1, 10, 3));
        copy.TargetRange.Should().Be(Range(copySheet.Id, 12, 2, 18, 6));
        copy.LastRenderedRange.Should().Be(Range(copySheet.Id, 12, 2, 20, 7));
        copy.FieldListSortAscending.Should().BeTrue();
        copy.ShowRowGrandTotals.Should().BeFalse();
        copy.ShowColumnGrandTotals.Should().BeTrue();
        workbook.PivotCaches.Should().ContainSingle(cache => cache.CacheId == copy.CacheId)
            .Which.PackagePart.Should().BeEmpty();
        AssertIndependentLists(pivot, copy);
    }

    private static PivotTableModel CreateFullyPopulatedPivot(Sheet sheet)
    {
        var pivot = new PivotTableModel
        {
            Name = "Pivot3",
            CacheId = 3,
            SourceRange = Range(sheet.Id, 1, 1, 10, 3),
            TargetRange = Range(sheet.Id, 12, 2, 18, 6),
            LastRenderedRange = Range(sheet.Id, 12, 2, 20, 7),
            PackagePart = "xl/pivotTables/pivotTable3.xml",
            CreatedVersion = 4,
            UpdatedVersion = 5,
            MinRefreshableVersion = 2,
            DataOnRows = false,
            FirstHeaderRow = 2,
            FirstDataRow = 3,
            FirstDataColumn = 4,
            ShowSubtotals = false,
            SubtotalPlacement = PivotSubtotalPlacement.Bottom,
            ShowRowGrandTotals = false,
            ShowColumnGrandTotals = true,
            RepeatItemLabels = false,
            BlankLineAfterItems = true,
            ReportLayout = PivotReportLayout.Outline,
            CompactRowLabelIndent = 3,
            StyleName = "PivotStyleDark3",
            ShowRowHeaders = false,
            ShowColumnHeaders = false,
            ShowRowStripes = true,
            ShowColumnStripes = true,
            ShowFieldHeaders = false,
            ShowContextualTooltips = false,
            ShowPropertiesInTooltips = false,
            ShowClassicLayout = true,
            FieldListSortAscending = true,
            MergeAndCenterLabels = true,
            ShowItemsWithNoDataOnRows = true,
            ShowItemsWithNoDataOnColumns = true,
            PageOverThenDown = true,
            PageWrap = 4,
            EmptyValueText = "empty",
            ApplyNumberFormats = false,
            ApplyBorderFormats = false,
            ApplyFontFormats = false,
            ApplyPatternFormats = false,
            AutofitColumnsOnUpdate = false,
            PreserveFormattingOnUpdate = false,
            ShowExpandCollapseButtons = false,
            EnableDrill = false,
            AsteriskTotals = true,
            MultipleFieldFilters = false,
            EnableFieldDialog = false,
            EnableFieldProperties = false,
            EnableDataValueEditing = true,
            PrintTitles = true,
            PrintExpandCollapseButtons = true,
            AltTextTitle = "title",
            AltTextDescription = "description",
            DataCaption = "data",
            GrandTotalCaption = "grand",
            MissingCaption = "missing",
            ErrorCaption = "error"
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItem: "East"));
        pivot.ColumnFields.Add(new PivotFieldModel(1, SelectedItems: ["2025", "2026"]));
        pivot.PageFields.Add(new PivotFieldModel(2, IsUnplacedFilterField: true));
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Sales", "sum", NumberFormatId: 4));
        pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Margin", "Sales-Cost"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(0, "Other", "East+West"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "E"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.Top, Count: 5));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Descending));
        return pivot;
    }

    private static void AssertIndependentLists(PivotTableModel source, PivotTableModel copy)
    {
        copy.RowFields.Should().NotBeSameAs(source.RowFields);
        copy.ColumnFields.Should().NotBeSameAs(source.ColumnFields);
        copy.PageFields.Should().NotBeSameAs(source.PageFields);
        copy.DataFields.Should().NotBeSameAs(source.DataFields);
        copy.CalculatedFields.Should().NotBeSameAs(source.CalculatedFields);
        copy.CalculatedItems.Should().NotBeSameAs(source.CalculatedItems);
        copy.LabelFilters.Should().NotBeSameAs(source.LabelFilters);
        copy.ValueFilters.Should().NotBeSameAs(source.ValueFilters);
        copy.Sorts.Should().NotBeSameAs(source.Sorts);
    }

    private static GridRange Range(
        SheetId sheetId,
        uint startRow,
        uint startColumn,
        uint endRow,
        uint endColumn) =>
        new(
            new CellAddress(sheetId, startRow, startColumn),
            new CellAddress(sheetId, endRow, endColumn));
}
