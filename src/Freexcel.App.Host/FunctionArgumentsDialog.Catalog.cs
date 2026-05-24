namespace Freexcel.App.Host;

public sealed partial class FunctionArgumentsDialog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<FunctionArgumentSpec>> KnownArguments =
        new Dictionary<string, IReadOnlyList<FunctionArgumentSpec>>(StringComparer.OrdinalIgnoreCase)
        {
            ["SUM"] =
            [
                new("Number1", "The first number, cell reference, or range."),
                new("Number2", "Additional numbers, cell references, or ranges.", Optional: true)
            ],
            ["AVERAGE"] =
            [
                new("Number1", "The first number, cell reference, or range."),
                new("Number2", "Additional numbers, cell references, or ranges.", Optional: true)
            ],
            ["COUNT"] =
            [
                new("Value1", "The first value, cell reference, or range."),
                new("Value2", "Additional values, cell references, or ranges.", Optional: true)
            ],
            ["COUNTA"] =
            [
                new("Value1", "The first value, cell reference, or range."),
                new("Value2", "Additional values, cell references, or ranges.", Optional: true)
            ],
            ["MIN"] =
            [
                new("Number1", "The first number, cell reference, or range."),
                new("Number2", "Additional numbers, cell references, or ranges.", Optional: true)
            ],
            ["MAX"] =
            [
                new("Number1", "The first number, cell reference, or range."),
                new("Number2", "Additional numbers, cell references, or ranges.", Optional: true)
            ],
            ["IF"] =
            [
                new("Logical_test", "Any value or expression that can be evaluated as TRUE or FALSE."),
                new("Value_if_true", "The value returned when the logical test is TRUE."),
                new("Value_if_false", "The value returned when the logical test is FALSE.", Optional: true)
            ],
            ["IFS"] =
            [
                new("Logical_test1", "The first condition to evaluate."),
                new("Value_if_true1", "The value returned when the first condition is TRUE."),
                new("Logical_test2", "An additional condition to evaluate.", Optional: true),
                new("Value_if_true2", "The value returned when the additional condition is TRUE.", Optional: true)
            ],
            ["AND"] =
            [
                new("Logical1", "The first condition to test."),
                new("Logical2", "Additional conditions to test.", Optional: true)
            ],
            ["OR"] =
            [
                new("Logical1", "The first condition to test."),
                new("Logical2", "Additional conditions to test.", Optional: true)
            ],
            ["NOT"] = [new("Logical", "A value or expression that can be evaluated as TRUE or FALSE.")],
            ["IFERROR"] =
            [
                new("Value", "The expression to check for an error."),
                new("Value_if_error", "The value returned if the expression evaluates to an error.")
            ],
            ["IFNA"] =
            [
                new("Value", "The expression to check for the #N/A error."),
                new("Value_if_na", "The value returned if the expression evaluates to #N/A.")
            ],
            ["XLOOKUP"] =
            [
                new("Lookup_value", "The value to search for."),
                new("Lookup_array", "The array or range to search."),
                new("Return_array", "The array or range to return from."),
                new("If_not_found", "The value returned when no match is found.", Optional: true),
                new("Match_mode", "The match behavior: exact, next smaller, next larger, or wildcard.", Optional: true),
                new("Search_mode", "The search direction and binary-search option.", Optional: true)
            ],
            ["VLOOKUP"] =
            [
                new("Lookup_value", "The value to search for in the first column."),
                new("Table_array", "The range containing lookup and return columns."),
                new("Col_index_num", "The column number in the table to return."),
                new("Range_lookup", "TRUE for approximate match or FALSE for exact match.", Optional: true)
            ],
            ["HLOOKUP"] =
            [
                new("Lookup_value", "The value to search for in the first row."),
                new("Table_array", "The range containing lookup and return rows."),
                new("Row_index_num", "The row number in the table to return."),
                new("Range_lookup", "TRUE for approximate match or FALSE for exact match.", Optional: true)
            ],
            ["INDEX"] =
            [
                new("Array", "The range or array from which to return a value."),
                new("Row_num", "The row position in the array."),
                new("Column_num", "The column position in the array.", Optional: true)
            ],
            ["MATCH"] =
            [
                new("Lookup_value", "The value to match."),
                new("Lookup_array", "The one-row or one-column range to search."),
                new("Match_type", "1, 0, or -1 to choose approximate or exact matching.", Optional: true)
            ],
            ["XMATCH"] =
            [
                new("Lookup_value", "The value to match."),
                new("Lookup_array", "The array or range to search."),
                new("Match_mode", "The match behavior: exact, next smaller, next larger, or wildcard.", Optional: true),
                new("Search_mode", "The search direction and binary-search option.", Optional: true)
            ],
            ["LEFT"] =
            [
                new("Text", "The text containing the characters to extract."),
                new("Num_chars", "The number of characters to extract.", Optional: true)
            ],
            ["RIGHT"] =
            [
                new("Text", "The text containing the characters to extract."),
                new("Num_chars", "The number of characters to extract.", Optional: true)
            ],
            ["MID"] =
            [
                new("Text", "The text containing the characters to extract."),
                new("Start_num", "The position of the first character to extract."),
                new("Num_chars", "The number of characters to extract.")
            ],
            ["LEN"] = [new("Text", "The text whose length is returned.")],
            ["TRIM"] = [new("Text", "The text from which extra spaces are removed.")],
            ["TEXT"] =
            [
                new("Value", "The value to format."),
                new("Format_text", "The number format code to apply.")
            ],
            ["CONCAT"] =
            [
                new("Text1", "The first text item, cell reference, or range."),
                new("Text2", "Additional text items, references, or ranges.", Optional: true)
            ],
            ["SUBSTITUTE"] =
            [
                new("Text", "The text in which to substitute characters."),
                new("Old_text", "The text to replace."),
                new("New_text", "The replacement text."),
                new("Instance_num", "The occurrence to replace. Omit to replace all occurrences.", Optional: true)
            ],
            ["FIND"] =
            [
                new("Find_text", "The text to find."),
                new("Within_text", "The text to search within."),
                new("Start_num", "The character position at which to start.", Optional: true)
            ],
            ["SEARCH"] =
            [
                new("Find_text", "The text to find; wildcards are allowed."),
                new("Within_text", "The text to search within."),
                new("Start_num", "The character position at which to start.", Optional: true)
            ],
            ["TODAY"] = [],
            ["NOW"] = [],
            ["DATE"] =
            [
                new("Year", "The year component of the date."),
                new("Month", "The month component of the date."),
                new("Day", "The day component of the date.")
            ],
            ["YEAR"] = [new("Serial_number", "The date whose year is returned.")],
            ["MONTH"] = [new("Serial_number", "The date whose month is returned.")],
            ["DAY"] = [new("Serial_number", "The date whose day is returned.")],
            ["ROUND"] =
            [
                new("Number", "The number to round."),
                new("Num_digits", "The number of digits to round to.")
            ],
            ["ABS"] = [new("Number", "The number whose absolute value is returned.")],
            ["SQRT"] = [new("Number", "The number whose square root is returned.")],
            ["POWER"] =
            [
                new("Number", "The base number."),
                new("Power", "The exponent to raise the base number to.")
            ],
            ["SUMIF"] =
            [
                new("Range", "The range to evaluate."),
                new("Criteria", "The condition that determines which cells are added."),
                new("Sum_range", "The cells to add. Omit to add cells in Range.", Optional: true)
            ],
            ["COUNTIF"] =
            [
                new("Range", "The range to evaluate."),
                new("Criteria", "The condition that determines which cells are counted.")
            ],
            ["AVERAGEIF"] =
            [
                new("Range", "The range to evaluate."),
                new("Criteria", "The condition that determines which cells are averaged."),
                new("Average_range", "The cells to average. Omit to average cells in Range.", Optional: true)
            ]
        };
}
