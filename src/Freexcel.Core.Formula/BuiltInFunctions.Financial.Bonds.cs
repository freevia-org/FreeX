using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- Standard bond price/yield helpers --------------------------------

    private static double CalcBondPrice(DateTime settlement, DateTime maturity, double couponRate,
        double yld, double redemption, int frequency, int basis)
    {
        DateTime pcd = CouponDateBefore(settlement, maturity, frequency);
        DateTime ncd = CouponDateAfter(settlement, maturity, frequency);
        double daysInPeriod = (ncd - pcd).TotalDays;
        double daysToNext = (ncd - settlement).TotalDays;
        double a = daysInPeriod > 0 ? daysToNext / daysInPeriod : 1.0;

        // Count coupons from next coupon date to maturity
        int n = 0;
        DateTime d = ncd;
        int months = 12 / frequency;
        while (d <= maturity)
        {
            n++;
            d = d.AddMonths(months);
        }
        if (n == 0) n = 1;

        double c = couponRate / frequency * redemption;
        double y = yld / frequency;
        double price = 0;
        for (int k = 1; k <= n; k++)
            price += c / Math.Pow(1 + y, k - 1 + a);
        price += redemption / Math.Pow(1 + y, n - 1 + a);
        return price;
    }

    private static ScalarValue Price(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 6 ? args[6] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], args[5], basisArg],
            values => PriceScalar(values[0], values[1], values[2], values[3], values[4], values[5], values[6]));
    }

    private static ScalarValue PriceScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue rateValue, ScalarValue yieldValue, ScalarValue redemptionValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double rate = ToNumber(rateValue);
        double redemption = ToNumber(redemptionValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return PriceScalar(settlement, maturity, rate, yieldValue, redemption, frequency, basis);
    }

    private static ScalarValue PriceScalar(double settlement, double maturity, double rate, ScalarValue yieldValue, double redemption, int frequency, int basis)
    {
        double yld = ToNumber(yieldValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(rate) ||
            !double.IsFinite(yld) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || yld < 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        double price = CalcBondPrice(sd, md, rate, yld, redemption, frequency, basis);
        return NumberResult(price);
    }

    private static ScalarValue Yield(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 6 ? args[6] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], args[5], basisArg],
            values => YieldScalar(values[0], values[1], values[2], values[3], values[4], values[5], values[6]));
    }

    private static ScalarValue YieldScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue rateValue, ScalarValue priceValue, ScalarValue redemptionValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double rate = ToNumber(rateValue);
        double redemption = ToNumber(redemptionValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return YieldScalar(settlement, maturity, rate, priceValue, redemption, frequency, basis);
    }

    private static ScalarValue YieldScalar(double settlement, double maturity, double rate, ScalarValue priceValue, double redemption, int frequency, int basis)
    {
        double pr = ToNumber(priceValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(rate) ||
            !double.IsFinite(pr) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || pr <= 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        // Newton-Raphson: find y such that Price(y) = pr
        double y = 0.1;
        for (int iter = 0; iter < 200; iter++)
        {
            double p = CalcBondPrice(sd, md, rate, y, redemption, frequency, basis);
            double dy = 1e-6;
            double dp = (CalcBondPrice(sd, md, rate, y + dy, redemption, frequency, basis) - p) / dy;
            if (Math.Abs(dp) < 1e-14) break;
            double delta = (p - pr) / dp;
            y -= delta;
            if (y < -0.999) y = -0.999;
            if (Math.Abs(delta) < 1e-10) break;
        }
        return NumberResult(y);
    }
}
