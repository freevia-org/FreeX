using System;

using FluentAssertions;

using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the non-UI glue still owned by the Avalonia Quick Analysis entry point. Shared route
/// planning is covered in the presentation tests; this file keeps the shell-side sparkline command shape.
/// </summary>
public sealed class QuickAnalysisShellGlueTests
{
    [Fact]
    public void SparklinePlanner_BuildsOneCommandPerDataRow_PlacedRightOfSelection()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 4, 3));

        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheetId, range, hasHeaderRow: true, SparklineKind.Line);

        commands.Should().HaveCount(2);
    }

    [Fact]
    public void SparklinePlanner_ReturnsEmpty_ForSingleColumn()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 5, 1));

        var commands = QuickAnalysisSparklinePlanner.BuildCommands(
            sheetId, range, hasHeaderRow: false, SparklineKind.Column);

        commands.Should().BeEmpty();
    }
}
