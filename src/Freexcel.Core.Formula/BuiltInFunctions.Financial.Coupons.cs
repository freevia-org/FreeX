using Freexcel.Core.Model;

namespace Freexcel.Core.Formula;

public static partial class BuiltInFunctions
{
    // -- Coupon date functions --------------------------------------------

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
}
