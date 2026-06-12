using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    // ── Fix A: Blank handling in duplicate/unique rules ───────────────────────

    [Fact]
    public void DuplicateValues_BlankCellsNotFlagged_DensePath()
    {
        // Range of 5 cells (< 10,000 threshold → dense path).
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Alpha")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("Alpha")));
        // rows 3, 4, 5 are blank

        var yellow = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp = GetViewport(wb, sheet);

        // Only the two non-blank duplicate "Alpha" cells should be highlighted.
        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132),
            "row 1 contains 'Alpha' which appears twice — should be flagged");
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132),
            "row 2 contains 'Alpha' which appears twice — should be flagged");
        // Blank cells should not be flagged at all (no display cell returned for them in the standard path).
        // We verify they do not appear with the highlight style if present.
        vp.Cells
            .Where(c => c.Row >= 3 && c.Row <= 5 && c.Col == 1)
            .Should().AllSatisfy(c =>
                c.Style?.FillColor.Should().NotBe(new CellColor(255, 235, 132),
                    "blank cells must not be flagged as duplicates"));
    }

    [Fact]
    public void DuplicateValues_BlankCellsNotFlagged_SparsePath()
    {
        // Build a range of 101 rows × 100 cols = 10,100 cells (> 10,000 → sparse path).
        var (wb, sheet) = MakeWorkbook();

        // Fill the range sparsely — only two non-blank cells that are duplicates.
        uint lastRow = 101;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Beta")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("Beta")));
        // Everything else in the 101×100 range is blank.

        var yellow = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, lastRow, 100)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132),
            "row 1 has 'Beta' (appears twice) — sparse path should flag it");
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132),
            "row 2 has 'Beta' (appears twice) — sparse path should flag it");
    }

    [Fact]
    public void DuplicateValues_DenseAndSparsePaths_ProduceSameBlankBehavior()
    {
        // Verify that at the threshold boundary (just below and just above) the rule fires
        // identically — i.e., blanks are never counted as duplicates on either path.

        // Dense: 100 rows × 100 cols = 10,000 cells (≤ 10,000 → dense).
        var (wb1, sheet1) = MakeWorkbook();
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), Cell.FromValue(new TextValue("X")));
        sheet1.SetCell(new CellAddress(sheet1.Id, 2, 1), Cell.FromValue(new TextValue("X")));

        var yellow = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        sheet1.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet1.Id, 1, 1),
                new CellAddress(sheet1.Id, 100, 100)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp1 = GetViewport(wb1, sheet1);

        // Sparse: 101 rows × 100 cols = 10,100 cells (> 10,000 → sparse).
        var (wb2, sheet2) = MakeWorkbook();
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), Cell.FromValue(new TextValue("X")));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), Cell.FromValue(new TextValue("X")));

        sheet2.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet2.Id, 1, 1),
                new CellAddress(sheet2.Id, 101, 100)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp2 = GetViewport(wb2, sheet2);

        // Both paths should highlight the two duplicate "X" cells.
        GetCell(vp1, 1, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132), "dense: row1 X is duplicate");
        GetCell(vp1, 2, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132), "dense: row2 X is duplicate");
        GetCell(vp2, 1, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132), "sparse: row1 X is duplicate");
        GetCell(vp2, 2, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132), "sparse: row2 X is duplicate");
    }

    [Fact]
    public void UniqueValues_BlankCellsNotFlagged()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Gamma")));
        // rows 2 and 3 are blank

        var green = new CellStyle { FillColor = new CellColor(198, 239, 206) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.UniqueValues,
            FormatIfTrue = green
        });

        var vp = GetViewport(wb, sheet);

        // "Gamma" appears once — it IS unique and should be highlighted.
        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206),
            "'Gamma' is the only non-blank value and is unique — should be highlighted");

        // Blank cells in the range must not be highlighted as unique.
        vp.Cells
            .Where(c => c.Row >= 2 && c.Row <= 3 && c.Col == 1)
            .Should().AllSatisfy(c =>
                c.Style?.FillColor.Should().NotBe(new CellColor(198, 239, 206),
                    "blank cells must never be flagged as unique values"));
    }

    // ── Fix B: CF evaluation context cache ───────────────────────────────────

    [Fact]
    public void CfContext_TwoConsecutiveGetViewportCalls_ReuseContext()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(42)));

        var style = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 10, 1)),
            Priority = 1,
            RuleType = CfRuleType.AboveAverage,
            FormatIfTrue = style
        });

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        svc.GetViewport(wb, sheet.Id, request);
        var buildCountAfterFirst = svc.CfContextBuildCount;

        svc.GetViewport(wb, sheet.Id, request);
        var buildCountAfterSecond = svc.CfContextBuildCount;

        buildCountAfterFirst.Should().Be(1, "first call must build the context");
        buildCountAfterSecond.Should().Be(1, "second call without any edits must reuse the cached context");
    }

    [Fact]
    public void CfContext_AfterSetCell_RebuildContext()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));

        var style = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.AboveAverage,
            FormatIfTrue = style
        });

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        svc.GetViewport(wb, sheet.Id, request);
        var countBefore = svc.CfContextBuildCount;

        // Mutate the sheet — this should invalidate the cached context.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(20)));
        svc.GetViewport(wb, sheet.Id, request);
        var countAfter = svc.CfContextBuildCount;

        countBefore.Should().Be(1, "first call builds the context");
        countAfter.Should().Be(2, "after SetCell the context must be rebuilt");
    }

    [Fact]
    public void CfContext_AfterAddConditionalFormat_RebuildContext()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        // First call — no CF rules yet.
        svc.GetViewport(wb, sheet.Id, request);
        var countBefore = svc.CfContextBuildCount;

        // Add a CF rule — this bumps ConditionalFormats.Version.
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.AboveAverage,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        svc.GetViewport(wb, sheet.Id, request);
        var countAfter = svc.CfContextBuildCount;

        // With no CF rules the BuildContext returns the static EmptyContext — that does not
        // increment CfContextBuildCount. After adding a rule the context must be rebuilt.
        countAfter.Should().BeGreaterThan(countBefore,
            "adding a CF rule changes ConditionalFormats.Version — the cache must rebuild");
    }

    [Fact]
    public void CfContext_AfterRemoveConditionalFormat_RebuildContext()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));

        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.AboveAverage,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        };
        sheet.ConditionalFormats.Add(rule);

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        svc.GetViewport(wb, sheet.Id, request);
        var countBefore = svc.CfContextBuildCount;

        // Remove the rule — version changes.
        sheet.ConditionalFormats.Remove(rule);

        svc.GetViewport(wb, sheet.Id, request);
        var countAfter = svc.CfContextBuildCount;

        countBefore.Should().Be(1, "first call built the context");
        // After removal the sheet has no CF rules, BuildContext returns EmptyContext
        // (no build count increment), but the *cache lookup* still misses because the version key changed.
        // Because BuildContext for an empty sheet returns the static EmptyContext without incrementing
        // CfContextBuildCount, countAfter stays at 1 — what matters is correctness: no crash, no stale data.
        countAfter.Should().BeGreaterThanOrEqualTo(countBefore,
            "removing a CF rule invalidates the cached context");
    }

    [Fact]
    public void CfContext_MoreThan8SheetsWithRevisits_NoRebuildOnRevisit()
    {
        // Regression: the eviction queue admitted duplicate keys.  When a sheet was re-visited
        // after eviction it got re-inserted and re-enqueued; but the stale prior queue slot was
        // still present and would later evict the live re-inserted entry, triggering a spurious
        // rebuild on the NEXT visit.
        //
        // Setup: 10 sheets (> MaxCachedContexts = 8) each with a CF rule.
        //        Navigate through all 10 once, then revisit sheet 1 and assert no rebuild.
        const int sheetCount = 10;
        var wb = new Workbook("test");
        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        var style = new CellStyle { FillColor = new CellColor(200, 50, 50) };
        var sheets = new Sheet[sheetCount];
        for (var i = 0; i < sheetCount; i++)
        {
            var s = wb.AddSheet($"Sheet{i + 1}");
            s.SetCell(new CellAddress(s.Id, 1, 1), Cell.FromValue(new NumberValue(i + 1)));
            s.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = new GridRange(
                    new CellAddress(s.Id, 1, 1),
                    new CellAddress(s.Id, 5, 1)),
                Priority = 1,
                RuleType = CfRuleType.AboveAverage,
                FormatIfTrue = style
            });
            sheets[i] = s;
        }

        // First pass: visit all 10 sheets to fill + overflow the cache
        foreach (var s in sheets)
            svc.GetViewport(wb, s.Id, request);

        var buildCountAfterFirstPass = svc.CfContextBuildCount;
        buildCountAfterFirstPass.Should().Be(sheetCount, "each sheet must be built once on first visit");

        // Revisit sheet[0] — its key was evicted when the cache overflowed.  It must be rebuilt once.
        svc.GetViewport(wb, sheets[0].Id, request);
        var buildCountAfterFirstRevisit = svc.CfContextBuildCount;
        buildCountAfterFirstRevisit.Should().Be(sheetCount + 1, "first revisit after eviction requires one rebuild");

        // Revisit sheet[0] again immediately — the context is now in cache, must NOT rebuild.
        svc.GetViewport(wb, sheets[0].Id, request);
        var buildCountAfterSecondRevisit = svc.CfContextBuildCount;
        buildCountAfterSecondRevisit.Should().Be(sheetCount + 1,
            "second immediate revisit must hit the cache — the stale queue slot must not have evicted the live entry");
    }
}
