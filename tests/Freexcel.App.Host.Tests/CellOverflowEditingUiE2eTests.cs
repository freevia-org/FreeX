using FluentAssertions;
using System.IO;
using System.Text;
using Xunit;

namespace Freexcel.App.Host.Tests;

public sealed class CellOverflowEditingUiE2eTests
{
    [Fact]
    [Trait("Category", "UIE2E")]
    public void OverflowingCellText_ClipsAsSoonAsNeighborEditingStarts()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var run = FreexcelUiRun.Start();

        run.ClickCell(col: 1, row: 1);
        run.TypeText("this is a long overflow value from A1");
        run.Press(VirtualKey.Enter);
        run.Capture("01-overflow-before-neighbor-edit");

        run.ClickCell(col: 2, row: 1);
        run.TypeText("x");
        run.Capture("02-overflow-clipped-during-neighbor-edit");

        var screenshots = run.Artifacts.GetFiles("*.png").OrderBy(file => file.Name).ToList();
        screenshots.Should().HaveCount(2);
        foreach (var screenshot in screenshots)
            screenshot.Length.Should().BeGreaterThan(10_000, screenshot.Name);

        File.WriteAllText(
            Path.Combine(run.Artifacts.FullName, "cell-overflow-editing-analysis.md"),
            $"""
            # Cell Overflow Editing UI E2E Analysis

            Artifact directory: `{run.Artifacts.FullName}`

            ## Scenario

            - Entered a long text value in `A1` that normally spills into empty neighboring cells.
            - Started editing `B1` and typed `x`.
            - Expected Excel-like behavior: `A1` clips immediately when `B1` enters edit mode, before the edit is committed.

            ## Captured Screenshots

            - `01-overflow-before-neighbor-edit.png`
            - `02-overflow-clipped-during-neighbor-edit.png`
            """,
            Encoding.UTF8);
    }
}
