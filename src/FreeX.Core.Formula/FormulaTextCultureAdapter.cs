using System.Globalization;
using System.Text;

namespace FreeX.Core.Formula;

/// <summary>
/// Translates between the invariant formula text stored by FreeX and the regional separators
/// Excel uses while a user enters or edits a formula.
/// </summary>
public static class FormulaTextCultureAdapter
{
    /// <summary>Converts formula-bar input into the invariant syntax understood by the parser.</summary>
    public static string ToInvariantFormulaText(string formulaText, CultureInfo? culture = null) =>
        Rewrite(formulaText, culture ?? CultureInfo.CurrentCulture, toInvariant: true);

    /// <summary>Converts invariant formula text into the syntax shown by Excel in the current region.</summary>
    public static string ToRegionalFormulaText(string formulaText, CultureInfo? culture = null) =>
        Rewrite(formulaText, culture ?? CultureInfo.CurrentCulture, toInvariant: false);

    private static string Rewrite(string formulaText, CultureInfo culture, bool toInvariant)
    {
        ArgumentNullException.ThrowIfNull(formulaText);
        ArgumentNullException.ThrowIfNull(culture);

        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        var listSeparator = culture.TextInfo.ListSeparator;
        if (decimalSeparator.Length != 1 || listSeparator.Length != 1)
            return formulaText;

        var decimalChar = decimalSeparator[0];
        var listChar = listSeparator[0];
        if (decimalChar == listChar)
            return formulaText;

        if (decimalChar == '.' && listChar == ',')
            return formulaText;

        var builder = new StringBuilder(formulaText.Length);
        var inString = false;
        var inQuotedSheetName = false;
        var bracketDepth = 0;
        var arrayDepth = 0;

        for (var i = 0; i < formulaText.Length; i++)
        {
            var current = formulaText[i];

            if (inString)
            {
                builder.Append(current);
                if (current == '"')
                {
                    if (i + 1 < formulaText.Length && formulaText[i + 1] == '"')
                        builder.Append(formulaText[++i]);
                    else
                        inString = false;
                }

                continue;
            }

            if (inQuotedSheetName)
            {
                builder.Append(current);
                if (current == '\'')
                {
                    if (i + 1 < formulaText.Length && formulaText[i + 1] == '\'')
                        builder.Append(formulaText[++i]);
                    else
                        inQuotedSheetName = false;
                }

                continue;
            }

            if (bracketDepth > 0)
            {
                builder.Append(current);
                if (current == '[')
                    bracketDepth++;
                else if (current == ']')
                    bracketDepth--;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                builder.Append(current);
                continue;
            }

            if (current == '\'')
            {
                inQuotedSheetName = true;
                builder.Append(current);
                continue;
            }

            if (current == '[')
            {
                bracketDepth = 1;
                builder.Append(current);
                continue;
            }

            if (current == '{')
            {
                arrayDepth++;
                builder.Append(current);
                continue;
            }

            if (current == '}')
            {
                arrayDepth = Math.Max(0, arrayDepth - 1);
                builder.Append(current);
                continue;
            }

            var sourceDecimalChar = toInvariant ? decimalChar : '.';
            var targetDecimalChar = toInvariant ? '.' : decimalChar;
            if (decimalChar != '.' && current == sourceDecimalChar && IsDecimalSeparatorAt(formulaText, i))
            {
                builder.Append(targetDecimalChar);
                continue;
            }

            if (arrayDepth > 0)
            {
                // Excel uses a backslash for array-column separation in comma-decimal regions;
                // FreeX's invariant array grammar uses commas for columns and semicolons for rows.
                if (toInvariant && decimalChar == ',' && current == '\\')
                    builder.Append(',');
                else if (!toInvariant && decimalChar == ',' && current == ',')
                    builder.Append('\\');
                else
                    builder.Append(current);

                continue;
            }

            if (toInvariant && current == listChar && listChar != ',')
                builder.Append(',');
            else if (!toInvariant && current == ',' && listChar != ',')
                builder.Append(listChar);
            else
                builder.Append(current);
        }

        return builder.ToString();
    }

    private static bool IsDigitAt(string text, int index) =>
        index >= 0 && index < text.Length && char.IsDigit(text[index]);

    private static bool IsDecimalSeparatorAt(string text, int index)
    {
        if (!IsDigitAt(text, index + 1))
            return false;

        if (IsDigitAt(text, index - 1))
            return true;

        if (index == 0)
            return true;

        return text[index - 1] is '(' or '{' or '\\' or ';' or ',' or '+' or '-' or '*' or '/' or '^' or '&' or '=' or '<' or '>' or ':' ||
               char.IsWhiteSpace(text[index - 1]);
    }
}
