using FreeX.Core.Model;

namespace FreeX.Core.Formula;

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
        if (dsm <= 0 || dsm > 365) return ErrorValue.Num;
        if (dsm <= 182)
            return NumberResult((365 * discount) / (360 - discount * dsm));

        // 182 < DSM <= 365: Excel's documented bond-equivalent-yield quadratic.
        // P = TBILLPRICE(settlement, maturity, discount); M = DSM.
        // BEY = (-2*(M/365) + sqrt((M/365)^2 - (2*M/365-1)*(1-100/P)) * 2) / (2*M/365 - 1)
        double price = 100 * (1 - discount * dsm / 360.0);
        double y = dsm / 365.0;
        double a = 2 * y - 1;
        double radicand = y * y - a * (1 - 100 / price);
        if (price <= 0 || radicand < 0 || a == 0) return ErrorValue.Num;
        return NumberResult((-2 * y + 2 * Math.Sqrt(radicand)) / a);
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
        if (dsm <= 0 || dsm > 365) return ErrorValue.Num;
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
        if (dsm <= 0 || dsm > 365) return ErrorValue.Num;
        return NumberResult((100 - pr) / pr * 360.0 / dsm);
    }
}
