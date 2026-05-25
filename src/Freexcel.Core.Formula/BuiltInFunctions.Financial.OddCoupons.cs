using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // ── Odd coupon period functions ──────────────────────────────────────

    private static ScalarValue Oddfprice(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // Odd first period: settlement, maturity, issue, first_coupon, rate, yld, redemption, frequency, [basis]
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double issue       = ToNumber(args[2]);
        double firstCoupon = ToNumber(args[3]);
        double rate        = ToNumber(args[4]);
        double redemption  = ToNumber(args[6]);
        int frequency      = (int)Math.Truncate(ToNumber(args[7]));
        int basis = args.Count > 8 && args[8] is not BlankValue ? (int)Math.Truncate(ToNumber(args[8])) : 0;
        if (args[5] is RangeValue yieldRange) return MapUnaryTextRange(yieldRange, value => OddfpriceScalar(settlement, maturity, issue, firstCoupon, rate, value, redemption, frequency, basis));
        return OddfpriceScalar(settlement, maturity, issue, firstCoupon, rate, args[5], redemption, frequency, basis);
    }

    private static ScalarValue OddfpriceScalar(double settlement, double maturity, double issue, double firstCoupon, double rate, ScalarValue yieldValue, double redemption, int frequency, int basis)
    {
        double yld = ToNumber(yieldValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(issue) ||
            !double.IsFinite(firstCoupon) || !double.IsFinite(rate) || !double.IsFinite(yld) ||
            !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || yld < 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md) ||
            !TryGetFinancialDate(issue, out DateTime id) ||
            !TryGetFinancialDate(firstCoupon, out DateTime fcd)) return ErrorValue.Num;
        if (!(md > fcd && fcd > sd && sd > id)) return ErrorValue.Num;
        double price = OddFirstPrice(id, sd, md, fcd, rate, yld, redemption, frequency, basis);
        return NumberResult(price);
    }

    private static ScalarValue Oddfyield(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // settlement, maturity, issue, first_coupon, rate, pr, redemption, frequency, [basis]
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double issue       = ToNumber(args[2]);
        double firstCoupon = ToNumber(args[3]);
        double rate        = ToNumber(args[4]);
        double redemption  = ToNumber(args[6]);
        int frequency      = (int)Math.Truncate(ToNumber(args[7]));
        int basis = args.Count > 8 && args[8] is not BlankValue ? (int)Math.Truncate(ToNumber(args[8])) : 0;
        if (args[5] is RangeValue priceRange) return MapUnaryTextRange(priceRange, value => OddfyieldScalar(settlement, maturity, issue, firstCoupon, rate, value, redemption, frequency, basis));
        return OddfyieldScalar(settlement, maturity, issue, firstCoupon, rate, args[5], redemption, frequency, basis);
    }

    private static ScalarValue OddfyieldScalar(double settlement, double maturity, double issue, double firstCoupon, double rate, ScalarValue priceValue, double redemption, int frequency, int basis)
    {
        double pr = ToNumber(priceValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(issue) ||
            !double.IsFinite(firstCoupon) || !double.IsFinite(rate) || !double.IsFinite(pr) ||
            !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || pr <= 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md) ||
            !TryGetFinancialDate(issue, out DateTime id) ||
            !TryGetFinancialDate(firstCoupon, out DateTime fcd)) return ErrorValue.Num;
        if (!(md > fcd && fcd > sd && sd > id)) return ErrorValue.Num;

        double y = 0.1;
        for (int iter = 0; iter < 200; iter++)
        {
            double p = OddFirstPrice(id, sd, md, fcd, rate, y, redemption, frequency, basis);
            double dy = 1e-6;
            double dp = (OddFirstPrice(id, sd, md, fcd, rate, y + dy, redemption, frequency, basis) - p) / dy;
            if (Math.Abs(dp) < 1e-14) break;
            double delta = (p - pr) / dp;
            y -= delta;
            if (y < -0.999) y = -0.999;
            if (Math.Abs(delta) < 1e-10) break;
        }
        return NumberResult(y);
    }

    private static ScalarValue Oddlprice(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // settlement, maturity, last_interest, rate, yld, redemption, frequency, [basis]
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double lastInterest = ToNumber(args[2]);
        double rate        = ToNumber(args[3]);
        double redemption  = ToNumber(args[5]);
        int frequency      = (int)Math.Truncate(ToNumber(args[6]));
        int basis = args.Count > 7 && args[7] is not BlankValue ? (int)Math.Truncate(ToNumber(args[7])) : 0;
        if (args[4] is RangeValue yieldRange) return MapUnaryTextRange(yieldRange, value => OddlpriceScalar(settlement, maturity, lastInterest, rate, value, redemption, frequency, basis));
        return OddlpriceScalar(settlement, maturity, lastInterest, rate, args[4], redemption, frequency, basis);
    }

    private static ScalarValue OddlpriceScalar(double settlement, double maturity, double lastInterest, double rate, ScalarValue yieldValue, double redemption, int frequency, int basis)
    {
        double yld = ToNumber(yieldValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(lastInterest) ||
            !double.IsFinite(rate) || !double.IsFinite(yld) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || yld < 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md) ||
            !TryGetFinancialDate(lastInterest, out DateTime li)) return ErrorValue.Num;
        if (!(md > sd && sd > li)) return ErrorValue.Num;
        return NumberResult(OddLastPrice(li, sd, md, rate, yld, redemption, frequency, basis));
    }

    private static ScalarValue Oddlyield(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // settlement, maturity, last_interest, rate, pr, redemption, frequency, [basis]
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double lastInterest = ToNumber(args[2]);
        double rate        = ToNumber(args[3]);
        double redemption  = ToNumber(args[5]);
        int frequency      = (int)Math.Truncate(ToNumber(args[6]));
        int basis = args.Count > 7 && args[7] is not BlankValue ? (int)Math.Truncate(ToNumber(args[7])) : 0;
        if (args[4] is RangeValue priceRange) return MapUnaryTextRange(priceRange, value => OddlyieldScalar(settlement, maturity, lastInterest, rate, value, redemption, frequency, basis));
        return OddlyieldScalar(settlement, maturity, lastInterest, rate, args[4], redemption, frequency, basis);
    }

    private static ScalarValue OddlyieldScalar(double settlement, double maturity, double lastInterest, double rate, ScalarValue priceValue, double redemption, int frequency, int basis)
    {
        double pr = ToNumber(priceValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(lastInterest) ||
            !double.IsFinite(rate) || !double.IsFinite(pr) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (rate < 0 || pr <= 0 || redemption <= 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (basis < 0 || basis > 4) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md) ||
            !TryGetFinancialDate(lastInterest, out DateTime li)) return ErrorValue.Num;
        if (!(md > sd && sd > li)) return ErrorValue.Num;

        double daysInCoupon = CouponPeriodDays(li, frequency, basis);
        double accruedPeriods = FinancialDays(li, sd, basis) / daysInCoupon;
        double remainingPeriods = FinancialDays(sd, md, basis) / daysInCoupon;
        double oddCouponPeriods = FinancialDays(li, md, basis) / daysInCoupon;
        double couponAmt = rate / frequency * redemption;
        double numerator = redemption + couponAmt * oddCouponPeriods;
        double denominator = pr + couponAmt * accruedPeriods;
        if (Math.Abs(remainingPeriods) < 1e-14 || Math.Abs(denominator) < 1e-14) return ErrorValue.DivByZero;
        double y = (numerator / denominator - 1) / remainingPeriods * frequency;
        return NumberResult(y);
    }

    private static double OddFirstPrice(DateTime issue, DateTime settlement, DateTime maturity, DateTime firstCoupon,
        double rate, double yld, double redemption, int frequency, int basis)
    {
        int months = 12 / frequency;
        DateTime previousCoupon = firstCoupon.AddMonths(-months);
        double daysInCoupon = CouponPeriodDays(previousCoupon, frequency, basis);
        double accrued = FinancialDays(issue, settlement, basis) / daysInCoupon;
        double firstCouponPeriods = FinancialDays(issue, firstCoupon, basis) / daysInCoupon;
        double periodsToFirstCoupon = FinancialDays(settlement, firstCoupon, basis) / daysInCoupon;
        double couponAmt = rate / frequency * redemption;
        double yieldPerPeriod = yld / frequency;

        double price = couponAmt * firstCouponPeriods / Math.Pow(1 + yieldPerPeriod, periodsToFirstCoupon);
        int k = 1;
        for (DateTime d = firstCoupon.AddMonths(months); d <= maturity; d = d.AddMonths(months))
        {
            double cash = couponAmt;
            if (d == maturity)
                cash += redemption;
            price += cash / Math.Pow(1 + yieldPerPeriod, k + periodsToFirstCoupon);
            k++;
        }
        return price - couponAmt * accrued;
    }

    private static double OddLastPrice(DateTime lastInterest, DateTime settlement, DateTime maturity,
        double rate, double yld, double redemption, int frequency, int basis)
    {
        double daysInCoupon = CouponPeriodDays(lastInterest, frequency, basis);
        double accruedPeriods = FinancialDays(lastInterest, settlement, basis) / daysInCoupon;
        double remainingPeriods = FinancialDays(settlement, maturity, basis) / daysInCoupon;
        double oddCouponPeriods = FinancialDays(lastInterest, maturity, basis) / daysInCoupon;
        double couponAmt = rate / frequency * redemption;
        if (Math.Abs(remainingPeriods) < 1e-14) return double.NaN;
        double y = yld / frequency;
        return (redemption + couponAmt * oddCouponPeriods) / (1 + remainingPeriods * y)
             - couponAmt * accruedPeriods;
    }

    private static double CouponPeriodDays(DateTime periodStart, int frequency, int basis)
        => FinancialDays(periodStart, periodStart.AddMonths(12 / frequency), basis);

    private static double FinancialDays(DateTime d1, DateTime d2, int basis)
    {
        return basis switch
        {
            0 => Days360Us(d1, d2),
            1 => (d2 - d1).TotalDays,
            2 => (d2 - d1).TotalDays,
            3 => (d2 - d1).TotalDays,
            4 => Days360European(d1, d2),
            _ => (d2 - d1).TotalDays
        };
    }

    private static double Days360Us(DateTime d1, DateTime d2)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
        if (dd2 == 31 && dd1 >= 30) dd2 = 30;
        if (dd1 == 31) dd1 = 30;
        return (y2 - y1) * 360 + (m2 - m1) * 30 + (dd2 - dd1);
    }

    private static double Days360European(DateTime d1, DateTime d2)
    {
        int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
        int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
        if (dd1 == 31) dd1 = 30;
        if (dd2 == 31) dd2 = 30;
        return (y2 - y1) * 360 + (m2 - m1) * 30 + (dd2 - dd1);
    }
}
