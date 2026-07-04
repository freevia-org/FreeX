using System.Globalization;
using System.Threading;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// Regression coverage for group E-pivots finding H43: GETPIVOTDATA's numeric item-argument
// formatting must use the same culture convention the pivot layout uses to render row/column
// labels, or a non-integer numeric item lookup fails with #REF! under comma-decimal cultures.
public partial class PhaseA2FunctionTests
{
    [Fact]
    public void GetPivotData_NonIntegerRowItem_MatchesUnderCommaDecimalCulture()
    {
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            var (wb, sheet) = MakeWb(
                (1, 1, new TextValue("Price")),
                (1, 2, new TextValue("Qty")),
                (2, 5, new TextValue("Price")),
                (2, 6, new TextValue("Sum of Qty")),
                (3, 5, new TextValue("1000,5")),
                (3, 6, new NumberValue(3)),
                (4, 5, new TextValue("Grand Total")),
                (4, 6, new NumberValue(3)));
            var pivot = new PivotTableModel
            {
                Name = "PivotTable1",
                CacheId = 1,
                SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
                TargetRange = new GridRange(new CellAddress(sheet.Id, 2, 5), new CellAddress(sheet.Id, 4, 6))
            };
            pivot.RowFields.Add(new PivotFieldModel(0));
            pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Qty", "sum"));
            sheet.PivotTables.Add(pivot);

            // The rendered row label ("1000,5") was produced with CurrentCulture (comma
            // decimal). Before the fix, the formula's numeric item argument was formatted
            // with InvariantCulture ("1000.5"), so the two strings never matched and the
            // formula returned #REF! even though the row genuinely exists.
            _eval.Evaluate("=GETPIVOTDATA(\"Sum of Qty\",E2,\"Price\",1000.5)", sheet, wb)
                .Should()
                .Be(new NumberValue(3));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }
}
