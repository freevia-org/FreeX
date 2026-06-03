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
}

