using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- Treasury bill helpers --------------------------------------------

    private static ScalarValue Tbilleq(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapTernaryTextArgs(args[0], args[1], args[2], TbilleqScalar);
    }

    private static ScalarValue TbilleqScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue discountValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double discount = ToNumber(discountValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(discount))
            return ErrorValue.Num;
        if (discount <= 0 || discount >= 1) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        double dsm = (md - sd).TotalDays;
        if (dsm <= 0 || dsm > 182) return ErrorValue.Num;
        return NumberResult((365 * discount) / (360 - discount * dsm));
    }

    private static ScalarValue Tbillprice(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapTernaryTextArgs(args[0], args[1], args[2], TbillpriceScalar);
    }

    private static ScalarValue TbillpriceScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue discountValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double discount = ToNumber(discountValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(discount))
            return ErrorValue.Num;
        if (discount <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        double dsm = (md - sd).TotalDays;
        if (dsm <= 0) return ErrorValue.Num;
        return NumberResult(100 * (1 - discount * dsm / 360.0));
    }

    private static ScalarValue Tbillyield(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        return MapTernaryTextArgs(args[0], args[1], args[2], TbillyieldScalar);
    }

    private static ScalarValue TbillyieldScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue priceValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double pr = ToNumber(priceValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(pr))
            return ErrorValue.Num;
        if (pr <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        double dsm = (md - sd).TotalDays;
        if (dsm <= 0) return ErrorValue.Num;
        return NumberResult((100 - pr) / pr * 360.0 / dsm);
    }
}
