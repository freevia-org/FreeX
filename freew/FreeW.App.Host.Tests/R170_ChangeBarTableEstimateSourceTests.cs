using System.IO;

namespace FreeW.App.Host.Tests;

/// <summary>
/// r170. The print preview's change-bar band estimator kept a twin of the table height rule that
/// ignored cell margins entirely: it wrapped cell text at the full column width and added no
/// vertical padding, while the page that actually renders takes both from
/// DocumentViewLayoutPlanner. About 1.3-1.6 DIP of drift per row compounds to roughly a third of a
/// page over a hundred-row table, which moves every change bar after that table off its paragraph.
/// The rules now live once on the planner; this pins that the preview reads them rather than
/// growing a third copy. The behaviour of the rules themselves is pinned by
/// R170_TableCellPaddingRuleTests in FreeW.App.Presentation.Tests.
/// </summary>
public sealed class R170_ChangeBarTableEstimateSourceTests
{
    [Fact]
    public void ChangeBarTableEstimate_ReadsTheSharedCellPaddingRules()
    {
        var estimator = ExtractMethod(
            ReadHostSource("PrintPreviewWindow.cs"),
            "private static double EstimateChangeBarTableHeightDip(");

        estimator.Should().Contain("DocumentViewLayoutPlanner.ResolveTableCellContentWidthDip(");
        estimator.Should().Contain("DocumentViewLayoutPlanner.AddTableCellVerticalPaddingDip(");
        estimator.Should().Contain("DocumentViewLayoutPlanner.ApplyTableRowHeightFloorDip(");
    }

    [Fact]
    public void ChangeBarTableEstimate_WrapsCellTextAtTheContentWidthNotTheColumnWidth()
    {
        var estimator = ExtractMethod(
            ReadHostSource("PrintPreviewWindow.cs"),
            "private static double EstimateChangeBarTableHeightDip(");

        estimator.Should().NotContain(
            "EstimateChangeBarParagraphHeightDip(cellParagraph, cellWidthDip",
            "wrapping at the full allocated column width ignores the cell margins and under-counts lines");
    }

    private static string ReadHostSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(
            Path.Combine(new[] { root, "freew", "FreeW.App.Host" }.Concat(parts).ToArray()));
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "{0} must exist", signature);

        var depth = 0;
        var seenOpen = false;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
                seenOpen = true;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (seenOpen && depth == 0)
                    return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"unterminated method body for {signature}");
    }
}
