using System.Diagnostics;
using System.Linq;
using System.Reflection;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// The clipboard HTML-table parser expands a cell's <c>colspan</c>/<c>rowspan</c> into that many
/// columns. The attribute is taken straight from the clipboard payload, and a page's copy handler can
/// put arbitrary HTML there, so an absurd span (<c>&lt;td colspan="500000000"&gt;</c>) turned Ctrl+V
/// into hundreds of millions of list operations on the UI thread — a hang, then OutOfMemoryException.
/// Nothing beyond the sheet's own column limit is pasteable, so the span is clamped there.
/// </summary>
public sealed class HtmlClipboardSpanClampTests
{
    private static string[][] ParseRows(string html)
    {
        var method = typeof(MainWindow).GetMethod(
            "TryParseHtmlClipboardTableRows", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(MainWindow), "TryParseHtmlClipboardTableRows");

        var result = (System.Collections.Generic.List<System.Collections.Generic.IReadOnlyList<string>>?)
            method.Invoke(null, [html]);
        result.Should().NotBeNull();
        return result!.Select(row => row.ToArray()).ToArray();
    }

    [Theory]
    [InlineData("500000000")]
    [InlineData("2147483647")]
    public void AbsurdColspan_IsClampedInsteadOfExhaustingMemory(string colspan)
    {
        StaTestRunner.Run(() =>
        {
            var html =
                "<html><body><!--StartFragment--><table>" +
                $"<tr><td colspan=\"{colspan}\">wide</td></tr>" +
                "</table><!--EndFragment--></body></html>";

            var stopwatch = Stopwatch.StartNew();
            var rows = ParseRows(html);
            stopwatch.Stop();

            rows.Should().HaveCount(1);
            rows[0].Length.Should().BeLessThanOrEqualTo(16384,
                "a span wider than the sheet cannot be pasted, so it must be clamped to the column limit");
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
                "an unclamped span hung the UI thread before exhausting memory");
        });
    }

    [Fact]
    public void OrdinaryColspan_StillExpandsExactly()
    {
        StaTestRunner.Run(() =>
        {
            const string html =
                "<html><body><!--StartFragment--><table>" +
                "<tr><td colspan=\"3\">merged</td><td>tail</td></tr>" +
                "</table><!--EndFragment--></body></html>";

            var rows = ParseRows(html);

            rows.Should().HaveCount(1);
            // Pass the expectation as a collection: the params overload would swallow a trailing
            // "because" string as another expected element.
            rows[0].Should().Equal(
                new[] { "merged", "merged", "merged", "tail" },
                "clamping must not disturb normal merged-cell expansion");
        });
    }
}
