using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- Settlement and discount helpers ----------------------------------

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
        var calcMethodArg = args.Count > 7 ? args[7] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], args[3], args[4], args[5], basisArg, calcMethodArg], values => AccrintScalar(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]));
    }

    private static ScalarValue AccrintScalar(ScalarValue issueValue, ScalarValue firstInterestValue, ScalarValue settlementValue, ScalarValue rateValue, ScalarValue parValue, ScalarValue frequencyValue, ScalarValue basisValue, ScalarValue calcMethodValue)
    {
        double issue = ToNumber(issueValue);
        double firstInterest = ToNumber(firstInterestValue);
        double settlement = ToNumber(settlementValue);
        double par = ToNumber(parValue);
        double frequency = ToNumber(frequencyValue);
        double rate = ToNumber(rateValue);
        if (!double.IsFinite(issue) || !double.IsFinite(firstInterest) || !double.IsFinite(settlement) ||
            !double.IsFinite(rate) || !double.IsFinite(par) || !double.IsFinite(frequency))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        if (rate <= 0 || par <= 0 || frequency <= 0) return ErrorValue.Num;
        int freq = (int)Math.Truncate(frequency);
        // Excel: ACCRINT returns #NUM! for any frequency other than 1, 2 or 4 (same rule enforced by
        // every sibling bond/coupon function in this file set -- see R28-financial-functions-deep-2-2).
        if (freq != 1 && freq != 2 && freq != 4) return ErrorValue.Num;
        if (!TryGetFinancialDate(issue, out DateTime sd) ||
            !TryGetFinancialDate(firstInterest, out DateTime fi) ||
            !TryGetFinancialDate(settlement, out DateTime sett)) return ErrorValue.Num;
        if (sd >= sett || fi < sd) return ErrorValue.Num;
        // calc_method (default TRUE) accrues from the issue date; FALSE accrues from first_interest when it
        // precedes settlement (Excel ACCRINT semantics). Previously calc_method/first_interest were ignored.
        bool calcMethod = calcMethodValue is BlankValue || ToBool(calcMethodValue);
        DateTime accrualStart = !calcMethod && sett > fi ? fi : sd;
        // Basis 1 (Actual/Actual) must accrue over the bond's own quasi-coupon periods (Excel's documented
        // ACCRINT = par*(rate/frequency)*Sum(Ai/NLi)), not a single calendar-year-split fraction over the
        // whole span -- a leap day inside the span otherwise throws off an accrual that should land on an
        // exact whole number of coupon periods (see R28-financial-functions-deep-2-3). This is only
        // unambiguous for the common "regular" first coupon (first_interest exactly one period after
        // issue); an irregular/odd first coupon needs the fuller odd-period sub-division Excel documents,
        // which is out of scope here, so that case keeps the existing whole-span fraction -- as do bases
        // 0/2/3/4, which use a fixed period length so the whole-span and summed-period fractions are
        // algebraically identical (left untouched).
        double dcf = basis == 1 && sd.AddMonths(12 / freq) == fi
            ? AccrintActualActualCouponFraction(sd, accrualStart, sett, freq)
            : DayCountFraction(accrualStart, sett, basis);
        return NumberResult(par * rate * dcf);
    }

    // Sum of Ai/NLi over the quasi-coupon periods (anchored at the issue date, each 12/frequency months
    // long) that overlap [accrualStart, settlement], divided by frequency. Period boundaries are computed
    // directly from the issue date via AddMonths(k * monthsPerPeriod) rather than iteratively from the
    // previous boundary, so an end-of-month issue date (e.g. Aug 31) keeps landing on the matching
    // end-of-month coupon date every period instead of drifting (Feb 29 + 6 months = Aug 29, not Aug 31,
    // if re-added from the already-clamped date).
    private static double AccrintActualActualCouponFraction(DateTime issue, DateTime accrualStart, DateTime settlement, int frequency)
    {
        if (settlement <= accrualStart) return 0.0;
        int months = 12 / frequency;
        int k = 0;
        while (issue.AddMonths((k + 1) * months) <= accrualStart) k++;

        double periods = 0.0;
        DateTime periodStart = issue.AddMonths(k * months);
        while (periodStart < settlement)
        {
            DateTime periodEnd = issue.AddMonths((k + 1) * months);
            DateTime segStart = periodStart > accrualStart ? periodStart : accrualStart;
            DateTime segEnd = periodEnd < settlement ? periodEnd : settlement;
            double normalLength = (periodEnd - periodStart).TotalDays;
            if (segEnd > segStart && normalLength > 0)
                periods += (segEnd - segStart).TotalDays / normalLength;
            k++;
            periodStart = periodEnd;
        }

        return periods / frequency;
    }

    private static ScalarValue Accrintm(IReadOnlyList<ScalarValue> args, IEvalContext ctx)
    {
        if (FirstError(args) is { } e) return e;
        var parArg = args.Count > 3 ? args[3] : BlankValue.Instance;
        var basisArg = args.Count > 4 ? args[4] : BlankValue.Instance;
        return MapScalarArgs([args[0], args[1], args[2], parArg, basisArg], values => AccrintmScalar(values[0], values[1], values[2], values[3], values[4]));
    }

    private static ScalarValue AccrintmScalar(ScalarValue issueValue, ScalarValue settlementValue, ScalarValue rateValue, ScalarValue parValue, ScalarValue basisValue)
    {
        double issue = ToNumber(issueValue);
        double settlement = ToNumber(settlementValue);
        double rate = ToNumber(rateValue);
        double par = parValue is BlankValue ? 1000.0 : ToNumber(parValue);
        if (!double.IsFinite(issue) || !double.IsFinite(settlement) ||
            !double.IsFinite(rate) || !double.IsFinite(par))
            return ErrorValue.Num;
        if (!TryGetFinancialBasis(basisValue, out int basis)) return ErrorValue.Num;
        if (rate <= 0 || par <= 0) return ErrorValue.Num;
        if (!TryGetFinancialDate(issue, out DateTime issueDate) ||
            !TryGetFinancialDate(settlement, out DateTime settlementDate)) return ErrorValue.Num;
        if (issueDate >= settlementDate) return ErrorValue.Num;
        double dcf = DayCountFraction(issueDate, settlementDate, basis);
        return NumberResult(par * rate * dcf);
    }
}
