namespace Free.Shared.Opc;

/// <summary>
/// Parses the <c>xsd:boolean</c> lexical space (<c>1</c>, <c>true</c>, <c>0</c>, <c>false</c>; case-sensitive)
/// used by SpreadsheetML and DrawingML attributes. Unlike <see cref="OoxmlOnOffLexical"/> it does not accept
/// the WordprocessingML-only <c>on</c>/<c>off</c> tokens.
/// </summary>
public static class OoxmlBooleanLexical
{
    /// <summary>Returns the parsed value, or <c>null</c> when the value is absent or not a valid token.</summary>
    public static bool? TryParse(string? value) =>
        value switch
        {
            "1" or "true" => true,
            "0" or "false" => false,
            _ => null,
        };

    /// <summary>True only for <c>1</c> or <c>true</c>; absent, <c>0</c>/<c>false</c> and invalid values are false.</summary>
    public static bool IsTrue(string? value) => value is "1" or "true";
}
