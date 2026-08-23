using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class PastePlacementPolicyTests
{
    public static TheoryData<uint, uint, uint?, uint?, bool, (uint Row, uint Col)[]> TileCases => new()
    {
        { 2, 3, null, null, false, [(10, 20)] },
        { 2, 3, 2, 3, false, [(10, 20)] },
        { 2, 3, 1, 2, false, [(10, 20)] },
        { 2, 3, 4, 6, false, [(10, 20), (10, 23), (12, 20), (12, 23)] },
        { 2, 3, 5, 7, false, [(10, 20), (10, 23), (12, 20), (12, 23)] },
        { 2, 3, 1, 6, false, [] },
        { 2, 3, 6, 4, true, [(10, 20), (10, 22), (13, 20), (13, 22)] },
        { 2, 3, 2, 1, true, [(10, 20)] },
    };

    [Theory]
    [MemberData(nameof(TileCases))]
    public void EnumerateTileAnchors_PreservesWholeTileAndPartialSelectionPolicy(
        uint sourceRows,
        uint sourceCols,
        uint? targetRows,
        uint? targetCols,
        bool transpose,
        (uint Row, uint Col)[] expected)
    {
        var sourceSheet = SheetId.New();
        var destinationSheet = SheetId.New();
        var sourceRange = Range(sourceSheet, 3, 5, sourceRows, sourceCols);
        var destination = new CellAddress(destinationSheet, 10, 20);
        var destinationRange = targetRows is { } rows && targetCols is { } cols
            ? Range(destinationSheet, destination.Row, destination.Col, rows, cols)
            : (GridRange?)null;

        var actual = PastePlacementPolicy
            .EnumerateTileAnchors(sourceRange, destination, destinationRange, transpose)
            .Select(address => (address.Row, address.Col))
            .ToArray();

        actual.Should().Equal(expected);
        PastePlacementPolicy
            .EnumerateTileAnchors(sourceRange, destination, destinationRange, transpose)
            .Should()
            .OnlyContain(address => address.Sheet == destinationSheet);
    }

    [Theory]
    [InlineData(4U, 7U, false, 20U, 30U)]
    [InlineData(6U, 10U, false, 22U, 33U)]
    [InlineData(4U, 7U, true, 20U, 30U)]
    [InlineData(6U, 10U, true, 23U, 32U)]
    public void MapAddress_MapsOffsetsAndTransposeOntoDestinationSheet(
        uint sourceRow,
        uint sourceCol,
        bool transpose,
        uint expectedRow,
        uint expectedCol)
    {
        var sourceSheet = SheetId.New();
        var destinationSheet = SheetId.New();
        var sourceRange = Range(sourceSheet, 4, 7, 3, 4);
        var destination = new CellAddress(destinationSheet, 20, 30);

        var actual = PastePlacementPolicy.MapAddress(
            new CellAddress(sourceSheet, sourceRow, sourceCol),
            sourceRange,
            destination,
            transpose);

        actual.Should().Be(new CellAddress(destinationSheet, expectedRow, expectedCol));
    }

    [Theory]
    [InlineData(false, 20U, 31U, 21U, 33U)]
    [InlineData(true, 21U, 30U, 23U, 31U)]
    public void MapRange_MapsBothCornersUsingTheSameTransposePolicy(
        bool transpose,
        uint expectedStartRow,
        uint expectedStartCol,
        uint expectedEndRow,
        uint expectedEndCol)
    {
        var sourceSheet = SheetId.New();
        var destinationSheet = SheetId.New();
        var sourceRange = Range(sourceSheet, 4, 7, 3, 4);
        var subset = new GridRange(
            new CellAddress(sourceSheet, 4, 8),
            new CellAddress(sourceSheet, 5, 10));

        var actual = PastePlacementPolicy.MapRange(
            subset,
            sourceRange,
            new CellAddress(destinationSheet, 20, 30),
            transpose);

        actual.Should().Be(new GridRange(
            new CellAddress(destinationSheet, expectedStartRow, expectedStartCol),
            new CellAddress(destinationSheet, expectedEndRow, expectedEndCol)));
    }

    [Theory]
    [InlineData(false, 20U, 30U, 21U, 33U)]
    [InlineData(true, 20U, 30U, 23U, 31U)]
    public void GetDestinationRange_UsesTransposedSourceDimensions(
        bool transpose,
        uint expectedStartRow,
        uint expectedStartCol,
        uint expectedEndRow,
        uint expectedEndCol)
    {
        var sourceRange = Range(SheetId.New(), 4, 7, 2, 4);
        var destinationSheet = SheetId.New();

        var actual = PastePlacementPolicy.GetDestinationRange(
            sourceRange,
            new CellAddress(destinationSheet, 20, 30),
            transpose);

        actual.Should().Be(new GridRange(
            new CellAddress(destinationSheet, expectedStartRow, expectedStartCol),
            new CellAddress(destinationSheet, expectedEndRow, expectedEndCol)));
    }

    [Fact]
    public void EnumerateMappedItems_IsTileMajorAndSourceStable()
    {
        var sourceSheet = SheetId.New();
        var destinationSheet = SheetId.New();
        var sourceRange = Range(sourceSheet, 3, 5, 2, 3);
        var destinationRange = Range(destinationSheet, 10, 20, 2, 6);
        var sources = new[]
        {
            (Name: "first", Address: new CellAddress(sourceSheet, 3, 6)),
            (Name: "second", Address: new CellAddress(sourceSheet, 4, 7)),
        };

        var actual = PastePlacementPolicy.EnumerateMappedItems(
                sources,
                static source => source.Address,
                sourceRange,
                destinationRange.Start,
                destinationRange,
                transpose: false)
            .Select(placement => (placement.Source.Name, placement.Destination.Row, placement.Destination.Col))
            .ToArray();

        actual.Should().Equal(
            ("first", 10U, 21U),
            ("second", 11U, 22U),
            ("first", 10U, 24U),
            ("second", 11U, 25U));
    }

    [Theory]
    [InlineData("picture")]
    [InlineData("shape")]
    [InlineData("textbox")]
    public void CellAnchoredObjectCommands_RetainTransposedTilingAndUndo(string kind)
    {
        var workbook = new Workbook("test");
        var sourceSheet = workbook.AddSheet("Source");
        var targetSheet = workbook.AddSheet("Target");
        var context = new TestCommandContext(workbook);
        var sourceRange = Range(sourceSheet.Id, 2, 3, 2, 3);
        var sourceAnchor = new CellAddress(sourceSheet.Id, 3, 5);
        var destinationRange = Range(targetSheet.Id, 10, 20, 6, 4);

        IWorkbookCommand command = kind switch
        {
            "picture" => new PastePicturesCommand(
                targetSheet.Id,
                sourceRange,
                destinationRange,
                [new PictureModel { Anchor = sourceAnchor, ImageBytes = [1], ContentType = "image/png" }],
                transpose: true),
            "shape" => new PasteShapesCommand(
                targetSheet.Id,
                sourceRange,
                destinationRange,
                [new DrawingShapeModel { Anchor = sourceAnchor }],
                transpose: true),
            "textbox" => new PasteTextBoxesCommand(
                targetSheet.Id,
                sourceRange,
                destinationRange,
                [new TextBoxModel { Anchor = sourceAnchor, Text = "text" }],
                transpose: true),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var outcome = command.Apply(context);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        ObjectAnchors(targetSheet, kind).Should().BeEquivalentTo(ExpectedMappedAnchors(targetSheet.Id));
        outcome.AffectedCells.Should().BeEquivalentTo(ExpectedMappedAnchors(targetSheet.Id));

        command.Revert(context);
        ObjectAnchors(targetSheet, kind).Should().BeEmpty();
    }

    [Fact]
    public void PasteChartsCommand_RetainsPixelOffsetTransposeTilingAndUndo()
    {
        var workbook = new Workbook("test");
        var sourceSheet = workbook.AddSheet("Source");
        var targetSheet = workbook.AddSheet("Target");
        var context = new TestCommandContext(workbook);
        var sourceRange = Range(sourceSheet.Id, 2, 3, 2, 3);
        var destinationRange = Range(targetSheet.Id, 10, 20, 6, 4);
        var sourceLeft = 2 * sourceSheet.DefaultColumnWidth * 8;
        var sourceTop = sourceSheet.DefaultRowHeight;
        var chart = new ChartModel { Left = sourceLeft + 5, Top = sourceTop + 7, Width = 100, Height = 80 };
        var command = new PasteChartsCommand(
            sourceSheet.Id,
            targetSheet.Id,
            sourceRange,
            destinationRange,
            [chart],
            transpose: true);

        var outcome = command.Apply(context);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        targetSheet.Charts.Should().HaveCount(4);
        var tileAnchors = new[] { (10U, 20U), (10U, 22U), (13U, 20U), (13U, 22U) };
        targetSheet.Charts.Zip(tileAnchors).Should().OnlyContain(pair =>
            Math.Abs(pair.First.Left - (((pair.Second.Item2 - 1) * targetSheet.DefaultColumnWidth * 8) + 7)) < 0.001 &&
            Math.Abs(pair.First.Top - (((pair.Second.Item1 - 1) * targetSheet.DefaultRowHeight) + 5)) < 0.001);
        outcome.AffectedCells.Should().Equal(tileAnchors.Select(tile =>
            new CellAddress(targetSheet.Id, tile.Item1, tile.Item2)));

        command.Revert(context);
        targetSheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void CommentAndValidationCommands_RetainTransposedTilingAndUndo()
    {
        var workbook = new Workbook("test");
        var sourceSheet = workbook.AddSheet("Source");
        var targetSheet = workbook.AddSheet("Target");
        var context = new TestCommandContext(workbook);
        var sourceRange = Range(sourceSheet.Id, 2, 3, 2, 3);
        var sourceAnchor = new CellAddress(sourceSheet.Id, 3, 5);
        var destinationRange = Range(targetSheet.Id, 10, 20, 6, 4);
        sourceSheet.Comments[sourceAnchor] = "note";
        sourceSheet.CommentAuthors[sourceAnchor] = "author";
        sourceSheet.ShownComments.Add(sourceAnchor);
        sourceSheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(sourceAnchor, sourceAnchor),
            Type = DvType.WholeNumber,
            Formula1 = "1",
        });
        var comments = new PasteCommentsCommand(
            targetSheet.Id, sourceRange, destinationRange, transpose: true);
        var validations = new PasteDataValidationCommand(
            targetSheet.Id, sourceRange, destinationRange, transpose: true);

        comments.Apply(context).Success.Should().BeTrue();
        validations.Apply(context).Success.Should().BeTrue();

        var expected = ExpectedMappedAnchors(targetSheet.Id);
        targetSheet.Comments.Keys.Should().BeEquivalentTo(expected);
        targetSheet.CommentAuthors.Keys.Should().BeEquivalentTo(expected);
        targetSheet.ShownComments.Should().BeEquivalentTo(expected);
        targetSheet.DataValidations.Select(rule => rule.AppliesTo.Start).Should().BeEquivalentTo(expected);

        validations.Revert(context);
        comments.Revert(context);
        targetSheet.DataValidations.Should().BeEmpty();
        targetSheet.Comments.Should().BeEmpty();
        targetSheet.CommentAuthors.Should().BeEmpty();
        targetSheet.ShownComments.Should().BeEmpty();
    }

    [Fact]
    public void PasteCommands_AdoptTheSharedPlacementPolicy()
    {
        var commandNames = new[]
        {
            "PastePicturesCommand.cs",
            "PasteShapesCommand.cs",
            "PasteTextBoxesCommand.cs",
            "PasteChartsCommand.cs",
            "PasteCommentsCommand.cs",
            "PasteDataValidationCommand.cs",
        };

        var sources = commandNames.ToDictionary(
            name => name,
            name => TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
                "src", "FreeX.Core.Commands", name));

        sources.Values.Should().OnlyContain(source => source.Contains("PastePlacementPolicy", StringComparison.Ordinal));
        sources.Values.Should().OnlyContain(source => !source.Contains("private IEnumerable<CellAddress> EnumerateTileAnchors", StringComparison.Ordinal));
        sources.Values.Should().OnlyContain(source => !source.Contains("private static CellAddress MapDestination", StringComparison.Ordinal));
        sources["PastePicturesCommand.cs"].Should().Contain("PastePlacementPolicy.EnumerateMappedItems(");
        sources["PasteShapesCommand.cs"].Should().Contain("PastePlacementPolicy.EnumerateMappedItems(");
        sources["PasteTextBoxesCommand.cs"].Should().Contain("PastePlacementPolicy.EnumerateMappedItems(");
        sources["PasteChartsCommand.cs"].Should().Contain("PastePlacementPolicy.EnumerateTileAnchors(");
        sources["PasteCommentsCommand.cs"].Should().Contain("PastePlacementPolicy.MapAddress(");
        sources["PasteDataValidationCommand.cs"].Should().Contain("PastePlacementPolicy.MapRange(")
            .And.Contain("PastePlacementPolicy.GetDestinationRange(");
    }

    private static GridRange Range(
        SheetId sheet,
        uint startRow,
        uint startCol,
        uint rowCount,
        uint colCount) =>
        new(
            new CellAddress(sheet, startRow, startCol),
            new CellAddress(sheet, startRow + rowCount - 1, startCol + colCount - 1));

    private static CellAddress[] ExpectedMappedAnchors(SheetId sheet) =>
    [
        new CellAddress(sheet, 12, 21),
        new CellAddress(sheet, 12, 23),
        new CellAddress(sheet, 15, 21),
        new CellAddress(sheet, 15, 23),
    ];

    private static IEnumerable<CellAddress> ObjectAnchors(Sheet sheet, string kind) => kind switch
    {
        "picture" => sheet.Pictures.Select(picture => picture.Anchor),
        "shape" => sheet.DrawingShapes.Select(shape => shape.Anchor),
        "textbox" => sheet.TextBoxes.Select(textBox => textBox.Anchor),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
