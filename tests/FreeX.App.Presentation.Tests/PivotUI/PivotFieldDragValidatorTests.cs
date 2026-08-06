using FluentAssertions;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PivotUI;

public sealed class PivotFieldDragValidatorTests
{
    private static readonly string[] Headers = ["Region", "Product", "Quarter", "Amount"];

    private static PivotTableModel BuildPivot()
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.DataFields.Add(new PivotDataFieldModel(3, "Sum of Amount", "sum"));
        return pivot;
    }

    [Fact]
    public void Validate_OutOfRangeSourceIndexIsRejected()
    {
        var result = new PivotFieldDragValidator().Validate(
            BuildPivot(), Headers, new PivotFieldDropRequest(9, PivotFieldBucket.Rows));

        result.IsAllowed.Should().BeFalse();
        result.RejectionReason.Should().Contain("out of range");
    }

    [Fact]
    public void Validate_MoveAvailableFieldIntoRowsAppendsToRows()
    {
        var result = new PivotFieldDragValidator().Validate(
            BuildPivot(), Headers, new PivotFieldDropRequest(1, PivotFieldBucket.Rows));

        result.IsAllowed.Should().BeTrue();
        result.ResultingLayout!.Rows.Should().Equal(0, 1);
        result.ResultingLayout.Columns.Should().Equal(2);
    }

    [Fact]
    public void Validate_MoveWithTargetIndexInsertsAtPosition()
    {
        var result = new PivotFieldDragValidator().Validate(
            BuildPivot(), Headers, new PivotFieldDropRequest(1, PivotFieldBucket.Rows, TargetIndex: 0));

        result.ResultingLayout!.Rows.Should().Equal(1, 0);
    }

    [Fact]
    public void Validate_ReordersFieldWithinItsExistingBucket()
    {
        var pivot = BuildPivot();
        pivot.RowFields.Add(new PivotFieldModel(1));

        var result = new PivotFieldDragValidator().Validate(
            pivot,
            Headers,
            new PivotFieldDropRequest(0, PivotFieldBucket.Rows, TargetIndex: 1));

        result.IsAllowed.Should().BeTrue();
        result.ResultingLayout!.Rows.Should().Equal(1, 0);
        result.ResultingLayout.Columns.Should().Equal(2);
        result.ResultingLayout.Values.Should().Equal(3);
    }

    [Fact]
    public void Validate_InsertsAcrossBucketsAtTheRequestedPosition()
    {
        var pivot = BuildPivot();
        pivot.ColumnFields.Add(new PivotFieldModel(1));

        var result = new PivotFieldDragValidator().Validate(
            pivot,
            Headers,
            new PivotFieldDropRequest(0, PivotFieldBucket.Columns, TargetIndex: 1));

        result.IsAllowed.Should().BeTrue();
        result.ResultingLayout!.Rows.Should().BeEmpty();
        result.ResultingLayout.Columns.Should().Equal(2, 0, 1);
    }

    [Fact]
    public void Validate_ReordersAnExactValuesFieldWithoutDuplicatingIt()
    {
        var pivot = BuildPivot();
        pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Quarter", "sum"));

        var result = new PivotFieldDragValidator().Validate(
            pivot,
            Headers,
            new PivotFieldDropRequest(
                3,
                PivotFieldBucket.Values,
                TargetIndex: 1,
                SourceBucket: PivotFieldBucket.Values,
                SourceItemIndex: 0));

        result.IsAllowed.Should().BeTrue();
        result.ResultingLayout!.Values.Should().Equal(2, 3);
    }

    [Fact]
    public void Validate_MovingAnExactValuesFieldToAnAxisRemovesItFromValues()
    {
        var pivot = BuildPivot();

        var result = new PivotFieldDragValidator().Validate(
            pivot,
            Headers,
            new PivotFieldDropRequest(
                3,
                PivotFieldBucket.Rows,
                TargetIndex: 1,
                SourceBucket: PivotFieldBucket.Values,
                SourceItemIndex: 0));

        result.IsAllowed.Should().BeTrue();
        result.ResultingLayout!.Rows.Should().Equal(0, 3);
        result.ResultingLayout.Values.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MovingFieldBetweenAxesRemovesItFromOldAxis()
    {
        // Column field index 2 -> rows. It should leave the columns area.
        var result = new PivotFieldDragValidator().Validate(
            BuildPivot(), Headers, new PivotFieldDropRequest(2, PivotFieldBucket.Rows));

        result.ResultingLayout!.Rows.Should().Equal(0, 2);
        result.ResultingLayout.Columns.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MoveToAvailableRemovesFromValues()
    {
        var result = new PivotFieldDragValidator().Validate(
            BuildPivot(), Headers, new PivotFieldDropRequest(3, PivotFieldBucket.Available));

        result.IsAllowed.Should().BeTrue();
        result.ResultingLayout!.Values.Should().BeEmpty();
    }

    [Fact]
    public void Validate_NumericFieldDroppedIntoValuesDefaultsToSum()
    {
        var validator = new PivotFieldDragValidator(sourceFieldIndex => sourceFieldIndex == 3);

        var result = validator.Validate(
            BuildPivot(), Headers, new PivotFieldDropRequest(3, PivotFieldBucket.Values));

        result.DefaultSummaryFunction.Should().Be("sum");
        result.ResultingLayout!.Values.Should().Equal(3, 3);
    }

    [Fact]
    public void Validate_NonNumericFieldDroppedIntoValuesDefaultsToCount()
    {
        var validator = new PivotFieldDragValidator(sourceFieldIndex => false);

        var result = validator.Validate(
            BuildPivot(), Headers, new PivotFieldDropRequest(1, PivotFieldBucket.Values));

        result.DefaultSummaryFunction.Should().Be("count");
    }

    [Fact]
    public void Validate_NonValuesDropHasNoDefaultSummaryFunction()
    {
        var result = new PivotFieldDragValidator(_ => true).Validate(
            BuildPivot(), Headers, new PivotFieldDropRequest(1, PivotFieldBucket.Rows));

        result.DefaultSummaryFunction.Should().BeNull();
    }

    [Fact]
    public void Validate_RespectsPerFieldDragPermission()
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.ColumnFields.Add(new PivotFieldModel(2, DragToRow: false));

        var result = new PivotFieldDragValidator().Validate(
            pivot, Headers, new PivotFieldDropRequest(2, PivotFieldBucket.Rows));

        result.IsAllowed.Should().BeFalse();
        result.RejectionReason.Should().Contain("Rows");
    }

    [Fact]
    public void Validate_AllowsDropWhenPermissionFlagIsNull()
    {
        var pivot = new PivotTableModel { Name = "P" };
        pivot.ColumnFields.Add(new PivotFieldModel(2));

        var result = new PivotFieldDragValidator().Validate(
            pivot, Headers, new PivotFieldDropRequest(2, PivotFieldBucket.Rows));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void DefaultSummaryFunction_ReflectsNumericPredicate()
    {
        var validator = new PivotFieldDragValidator(index => index == 5);

        validator.DefaultSummaryFunction(5).Should().Be("sum");
        validator.DefaultSummaryFunction(4).Should().Be("count");
    }

    [Fact]
    public void PlanDrop_ReordersTheExactDuplicateValueFieldAndPreservesItsSettings()
    {
        var sum = new PivotDataFieldModel(3, "Sum of Amount", "sum", NumberFormatCode: "$0");
        var average = new PivotDataFieldModel(3, "Average of Amount", "average", NumberFormatCode: "0.00");
        var areas = new PivotFieldAreas([], [], [], [sum, average]);

        var plan = PivotFieldLayoutPlanner.PlanDrop(
            areas,
            Headers,
            new PivotFieldDropRequest(
                3,
                PivotFieldBucket.Values,
                TargetIndex: 0,
                SourceBucket: PivotFieldBucket.Values,
                SourceItemIndex: 1),
            new PivotFieldDragValidator(index => index == 3));

        plan.CanApply.Should().BeTrue();
        plan.Areas!.DataFields.Should().Equal(average, sum);
    }

    [Fact]
    public void PlanDrop_RejectsAConcreteLayoutWithNoValueField()
    {
        var areas = new PivotFieldAreas(
            [new PivotFieldModel(0)],
            [],
            [],
            [new PivotDataFieldModel(3, "Sum of Amount", "sum")]);

        var plan = PivotFieldLayoutPlanner.PlanDrop(
            areas,
            Headers,
            new PivotFieldDropRequest(
                3,
                PivotFieldBucket.Available,
                SourceBucket: PivotFieldBucket.Values,
                SourceItemIndex: 0),
            new PivotFieldDragValidator());

        plan.Result.IsAllowed.Should().BeTrue();
        plan.CanApply.Should().BeFalse();
        plan.Areas.Should().BeNull();
    }

    [Fact]
    public void ResolveSourceFieldIndex_PrefersTheConcreteBucketPositionOverCaption()
    {
        var areas = new PivotFieldAreas(
            [new PivotFieldModel(0)],
            [],
            [],
            [new PivotDataFieldModel(3, "Renamed Value", "sum")]);

        PivotFieldLayoutPlanner.ResolveSourceFieldIndex(
                areas,
                Headers,
                "unrelated caption",
                PivotFieldBucket.Values,
                sourceItemIndex: 0)
            .Should()
            .Be(3);
    }
}
