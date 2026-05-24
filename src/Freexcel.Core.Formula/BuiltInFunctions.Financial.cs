using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // ═══════════════════════════════════════════════════════════════════
    // Phase C  –  Financial functions
    // ═══════════════════════════════════════════════════════════════════

    // ── Private helpers ─────────────────────────────────────────────────

    private static double CalcPmt(double rate, double nper, double pv, double fv, int type)
    {
        if (Math.Abs(rate) < 1e-14) return -(pv + fv) / nper;
        double r1 = Math.Pow(1 + rate, nper);
        return -(pv * r1 + fv) * rate / ((1 + rate * type) * (r1 - 1));
    }

    private static double CalcIpmt(double rate, double per, double nper, double pv, double fv, int type)
    {
        double pmt = CalcPmt(rate, nper, pv, fv, type);
        if (Math.Abs(rate) < 1e-14) return 0.0;
        double pvAtPer = pv * Math.Pow(1 + rate, per - 1)
                       + pmt * (1 + rate * type) * (Math.Pow(1 + rate, per - 1) - 1) / rate;
        // Interest payment matches PMT sign convention: negative = outflow (borrower)
        return type == 0 ? -(pvAtPer * rate) : -((pvAtPer - pmt) * rate);
    }

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

        double rawBasis = ToNumber(args[index]);
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

    // ── C1: High-usage financial ─────────────────────────────────────────

    private static ScalarValue Ipmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate  = ToNumber(args[0]);
        double nper  = ToNumber(args[2]);
        double pv    = ToNumber(args[3]);
        double fv    = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        double type  = args.Count > 5 && args[5] is not BlankValue ? ToNumber(args[5]) : 0;
        if (args[1] is RangeValue periodRange) return MapUnaryTextRange(periodRange, value => IpmtScalar(rate, value, nper, pv, fv, type));
        return IpmtScalar(rate, args[1], nper, pv, fv, type);
    }

    private static ScalarValue IpmtScalar(double rate, ScalarValue periodValue, double nper, double pv, double fv, double type)
    {
        double per = ToNumber(periodValue);
        if (!double.IsFinite(rate) || !double.IsFinite(per) || !double.IsFinite(nper) ||
            !double.IsFinite(pv)   || !double.IsFinite(fv)  || !double.IsFinite(type))
            return ErrorValue.Num;
        int itype = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
        if (nper <= 0) return ErrorValue.Num;
        int iper = (int)Math.Truncate(per);
        if (iper < 1 || iper > (int)Math.Truncate(nper)) return ErrorValue.Num;
        return NumberResult(CalcIpmt(rate, iper, nper, pv, fv, itype));
    }

    private static ScalarValue Ppmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate  = ToNumber(args[0]);
        double nper  = ToNumber(args[2]);
        double pv    = ToNumber(args[3]);
        double fv    = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        double type  = args.Count > 5 && args[5] is not BlankValue ? ToNumber(args[5]) : 0;
        if (args[1] is RangeValue periodRange) return MapUnaryTextRange(periodRange, value => PpmtScalar(rate, value, nper, pv, fv, type));
        return PpmtScalar(rate, args[1], nper, pv, fv, type);
    }

    private static ScalarValue PpmtScalar(double rate, ScalarValue periodValue, double nper, double pv, double fv, double type)
    {
        double per = ToNumber(periodValue);
        if (!double.IsFinite(rate) || !double.IsFinite(per) || !double.IsFinite(nper) ||
            !double.IsFinite(pv)   || !double.IsFinite(fv)  || !double.IsFinite(type))
            return ErrorValue.Num;
        int itype = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
        if (nper <= 0) return ErrorValue.Num;
        int iper = (int)Math.Truncate(per);
        if (iper < 1 || iper > (int)Math.Truncate(nper)) return ErrorValue.Num;
        double pmt  = CalcPmt(rate, nper, pv, fv, itype);
        double ipmt = CalcIpmt(rate, iper, nper, pv, fv, itype);
        return NumberResult(pmt - ipmt);
    }

    private static ScalarValue Cumipmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate  = ToNumber(args[0]);
        double nper  = ToNumber(args[1]);
        double pv    = ToNumber(args[2]);
        double end   = ToNumber(args[4]);
        double type  = ToNumber(args[5]);
        if (args[3] is RangeValue startRange) return MapUnaryTextRange(startRange, value => CumipmtScalar(rate, nper, pv, value, end, type));
        return CumipmtScalar(rate, nper, pv, args[3], end, type);
    }

    private static ScalarValue CumipmtScalar(double rate, double nper, double pv, ScalarValue startValue, double end, double type)
    {
        double start = ToNumber(startValue);
        if (!double.IsFinite(rate) || !double.IsFinite(nper) || !double.IsFinite(pv) ||
            !double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(type))
            return ErrorValue.Num;
        int itype  = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
        if (rate <= 0 || nper <= 0 || pv <= 0) return ErrorValue.Num;
        int is_ = (int)Math.Truncate(start), ie = (int)Math.Truncate(end);
        if (is_ < 1 || ie < is_ || ie > (int)Math.Truncate(nper)) return ErrorValue.Num;
        double sum = 0;
        for (int per = is_; per <= ie; per++)
            sum += CalcIpmt(rate, per, nper, pv, 0, itype);
        return NumberResult(sum);
    }

    private static ScalarValue Cumprinc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate  = ToNumber(args[0]);
        double nper  = ToNumber(args[1]);
        double pv    = ToNumber(args[2]);
        double end   = ToNumber(args[4]);
        double type  = ToNumber(args[5]);
        if (args[3] is RangeValue startRange) return MapUnaryTextRange(startRange, value => CumprincScalar(rate, nper, pv, value, end, type));
        return CumprincScalar(rate, nper, pv, args[3], end, type);
    }

    private static ScalarValue CumprincScalar(double rate, double nper, double pv, ScalarValue startValue, double end, double type)
    {
        double start = ToNumber(startValue);
        if (!double.IsFinite(rate) || !double.IsFinite(nper) || !double.IsFinite(pv) ||
            !double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(type))
            return ErrorValue.Num;
        int itype = (int)Math.Truncate(type);
        if (itype != 0 && itype != 1) return ErrorValue.Num;
        if (rate <= 0 || nper <= 0 || pv <= 0) return ErrorValue.Num;
        int is_ = (int)Math.Truncate(start), ie = (int)Math.Truncate(end);
        if (is_ < 1 || ie < is_ || ie > (int)Math.Truncate(nper)) return ErrorValue.Num;
        double pmt = CalcPmt(rate, nper, pv, 0, itype);
        double sum = 0;
        for (int per = is_; per <= ie; per++)
            sum += pmt - CalcIpmt(rate, per, nper, pv, 0, itype);
        return NumberResult(sum);
    }

    private static ScalarValue Effect(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double npery   = Math.Truncate(ToNumber(args[1]));
        if (args[0] is RangeValue rateRange) return MapUnaryTextRange(rateRange, value => EffectScalar(value, npery));
        return EffectScalar(args[0], npery);
    }

    private static ScalarValue EffectScalar(ScalarValue rateValue, double npery)
    {
        double nomRate = ToNumber(rateValue);
        if (!double.IsFinite(nomRate) || !double.IsFinite(npery)) return ErrorValue.Num;
        if (nomRate <= 0 || npery < 1) return ErrorValue.Num;
        return NumberResult(Math.Pow(1 + nomRate / npery, npery) - 1);
    }

    private static ScalarValue Nominal(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double npery      = Math.Truncate(ToNumber(args[1]));
        if (args[0] is RangeValue rateRange) return MapUnaryTextRange(rateRange, value => NominalScalar(value, npery));
        return NominalScalar(args[0], npery);
    }

    private static ScalarValue NominalScalar(ScalarValue rateValue, double npery)
    {
        double effectRate = ToNumber(rateValue);
        if (!double.IsFinite(effectRate) || !double.IsFinite(npery)) return ErrorValue.Num;
        if (effectRate <= 0 || npery < 1) return ErrorValue.Num;
        return NumberResult((Math.Pow(1 + effectRate, 1.0 / npery) - 1) * npery);
    }

    private static ScalarValue Mirr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var valRange = args[0] is RangeValue valuesRange
            ? valuesRange
            : SingleCellArray(args[0]);
        double financeRate  = ToNumber(args[1]);
        double reinvestRate = ToNumber(args[2]);
        if (!double.IsFinite(financeRate) || !double.IsFinite(reinvestRate)) return ErrorValue.Num;
        var (values, err) = CollectRangeNumbers(valRange);
        if (err is not null) return err;
        var cf = values!;
        int n = cf.Count;
        if (n < 2) return ErrorValue.DivByZero;
        // NPV of negative flows at finance_rate
        double npvNeg = 0;
        for (int i = 0; i < n; i++)
            if (cf[i] < 0) npvNeg += cf[i] / Math.Pow(1 + financeRate, i);
        // NPV of positive flows at reinvest_rate
        double npvPos = 0;
        for (int i = 0; i < n; i++)
            if (cf[i] > 0) npvPos += cf[i] / Math.Pow(1 + reinvestRate, i);
        if (npvNeg == 0 || npvPos == 0) return ErrorValue.DivByZero;
        double mirr = Math.Pow((-npvPos * Math.Pow(1 + reinvestRate, n - 1)) / npvNeg, 1.0 / (n - 1)) - 1;
        return NumberResult(mirr);
    }

    private static ScalarValue Xirr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var valRange = args[0] is RangeValue valuesRange
            ? valuesRange
            : SingleCellArray(args[0]);
        var dateRange = args[1] is RangeValue datesRange
            ? datesRange
            : SingleCellArray(args[1]);
        double guess = args.Count > 2 && args[2] is not BlankValue ? ToNumber(args[2]) : 0.1;
        var (vals, ve) = CollectRangeNumbers(valRange);
        var (datesRaw, de) = CollectRangeNumbers(dateRange);
        if (ve is not null) return ve;
        if (de is not null) return de;
        var cf = vals!;
        var ds = datesRaw!;
        if (cf.Count < 2) return ErrorValue.NA;
        if (cf.Count != ds.Count) return ErrorValue.Num;
        var dates = ds.Select(SerialToDate).ToList();
        DateTime d0 = dates[0];
        // Newton-Raphson
        double r = guess;
        for (int iter = 0; iter < 200; iter++)
        {
            double f = 0, df = 0;
            for (int i = 0; i < cf.Count; i++)
            {
                double t = (dates[i] - d0).TotalDays / 365.0;
                double denom = Math.Pow(1 + r, t);
                f  += cf[i] / denom;
                df -= t * cf[i] / (denom * (1 + r));
            }
            if (Math.Abs(df) < 1e-14) break;
            double delta = f / df;
            r -= delta;
            if (Math.Abs(delta) < 1e-10) break;
        }
        if (!double.IsFinite(r)) return ErrorValue.Num;
        return NumberResult(r);
    }

    private static ScalarValue Xnpv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double rate = ToNumber(args[0]);
        var valRange = args[1] is RangeValue valuesRange
            ? valuesRange
            : SingleCellArray(args[1]);
        var dateRange = args[2] is RangeValue datesRange
            ? datesRange
            : SingleCellArray(args[2]);
        if (!double.IsFinite(rate) || rate <= -1) return ErrorValue.Num;
        var (vals, ve) = CollectRangeNumbers(valRange);
        var (datesRaw, de) = CollectRangeNumbers(dateRange);
        if (ve is not null) return ve;
        if (de is not null) return de;
        var cf = vals!;
        var ds = datesRaw!;
        if (cf.Count != ds.Count || cf.Count == 0) return ErrorValue.Num;
        var dates = ds.Select(SerialToDate).ToList();
        DateTime d0 = dates[0];
        double result = 0;
        for (int i = 0; i < cf.Count; i++)
        {
            double t = (dates[i] - d0).TotalDays / 365.0;
            result += cf[i] / Math.Pow(1 + rate, t);
        }
        return NumberResult(result);
    }

    private static ScalarValue Rri(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double pv   = ToNumber(args[1]);
        double fv   = ToNumber(args[2]);
        if (args[0] is RangeValue nperRange) return MapUnaryTextRange(nperRange, value => RriScalar(value, pv, fv));
        return RriScalar(args[0], pv, fv);
    }

    private static ScalarValue RriScalar(ScalarValue nperValue, double pv, double fv)
    {
        double nper = ToNumber(nperValue);
        if (!double.IsFinite(nper) || !double.IsFinite(pv) || !double.IsFinite(fv)) return ErrorValue.Num;
        if (nper <= 0 || pv == 0) return ErrorValue.Num;
        if ((pv > 0 && fv < 0) || (pv < 0 && fv > 0)) return ErrorValue.Num;
        double result = Math.Pow(fv / pv, 1.0 / nper) - 1;
        return NumberResult(result);
    }

    private static ScalarValue Pduration(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double pv   = ToNumber(args[1]);
        double fv   = ToNumber(args[2]);
        if (args[0] is RangeValue rateRange) return MapUnaryTextRange(rateRange, value => PdurationScalar(value, pv, fv));
        return PdurationScalar(args[0], pv, fv);
    }

    private static ScalarValue PdurationScalar(ScalarValue rateValue, double pv, double fv)
    {
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(rate) || !double.IsFinite(pv) || !double.IsFinite(fv)) return ErrorValue.Num;
        if (rate <= 0 || pv <= 0 || fv <= 0) return ErrorValue.Num;
        return NumberResult((Math.Log(fv) - Math.Log(pv)) / Math.Log(1 + rate));
    }

    private static ScalarValue Fvschedule(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double principal = ToNumber(args[0]);
        var schedRange = args[1] is RangeValue scheduleRange
            ? scheduleRange
            : SingleCellArray(args[1]);
        if (!double.IsFinite(principal)) return ErrorValue.Num;
        var (rates, re) = CollectRangeNumbers(schedRange);
        if (re is not null) return re;
        double result = principal;
        foreach (double r in rates!)
        {
            if (!double.IsFinite(r)) return ErrorValue.Num;
            result *= (1 + r);
        }
        return NumberResult(result);
    }

    // ── C2: Depreciation functions ───────────────────────────────────────

    private static ScalarValue Db(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost    = ToNumber(args[0]);
        double salvage = ToNumber(args[1]);
        double life    = ToNumber(args[2]);
        double period  = ToNumber(args[3]);
        double month   = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 12;
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) ||
            !double.IsFinite(period) || !double.IsFinite(month))
            return ErrorValue.Num;
        int ilife = (int)Math.Truncate(life), iper = (int)Math.Truncate(period);
        int imonth = (int)Math.Truncate(month);
        if (cost <= 0 || salvage < 0 || ilife <= 0 || iper <= 0 || iper > ilife + 1) return ErrorValue.Num;
        if (imonth < 1 || imonth > 12) return ErrorValue.Num;
        if (salvage >= cost) return NumberResult(0);
        // Rate rounded to 3 decimal places
        double rate = Math.Round(1 - Math.Pow(salvage / cost, 1.0 / ilife), 3);
        double accumulated = 0;
        double dep = 0;
        for (int p = 1; p <= iper; p++)
        {
            if (p == 1)
                dep = cost * rate * imonth / 12.0;
            else if (p <= ilife)
                dep = (cost - accumulated) * rate;
            else // p == ilife + 1
                dep = (cost - accumulated) * rate * (12 - imonth + 1) / 12.0;
            if (p < iper) accumulated += dep;
        }
        return NumberResult(dep);
    }

    private static ScalarValue Ddb(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost    = ToNumber(args[0]);
        double salvage = ToNumber(args[1]);
        double life    = ToNumber(args[2]);
        double period  = ToNumber(args[3]);
        double factor  = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 2.0;
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) ||
            !double.IsFinite(period) || !double.IsFinite(factor))
            return ErrorValue.Num;
        if (cost < 0 || salvage < 0 || life <= 0 || period <= 0 || factor <= 0) return ErrorValue.Num;
        double bookValue = cost;
        double accumulated = 0;
        int ip = (int)Math.Truncate(period);
        for (int p = 1; p <= ip; p++)
        {
            double dep = Math.Min(bookValue - salvage, bookValue * factor / life);
            dep = Math.Max(dep, 0);
            if (p < ip) { accumulated += dep; bookValue -= dep; }
            else return NumberResult(dep);
        }
        return NumberResult(0);
    }

    private static ScalarValue Vdb(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost        = ToNumber(args[0]);
        double salvage     = ToNumber(args[1]);
        double life        = ToNumber(args[2]);
        double startPeriod = ToNumber(args[3]);
        double endPeriod   = ToNumber(args[4]);
        double factor      = args.Count > 5 && args[5] is not BlankValue ? ToNumber(args[5]) : 2.0;
        bool noSwitch      = args.Count > 6 && args[6] is not BlankValue && ToBool(args[6]);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) ||
            !double.IsFinite(startPeriod) || !double.IsFinite(endPeriod) || !double.IsFinite(factor))
            return ErrorValue.Num;
        if (cost < 0 || salvage < 0 || life <= 0 || startPeriod < 0 || endPeriod < startPeriod || factor <= 0)
            return ErrorValue.Num;
        if (endPeriod > life) return ErrorValue.Num;
        // Compute depreciation for fractional periods
        double totalDep = 0;
        double bookValue = cost;
        // Process each integer period between startPeriod and endPeriod
        double currentPeriod = startPeriod;
        while (currentPeriod < endPeriod)
        {
            double periodEnd = Math.Min(Math.Ceiling(currentPeriod + 1e-10), endPeriod);
            double fraction = periodEnd - currentPeriod;
            // Which integer period are we in?
            int p = (int)Math.Floor(currentPeriod + 1e-10);
            double ddbDep = bookValue * factor / life;
            double slnDep = (bookValue - salvage) / (life - p);
            double dep;
            if (!noSwitch && slnDep > ddbDep)
                dep = slnDep;
            else
                dep = ddbDep;
            dep = Math.Max(0, Math.Min(dep, bookValue - salvage));
            double partialDep = dep * fraction;
            totalDep += partialDep;
            bookValue -= partialDep;
            currentPeriod = periodEnd;
        }
        return NumberResult(totalDep);
    }

    private static ScalarValue Syd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost    = ToNumber(args[0]);
        double salvage = ToNumber(args[1]);
        double life    = ToNumber(args[2]);
        double per     = ToNumber(args[3]);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life) || !double.IsFinite(per))
            return ErrorValue.Num;
        if (life <= 0 || per <= 0 || per > life) return ErrorValue.Num;
        double result = (cost - salvage) * (life - per + 1) / (life * (life + 1) / 2.0);
        return NumberResult(result);
    }

    private static ScalarValue Amordegrc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost          = ToNumber(args[0]);
        double datePurchased = ToNumber(args[1]);
        double firstPeriod   = ToNumber(args[2]);
        double salvage       = ToNumber(args[3]);
        double period        = ToNumber(args[4]);
        double rate          = ToNumber(args[5]);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(rate) || !double.IsFinite(period))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 6, out int basis)) return ErrorValue.Num;
        if (cost <= 0 || salvage < 0 || rate <= 0) return ErrorValue.Num;
        // Compute life in years
        double life = 1.0 / rate;
        // Determine coefficient
        double coeff;
        if (life < 3)       coeff = 1.0;
        else if (life < 5)  coeff = 1.5;
        else if (life <= 6) coeff = 2.0;
        else                coeff = 2.5;
        double depRate = rate * coeff;
        // First period proration
        if (!TryGetFinancialDate(datePurchased, out DateTime dp) ||
            !TryGetFinancialDate(firstPeriod, out DateTime fp)) return ErrorValue.Num;
        double firstFrac = DayCountFraction(dp, fp, basis);
        double bookValue = cost;
        int iper = (int)Math.Truncate(period);
        for (int p = 0; p <= iper; p++)
        {
            double dep;
            if (p == 0)
                dep = bookValue * depRate * firstFrac;
            else
                dep = bookValue * depRate;
            dep = Math.Max(0, Math.Min(dep, bookValue - salvage));
            if (p < iper)
                bookValue -= dep;
            else
                return NumberResult(dep);
        }
        return NumberResult(0);
    }

    private static ScalarValue Amorlinc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double cost          = ToNumber(args[0]);
        double datePurchased = ToNumber(args[1]);
        double firstPeriod   = ToNumber(args[2]);
        double salvage       = ToNumber(args[3]);
        double period        = ToNumber(args[4]);
        double rate          = ToNumber(args[5]);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(rate) || !double.IsFinite(period))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 6, out int basis)) return ErrorValue.Num;
        if (cost <= 0 || salvage < 0 || rate <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(datePurchased, out DateTime dp) ||
            !TryGetFinancialDate(firstPeriod, out DateTime fp)) return ErrorValue.Num;
        double firstFrac = DayCountFraction(dp, fp, basis);
        double annualDep = cost * rate;
        double bookValue = cost;
        int iper = (int)Math.Truncate(period);
        for (int p = 0; p <= iper; p++)
        {
            double dep;
            if (p == 0)
                dep = annualDep * firstFrac;
            else
                dep = annualDep;
            dep = Math.Max(0, Math.Min(dep, bookValue - salvage));
            if (p < iper)
                bookValue -= dep;
            else
                return NumberResult(dep);
        }
        return NumberResult(0);
    }

    // ── C3: Dollar conversion helpers ────────────────────────────────────

    private static ScalarValue Dollarde(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double f = Math.Truncate(ToNumber(args[1]));
        if (args[0] is RangeValue dollarRange) return MapUnaryTextRange(dollarRange, value => DollardeScalar(value, f));
        return DollardeScalar(args[0], f);
    }

    private static ScalarValue DollardeScalar(ScalarValue dollarValue, double f)
    {
        double d = ToNumber(dollarValue);
        if (!double.IsFinite(d) || !double.IsFinite(f)) return ErrorValue.Num;
        if (f < 0) return ErrorValue.Num;
        if (f == 0) return ErrorValue.DivByZero;
        double intPart  = Math.Truncate(d);
        double fracPart = d - intPart;
        int digits = (int)Math.Ceiling(Math.Log10(f));
        if (digits < 1) digits = 1;
        return NumberResult(intPart + fracPart * Math.Pow(10, digits) / f);
    }

    private static ScalarValue Dollarfr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double f = Math.Truncate(ToNumber(args[1]));
        if (args[0] is RangeValue dollarRange) return MapUnaryTextRange(dollarRange, value => DollarfrScalar(value, f));
        return DollarfrScalar(args[0], f);
    }

    private static ScalarValue DollarfrScalar(ScalarValue dollarValue, double f)
    {
        double d = ToNumber(dollarValue);
        if (!double.IsFinite(d) || !double.IsFinite(f)) return ErrorValue.Num;
        if (f < 0) return ErrorValue.Num;
        if (f == 0) return ErrorValue.DivByZero;
        double intPart  = Math.Truncate(d);
        double fracPart = d - intPart;
        int digits = (int)Math.Ceiling(Math.Log10(f));
        if (digits < 1) digits = 1;
        return NumberResult(intPart + fracPart * f / Math.Pow(10, digits));
    }

    // ── C3: Bond/discount/settlement functions ───────────────────────────

    private static ScalarValue Disc(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double redemption  = ToNumber(args[3]);
        if (args[2] is RangeValue priceRange) return MapUnaryTextRange(priceRange, value => DiscScalar(settlement, maturity, value, redemption, args));
        return DiscScalar(settlement, maturity, args[2], redemption, args);
    }

    private static ScalarValue DiscScalar(double settlement, double maturity, ScalarValue priceValue, double redemption, IReadOnlyList<ScalarValue> args)
    {
        double pr = ToNumber(priceValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(pr) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 4, out int basis)) return ErrorValue.Num;
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
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double redemption  = ToNumber(args[3]);
        if (args[2] is RangeValue investmentRange) return MapUnaryTextRange(investmentRange, value => IntrateScalar(settlement, maturity, value, redemption, args));
        return IntrateScalar(settlement, maturity, args[2], redemption, args);
    }

    private static ScalarValue IntrateScalar(double settlement, double maturity, ScalarValue investmentValue, double redemption, IReadOnlyList<ScalarValue> args)
    {
        double investment = ToNumber(investmentValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(investment) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 4, out int basis)) return ErrorValue.Num;
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
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        double investment = ToNumber(args[2]);
        if (args[3] is RangeValue discountRange) return MapUnaryTextRange(discountRange, value => ReceivedScalar(settlement, maturity, investment, value, args));
        return ReceivedScalar(settlement, maturity, investment, args[3], args);
    }

    private static ScalarValue ReceivedScalar(double settlement, double maturity, double investment, ScalarValue discountValue, IReadOnlyList<ScalarValue> args)
    {
        double discount = ToNumber(discountValue);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(investment) || !double.IsFinite(discount))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 4, out int basis)) return ErrorValue.Num;
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
        double issue         = ToNumber(args[0]);
        double firstInterest = ToNumber(args[1]);
        double settlement    = ToNumber(args[2]);
        double rate          = ToNumber(args[3]);
        double par           = ToNumber(args[4]);
        double frequency     = ToNumber(args[5]);
        if (!double.IsFinite(issue) || !double.IsFinite(settlement) || !double.IsFinite(rate) ||
            !double.IsFinite(par) || !double.IsFinite(frequency))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 6, out int basis)) return ErrorValue.Num;
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
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        double discount   = ToNumber(args[2]);
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
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        double discount   = ToNumber(args[2]);
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
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        double pr         = ToNumber(args[2]);
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
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 3, out int basis)) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        DateTime pcd = CouponDateBefore(sd, md, frequency);
        return NumberResult((sd - pcd).TotalDays);
    }

    private static ScalarValue Coupdays(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 3, out int basis)) return ErrorValue.Num;
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
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 3, out _)) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        DateTime ncd = CouponDateAfter(sd, md, frequency);
        return NumberResult((ncd - sd).TotalDays);
    }

    private static ScalarValue Coupncd(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 3, out _)) return ErrorValue.Num;
        if (!TryGetFinancialDate(settlement, out DateTime sd) ||
            !TryGetFinancialDate(maturity, out DateTime md)) return ErrorValue.Num;
        if (sd >= md) return ErrorValue.Num;
        DateTime ncd = CouponDateAfter(sd, md, frequency);
        return NumberResult(DateToSerial(ncd));
    }

    private static ScalarValue Coupnum(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 3, out _)) return ErrorValue.Num;
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
        double settlement = ToNumber(args[0]);
        double maturity   = ToNumber(args[1]);
        int frequency     = (int)Math.Truncate(ToNumber(args[2]));
        if (frequency != 1 && frequency != 2 && frequency != 4) return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 3, out _)) return ErrorValue.Num;
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
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double rate        = ToNumber(args[2]);
        double yld         = ToNumber(args[3]);
        double redemption  = ToNumber(args[4]);
        int frequency      = (int)Math.Truncate(ToNumber(args[5]));
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(rate) ||
            !double.IsFinite(yld) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 6, out int basis)) return ErrorValue.Num;
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
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double rate        = ToNumber(args[2]);
        double pr          = ToNumber(args[3]);
        double redemption  = ToNumber(args[4]);
        int frequency      = (int)Math.Truncate(ToNumber(args[5]));
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(rate) ||
            !double.IsFinite(pr) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 6, out int basis)) return ErrorValue.Num;
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
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double discount    = ToNumber(args[2]);
        double redemption  = ToNumber(args[3]);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(discount) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 4, out int basis)) return ErrorValue.Num;
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
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double issue       = ToNumber(args[2]);
        double rate        = ToNumber(args[3]);
        double yld         = ToNumber(args[4]);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(issue) ||
            !double.IsFinite(rate) || !double.IsFinite(yld))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 5, out int basis)) return ErrorValue.Num;
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
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double pr          = ToNumber(args[2]);
        double redemption  = ToNumber(args[3]);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(pr) || !double.IsFinite(redemption))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 4, out int basis)) return ErrorValue.Num;
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
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double issue       = ToNumber(args[2]);
        double rate        = ToNumber(args[3]);
        double pr          = ToNumber(args[4]);
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(issue) ||
            !double.IsFinite(rate) || !double.IsFinite(pr))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 5, out int basis)) return ErrorValue.Num;
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
        double settlement  = ToNumber(args[0]);
        double maturity    = ToNumber(args[1]);
        double coupon      = ToNumber(args[2]);
        double yld         = ToNumber(args[3]);
        int frequency      = (int)Math.Truncate(ToNumber(args[4]));
        if (!double.IsFinite(settlement) || !double.IsFinite(maturity) || !double.IsFinite(coupon) ||
            !double.IsFinite(yld))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(args, 5, out int basis)) return ErrorValue.Num;
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
        var dur = Duration(args, ctx);
        if (dur is not NumberValue dv) return dur;
        double yld = ToNumber(args[3]);
        double frequency = Math.Truncate(ToNumber(args[4]));
        if (frequency <= 0) return ErrorValue.Num;
        return NumberResult(dv.Value / (1 + yld / frequency));
    }

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
        double yld         = ToNumber(args[5]);
        double redemption  = ToNumber(args[6]);
        int frequency      = (int)Math.Truncate(ToNumber(args[7]));
        int basis = args.Count > 8 && args[8] is not BlankValue ? (int)Math.Truncate(ToNumber(args[8])) : 0;
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
        double pr          = ToNumber(args[5]);
        double redemption  = ToNumber(args[6]);
        int frequency      = (int)Math.Truncate(ToNumber(args[7]));
        int basis = args.Count > 8 && args[8] is not BlankValue ? (int)Math.Truncate(ToNumber(args[8])) : 0;
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
        double yld         = ToNumber(args[4]);
        double redemption  = ToNumber(args[5]);
        int frequency      = (int)Math.Truncate(ToNumber(args[6]));
        int basis = args.Count > 7 && args[7] is not BlankValue ? (int)Math.Truncate(ToNumber(args[7])) : 0;
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
        double pr          = ToNumber(args[4]);
        double redemption  = ToNumber(args[5]);
        int frequency      = (int)Math.Truncate(ToNumber(args[6]));
        int basis = args.Count > 7 && args[7] is not BlankValue ? (int)Math.Truncate(ToNumber(args[7])) : 0;
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

    // Core financial functions migrated from the central registry file.

    private static bool IsValidPaymentType(double type) =>
        double.IsFinite(type) && (type == 0 || type == 1);

    private static ScalarValue Pmt(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double nperValue = ToNumber(args[1]);
        double pv   = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (args[0] is RangeValue rateRange) return MapUnaryTextRange(rateRange, value => PmtScalar(value, nperValue, pv, fv, type));
        return PmtScalar(args[0], nperValue, pv, fv, type);
    }

    private static ScalarValue PmtScalar(ScalarValue rateValue, double nperValue, double pv, double fv, double type)
    {
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        double nper = nperValue;
        if (nper == 0) return ErrorValue.DivByZero;
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-(pv + fv) / nper);
        double rn  = Math.Pow(1 + rate, nper);
        double pmt = -(pv * rn + fv) * rate / ((1 + rate * type) * (rn - 1));
        return NumberResult(pmt);
    }

    private static ScalarValue Pv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double nperValue = ToNumber(args[1]);
        double pmt  = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (args[0] is RangeValue rateRange) return MapUnaryTextRange(rateRange, value => PvScalar(value, nperValue, pmt, fv, type));
        return PvScalar(args[0], nperValue, pmt, fv, type);
    }

    private static ScalarValue PvScalar(ScalarValue rateValue, double nperValue, double pmt, double fv, double type)
    {
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        double nper = nperValue;
        if (nper == 0) return ErrorValue.DivByZero;
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-pmt * nper - fv);
        double rn = Math.Pow(1 + rate, nper);
        double pv = (-pmt * (1 + rate * type) * (rn - 1) / rate - fv) / rn;
        return NumberResult(pv);
    }

    private static ScalarValue Fv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double nperValue = ToNumber(args[1]);
        double pmt  = ToNumber(args[2]);
        double pv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (args[0] is RangeValue rateRange) return MapUnaryTextRange(rateRange, value => FvScalar(value, nperValue, pmt, pv, type));
        return FvScalar(args[0], nperValue, pmt, pv, type);
    }

    private static ScalarValue FvScalar(ScalarValue rateValue, double nperValue, double pmt, double pv, double type)
    {
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(rate) || !double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        double nper = nperValue;
        if (Math.Abs(rate) < 1e-10)
            return NumberResult(-pv - pmt * nper);
        double rn = Math.Pow(1 + rate, nper);
        return NumberResult(-pv * rn - pmt * (1 + rate * type) * (rn - 1) / rate);
    }

    private static ScalarValue Nper(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double pmt  = ToNumber(args[1]);
        double pv   = ToNumber(args[2]);
        double fv   = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        if (args[0] is RangeValue rateRange) return MapUnaryTextRange(rateRange, value => NperScalar(value, pmt, pv, fv, type));
        return NperScalar(args[0], pmt, pv, fv, type);
    }

    private static ScalarValue NperScalar(ScalarValue rateValue, double pmt, double pv, double fv, double type)
    {
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(rate) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        if (Math.Abs(rate) < 1e-10)
        {
            if (Math.Abs(pmt) < 1e-10) return ErrorValue.DivByZero;
            return NumberResult(-(pv + fv) / pmt);
        }
        double pmtAdj = pmt * (1 + rate * type);
        double ratio  = (pmtAdj - fv * rate) / (pmtAdj + pv * rate);
        if (ratio <= 0) return ErrorValue.Num;
        return NumberResult(Math.Log(ratio) / Math.Log(1 + rate));
    }

    private static ScalarValue Rate(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        double pmt   = ToNumber(args[1]);
        double pv    = ToNumber(args[2]);
        double fv    = args.Count > 3 && args[3] is not BlankValue ? ToNumber(args[3]) : 0;
        double type  = args.Count > 4 && args[4] is not BlankValue ? ToNumber(args[4]) : 0;
        double guess = args.Count > 5 && args[5] is not BlankValue ? ToNumber(args[5]) : 0.1;
        if (args[0] is RangeValue nperRange) return MapUnaryTextRange(nperRange, value => RateScalar(value, pmt, pv, fv, type, guess));
        return RateScalar(args[0], pmt, pv, fv, type, guess);
    }

    private static ScalarValue RateScalar(ScalarValue nperValueArg, double pmt, double pv, double fv, double type, double guess)
    {
        double nperValue = ToNumber(nperValueArg);
        if (!double.IsFinite(nperValue) || !double.IsFinite(pmt) || !double.IsFinite(pv) || !double.IsFinite(fv) || !double.IsFinite(type) || !double.IsFinite(guess))
            return ErrorValue.Num;
        if (!IsValidPaymentType(type)) return ErrorValue.Num;
        if (nperValue == 0) return ErrorValue.DivByZero;
        double nper = nperValue;
        double r = guess;
        for (int i = 0; i < 100; i++)
        {
            double rn   = Math.Pow(1 + r, nper);
            double rn1  = nper * Math.Pow(1 + r, nper - 1);
            double f, df;
            if (Math.Abs(r) < 1e-10)
            {
                f  = pv + pmt * nper + fv;
                df = pv * nper + pmt * nper * (nper - 1) / 2.0;
            }
            else
            {
                f  = pv * rn + pmt * (1 + r * type) * (rn - 1) / r + fv;
                df = pv * rn1
                   + pmt * type * (rn - 1) / r
                   + pmt * (1 + r * type) * (rn1 * r - (rn - 1)) / (r * r);
            }
            if (Math.Abs(df) < 1e-15) break;
            double delta = f / df;
            r -= delta;
            if (Math.Abs(delta) < 1e-10) break;
        }
        return double.IsNaN(r) || double.IsInfinity(r) ? ErrorValue.Num : new NumberValue(r);
    }

    private static ScalarValue Npv(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e) return e;
        double rate   = ToNumber(args[0]);
        if (!double.IsFinite(rate)) return ErrorValue.Num;
        var (values, err) = CollectNumbers(args, start: 1);
        if (err is not null) return err;

        double result = 0;
        for (int i = 0; i < values!.Count; i++)
            result += values[i] / Math.Pow(1 + rate, i + 1);
        return NumberResult(result);
    }

    private static ScalarValue Irr(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var valRange = args[0] is RangeValue valuesRange
            ? valuesRange
            : SingleCellArray(args[0]);
        double guess = args.Count > 1 && args[1] is not BlankValue ? ToNumber(args[1]) : 0.1;
        if (!double.IsFinite(guess) || guess <= -1) return ErrorValue.Num;
        var (values, err) = CollectRangeNumbers(valRange);
        if (err is not null) return err;
        var cashflows = values!;
        if (cashflows.Count < 2) return ErrorValue.Num;
        // Excel requires at least one positive and one negative cashflow.
        bool hasPositive = false, hasNegative = false;
        for (int i = 0; i < cashflows.Count; i++)
        {
            if (cashflows[i] > 0) hasPositive = true;
            else if (cashflows[i] < 0) hasNegative = true;
        }
        if (!hasPositive || !hasNegative) return ErrorValue.Num;
        double r = guess;
        for (int iter = 0; iter < 100; iter++)
        {
            double f = 0, df = 0;
            for (int i = 0; i < cashflows.Count; i++)
            {
                double denom = Math.Pow(1 + r, i);
                f  += cashflows[i] / denom;
                if (i > 0) df -= i * cashflows[i] / (denom * (1 + r));
            }
            if (Math.Abs(f) < 1e-10) break;
            if (Math.Abs(df) < 1e-15) return ErrorValue.Num;
            double delta = f / df;
            r -= delta;
            if (Math.Abs(delta) < 1e-10) break;
        }
        return double.IsNaN(r) || double.IsInfinity(r) ? ErrorValue.Num : new NumberValue(r);
    }

    private static ScalarValue Sln(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (args[0] is ErrorValue e0) return e0;
        if (args[1] is ErrorValue e1) return e1;
        if (args[2] is ErrorValue e2) return e2;
        double cost    = ToNumber(args[0]);
        double salvage = ToNumber(args[1]);
        double life    = ToNumber(args[2]);
        if (!double.IsFinite(cost) || !double.IsFinite(salvage) || !double.IsFinite(life))
            return ErrorValue.Num;
        if (life == 0) return ErrorValue.DivByZero;
        return NumberResult((cost - salvage) / life);
    }
}

