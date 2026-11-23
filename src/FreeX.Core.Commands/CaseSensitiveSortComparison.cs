namespace FreeX.Core.Commands;

internal static class CaseSensitiveSortComparison
{
    internal static int Compare(string a, string b) =>
        ExcelTextCollation.Compare(a, b, matchCase: true);
}
