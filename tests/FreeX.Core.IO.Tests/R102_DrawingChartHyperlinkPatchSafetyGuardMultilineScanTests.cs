using System.IO;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R102-multiline-hyperlink-guard-scan: the R101 guard
/// (<see cref="R101_DrawingChartHyperlinkPatchSafetyGuardTests"/>) originally scanned each source file
/// one line at a time with a regex requiring the terminating `,`/`;` to appear on the SAME line as
/// `Hyperlink =`. Two common, legal C# shapes evaded it entirely:
/// <list type="number">
/// <item>a wrapped multi-line RHS, e.g. a ternary split across lines, where the first line has no
/// trailing comma/semicolon and the continuation lines don't contain the literal word "Hyperlink";</item>
/// <item>an object/`with`-initializer where `Hyperlink` is the LAST member before the closing brace,
/// with no trailing comma, so the required `[,;]` terminator never appears at all.</item>
/// </list>
/// These tests drive the real <c>ScanForViolationsForTesting</c> seam (the exact scan the R101 guard
/// runs in production) against synthetic fixture directories, so they exercise the actual regex/scan
/// logic rather than a hand-rolled duplicate.
/// </summary>
public sealed class R102_DrawingChartHyperlinkPatchSafetyGuardMultilineScanTests
{
    [Fact]
    public void Scan_CatchesNonCopyForwardHyperlinkAssignment_SplitAcrossMultipleLines()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("FreeX.R102HyperlinkGuardFixture-"))
        {
            var fixtureDirectory = temporaryDirectory.Path;
            File.WriteAllText(
                Path.Combine(fixtureDirectory, "SetShapeHyperlinkCommand.cs"),
                """
                namespace FreeX.Core.Commands;

                public sealed class SetShapeHyperlinkCommand
                {
                    public void Apply(DrawingShapeModel shape, bool flag, string target)
                    {
                        shape.Hyperlink = flag
                            ? new DrawingObjectHyperlink(target, DrawingHyperlinkMode.Uri, null)
                            : null;
                    }
                }
                """);

            var violations = R101_DrawingChartHyperlinkPatchSafetyGuardTests.ScanForViolationsForTesting(fixtureDirectory);

            violations.Should().ContainSingle(
                "a multi-line ternary RHS assigning a new DrawingObjectHyperlink must be caught even " +
                "though the terminating token is on a later line than `Hyperlink =`");
            violations[0].Should().Contain("SetShapeHyperlinkCommand.cs");
        }
    }

    [Fact]
    public void Scan_CatchesNonCopyForwardHyperlinkAssignment_AsLastInitializerMemberWithNoTrailingComma()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("FreeX.R102HyperlinkGuardFixture-"))
        {
            var fixtureDirectory = temporaryDirectory.Path;
            File.WriteAllText(
                Path.Combine(fixtureDirectory, "SetChartHyperlinkCommand.cs"),
                """
                namespace FreeX.Core.Commands;

                public sealed class SetChartHyperlinkCommand
                {
                    public ChartModel Apply(ChartModel chart, DrawingObjectHyperlink newLink)
                    {
                        return chart with
                        {
                            Title = chart.Title,
                            Hyperlink = newLink
                        };
                    }
                }
                """);

            var violations = R101_DrawingChartHyperlinkPatchSafetyGuardTests.ScanForViolationsForTesting(fixtureDirectory);

            violations.Should().ContainSingle(
                "Hyperlink as the LAST member of a `with`-initializer, on its OWN line with no trailing " +
                "comma and with the closing brace on the NEXT line, must still be caught even though no " +
                "`,`/`;`/`}` terminator appears on the same line as `Hyperlink =`");
            violations[0].Should().Contain("SetChartHyperlinkCommand.cs");
        }
    }

    /// <summary>
    /// No-regression sibling: the two new shapes above must not cause the scan to over-match legitimate
    /// copy-forward sites (single-line, mid-initializer with a trailing comma) that the original R101
    /// guard already relied on staying silent for.
    /// </summary>
    [Fact]
    public void Scan_StillAllowsKnownCopyForwardShapes_SingleLineAndLastMember()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("FreeX.R102HyperlinkGuardFixture-"))
        {
            var fixtureDirectory = temporaryDirectory.Path;
            File.WriteAllText(
                Path.Combine(fixtureDirectory, "CloneShapeSite.cs"),
                """
                namespace FreeX.Core.Commands;

                public sealed class CloneShapeSite
                {
                    public DrawingShapeModel Clone(DrawingShapeModel shape) => new()
                    {
                        Name = shape.Name,
                        Hyperlink = shape.Hyperlink,
                    };

                    public ChartModel CloneChart(ChartModel chart) => chart with { Hyperlink = chart.Hyperlink };
                }
                """);

            var violations = R101_DrawingChartHyperlinkPatchSafetyGuardTests.ScanForViolationsForTesting(fixtureDirectory);

            violations.Should().BeEmpty(
                "a plain copy-forward of an existing object's own Hyperlink -- whether mid-initializer " +
                "with a trailing comma or as the last member of a `with`-initializer -- introduces no " +
                "new data and must remain a non-violation");
        }
    }
}
