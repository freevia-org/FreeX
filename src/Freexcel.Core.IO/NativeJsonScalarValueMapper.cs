using System.Globalization;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class NativeJsonScalarValueMapper
{
    public static string? Serialize(ScalarValue value) => value switch
    {
        BlankValue => null,
        NumberValue n => n.Value.ToString(CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        TextValue t => t.Value,
        ErrorValue e => e.Code,
        _ => null,
    };

    public static string? GetValueType(ScalarValue value) => value switch
    {
        NumberValue => "n",
        BoolValue => "b",
        TextValue => "t",
        ErrorValue => "e",
        _ => null,
    };

    public static ScalarValue Deserialize(string? value, string? type)
    {
        if (value == null)
            return BlankValue.Instance;

        return type switch
        {
            "n" => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                ? new NumberValue(d)
                : new TextValue(value),
            "b" => new BoolValue(value == "TRUE"),
            "t" => new TextValue(value),
            "e" => value switch
            {
                "#DIV/0!" => ErrorValue.DivByZero,
                "#VALUE!" => ErrorValue.Value,
                "#REF!" => ErrorValue.Ref,
                "#NAME?" => ErrorValue.Name,
                "#NULL!" => ErrorValue.Null,
                "#N/A" => ErrorValue.NA,
                "#NUM!" => ErrorValue.Num,
                _ => new ErrorValue(value)
            },
            _ => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var dn)
                ? new NumberValue(dn)
                : bool.TryParse(value, out var db) ? new BoolValue(db)
                : new TextValue(value)
        };
    }
}
