namespace Free.Shared.Opc;

/// <summary>Parses the common OOXML on/off lexical tokens with caller-owned fallbacks.</summary>
public static class OoxmlOnOffLexical
{
    public static bool Parse(
        string? value,
        bool absentDefault,
        bool invalidDefault) =>
        value switch
        {
            null => absentDefault,
            "1" or "true" or "on" => true,
            "0" or "false" or "off" => false,
            _ => invalidDefault,
        };
}
