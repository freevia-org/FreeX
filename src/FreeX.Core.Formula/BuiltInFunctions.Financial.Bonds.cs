using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- Standard bond price/yield helpers --------------------------------

    /// <summary>
    /// Days in the coupon period bounded by <paramref name="pcd"/>/<paramref name="ncd"/>, using the
    /// same basis-aware convention as COUPDAYS (see BuiltInFunctions.Financial.Coupons.cs): the actual
    /// calendar day count for basis 1 (Actual/Actual), a fixed 365/frequency for basis 3 (Actual/365),
    /// and a fixed 360/frequency for bases 0, 2 and 4 (30/360 and Actual/360 both use a 360-day year
    /// for the period length even though basis 2 counts *elapsed* days actually).
    /// </summary>
    private static double CouponPeriodDayCount(DateTime pcd, DateTime ncd, int frequency, int basis)
    {
        if (basis == 1) return (ncd - pcd).TotalDays;
        if (basis == 3) return 365.0 / frequency;
        return 360.0 / frequency;
    }

    private static double CalcBondPrice(DateTime settlement, DateTime maturity, double couponRate,
        double yld, double redemption, int frequency, int basis)
    {
        DateTime pcd = CouponDateBefore(settlement, maturity, frequency);
        DateTime ncd = CouponDateAfter(settlement, maturity, frequency);
        double daysInPeriod = CouponPeriodDayCount(pcd, ncd, frequency, basis);
        double daysToNext = CouponDayCount(settlement, ncd, basis);
        double a = daysInPeriod > 0 ? daysToNext / daysInPeriod : 1.0;

        // Count coupons from the first post-settlement coupon date (ncd) through maturity.
        // Derive every schedule candidate as an offset from the ORIGINAL maturity
        // (maturity.AddMonths(-n*months)) rather than iterating AddMonths on a drifting date,
        // matching Coupnum (BuiltInFunctions.Financial.Coupons.cs) and CouponDatesAround
        // (BuiltInFunctions.Financial.cs). Walking backward from settlement gives the same set as
        // ncd..maturity because ncd is the first coupon strictly after settlement.
        //
        // The old forward walk (d = ncd; d = d.AddMonths(months)) happened to produce the right
        // *count* even for a month-end maturity whose schedule crosses a shorter month -- there the
        // intermediate dates drift down a day (e.g. Aug-31 -> Feb-29 -> Aug-29 -> Feb-28 -> Aug-28),
        // but each drifted date stays in the same calendar month as the true schedule date, so it
        // is still <= maturity exactly when the anchored date is, leaving n unchanged. It is also
        // safe because the redemption below is added unconditionally (never gated on a date-equality
        // test, unlike the DURATION/ODDFPRICE date-drift bugs that silently drop the principal).
        // Anchoring keeps n exact regardless, so a future date-sensitive change here can't miscount.
        int months = 12 / frequency;
        int n = 0;
        DateTime d = maturity;
        while (d > settlement)
        {
            n++;
            d = maturity.AddMonths(-n * months);
        }
        if (n == 0) n = 1;

        double c = couponRate / frequency * redemption;
        double y = yld / frequency;
        double price = 0;
        for (int k = 1; k <= n; k++)
            price += c / Math.Pow(1 + y, k - 1 + a);
        price += redemption / Math.Pow(1 + y, n - 1 + a);
        // PRICE/YIELD quote the clean price: subtract accrued interest for the partial
        // first period so the dirty (full) price above is converted to Excel's clean price.
        price -= c * (1 - a);
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

    private static ScalarValue Pricedisc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 4 ? args[4] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], basisArg],
            values => PricediscScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue PricediscScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue discountValue, ScalarValue redemptionValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double redemption = ToNumber(redemptionValue);
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return PricediscScalar(settlement, maturity, discountValue, redemption, basis);
    }

    private static ScalarValue PricediscScalar(double settlement, double maturity, ScalarValue discountValue, double redemption, int basis)
    {
        double discount = ToNumber(discountValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(discount) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (discount <= 0 || redemption <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, md, basis);
        return NumberResult(redemption * (1 - discount * dcf));
    }

    private static ScalarValue Pricemat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 5 ? args[5] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], basisArg],
            values => PricematScalar(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    private static ScalarValue PricematScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue issueValue, ScalarValue rateValue, ScalarValue yieldValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double issue = ToNumber(issueValue);
        double rate = ToNumber(rateValue);
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return PricematScalar(settlement, maturity, issue, rate, yieldValue, basis);
    }

    private static ScalarValue PricematScalar(double settlement, double maturity, double issue, double rate, ScalarValue yieldValue, int basis)
    {
        double yld = ToNumber(yieldValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(issue) ||
            !double.IsFinite(rate) || !double.IsFinite(yld))
            return ErrorValue.Num;
        if (rate < 0 || yld < 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md) ||
            !TryGetFinancialDate(issue, out DateTime id)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        double dim = DayCountFraction(id, md, basis);
        double dsm = DayCountFraction(sd, md, basis);
        double a = DayCountFraction(id, sd, basis);
        double result = 100.0 * (1 + rate * dim) / (1 + yld * dsm) - a * rate * 100.0;
        return NumberResult(result);
    }

    private static ScalarValue Yielddisc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 4 ? args[4] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], basisArg],
            values => YielddiscScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue YielddiscScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue priceValue, ScalarValue redemptionValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double redemption = ToNumber(redemptionValue);
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return YielddiscScalar(settlement, maturity, priceValue, redemption, basis);
    }

    private static ScalarValue YielddiscScalar(double settlement, double maturity, ScalarValue priceValue, double redemption, int basis)
    {
        double pr = ToNumber(priceValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(pr) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (pr <= 0 || redemption <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, md, basis);
        if (dcf <= 0) return ErrorValue.Num;
        return NumberResult((redemption / pr - 1) / dcf);
    }

    private static ScalarValue Yieldmat(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 5 ? args[5] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], basisArg],
            values => YieldmatScalar(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    private static ScalarValue YieldmatScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue issueValue, ScalarValue rateValue, ScalarValue priceValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double issue = ToNumber(issueValue);
        double rate = ToNumber(rateValue);
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return YieldmatScalar(settlement, maturity, issue, rate, priceValue, basis);
    }

    private static ScalarValue YieldmatScalar(double settlement, double maturity, double issue, double rate, ScalarValue priceValue, int basis)
    {
        double pr = ToNumber(priceValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(issue) ||
            !double.IsFinite(rate) || !double.IsFinite(pr))
            return ErrorValue.Num;
        if (rate < 0 || pr <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md) ||
            !TryGetFinancialDate(issue, out DateTime id)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        double dim = DayCountFraction(id, md, basis);
        double dsm = DayCountFraction(sd, md, basis);
        if (dsm <= 0) return ErrorValue.Num;
        double a = DayCountFraction(id, sd, basis);
        double num = (1 + rate * dim) / (pr / 100.0 + a * rate) - 1;
        return NumberResult(num / dsm);
    }

    private static ScalarValue Duration(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 5 ? args[5] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], basisArg],
            values => DurationScalar(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    private static ScalarValue DurationScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue couponValue, ScalarValue yieldValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double coupon = ToNumber(couponValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return DurationScalar(settlement, maturity, coupon, yieldValue, frequency, basis);
    }

    private static ScalarValue DurationScalar(double settlement, double maturity, double coupon, ScalarValue yieldValue, int frequency, int basis)
    {
        double yld = ToNumber(yieldValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(coupon) ||
            !double.IsFinite(yld))
            return ErrorValue.Num;
        if (coupon < 0 || yld < 0) return ErrorValue.Num;
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        // Build coupon schedule
        DateTime pcd = CouponDateBefore(sd, md, frequency);
        DateTime ncd = CouponDateAfter(sd, md, frequency);
        double daysInPeriod = CouponPeriodDayCount(pcd, ncd, frequency, basis);
        double daysToNext = CouponDayCount(sd, ncd, basis);
        double a = daysInPeriod > 0 ? daysToNext / daysInPeriod : 1.0;

        int months = 12 / frequency;
        // Anchor each schedule candidate off the ORIGINAL maturity (md.AddMonths(-count*months)),
        // not off the shrinking `d`, to avoid compounding .NET's day-of-month clamp for month-end
        // maturities whose forward schedule crosses a shorter month (same fix as Coupnum in
        // BuiltInFunctions.Financial.Coupons.cs). Without this, the last generated date can drift
        // away from `md` and never compare equal to it, silently dropping the redemption below.
        int couponCount = 0;
        DateTime probe = md;
        while (probe > sd) { couponCount++; probe = md.AddMonths(-couponCount * months); }
        var couponDates = new List<DateTime>();
        for (int i = 0; i < couponCount; i++)
            couponDates.Add(md.AddMonths(-(couponCount - 1 - i) * months));
        if (couponDates.Count == 0) couponDates.Add(ncd);

        double c = coupon / frequency * 100;
        double y = yld / frequency;
        double price = 0, weightedTime = 0;
        for (int k = 0; k < couponDates.Count; k++)
        {
            double t = k + a;  // periods from settlement
            double cashflow = c;
            if (couponDates[k] == md) cashflow += 100;
            double pv = cashflow / Math.Pow(1 + y, t);
            price += pv;
            weightedTime += t / frequency * pv;
        }
        if (Math.Abs(price) < 1e-14) return ErrorValue.Num;
        return NumberResult(weightedTime / price);
    }

    private static ScalarValue Mduration(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 5 ? args[5] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], basisArg],
            values => MdurationScalar(values[0], values[1], values[2], values[3], values[4], values[5]));
    }

    private static ScalarValue MdurationScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue couponValue, ScalarValue yieldValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double coupon = ToNumber(couponValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return MdurationScalar(settlement, maturity, coupon, yieldValue, frequency, basis);
    }

    private static ScalarValue MdurationScalar(double settlement, double maturity, double coupon, ScalarValue yieldValue, int frequency, int basis)
    {
        var dur = DurationScalar(settlement, maturity, coupon, yieldValue, frequency, basis);
        if (dur is not NumberValue dv) return dur;
        double yld = ToNumber(yieldValue);
        if (frequency <= 0) return ErrorValue.Num;
        return NumberResult(dv.Value / (1 + yld / frequency));
    }
}
