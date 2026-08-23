namespace FreeX.Core.Commands;

internal static class CaseSensitiveSortComparison
{
    internal static int Compare(string a, string b)
    {
        var primary = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        if (primary != 0)
            return primary;

        var length = Math.Min(a.Length, b.Length);
        for (var index = 0; index < length; index++)
        {
            var left = a[index];
            var right = b[index];
            if (left == right)
                continue;

            var leftIsLower = char.IsLower(left);
            var rightIsLower = char.IsLower(right);
            if (leftIsLower != rightIsLower)
                return leftIsLower ? -1 : 1;

            return left.CompareTo(right);
        }

        return a.Length.CompareTo(b.Length);
    }
}
