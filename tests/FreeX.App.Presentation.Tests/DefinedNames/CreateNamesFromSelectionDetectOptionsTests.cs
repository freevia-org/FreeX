using FreeX.App.Presentation.DefinedNames;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.DefinedNames;

/// <summary>
/// Expectations in this class were verified empirically against real Microsoft Excel 16.0 (en-US) by driving
/// its own Create Names from Selection dialog for each layout and reading the four checkboxes back. Layout
/// letters A–G below refer to the recorded Excel result table:
/// A text top row + text left column (blank corner), numeric body  -> Top row + Left column
/// B text top row only, numeric body                               -> Top row
/// C text left column only, numeric body                           -> Left column
/// D all numeric                                                   -> nothing
/// E text bottom row only                                          -> nothing
/// F text right column only                                        -> nothing
/// G all text                                                      -> nothing
/// Excel never auto-checks Bottom row or Right column.
/// </summary>
public sealed class CreateNamesFromSelectionDetectOptionsTests
{
    private static readonly SheetId Sheet = SheetId.New();

    // Builds a value accessor from a dense grid whose [0,0] cell sits at (startRow, startCol). A string entry
    // becomes a TextValue, a double entry a NumberValue, null a blank cell.
    private static Func<CellAddress, ScalarValue?> Grid(uint startRow, uint startCol, object?[][] rows) =>
        addr =>
        {
            var r = (int)(addr.Row - startRow);
            var c = (int)(addr.Col - startCol);
            if (r < 0 || r >= rows.Length)
                return BlankValue.Instance;
            var row = rows[r];
            if (c < 0 || c >= row.Length)
                return BlankValue.Instance;
            return row[c] switch
            {
                null => BlankValue.Instance,
                string text => new TextValue(text),
                double number => new NumberValue(number),
                bool flag => new BoolValue(flag),
                ScalarValue value => value,
                _ => BlankValue.Instance,
            };
        };

    private static GridRange Range(uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(Sheet, r1, c1), new CellAddress(Sheet, r2, c2));

    private static CreateNamesFromSelectionOptions Expected(bool topRow, bool leftColumn) =>
        new(UseTopRow: topRow, UseLeftColumn: leftColumn, UseBottomRow: false, UseRightColumn: false);

