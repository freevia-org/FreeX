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
