using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// r170 remediation. The conditional-format evaluation cache added for the colour-agreement fix is
/// STATIC -- shared by every open document and window -- and was mutated with no synchronisation.
/// It is reachable from Sort, Filter by Colour and the AutoFilter swatch scan, and an existing test
/// class already drives the same path inside an assembly whose collections run in parallel, so the
/// corruption was live rather than theoretical: a probe reproduced dictionary and queue exceptions
/// within a few thousand iterations.
///
/// This drives the public entry point from many threads at once. It fails by throwing, not by
/// asserting a value, which is what a corrupted collection actually does.
/// </summary>
public sealed class R170_SortCommandCfSessionCacheConcurrencyTests
{
    [Fact]
    public void GetEffectiveColor_UnderConcurrentUse_DoesNotCorruptTheSharedSessionCache()
    {
        // Several distinct sheets so the cache both hits and evicts while threads contend.
        var workbook = new Workbook("ConcurrentCf");
        var sheets = new List<Sheet>();
        for (var s = 0; s < 12; s++)
        {
            var sheet = workbook.AddSheet($"S{s}");
            for (uint row = 1; row <= 8; row++)
                sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));

            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = new GridRange(
                    new CellAddress(sheet.Id, 1, 1),
                    new CellAddress(sheet.Id, 8, 1)),
                RuleType = CfRuleType.CellValue,
                Operator = CfOperator.GreaterThan,
                Value1 = "4",
                FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
            });
            sheets.Add(sheet);
        }

        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 16, thread =>
        {
            try
            {
                for (var i = 0; i < 400; i++)
                {
                    var sheet = sheets[i % sheets.Count];
                    var address = new CellAddress(sheet.Id, (uint)(i % 8) + 1, 1);
                    _ = SortCommand.GetEffectiveColor(
                        workbook,
                        sheet,
                        address,
                        sheet.GetCell(address),
                        wantFill: true,
                        effectiveValue: sheet.GetCell(address)?.Value);
                }
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        });

        failures.Should().BeEmpty(
            "the shared conditional-format session cache must tolerate concurrent readers");
    }
}
