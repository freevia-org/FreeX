using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-58 fixes:
/// (R58-render-cf-eval-6-1) CellIs Equal/NotEqual against a text-valued cell must never fall back
/// to a coincidental string compare when the rule's comparand is a genuine numeric literal or
/// resolves to a number -- Excel never treats a text cell value as equal to a number (="5"=5 is
/// FALSE). See ViewportConditionalFormatEvaluator.Aggregates.cs's MatchesCellValue/
/// IsNumericCellValueComparand.
/// (R58-render-comment-indicator-6-3) The threaded-comment hover-preview body must include each
/// message's CreatedAtUtc timestamp, matching the inline editor's heading format, instead of
/// silently dropping it. See ViewportService.cs's FormatThreadedComment/AppendCommentLine.
/// </summary>
public partial class ConditionalFormatTests
{
    [Fact]
    public void CellValue_EqualToBareNumericLiteral_TextCellNeverMatches()
    {
        var (wb, sheet) = MakeWorkbook();
        // A1 holds the TEXT string "5" (e.g. entered with a leading apostrophe, or a Text-formatted
        // ID/zip column) -- a very common real-world pattern.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("5")));
        // Sibling cell holding a genuinely different text value, to prove the rule isn't matching
        // everything.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("6")));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.Equal,
            // Bare, unquoted numeric literal -- Excel's normal encoding for "Equal To 5".
            Value1 = "5",
            FormatIfTrue = redStyle
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style?.FillColor.Should().NotBe(
            new CellColor(255, 0, 0),
            "real Excel never treats the text \"5\" as equal to the number 5 (=\"5\"=5 is FALSE)");
        GetCell(vp, 2, 1).Style?.FillColor.Should().NotBe(new CellColor(255, 0, 0));
    }

    [Fact]
    public void CellValue_NotEqualToBareNumericLiteral_TextCellAlwaysMatches()
    {
        // Sibling/no-regression test: the NotEqual direction of the very same rule must be the
        // logical inverse -- a text cell is always "not equal" to a numeric CellIs comparand.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("5")));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.NotEqual,
            Value1 = "5",
            FormatIfTrue = redStyle
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(
            new CellColor(255, 0, 0),
            "the text \"5\" is never equal to the number 5, so NotEqual must fire");
    }

    [Fact]
    public void CellValue_EqualToQuotedTextLiteral_StillUsesGenuineStringCompare()
    {
        // No-regression guard: a genuinely textual comparand (a quoted literal) must still go
        // through the ordinary case-insensitive string compare against a text cell, unaffected by
        // the new numeric-comparand short circuit.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("5")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("6")));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.Equal,
            Value1 = "\"5\"",
            FormatIfTrue = redStyle
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(
            new CellColor(255, 0, 0),
            "a quoted text criterion \"5\" is a genuine text comparand and must still match the text cell \"5\"");
        GetCell(vp, 2, 1).Style?.FillColor.Should().NotBe(new CellColor(255, 0, 0));
    }

    [Fact]
    public void GetViewport_ThreadedComment_HoverPreviewIncludesReplyTimestamps()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        var rootCreated = new DateTimeOffset(2026, 3, 5, 9, 30, 0, TimeSpan.Zero);
        var replyCreated = new DateTimeOffset(2026, 3, 6, 14, 45, 0, TimeSpan.Zero);
        sheet.ThreadedComments[address] = new ThreadedComment("Root review", "Quinn")
        {
            CreatedAtUtc = rootCreated,
            Replies = [new CommentReply("Looks good", "Codex") { CreatedAtUtc = replyCreated }]
        };

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        var dc = vp.Cells.Single(c => c.Row == 1 && c.Col == 1);
        dc.CommentDisplay.Should().NotBeNull();
        // Matches ThreadedCommentDialogPlanner.FormatMessageHeading's "yyyy-MM-dd HH:mm UTC" format
        // used by the inline editor for the very same thread.
        dc.CommentDisplay!.Body.Should().Contain("Quinn - 2026-03-05 09:30 UTC: Root review");
        dc.CommentDisplay.Body.Should().Contain("Codex - 2026-03-06 14:45 UTC: Looks good");
    }

    [Fact]
    public void GetViewport_ThreadedCommentWithoutTimestamps_HoverPreviewOmitsHeadingDate()
    {
        // No-regression guard: a message with no CreatedAtUtc (e.g. an in-session draft, or a
        // legacy file that never carried a dT) must keep the plain "Author: text" heading with no
        // stray timestamp/separator appended.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.ThreadedComments[address] = new ThreadedComment("Root review", "Quinn")
        {
            Replies = [new CommentReply("Looks good", "Codex")]
        };

        var svc = new ViewportService();
        var vp = svc.GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        var dc = vp.Cells.Single(c => c.Row == 1 && c.Col == 1);
        dc.CommentDisplay.Should().NotBeNull();
        dc.CommentDisplay!.Body.Should().Contain("Quinn: Root review");
        dc.CommentDisplay.Body.Should().Contain("Codex: Looks good");
        dc.CommentDisplay.Body.Should().NotContain("UTC");
    }
}
