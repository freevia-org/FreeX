using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // ═══════════════════════════════════════════════════════════════════
    // Phase C  –  Financial functions
    // ═══════════════════════════════════════════════════════════════════

    // ── Private helpers ─────────────────────────────────────────────────

    private static DateTime SerialToDate(double serial) =>
        serial < 60
            ? new DateTime(1899, 12, 30).AddDays(serial + 1)
            : new DateTime(1899, 12, 30).AddDays(serial);

    private static bool TryGetFinancialDate(double serial, out DateTime date)
    {
        date = default;
        if (!double.IsFinite(serial) || serial < 0 || serial > 2958465.0)
            return false;
        date = SerialToDate(serial);
        return true;
    }

    private static double DateToSerial(DateTime d) =>
        d < new DateTime(1900, 3, 1)
            ? (d - new DateTime(1899, 12, 30)).TotalDays - 1
            : (d - new DateTime(1899, 12, 30)).TotalDays;

    private static bool IsExcelFakeLeapDay(ScalarValue value)
    {
        if (value is ErrorValue) return false;
        double serial = ToNumber(value);
        return double.IsFinite(serial) && Math.Abs(serial - 60) < 1e-10;
    }

    private static bool IsExcelZeroDate(ScalarValue value)
    {
        if (value is ErrorValue) return false;
        double serial = ToNumber(value);
        return double.IsFinite(serial) && Math.Abs(serial) < 1e-10;
    }

    private static double ActualYearLength(DateTime d1, DateTime d2)
    {
        if (d1.Year == d2.Year)
            return DateTime.IsLeapYear(d1.Year) ? 366.0 : 365.0;
        double years = d2.Year - d1.Year;
        double days = (d2 - d1).TotalDays;
        return days / years;
    }

    private static double DayCountFraction(DateTime d1, DateTime d2, int basis)
    {
        switch (basis)
        {
            case 0: // US 30/360 (NASD)
            {
                int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
                int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
                if (dd2 == 31 && dd1 >= 30) dd2 = 30;
                if (dd1 == 31) dd1 = 30;
                return ((y2 - y1) * 360 + (m2 - m1) * 30 + (dd2 - dd1)) / 360.0;
            }
            case 1: return (d2 - d1).TotalDays / ActualYearLength(d1, d2);
            case 2: return (d2 - d1).TotalDays / 360.0;
            case 3: return (d2 - d1).TotalDays / 365.0;
            case 4: // European 30/360
            {
                int y1 = d1.Year, m1 = d1.Month, dd1 = d1.Day;
                int y2 = d2.Year, m2 = d2.Month, dd2 = d2.Day;
                if (dd1 == 31) dd1 = 30;
                if (dd2 == 31) dd2 = 30;
                return ((y2 - y1) * 360 + (m2 - m1) * 30 + (dd2 - dd1)) / 360.0;
            }
            default: return (d2 - d1).TotalDays / 365.0;
        }
    }

    private static bool TryGetFinancialBasis(IReadOnlyList<ScalarValue> args, int index, out int basis)
    {
        basis = 0;
        if (args.Count <= index || args[index] is BlankValue) return true;

        return TryGetFinancialBasis(args[index], out basis);
    }

    private static bool TryGetFinancialBasis(ScalarValue value, out int basis)
    {
        basis = 0;
        if (value is BlankValue) return true;

        double rawBasis = ToNumber(value);
        if (!double.IsFinite(rawBasis)) return false;
        basis = (int)Math.Truncate(rawBasis);
        return basis is >= 0 and <= 4;
    }

    private static DateTime CouponDateBefore(DateTime settlement, DateTime maturity, int frequency)
    {
        int months = 12 / frequency;
        DateTime prev = maturity;
        // Walk backward from maturity until we pass settlement
        while (prev > settlement)
            prev = prev.AddMonths(-months);
        return prev;
    }

    private static DateTime CouponDateAfter(DateTime settlement, DateTime maturity, int frequency)
    {
        DateTime prev = CouponDateBefore(settlement, maturity, frequency);
        return prev.AddMonths(12 / frequency);
    }

    // Bond price helper (for PRICE/YIELD)
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

    // ── C3: Bond/discount/settlement functions ───────────────────────────

    private static ScalarValue Disc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 4 ? args[4] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], basisArg], values => DiscScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue DiscScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue priceValue, ScalarValue redemptionValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double pr = ToNumber(priceValue);
        double redemption = ToNumber(redemptionValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(pr) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        if (pr <= 0 || redemption <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, md, basis);
        if (dcf <= 0) return ErrorValue.Num;
        return NumberResult((redemption - pr) / redemption / dcf);
    }

    private static ScalarValue Intrate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 4 ? args[4] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], basisArg], values => IntrateScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue IntrateScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue investmentValue, ScalarValue redemptionValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double investment = ToNumber(investmentValue);
        double redemption = ToNumber(redemptionValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(investment) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        if (investment <= 0 || redemption <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, md, basis);
        if (dcf <= 0) return ErrorValue.Num;
        return NumberResult((redemption - investment) / investment / dcf);
    }

    private static ScalarValue Received(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 4 ? args[4] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], basisArg], values => ReceivedScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue ReceivedScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue investmentValue, ScalarValue discountValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double investment = ToNumber(investmentValue);
        double discount = ToNumber(discountValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(investment) || !double.IsFinite(discount))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        if (investment <= 0 || discount <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, md, basis);
        double denom = 1 - discount * dcf;
        if (Math.Abs(denom) < 1e-14) return ErrorValue.DivByZero;
        return NumberResult(investment / denom);
    }

    private static ScalarValue Accrint(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 6 ? args[6] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], args[5], basisArg], values => AccrintScalar(values[0], values[1], values[2], values[3], values[4], values[5], values[6]));
    }

    private static ScalarValue AccrintScalar(ScalarValue issueValue, ScalarValue firstInterestValue, ScalarValue settlementValue, ScalarValue rateValue, ScalarValue parValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double issue = ToNumber(issueValue);
        double firstInterest = ToNumber(firstInterestValue);
        double settlement = ToNumber(settlementValue);
        double par = ToNumber(parValue);
        double frequency = ToNumber(frequencyValue);
        _ = firstInterest;
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(issue) || !double.IsFinite(settlement) || !double.IsFinite(rate) ||
            !double.IsFinite(par) || !double.IsFinite(frequency))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        if (rate <= 0 || par <= 0 || frequency <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(issue, out DateTime sd) ||
            !TryGetFinancialDate(settlement, out DateTime sett)) return ErrorValue.Num;
        if (sd >= sett) return ErrorValue.Num;
        double dcf = DayCountFraction(sd, sett, basis);
        return NumberResult(par * rate * dcf);
    }

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

    // ── Coupon date helpers ──────────────────────────────────────────────

    private static ScalarValue Coupdaybs(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], basisArg], values => CoupdaybsScalar(values[0], values[1], values[2], values[3]));
    }

    private static ScalarValue CoupdaybsScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double maturity = ToNumber(maturityValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        _ = basis;
        double settlement = ToNumber(settlementValue);
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        DateTime pcd = CouponDateBefore(sd, md, frequency);
        return NumberResult((sd - pcd).TotalDays);
    }

    private static ScalarValue Coupdays(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], basisArg], values => CoupdaysScalar(values[0], values[1], values[2], values[3]));
    }

    private static ScalarValue CoupdaysScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double maturity = ToNumber(maturityValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        double settlement = ToNumber(settlementValue);
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        DateTime pcd = CouponDateBefore(sd, md, frequency);
        DateTime ncd = CouponDateAfter(sd, md, frequency);
        if (basis == 1)
            return NumberResult((ncd - pcd).TotalDays);
        // Other bases use 360 or 365 adjusted
        return NumberResult(365.0 / frequency);
    }

    private static ScalarValue Coupdaysnc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], basisArg], values => CoupdaysncScalar(values[0], values[1], values[2], values[3]));
    }

    private static ScalarValue CoupdaysncScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double maturity = ToNumber(maturityValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out _)) return ErrorValue.Num;
        double settlement = ToNumber(settlementValue);
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        DateTime ncd = CouponDateAfter(sd, md, frequency);
        return NumberResult((ncd - sd).TotalDays);
    }

    private static ScalarValue Coupncd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], basisArg], values => CoupncdScalar(values[0], values[1], values[2], values[3]));
    }

    private static ScalarValue CoupncdScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double maturity = ToNumber(maturityValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out _)) return ErrorValue.Num;
        double settlement = ToNumber(settlementValue);
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        DateTime ncd = CouponDateAfter(sd, md, frequency);
        return NumberResult(DateToSerial(ncd));
    }

    private static ScalarValue Coupnum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], basisArg], values => CoupnumScalar(values[0], values[1], values[2], values[3]));
    }

    private static ScalarValue CoupnumScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double maturity = ToNumber(maturityValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out _)) return ErrorValue.Num;
        double settlement = ToNumber(settlementValue);
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        int months = 12 / frequency;
        int count = 0;
        DateTime d = md;
        while (d > sd) { count++; d = d.AddMonths(-months); }
        return NumberResult((double)count);
    }

    private static ScalarValue Couppcd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], basisArg], values => CouppcdScalar(values[0], values[1], values[2], values[3]));
    }

    private static ScalarValue CouppcdScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double maturity = ToNumber(maturityValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out _)) return ErrorValue.Num;
        double settlement = ToNumber(settlementValue);
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        DateTime pcd = CouponDateBefore(sd, md, frequency);
        return NumberResult(DateToSerial(pcd));
    }

    // ── Bond price/yield ─────────────────────────────────────────────────

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
        double result = 100.0 * (1 + rate * dim) / (1 + yld * dsm);
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
        double num = (1 + rate * dim) / (pr / 100.0) - 1;
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
        double daysInPeriod = (ncd - pcd).TotalDays;
        double daysToNext   = (ncd - sd).TotalDays;
        double a = daysInPeriod > 0 ? daysToNext / daysInPeriod : 1.0;

        int months = 12 / frequency;
        var couponDates = new List<DateTime>();
        DateTime d = ncd;
        while (d <= md) { couponDates.Add(d); d = d.AddMonths(months); }
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

    // ── Odd coupon period functions ──────────────────────────────────────

    private static ScalarValue Oddfprice(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        // Odd first period: settlement, maturity, issue, first_coupon, rate, yld, redemption, frequency, [basis]
        if (FirstError(args) is { } e) return e;
        var basisArg = args.Count > 8 ? args[8] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], basisArg],
            values => OddfpriceScalar(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8]));
    }

    private static ScalarValue OddfpriceScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue issueValue, ScalarValue firstCouponValue, ScalarValue rateValue, ScalarValue yieldValue, ScalarValue redemptionValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double issue = ToNumber(issueValue);
        double firstCoupon = ToNumber(firstCouponValue);
        double rate = ToNumber(rateValue);
        double redemption = ToNumber(redemptionValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return OddfpriceScalar(settlement, maturity, issue, firstCoupon, rate, yieldValue, redemption, frequency, basis);
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
        var basisArg = args.Count > 8 ? args[8] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], basisArg],
            values => OddfyieldScalar(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8]));
    }

    private static ScalarValue OddfyieldScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue issueValue, ScalarValue firstCouponValue, ScalarValue rateValue, ScalarValue priceValue, ScalarValue redemptionValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double issue = ToNumber(issueValue);
        double firstCoupon = ToNumber(firstCouponValue);
        double rate = ToNumber(rateValue);
        double redemption = ToNumber(redemptionValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return OddfyieldScalar(settlement, maturity, issue, firstCoupon, rate, priceValue, redemption, frequency, basis);
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
        var basisArg = args.Count > 7 ? args[7] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], args[5], args[6], basisArg],
            values => OddlpriceScalar(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]));
    }

    private static ScalarValue OddlpriceScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue lastInterestValue, ScalarValue rateValue, ScalarValue yieldValue, ScalarValue redemptionValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double lastInterest = ToNumber(lastInterestValue);
        double rate = ToNumber(rateValue);
        double redemption = ToNumber(redemptionValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return OddlpriceScalar(settlement, maturity, lastInterest, rate, yieldValue, redemption, frequency, basis);
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
        var basisArg = args.Count > 7 ? args[7] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], args[5], args[6], basisArg],
            values => OddlyieldScalar(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]));
    }

    private static ScalarValue OddlyieldScalar(ScalarValue settlementValue, ScalarValue maturityValue, ScalarValue lastInterestValue, ScalarValue rateValue, ScalarValue priceValue, ScalarValue redemptionValue, ScalarValue frequencyValue, ScalarValue basisValue)
    {
        double settlement = ToNumber(settlementValue);
        double maturity = ToNumber(maturityValue);
        double lastInterest = ToNumber(lastInterestValue);
        double rate = ToNumber(rateValue);
        double redemption = ToNumber(redemptionValue);
        int frequency = (int)Math.Truncate(ToNumber(frequencyValue));
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        return OddlyieldScalar(settlement, maturity, lastInterest, rate, priceValue, redemption, frequency, basis);
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