    // Layout A: blank corner, text headers across and down, numeric body.
    [Fact]
    public void DetectOptions_TextTopRowAndLeftColumn_ChecksTopAndLeft()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { null, "Q1", "Q2" },
            new object?[] { "North", 1d, 2d },
            new object?[] { "South", 3d, 4d },
        });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 3, 3), grid)
            .Should().Be(Expected(topRow: true, leftColumn: true));
    }

    // Layout B: text top row only.
    [Fact]
    public void DetectOptions_TextTopRowOnly_ChecksTopRowOnly()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { "Q1", "Q2", "Q3" },
            new object?[] { 1d, 2d, 3d },
            new object?[] { 4d, 5d, 6d },
        });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 3, 3), grid)
            .Should().Be(Expected(topRow: true, leftColumn: false));
    }

    // Layout C: text left column only.
    [Fact]
    public void DetectOptions_TextLeftColumnOnly_ChecksLeftColumnOnly()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { "North", 1d, 2d },
            new object?[] { "South", 3d, 4d },
            new object?[] { "East", 5d, 6d },
        });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 3, 3), grid)
            .Should().Be(Expected(topRow: false, leftColumn: true));
    }

    // Layout D: every cell numeric.
    [Fact]
    public void DetectOptions_AllNumeric_ChecksNothing()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { 1d, 2d, 3d },
            new object?[] { 4d, 5d, 6d },
            new object?[] { 7d, 8d, 9d },
        });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 3, 3), grid)
            .Should().Be(Expected(topRow: false, leftColumn: false));
    }

    // Layout E: labels only along the bottom row — Excel checks nothing.
    [Fact]
    public void DetectOptions_TextBottomRowOnly_ChecksNothing()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { 1d, 2d, 3d },
            new object?[] { 4d, 5d, 6d },
            new object?[] { "Total", "Total2", "Total3" },
        });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 3, 3), grid)
            .Should().Be(Expected(topRow: false, leftColumn: false));
    }

    // Layout F: labels only down the right column — Excel checks nothing.
    [Fact]
    public void DetectOptions_TextRightColumnOnly_ChecksNothing()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { 1d, 2d, "Total" },
            new object?[] { 3d, 4d, "Total2" },
            new object?[] { 5d, 6d, "Total3" },
        });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 3, 3), grid)
            .Should().Be(Expected(topRow: false, leftColumn: false));
    }

    // Layout G: every cell text — Excel cannot tell a label edge from the body, so it checks nothing.
    [Fact]
    public void DetectOptions_AllText_ChecksNothing()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { "a", "b", "c" },
            new object?[] { "d", "e", "f" },
            new object?[] { "g", "h", "i" },
        });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 3, 3), grid)
            .Should().Be(Expected(topRow: false, leftColumn: false));
    }

    [Fact]
    public void DetectOptions_NeverChecksBottomRowOrRightColumn()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { null, "Q1", "Q2" },
            new object?[] { "North", 1d, 2d },
            new object?[] { "South", 3d, 4d },
        });

        var detected = CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 3, 3), grid);

        detected.UseBottomRow.Should().BeFalse();
        detected.UseRightColumn.Should().BeFalse();
    }

    [Fact]
    public void DetectOptions_DatesBooleansAndBlanksAreNotLabels()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { new DateTimeValue(45000d), true, null },
            new object?[] { 1d, 2d, 3d },
        });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 2, 3), grid)
            .Should().Be(Expected(topRow: false, leftColumn: false));
    }

    [Fact]
    public void DetectOptions_WhitespaceOnlyHeaderIsNotALabel()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { "   ", "  " },
            new object?[] { 1d, 2d },
        });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 2, 2), grid)
            .Should().Be(Expected(topRow: false, leftColumn: false));
    }

    // Degenerate: a single cell has no data beyond the label, so no edge can be used.
    [Fact]
    public void DetectOptions_SingleCell_ChecksNothing()
    {
        var grid = Grid(1, 1, new object?[][] { new object?[] { "Header" } });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 1, 1), grid)
            .Should().Be(Expected(topRow: false, leftColumn: false));
    }

    // Degenerate: a single row cannot use its top row (no data rows beneath) but its first cell can name the
    // rest of the row, exactly as Plan() allows the left column whenever there is more than one column.
    [Fact]
    public void DetectOptions_SingleRowWithLeadingLabel_ChecksLeftColumnNotTopRow()
    {
        var grid = Grid(1, 1, new object?[][] { new object?[] { "North", 1d, 2d } });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 1, 3), grid)
            .Should().Be(Expected(topRow: false, leftColumn: true));
    }

    [Fact]
    public void DetectOptions_SingleRowOfHeaders_ChecksNothing()
    {
        var grid = Grid(1, 1, new object?[][] { new object?[] { "Q1", "Q2", "Q3" } });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 1, 3), grid)
            .Should().Be(Expected(topRow: false, leftColumn: false));
    }

    // Degenerate: transpose of the single-row case.
    [Fact]
    public void DetectOptions_SingleColumnWithLeadingLabel_ChecksTopRowNotLeftColumn()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { "Q1" },
            new object?[] { 1d },
            new object?[] { 2d },
        });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 3, 1), grid)
            .Should().Be(Expected(topRow: true, leftColumn: false));
    }

    [Fact]
    public void DetectOptions_SingleColumnOfText_ChecksNothing()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { "Q1" },
            new object?[] { "Q2" },
        });

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 2, 1), grid)
            .Should().Be(Expected(topRow: false, leftColumn: false));
    }

    [Fact]
    public void DetectOptions_EmptySelection_ChecksNothing()
    {
        var grid = Grid(1, 1, Array.Empty<object?[]>());

        CreateNamesFromSelectionPlanner.DetectOptions(Range(1, 1, 4, 4), grid)
            .Should().Be(Expected(topRow: false, leftColumn: false));
    }

    // Detection must never pre-check an edge that Plan() would then ignore or that would produce no names.
    [Fact]
    public void DetectOptions_DetectedEdgesAlwaysProduceNames()
    {
        var grid = Grid(1, 1, new object?[][]
        {
            new object?[] { null, "Q1", "Q2" },
            new object?[] { "North", 1d, 2d },
            new object?[] { "South", 3d, 4d },
        });
        var selection = Range(1, 1, 3, 3);

        var detected = CreateNamesFromSelectionPlanner.DetectOptions(selection, grid);
        var planned = CreateNamesFromSelectionPlanner.Plan(
            selection,
            detected,
            addr => grid(addr) is TextValue text ? text.Value : null);

        planned.Should().NotBeEmpty();
        // "Q1"/"Q2" read as cell references, so Plan prefixes them — the point here is only that every
        // auto-detected edge actually yields names.
        planned.Select(p => p.Name).Should().BeEquivalentTo("_Q1", "_Q2", "North", "South");
    }
}
