using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SlicerCopyCanonicalizationTests
{
    [Fact]
    public void CopyState_CoversEveryPublicSlicerProperty()
    {
        var modelProperties = typeof(SlicerModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name);
        var stateProperties = typeof(SlicerCopyState)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name);

        stateProperties.Should().BeEquivalentTo(modelProperties,
            because: "adding a slicer field must also extend the canonical complete copy state");

        var source = ModelSourceTestSupport.ReadCommandsSource("DuplicateSheetDrawingCloner.cs");
        source.Should().Contain("slicer.CaptureCopyState() with");
        source.Should().Contain("SlicerModel.FromCopyState(state)");
        source.Should().NotContain("var clone = new SlicerModel");
    }

    [Fact]
    public void FromCopyState_PreservesEveryFieldWithIndependentMutableContainers()
    {
        var availableItems = new[] { "East", "West" };
        var source = new SlicerModel
        {
            Name = "Slicer_Region",
            Caption = "Region",
            CacheName = "Slicer_Region_Cache",
            SourcePivotTableName = "Pivot1",
            ConnectedPivotTableNames = ["Pivot1", "Pivot2"],
            SourceFieldName = "Region",
            StyleName = "SlicerStyleDark2",
            SourceFieldIndex = 3,
            SelectionCaptured = true,
            PackagePart = "xl/slicers/slicer1.xml",
            DrawingAnchor = new DrawingAnchorRange(
                new DrawingAnchorPoint(2, 10, 3, 20),
                new DrawingAnchorPoint(5, 30, 9, 40)),
            DrawingShapeName = "Slicer Shape 1",
            ColumnCount = 2,
            ShowCaption = false,
            SourceSheetName = "Dashboard",
            SourceTableId = 7,
            SourceTableColumnId = 4,
            CacheItems = [new SlicerCacheItem(1, IsSelected: false)],
            AvailableItems = availableItems
        };
        source.SelectedItems.Add("West");

        var copy = SlicerModel.FromCopyState(source.CaptureCopyState());

        copy.CaptureCopyState().Should().BeEquivalentTo(source.CaptureCopyState());
        copy.Should().NotBeSameAs(source);
        copy.ConnectedPivotTableNames.Should().NotBeSameAs(source.ConnectedPivotTableNames);
        copy.SelectedItems.Should().NotBeSameAs(source.SelectedItems);
        copy.CacheItems.Should().NotBeSameAs(source.CacheItems);
        copy.AvailableItems.Should().BeSameAs(source.AvailableItems);
    }
}
