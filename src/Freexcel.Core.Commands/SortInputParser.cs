namespace Freexcel.Core.Commands;

public static class SortInputParser
{
    public static bool TryParse(string input, out IReadOnlyList<SortKey> keys, out string? error)
    {
        keys = [];
        error = null;

        var parts = input.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "Enter at least one sort column.";
            return false;
        }

        var parsed = new List<SortKey>();
        foreach (var part in parts)
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length is < 1 or > 2 || !uint.TryParse(tokens[0], out var column) || column == 0)
            {
                error = "Enter valid sort column numbers.";
                return false;
            }

            var ascending = true;
            if (tokens.Length == 2)
            {
                if (tokens[1].Equals("asc", StringComparison.OrdinalIgnoreCase) ||
                    tokens[1].Equals("ascending", StringComparison.OrdinalIgnoreCase) ||
                    tokens[1].Equals("az", StringComparison.OrdinalIgnoreCase))
                {
                    ascending = true;
                }
                else if (tokens[1].Equals("desc", StringComparison.OrdinalIgnoreCase) ||
                         tokens[1].Equals("descending", StringComparison.OrdinalIgnoreCase) ||
                         tokens[1].Equals("za", StringComparison.OrdinalIgnoreCase))
                {
                    ascending = false;
                }
                else
                {
                    error = "Use asc or desc for each sort direction.";
                    return false;
                }
            }

            parsed.Add(new SortKey(column - 1, ascending));
        }

        keys = parsed;
        return true;
    }
}
