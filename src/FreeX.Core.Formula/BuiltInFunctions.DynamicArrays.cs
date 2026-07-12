using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class BuiltInFunctions
{
    // Dynamic array shared helpers.

    private static bool TryGetScalarControlArgument(ScalarValue value, out ScalarValue scalar, out ScalarValue error)
    {
        error = ErrorValue.Value;
        if (value is RangeValue range)
        {
            if (range.RowCount != 1 || range.ColCount != 1)
            {
                scalar = ErrorValue.Value;
                return false;
            }

            scalar = range.Cells[0, 0];
            if (scalar is ErrorValue scalarError)
            {
                error = scalarError;
                return false;
            }

            return true;
        }

        scalar = value;
        if (value is ErrorValue directError)
        {
            error = directError;
            return false;
        }

        return true;
    }

    // Like TryGetScalarControlArgument, but for a "fill value" argument (e.g. EXPAND/WRAPROWS/WRAPCOLS
    // pad_with) that is a plain value, not a control flag: an ErrorValue is a legitimate fill value and
    // must be returned verbatim (not treated as a hard-abort signal) so the rest of the array still fills.
    private static bool TryGetScalarFillArgument(ScalarValue value, out ScalarValue scalar, out ScalarValue error)
    {
        error = ErrorValue.Value;
        if (value is RangeValue range)
        {
            if (range.RowCount != 1 || range.ColCount != 1)
            {
                scalar = ErrorValue.Value;
                return false;
            }

            scalar = range.Cells[0, 0];
            return true;
        }

        scalar = value;
        return true;
    }
}

