namespace FreeX.Core.Formula;

internal static class R1C1ReferencePartResolver
{
    internal static bool TryResolve(
        string text,
        uint anchorValue,
        out long value,
        out bool absolute)
    {
        if (string.IsNullOrEmpty(text))
        {
            absolute = false;
            value = anchorValue;
            return true;
        }

        if (text.StartsWith("[", StringComparison.Ordinal) &&
            text.EndsWith("]", StringComparison.Ordinal))
        {
            absolute = false;
            if (!long.TryParse(text[1..^1], out var offset))
            {
                value = 0;
                return false;
            }

            try
            {
                value = checked(anchorValue + offset);
                return true;
            }
            catch (OverflowException)
            {
                value = 0;
                return false;
            }
        }

        absolute = true;
        return long.TryParse(text, out value);
    }
}
