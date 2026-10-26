using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r422: where a gradient cell fill survives, and where it does not.
///
/// <para>The last complex member of <see cref="CellStyle"/> left unswept after r420 covered borders,
/// solid fills and patterns. Measurement found an asymmetry worth recording rather than a bug worth
/// fixing here: gradients round-trip through the native <c>.fxl</c> format and are DROPPED ENTIRELY
/// by the .xlsx one.</para>
///
/// <para>That is real loss, not a theoretical gap. Gradients are a live feature everywhere else:
/// <c>StyleDiff</c> carries one, so a formatting command can apply it; the cell renderer, the print
/// page planner and the gridline planner all draw it. A user can set a gradient, see it on screen and
/// in print, save to the primary format, and reopen to find it gone -- with nothing reported. Only
/// the native format keeps it.</para>
///
/// <para>These tests pin BOTH halves. The xlsx case documents the limitation as it stands, so the
/// suite tells the truth about the product; if someone implements gradient mapping, that test fails
/// and forces this note and the ledger entry to be updated rather than leaving a stale claim behind.
/// Implementing the mapping is a feature, not a review fix, so it is not attempted here.</para>
/// </summary>
public sealed class R422_GradientFillsReachTheFileTests
{
    private static CellGradientFill ThreeStopGradient() => new()
    {
        Type = CellGradientFillType.Linear,
        Degree = 45,
        Stops =
        [
            new CellGradientStop(0.0, new CellColor(0xFF, 0x00, 0x00)),
            new CellGradientStop(0.5, new CellColor(0x00, 0xFF, 0x00)),
            new CellGradientStop(1.0, new CellColor(0x00, 0x00, 0xFF)),
        ],
    };

    private static Workbook WorkbookWithGradient(CellGradientFill gradient)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.GetCell(1, 1)!.StyleId = workbook.RegisterStyle(new CellStyle { GradientFill = gradient });
        return workbook;
    }

    private static CellStyle RoundTrip(IFileAdapter adapter, Workbook workbook)
    {
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var reloaded = adapter.Load(stream);
        var cell = reloaded.Sheets[0].GetCell(1, 1);
        cell.Should().NotBeNull("the styled cell must survive before its fill can be judged");
        return reloaded.GetStyle(cell!.StyleId);
    }

    [Fact]
    public void TheNativeFormatKeepsAGradientWithItsStopsAndAngle()
    {
        var reloaded = RoundTrip(new NativeJsonAdapter(), WorkbookWithGradient(ThreeStopGradient()));

        reloaded.GradientFill.Should().NotBeNull("the native format is where a gradient must survive");
        reloaded.GradientFill!.Type.Should().Be(CellGradientFillType.Linear);
        reloaded.GradientFill.Degree.Should().Be(45, "the angle is the character of a linear gradient");

        // The middle stop is the one whose loss looks deliberate: a three-colour blend silently
        // becoming a two-colour one reads as a design choice, not a bug.
        reloaded.GradientFill.Stops.Should().HaveCount(3);
        reloaded.GradientFill.Stops[1].Position.Should().Be(0.5, "a stop at the wrong offset shifts the blend");
        reloaded.GradientFill.Stops[1].Color.Should().Be(new CellColor(0x00, 0xFF, 0x00));
    }

    [Fact]
    public void TheNativeFormatKeepsAPathGradientsInsetBounds()
    {
        // The path type carries four bounds the linear type never uses, so a mapper handling only
        // linear gradients would satisfy the test above.
        var reloaded = RoundTrip(new NativeJsonAdapter(), WorkbookWithGradient(new CellGradientFill
        {
            Type = CellGradientFillType.Path,
            Left = 0.25,
            Right = 0.75,
            Top = 0.125,
            Bottom = 0.875,
            Stops =
            [
                new CellGradientStop(0.0, new CellColor(0x10, 0x20, 0x30)),
                new CellGradientStop(1.0, new CellColor(0x40, 0x50, 0x60)),
            ],
        }));

        reloaded.GradientFill!.Type.Should().Be(CellGradientFillType.Path);
        reloaded.GradientFill.Left.Should().Be(0.25);
        reloaded.GradientFill.Right.Should().Be(0.75);
        reloaded.GradientFill.Top.Should().Be(0.125);
        reloaded.GradientFill.Bottom.Should().Be(0.875);
    }

    [Fact]
    public void TheXlsxFormatCurrentlyDropsAGradient()
    {
        // Documents a KNOWN LIMITATION, deliberately. There is no gradient mapping in either
        // direction for .xlsx -- only the native adapter handles CellStyle.GradientFill -- so a
        // gradient set by a formatting command, rendered on screen and in print, is lost on save to
        // the primary format with nothing reported.
        //
        // Asserted rather than left unwritten so the suite states the product's real behaviour. When
        // gradient mapping is implemented this fails, which is the point: it forces the claim to be
        // revisited instead of quietly going stale.
        RoundTrip(new XlsxFileAdapter(), WorkbookWithGradient(ThreeStopGradient())).GradientFill
            .Should().BeNull(
                "xlsx has no gradient mapping today; if this now survives, the limitation is fixed " +
                "and this test and its round-422 ledger entry both need updating");
    }

    [Fact]
    public void AnUngradientedCellGainsNoGradient()
    {
        // The control: without it, a reader that invented a gradient would be indistinguishable from
        // one that preserved the real one in the native case above.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.GetCell(1, 1)!.StyleId = workbook.RegisterStyle(new CellStyle { FontName = "Verdana" });

        RoundTrip(new NativeJsonAdapter(), workbook).GradientFill
            .Should().BeNull("a plain cell must not acquire a gradient fill");
    }
}
