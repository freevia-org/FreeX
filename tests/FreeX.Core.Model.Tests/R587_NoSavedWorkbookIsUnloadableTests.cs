using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r587: FreeX must never write a workbook it cannot read back, whatever combination of overlapping
/// entities the model holds.
///
/// <para>r586 found ONE such combination -- a worksheet AutoFilter over a structured table -- and
/// guarded the two commands that could create it. This probe asks the more general question by
/// placing every ordered pair of entities over the SAME range, saving, and reloading. Thirty pairs;
/// the table/autofilter pair in both orders was the only one with that property, which is a useful
/// bound on r586 rather than a new finding.</para>
///
/// <para>It builds the model DIRECTLY rather than through commands, which is the point: r586's
/// guards live in two commands, and a model can reach the same state by other roads -- an .fxl load,
/// a paste, a command not yet written. The remedy this test pins is at the WRITE chokepoint, so the
/// unreadable file is unwritable rather than merely unreachable by the paths known today.</para>
/// </summary>
public sealed class R587_NoSavedWorkbookIsUnloadableTests
{
    private static (Workbook Workbook, Sheet Sheet, GridRange Range) Fresh()
    {
        var workbook = new Workbook("Overlap");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint r = 1; r <= 6; r++)
        for (uint c = 1; c <= 4; c++)
            sheet.SetCell(new CellAddress(sheet.Id, r, c),
                r == 1 ? new TextValue($"H{c}") : new NumberValue(r * c));

        return (workbook, sheet, new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)));
    }

    private static readonly (string Name, Action<Workbook, Sheet, GridRange> Apply)[] Entities =
    [
        ("table", (wb, sheet, range) =>
        {
            var table = new StructuredTableModel
            {
                Id = 1, Name = "T1", DisplayName = "T1", Range = range,
            };
            for (var i = 1; i <= 4; i++)
                table.Columns.Add(new StructuredTableColumnModel(i, $"H{i}"));
            sheet.StructuredTables.Add(table);
        }),
        ("autofilter", (wb, sheet, range) =>
            sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null)),
        ("merge", (wb, sheet, range) => sheet.AddMergedRegion(range)),
        ("datavalidation", (wb, sheet, range) => sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.Decimal,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "100",
        })),
        ("conditionalformat", (wb, sheet, range) => sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.DataBar,
            DataBarGradient = true,
        })),
        ("hyperlink", (wb, sheet, range) =>
            sheet.Hyperlinks[range.Start] = "https://example.invalid/"),
    ];

    [Fact]
    public void NoPairOfOverlappingEntitiesProducesAnUnloadableFile()
    {
        var failures = new List<string>();
        var pairs = 0;

        for (var i = 0; i < Entities.Length; i++)
        for (var j = 0; j < Entities.Length; j++)
        {
            if (i == j) continue;
            pairs++;

            var (workbook, sheet, range) = Fresh();
            try
            {
                Entities[i].Apply(workbook, sheet, range);
                Entities[j].Apply(workbook, sheet, range);
            }
            catch (Exception ex)
            {
                failures.Add($"MODEL-REFUSED {Entities[i].Name}+{Entities[j].Name} -> {ex.GetType().Name}");
                continue;
            }

            byte[] saved;
            try
            {
                using var ms = new MemoryStream();
                new XlsxFileAdapter().Save(workbook, ms);
                saved = ms.ToArray();
            }
            catch (Exception ex)
            {
                failures.Add($"SAVE-THREW {Entities[i].Name}+{Entities[j].Name} -> {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                continue;
            }

            try
            {
                using var ms = new MemoryStream(saved);
                var reloaded = new XlsxFileAdapter().Load(ms);
                if (reloaded.Sheets.Count == 0)
                    failures.Add($"EMPTY {Entities[i].Name}+{Entities[j].Name}");
            }
            catch (Exception ex)
            {
                failures.Add($"SAVED-BUT-UNLOADABLE {Entities[i].Name}+{Entities[j].Name} -> {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        pairs.Should().BeGreaterThan(20,
            "the probe must actually be building and saving every ordered pair");

        failures.Should().BeEmpty(
            "FreeX must never write a file it cannot itself open, however the model reached that state");
    }
}
